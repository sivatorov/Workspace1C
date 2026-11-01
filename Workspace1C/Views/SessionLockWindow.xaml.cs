#if WINDOWS
using System;
using System.Collections.Generic;
using System.Windows;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Блокировка сеансов информационной базы» (функция №20 StartManager, CTRL+ALT+L).
    /// Позволяет задать время начала (по умолчанию +5 мин), длительность (по умолчанию 30 мин)
    /// и текст сообщения с параметрами {ДатаНач}/{ДатаКон}, выбрать шаблон («Технические работы»
    /// или «Обновление ИБ») и выполнить установку либо снятие блокировки сеансов файловой ИБ
    /// без открытия «1С:Предприятия».
    /// </summary>
    public partial class SessionLockWindow : Window
    {
        private readonly Infobase _infobase;
        private readonly ISessionLockService _service;
        private readonly List<string> _templates = new();

        /// <summary>Возвращает true, если пользователь запросил снятие блокировки.</summary>
        public bool UnlockRequested { get; private set; }

        /// <summary>Возвращает введённые параметры блокировки (после «Заблокировать»).</summary>
        public SessionLockOptions? Options { get; private set; }

        public SessionLockWindow(Infobase infobase)
        {
            InitializeComponent();
            // Адаптивная высота (issue #20): на малых разрешениях окно не должно уходить
            // за нижнюю границу экрана (WindowStartupLocation="CenterOwner").
            var work = SystemParameters.WorkArea;
            Height = Math.Min(680, work.Height - 80);
            _infobase = infobase;
            _service = AppServices.GetRequiredService<ISessionLockService>();

            Title = string.Format(LocalizationManager.T("SessionLock.TitleForBase"), infobase.Name);
            DescriptionLabel.Text = string.Format(LocalizationManager.T("SessionLock.DescriptionForBase"), infobase.Name);

            // ESC закрывает диалог без действий (issue #20): раньше окно не закрывалось
            // по ESC. PreviewKeyDown перехватывает клавишу даже при фокусе в TextBox.
            PreviewKeyDown += OnWindowPreviewKeyDown;

            LoadTemplates();
            ResetToDefaults();
        }

        /// <summary>Закрывает диалог по ESC без установки/снятия блокировки (issue #20).</summary>
        private void OnWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                DialogResult = false;
                e.Handled = true;
            }
        }

        private static string T(string key) => LocalizationManager.T(key);

        private void LoadTemplates()
        {
            _templates.Clear();
            _templates.Add(T("SessionLock.TemplateMaintenance"));
            _templates.Add(T("SessionLock.TemplateUpdate"));

            TemplateBox.ItemsSource = null;
            TemplateBox.Items.Clear();
            foreach (var tpl in _templates)
                TemplateBox.Items.Add(tpl);
            TemplateBox.SelectedIndex = 0;
        }

        /// <summary>Время начала по умолчанию — +5 минут от текущего момента, длительность 30 минут.</summary>
        private void ResetToDefaults()
        {
            var start = DateTime.Now.AddMinutes(5);
            StartDate.SelectedDate = start.Date;
            StartTimeBox.Text = start.ToString("HH:mm");
            DurationBox.Text = "30";
            MessageTextBox.Text = _templates.Count > 0 ? _templates[0] : string.Empty;
            ApplyTemplate(0);
        }

        private void TemplateBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (TemplateBox.SelectedIndex >= 0)
                ApplyTemplate(TemplateBox.SelectedIndex);
        }

        private void ApplyTemplate(int index)
        {
            if (index < 0 || index >= _templates.Count)
                return;
            // Шаблон — это текст с плейсхолдерами, который подставляется в поле сообщения.
            var templateMessage = _templates[index];
            if (string.Equals(templateMessage, T("SessionLock.TemplateMaintenance"), StringComparison.Ordinal))
                MessageTextBox.Text = T("SessionLock.MessageMaintenance");
            else if (string.Equals(templateMessage, T("SessionLock.TemplateUpdate"), StringComparison.Ordinal))
                MessageTextBox.Text = T("SessionLock.MessageUpdate");
        }

        private async void OnLock_Click(object sender, RoutedEventArgs e)
        {
            // v100120: async void — исключение не должно уходить в глобальный обработчик
            // без контекста операции (аудит async void, шаблон RelayCommand.Execute).
            var options = BuildOptions();
            if (options is null)
                return;

            IsEnabled = false;
            try
            {
                var result = await _service.LockAsync(_infobase, options);
                if (result.Success)
                {
                    Options = options;
                    DialogResult = true;
                }
                else
                {
                    System.Windows.MessageBox.Show(result.ErrorMessage ?? T("SessionLock.Failed"),
                        T("SessionLock.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                try { AppServices.TryGetService<Services.IAppLogger>()?.Error("Ошибка блокировки сеансов", ex); } catch { /* лог не роняет обработчик */ }
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private async void OnUnlock_Click(object sender, RoutedEventArgs e)
        {
            // v100120: async void — исключение не должно уходить в глобальный обработчик
            // без контекста операции (аудит async void, шаблон RelayCommand.Execute).
            IsEnabled = false;
            try
            {
                var result = await _service.UnlockAsync(_infobase);
                if (result.Success)
                {
                    UnlockRequested = true;
                    DialogResult = true;
                }
                else
                {
                    System.Windows.MessageBox.Show(result.ErrorMessage ?? T("SessionLock.UnlockFailed"),
                        T("SessionLock.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                try { AppServices.TryGetService<Services.IAppLogger>()?.Error("Ошибка разблокировки сеансов", ex); } catch { /* лог не роняет обработчик */ }
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private SessionLockOptions? BuildOptions()
        {
            if (StartDate.SelectedDate is not DateTime date)
            {
                System.Windows.MessageBox.Show(T("SessionLock.InvalidStartTime"), T("SessionLock.Title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            if (!TimeSpan.TryParse(StartTimeBox.Text?.Trim(), out var time))
            {
                System.Windows.MessageBox.Show(T("SessionLock.InvalidStartTime"), T("SessionLock.Title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            if (!int.TryParse(DurationBox.Text?.Trim(), out var minutes) || minutes <= 0)
            {
                System.Windows.MessageBox.Show(T("SessionLock.InvalidDuration"), T("SessionLock.Title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            return new SessionLockOptions
            {
                StartTime = date.Date + time,
                Duration = TimeSpan.FromMinutes(minutes),
                Message = MessageTextBox.Text ?? string.Empty,
                // Код разрешения (issue #20): необязательный, для файла 1cv8.cdn.
                PermissionCode = PermissionCodeBox.Text?.Trim() ?? string.Empty
            };
        }
    }
}
#endif