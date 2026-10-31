#if LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Диалог «Определение конфигураций всех баз» (issue #236), Avalonia/Linux-версия
    /// WPF-окна <see cref="DetectConfigurationsWindow"/>. Таблица баз с флажками, колонками
    /// платформы, индикатором логина/пароля и кнопкой свойств базы. Последовательно определяет
    /// имя конфигурации и номер релиза выбранных баз через
    /// <see cref="ConfigurationInfoService.ReadAndApply"/> в фоне (UI не блокируется). Показывает
    /// текущую базу и результат предыдущей, позволяет выбрать действие при ошибке и прекратить
    /// обработку.
    /// </summary>
    public sealed class DetectConfigurationsWindow : ModalWindowBase
    {
        private readonly List<DetectConfigRowViewModel> _rows = new();
        private readonly Dictionary<DetectConfigRowViewModel, CheckBox> _checks = new();
        private readonly Dictionary<DetectConfigRowViewModel, TextBlock> _platformTexts = new();
        private readonly Dictionary<DetectConfigRowViewModel, TextBlock> _configTexts = new();
        private readonly Dictionary<DetectConfigRowViewModel, TextBlock> _versionTexts = new();
        private readonly Dictionary<DetectConfigRowViewModel, TextBlock> _credTexts = new();
        private readonly Action<Infobase>? _editBase;

        private readonly StackPanel _rowsPanel = new() { Margin = new Thickness(4, 2) };
        private readonly TextBlock _checkedHeaderText = new();
        private readonly TextBlock _progressText = new();
        private readonly TextBlock _summaryText = new();
        private ComboBox _errorModeCombo = new();
        private Button _detectButton = new();
        private Button _stopButton = new();
        private readonly Services.IAppLogger _logger =
            AppServices.GetRequiredService<Services.IAppLogger>();
        private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

        private CancellationTokenSource? _cts;
        private bool _closeConfirmed;

        // Признак того, что вопрос подтверждения показывается или запланирован (issue #260).
        // Вопрос выносится из события Closing в отложенный вызов: если показывать Confirm
        // синхронно внутри Closing, его вложенный цикл PushFrame вызывает реентерабельный
        // Closing уже после завершения модального окна, и вопрос повторяется.
        private bool _closePromptOpen;

        /// <param name="infobases">Все информационные базы для определения.</param>
        /// <param name="editBase">Обратный вызов открытия окна свойств базы (под курсором). Может быть null.</param>
        public DetectConfigurationsWindow(IReadOnlyList<Infobase> infobases, Action<Infobase>? editBase = null)
        {
            Title = LocalizationManager.T("DetectConfigs.Title");
            Width = 820;
            Height = 560;
            MinWidth = 680;
            MinHeight = 440;
            FontSize = 13;
            CanResize = true;
            _editBase = editBase;

            foreach (var ib in infobases)
                _rows.Add(new DetectConfigRowViewModel(ib));

            Content = BuildRoot();
            foreach (var row in _rows)
                AddRow(row);
            UpdateCheckedHeader();
            Closing += OnClosingConfirm;
        }

        /// <summary>Признак того, что хотя бы одна база была изменена (для персиста в настройках).</summary>
        public bool DataChanged { get; private set; }

        private static string T(string key) => LocalizationManager.T(key);

        private static TextBlock MakeHeaderText(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
        }

        private static void BindSecondary(TextBlock block)
            => Themes.ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "TextSecondaryBrush");

        /// <summary>Обновляет заголовок колонки-флажка счётчиком отмеченных элементов.</summary>
        private void UpdateCheckedHeader()
        {
            _checkedHeaderText.Text = string.Format(
                LocalizationManager.T("DetectConfigs.Column.CheckedHeaderFormat"),
                _rows.Count(r => r.IsChecked));
        }

        private void UpdateSelectionButtonsEnabled()
        {
            var busy = !_detectButton.IsEnabled;
            foreach (var (_, check) in _checks)
                check.IsEnabled = !busy;
            _errorModeCombo.IsEnabled = !busy;
            // Флажки, которые прямо сейчас обрабатываются, тоже блокируем.
            foreach (var (row, check) in _checks)
                if (row.IsProcessing)
                    check.IsEnabled = false;
        }

        private void OnCheckAll()
        {
            foreach (var row in _rows.Where(r => !r.IsProcessing))
                row.IsChecked = true;
            UpdateCheckedHeader();
        }

        private void OnUncheckAll()
        {
            foreach (var row in _rows.Where(r => !r.IsProcessing))
                row.IsChecked = false;
            UpdateCheckedHeader();
        }

        private void OnInvert()
        {
            foreach (var row in _rows.Where(r => !r.IsProcessing))
                row.IsChecked = !row.IsChecked;
            UpdateCheckedHeader();
        }

        /// <summary>Открывает свойства базы под курсором, не закрывая список (issue #236, п.5).</summary>
        private void OnPropertiesClick(DetectConfigRowViewModel row)
        {
            if (_editBase is null)
                return;
            _editBase(row.Infobase);
            // После редактирования могли измениться платформа и логин/пароль — обновляем строку.
            row.SyncFromInfobase();
            UpdateRow(row);
            UpdateCheckedHeader();
        }

        private void OnStopClick()
        {
            _cts?.Cancel();
        }

        /// <summary>
        /// Последовательно обрабатывает отмеченные строки. Последовательность обязательна:
        /// <see cref="ComReadHost"/> сериализует запросы статической блокировкой, параллельный
        /// вызов упёрся бы в ту же блокировку без ускорения (план issue #236 §3).
        /// </summary>
        private async void OnDetectClick()
        {
            var targets = _rows.Where(r => r.IsChecked && !r.IsProcessing).ToList();
            if (targets.Count == 0)
            {
                _summaryText.Text = LocalizationManager.T("DetectConfigs.NoneSelected");
                return;
            }

            var continueOnError = _errorModeCombo.SelectedIndex == 1;

            _detectButton.IsEnabled = false;
            _stopButton.IsEnabled = true;
            UpdateSelectionButtonsEnabled();
            _summaryText.Text = string.Empty;
            _progressText.Text = string.Empty;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            var ok = 0;
            var errors = 0;
            var stopped = false;
            string? lastResult = null;

            foreach (var row in targets)
            {
                if (token.IsCancellationRequested)
                {
                    stopped = true;
                    break;
                }

                row.IsProcessing = true;
                _progressText.Text = string.Format(
                    LocalizationManager.T("DetectConfigs.ProgressFormat"), row.Name);
                if (lastResult != null)
                    _summaryText.Text = lastResult;

                OneCConfigInfo? info = null;
                string? errorText = null;
                try
                {
                    // Режим чтения сведений — «Конфигуратор» (issue #236): учитывается раздельная
                    // авторизация ConfiguratorAuth при её наличии, иначе авторизация базы.
                    info = await Task.Run(() =>
                        ConfigurationInfoService.ReadAndApply(row.Infobase, overwriteExisting: true,
                            mode: OneCLaunchMode.Configurator), token);
                }
                catch (OperationCanceledException)
                {
                    stopped = true;
                    row.IsProcessing = false;
                    break;
                }
                catch (Exception ex)
                {
                    errorText = ex.Message;
                    _logger.Error($"Ошибка определения конфигурации базы «{row.Name}»", ex);
                }

                // await продолжается на UI-потоке (Avalonia SynchronizationContext).
                row.SyncFromInfobase();
                row.IsProcessing = false;

                var success = info is not null;
                if (success)
                {
                    ok++;
                    DataChanged = true;
                    row.IsChecked = false;
                    lastResult = string.Format(
                        LocalizationManager.T("DetectConfigs.LastOkFormat"), row.Name);
                }
                else
                {
                    errors++;
                    row.ErrorText = errorText ?? ConfigurationInfoService.LastComError
                        ?? string.Format(LocalizationManager.T("DetectConfigs.ErrorRowFormat"), row.Name);
                    lastResult = string.Format(
                        LocalizationManager.T("DetectConfigs.LastErrorFormat"), row.Name, row.ErrorText);
                    if (!continueOnError)
                    {
                        UpdateRow(row);
                        UpdateCheckedHeader();
                        UpdateSelectionButtonsEnabled();
                        break;
                    }
                }
                UpdateRow(row);
                UpdateCheckedHeader();
                UpdateSelectionButtonsEnabled();
            }

            _cts.Dispose();
            _cts = null;

            _detectButton.IsEnabled = true;
            _stopButton.IsEnabled = false;
            UpdateSelectionButtonsEnabled();
            _progressText.Text = string.Empty;

            if (stopped)
            {
                _summaryText.Text = LocalizationManager.T("DetectConfigs.Stopped");
            }
            else
            {
                var summary = string.Format(
                    LocalizationManager.T("DetectConfigs.DoneFormat"), ok, errors);
                if (errors > 0 && !continueOnError && lastResult != null)
                    summary += "  " + lastResult;
                _summaryText.Text = summary;
            }
        }

        private void OnClosingConfirm(object? sender, WindowClosingEventArgs e)
        {
            // Закрытие уже подтверждено ранее — пропускаем вопрос и даём окну закрыться.
            if (_closeConfirmed || !_rows.Any(r => r.IsChecked))
                return;

            // Вопрос уже показывается/запланирован — повторное закрытие отменяем молча.
            if (_closePromptOpen)
            {
                e.Cancel = true;
                return;
            }

            // Отменяем текущее закрытие и показываем подтверждение ОТЛОЖЕННО, вне события Closing.
            // Прямой вызов Confirm здесь крутил бы вложенный цикл PushFrame модального окна
            // реентерабельно, и после ответа «Нет» повторно инициировалось бы Closing того же
            // окна — вопрос показывался бы второй раз (issue #260).
            _closePromptOpen = true;
            e.Cancel = true;
            try
            {
                // Background-приоритет: откладываем вопрос до завершения текущего закрытия,
                // чтобы Confirm не выполнялся внутри обработчика Closing.
                Dispatcher.UIThread.InvokeAsync(AskCloseConfirm, DispatcherPriority.Background);
            }
            catch
            {
                _closePromptOpen = false;
            }
        }

        /// <summary>
        /// Показывает вопрос подтверждения закрытия вне события Closing (issue #260).
        /// При согласии закрывает окно один раз; при отказе окно остаётся открытым.
        /// </summary>
        private void AskCloseConfirm()
        {
            _closePromptOpen = false;
            if (_closeConfirmed || !_rows.Any(r => r.IsChecked))
                return;

            try
            {
                if (_dialogs.Confirm(
                    LocalizationManager.T("DetectConfigs.CloseConfirm"),
                    LocalizationManager.T("DetectConfigs.Title")))
                {
                    // Флаг до фактического закрытия, чтобы повторный Closing не спрашивал заново.
                    _closeConfirmed = true;
                    Close();
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Ошибка при подтверждении закрытия окна определения конфигураций", ex);
            }
        }

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var title = new TextBlock
            {
                Text = LocalizationManager.T("DetectConfigs.TitleHeader"),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            var subtitle = new TextBlock
            {
                Text = LocalizationManager.T("DetectConfigs.Subtitle"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            };
            BindSecondary(subtitle);
            Grid.SetRow(subtitle, 1);
            grid.Children.Add(subtitle);

            // Список баз
            var listBorder = new Border
            {
                Margin = new Thickness(0, 12, 0, 0),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };
            Themes.ThemeBrushes.Bind(listBorder, Border.BackgroundProperty, "CardBackgroundColorBrush");
            Themes.ThemeBrushes.Bind(listBorder, Border.BorderBrushProperty, "BorderColorBrush");

            var dock = new DockPanel { LastChildFill = true };

            // Панель кнопок выбора + действие при ошибке
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var checkAll = new Button { Content = LocalizationManager.T("DetectConfigs.Button.CheckAll") };
            checkAll.Styled(ControlThemes.SelectAllButton);
            ToolTip.SetTip(checkAll, LocalizationManager.T("DetectConfigs.Button.CheckAllTooltip"));
            checkAll.Click += (_, _) => OnCheckAll();

            var uncheckAll = new Button { Content = LocalizationManager.T("DetectConfigs.Button.UncheckAll") };
            uncheckAll.Styled(ControlThemes.SelectAllButton);
            ToolTip.SetTip(uncheckAll, LocalizationManager.T("DetectConfigs.Button.UncheckAllTooltip"));
            uncheckAll.Click += (_, _) => OnUncheckAll();

            var invert = new Button { Content = LocalizationManager.T("DetectConfigs.Button.Invert") };
            invert.Styled(ControlThemes.SelectAllButton);
            ToolTip.SetTip(invert, LocalizationManager.T("DetectConfigs.Button.InvertTooltip"));
            invert.Click += (_, _) => OnInvert();

            toolbar.Children.Add(checkAll);
            toolbar.Children.Add(uncheckAll);
            toolbar.Children.Add(invert);

            // Разделитель — на кисти границы темы (аудит UI, п. 2.9) вместо Brushes.Gray.
            var separator = new Border { Width = 1, Height = 18 };
            ThemeBrushes.Bind(separator, Border.BackgroundProperty, "BorderColorBrush");
            toolbar.Children.Add(separator);

            toolbar.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("DetectConfigs.ErrorMode"),
                VerticalAlignment = VerticalAlignment.Center
            });
            _errorModeCombo = new ComboBox
            {
                MinWidth = 180,
                VerticalAlignment = VerticalAlignment.Center
            };
            _errorModeCombo.Items.Add(LocalizationManager.T("DetectConfigs.ErrorMode.Stop"));
            _errorModeCombo.Items.Add(LocalizationManager.T("DetectConfigs.ErrorMode.Continue"));
            _errorModeCombo.SelectedIndex = 0;
            toolbar.Children.Add(_errorModeCombo);

            var toolbarBorder = new Border
            {
                Padding = new Thickness(8, 4),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = Brushes.Transparent,
                Child = toolbar
            };
            Themes.ThemeBrushes.Bind(toolbarBorder, Border.BorderBrushProperty, "BorderColorBrush");
            DockPanel.SetDock(toolbarBorder, Dock.Top);
            dock.Children.Add(toolbarBorder);

            // Шапка таблицы
            var headerGrid = BuildHeaderGrid();
            DockPanel.SetDock(headerGrid, Dock.Top);
            dock.Children.Add(headerGrid);

            var scroll = new ScrollViewer
            {
                Content = _rowsPanel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(4)
            };
            dock.Children.Add(scroll);

            listBorder.Child = dock;
            Grid.SetRow(listBorder, 2);
            grid.Children.Add(listBorder);

            // Нижняя панель
            var bottom = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            bottom.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var leftStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            _progressText.FontSize = 12;
            _progressText.FontWeight = FontWeight.SemiBold;
            _progressText.TextWrapping = TextWrapping.Wrap;
            leftStack.Children.Add(_progressText);

            _summaryText.FontSize = 12;
            _summaryText.TextWrapping = TextWrapping.Wrap;
            _summaryText.TextTrimming = TextTrimming.CharacterEllipsis;
            BindSecondary(_summaryText);
            leftStack.Children.Add(_summaryText);

            Grid.SetColumn(leftStack, 0);
            bottom.Children.Add(leftStack);

            _stopButton = new Button
            {
                Content = LocalizationManager.T("DetectConfigs.Button.Stop"),
                Width = 120,
                Height = 36,
                IsEnabled = false
            };
            ToolTip.SetTip(_stopButton, LocalizationManager.T("DetectConfigs.Button.StopTooltip"));
            // Остановка поиска — опасное действие: DangerButton (issue #26).
            _stopButton.Styled(ControlThemes.DangerButton);
            _stopButton.Click += (_, _) => OnStopClick();

            _detectButton = new Button
            {
                Content = LocalizationManager.T("DetectConfigs.Button.Detect"),
                Width = 180,
                Height = 36,
                IsDefault = true
            };
            _detectButton.Styled(ControlThemes.DialogConfirmButton);
            _detectButton.Click += (_, _) => OnDetectClick();

            var cancel = BuildCancelActionButton(140);
            var rightPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Children = { _stopButton, _detectButton, cancel }
            };
            Grid.SetColumn(rightPanel, 1);
            bottom.Children.Add(rightPanel);

            Grid.SetRow(bottom, 3);
            grid.Children.Add(bottom);

            return grid;
        }

        /// <summary>Строит закреплённую шапку таблицы.</summary>
        private Grid BuildHeaderGrid()
        {
            var grid = new Grid { Margin = new Thickness(8, 0, 8, 2) };
            ApplyColumns(grid);

            _checkedHeaderText.FontSize = 12;
            _checkedHeaderText.FontWeight = FontWeight.SemiBold;
            BindSecondary(_checkedHeaderText);
            Grid.SetColumn(_checkedHeaderText, 0);
            grid.Children.Add(_checkedHeaderText);

            var platformHeader = MakeHeaderText(LocalizationManager.T("DetectConfigs.Column.Platform"));
            BindSecondary(platformHeader);
            Grid.SetColumn(platformHeader, 1);
            grid.Children.Add(platformHeader);

            var nameHeader = MakeHeaderText(LocalizationManager.T("DetectConfigs.Column.Name"));
            BindSecondary(nameHeader);
            Grid.SetColumn(nameHeader, 2);
            grid.Children.Add(nameHeader);

            var cfgHeader = MakeHeaderText(LocalizationManager.T("DetectConfigs.Column.CurrentConfig"));
            BindSecondary(cfgHeader);
            Grid.SetColumn(cfgHeader, 3);
            grid.Children.Add(cfgHeader);

            var verHeader = MakeHeaderText(LocalizationManager.T("DetectConfigs.Column.Version"));
            BindSecondary(verHeader);
            Grid.SetColumn(verHeader, 4);
            grid.Children.Add(verHeader);

            var credHeader = MakeHeaderText(LocalizationManager.T("DetectConfigs.Column.Credentials"));
            BindSecondary(credHeader);
            Grid.SetColumn(credHeader, 5);
            grid.Children.Add(credHeader);

            var propHeader = MakeHeaderText(LocalizationManager.T("DetectConfigs.Column.Properties"));
            BindSecondary(propHeader);
            Grid.SetColumn(propHeader, 6);
            grid.Children.Add(propHeader);

            return grid;
        }

        private void ApplyColumns(Grid grid)
        {
            grid.ColumnDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));              // 0 — флажок
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));              // 1 — платформа
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star))); // 2 — имя
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(3, GridUnitType.Star))); // 3 — конфигурация
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star))); // 4 — версия
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));              // 5 — логин/пароль
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));              // 6 — свойства
        }

        /// <summary>Строит строку базы и добавляет её в общий список.</summary>
        private void AddRow(DetectConfigRowViewModel row)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            ApplyColumns(grid);

            var check = new CheckBox
            {
                IsChecked = row.IsChecked,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };
            check.Styled(ControlThemes.CacheCleanCheckBox);
            check.IsCheckedChanged += (_, _) =>
            {
                row.IsChecked = check.IsChecked == true;
                UpdateCheckedHeader();
            };
            Grid.SetColumn(check, 0);
            grid.Children.Add(check);

            var platform = new TextBlock
            {
                Text = row.PlatformVersion,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(platform, 1);
            grid.Children.Add(platform);

            var name = new TextBlock
            {
                Text = row.Name,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(name, 2);
            grid.Children.Add(name);

            var config = new TextBlock
            {
                Text = row.ConfigurationName,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(config, 3);
            grid.Children.Add(config);

            var version = new TextBlock
            {
                Text = row.ConfigurationVersion,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(version, 4);
            grid.Children.Add(version);

            var cred = new TextBlock
            {
                Text = "●",
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };
            ApplyCredentialStyle(cred, row.HasCredentials);
            Grid.SetColumn(cred, 5);
            grid.Children.Add(cred);

            var props = new Button
            {
                Content = "⚙",
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0),
                Padding = new Thickness(6, 2)
            };
            props.Styled(ControlThemes.SelectAllButton);
            ToolTip.SetTip(props, LocalizationManager.T("DetectConfigs.Button.PropertiesTooltip"));
            props.Click += (_, _) => OnPropertiesClick(row);
            Grid.SetColumn(props, 6);
            grid.Children.Add(props);

            _checks[row] = check;
            _platformTexts[row] = platform;
            _configTexts[row] = config;
            _versionTexts[row] = version;
            _credTexts[row] = cred;
            _rowsPanel.Children.Add(grid);
        }

        /// <summary>Окрашивает индикатор логина/пароля: зелёный — заполнены, красный — нет.</summary>
        private static void ApplyCredentialStyle(TextBlock block, bool filled)
        {
            if (filled)
            {
                block.Foreground = Brush.Parse("#16A34A");
                ToolTip.SetTip(block, LocalizationManager.T("DetectConfigs.CredentialsFilledTooltip"));
            }
            else
            {
                block.Foreground = Brush.Parse("#EF4444");
                ToolTip.SetTip(block, LocalizationManager.T("DetectConfigs.CredentialsMissingTooltip"));
            }
        }

        /// <summary>Обновляет текстовые ячейки строки из её модели.</summary>
        private void UpdateRow(DetectConfigRowViewModel row)
        {
            if (_platformTexts.TryGetValue(row, out var platform))
                platform.Text = row.PlatformVersion;
            if (_configTexts.TryGetValue(row, out var config))
                config.Text = row.ConfigurationName;
            if (_versionTexts.TryGetValue(row, out var version))
                version.Text = row.ConfigurationVersion;
            if (_credTexts.TryGetValue(row, out var cred))
                ApplyCredentialStyle(cred, row.HasCredentials);
            if (_checks.TryGetValue(row, out var check))
                check.IsChecked = row.IsChecked;
        }
    }
}
#endif