#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Themes;
using Workspace1C.Services;

namespace Workspace1C
{
    /// <summary>
    /// Диалог ввода строки подключения к информационной базе 1С. Позволяет вставить
    /// строку из буфера обмена или ввести её вручную, после чего применить.
    /// Avalonia/Linux-версия WPF-окна <see cref="ConnectionStringInputWindow"/>.
    /// </summary>
    public class ConnectionStringInputWindow : ModalWindowBase
    {
        private readonly TextBox _inputBox;
        private readonly Button _okButton;
        private readonly IDialogService _dialogs;

        /// <summary>
        /// Создаёт диалог ввода строки подключения.
        /// </summary>
        /// <param name="initialValue">Текущее значение строки подключения базы (может быть пустым).</param>
        public ConnectionStringInputWindow(string? initialValue = null)
        {
            Title = LocalizationManager.T("ConnectionStringInput.Title");
            // Кегль окна из разметки: подписи без явного размера берут его по наследству.
            FontSize = 13;
            Width = 520;
            // Высота из разметки, а не подгонка по содержимому.
            Height = 230;
            CanResize = false;

            _dialogs = AppServices.GetRequiredService<IDialogService>();

            // Поле однострочное и во всю ширину, как в разметке
            // (ConnectionStringInputWindow.xaml:44).
            _inputBox = new TextBox
            {
                MinHeight = 34,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _inputBox.Styled(ControlThemes.ModernTextBox);
            _inputBox.TextChanged += (_, _) => UpdateOkEnabled();
            _inputBox.KeyDown += OnInputBox_KeyDown;

            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var title = new TextBlock
            {
                Text = LocalizationManager.T("ConnectionStringInput.Label"),
                Margin = new Thickness(0, 0, 0, 6)
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            var hint = new TextBlock
            {
                Text = LocalizationManager.T("ConnectionStringInput.Hint"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8)
            };
            Themes.ThemeBrushes.Bind(hint, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetRow(hint, 1);
            grid.Children.Add(hint);

            Grid.SetRow(_inputBox, 2);
            grid.Children.Add(_inputBox);

            // Нижняя строка: вставка из буфера слева, применение и отмена справа
            // (ConnectionStringInputWindow.xaml:50).
            var bottom = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            bottom.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            bottom.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var pasteCaption = new TextBlock
            {
                Text = LocalizationManager.T("ConnectionStringInput.Paste"),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            var pasteButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { IconHelper.MakeIcon("IconCopy", UiMetrics.IconControl, "SecondaryButtonTextBrush"), pasteCaption }
                },
                // Аудит UI P1c: высота нижней панели диалога — 36.
                Height = 36,
                // Отступ справа из разметки (ConnectionStringInputWindow.xaml:58).
                Margin = new Thickness(0, 0, 10, 0),
                Padding = new Thickness(12, 0)
            };
            pasteButton.Styled(ControlThemes.SecondaryButton);
            ToolTip.SetTip(pasteButton, LocalizationManager.T("ConnectionStringInput.PasteTooltip"));
            pasteButton.Click += (_, _) => OnPasteClipboard_Click();
            Grid.SetColumn(pasteButton, 0);
            bottom.Children.Add(pasteButton);

            // Аудит UI P1c: единое гашение недоступной кнопки — приглушение
            // (opacity 0.5) в теме DialogConfirmButton; серый класс greyed удалён.
            _okButton = BuildConfirmActionButton("Common.Apply", "IconCheck", 150, OnOk_Click);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Children = { _okButton, BuildCancelActionButton(140) }
            };
            Grid.SetColumn(buttons, 2);
            bottom.Children.Add(buttons);

            Grid.SetRow(bottom, 3);
            grid.Children.Add(bottom);

            Content = grid;
            UpdateOkEnabled();

            Opened += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(initialValue))
                {
                    _inputBox.Text = initialValue!.Trim();
                }
                else
                {
                    TryPrefillFromClipboard();
                }
                _inputBox.Focus();
                _inputBox.SelectAll();
            };
        }

        /// <summary>
        /// Введённая строка подключения (null, если пользователь отменил ввод).
        /// </summary>
        public string? Result { get; private set; }

        /// <summary>Определяет, является ли текст ссылкой на информационную базу 1С.</summary>
        private static bool LooksLikeLink(string? text)
        {
            var value = (text ?? string.Empty).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (value.StartsWith("e1c:", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return true;

            string[] keys = { "srvr=", "ref=", "file=", "ws=", "usr=", "pwd=" };
            foreach (var key in keys)
            {
                if (value.Contains(key, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (value.IndexOf('\\') >= 0 || value.IndexOf('/') >= 0)
                return true;

            return false;
        }

        /// <summary>Пытается подставить строку из буфера обмена, если она удовлетворяет критериям ссылки.</summary>
        private async void TryPrefillFromClipboard()
        {
            var clipboard = Clipboard;
            if (clipboard is null)
                return;

            string text;
            try
            {
                text = await clipboard.TryGetTextAsync() ?? string.Empty;
            }
            catch
            {
                return;
            }

            var trimmed = text.Trim();
            if (LooksLikeLink(trimmed))
                _inputBox.Text = trimmed;
        }

        /// <summary>Вставляет текст из буфера обмена в поле ввода (без проверки критериев).</summary>
        private async void OnPasteClipboard_Click()
        {
            var clipboard = Clipboard;
            if (clipboard is null)
            {
                _dialogs.ShowWarning(LocalizationManager.T("ConnectionStringInput.ClipboardAccessError"), LocalizationManager.T("ConnectionStringInput.PasteTitle"));
                return;
            }

            string text;
            try
            {
                text = await clipboard.TryGetTextAsync() ?? string.Empty;
            }
            catch
            {
                _dialogs.ShowWarning(LocalizationManager.T("ConnectionStringInput.ClipboardReadError"), LocalizationManager.T("ConnectionStringInput.PasteTitle"));
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                _dialogs.ShowInfo(LocalizationManager.T("ConnectionStringInput.ClipboardEmpty"), LocalizationManager.T("ConnectionStringInput.PasteTitle"));
                return;
            }

            _inputBox.Text = text.Trim();
            _inputBox.Focus();
            _inputBox.CaretIndex = _inputBox.Text.Length;
        }

        private void UpdateOkEnabled()
        {
            _okButton.IsEnabled = !string.IsNullOrWhiteSpace(_inputBox.Text);
        }

        private void OnOk_Click()
        {
            Result = _inputBox.Text?.Trim();
        }

        private void OnInputBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && _okButton.IsEnabled)
            {
                OnOk_Click();
                DialogResult = true;
                Close();
                e.Handled = true;
            }
        }
    }
}
#endif