#if LINUX
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// Окно «Актуальные релизы» (горячая клавиша Alt+F9): список отслеживаемых типовых конфигураций
    /// 1С, пакетная проверка наличия новых релизов и загрузка дистрибутива. Сетевые операции
    /// выполняются в фоновых потоках (Task.Run); результаты применяются к строкам в UI-потоке.
    /// Avalonia/Linux-версия WPF-окна <see cref="ActualReleasesWindow"/>.
    /// </summary>
    public sealed class ActualReleasesWindow : ModalWindowBase
    {
        private readonly Services.IOneCUpdatesService _updates = AppServices.GetRequiredService<Services.IOneCUpdatesService>();
        private readonly Services.ICustomConfigTypesStore _store = AppServices.GetRequiredService<Services.ICustomConfigTypesStore>();
        private readonly Services.IAppLogger _logger = AppServices.GetRequiredService<Services.IAppLogger>();
        private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

        private readonly ActualReleasesViewModel _viewModel;

        /// <summary>Все информационные базы списка (для фильтра «Только в базах», issue #375).</summary>
        private readonly IReadOnlyList<Infobase> _bases;

        /// <summary>Фильтр «Только в базах» (issue #375): по умолчанию включён.</summary>
        private bool _onlyInBases = true;

        private readonly StackPanel _rowsPanel = new();
        private readonly Dictionary<ActualReleaseRowViewModel, TextBlock> _latestTexts = new();
        private readonly Dictionary<ActualReleaseRowViewModel, TextBlock> _statusTexts = new();
        private readonly Dictionary<ActualReleaseRowViewModel, TextBlock> _errorTexts = new();
        private readonly Dictionary<ActualReleaseRowViewModel, StackPanel> _progressPanels = new();
        private readonly Dictionary<ActualReleaseRowViewModel, ProgressBar> _progressBars = new();
        private readonly Dictionary<ActualReleaseRowViewModel, TextBlock> _progressTexts = new();
        private readonly Dictionary<ActualReleaseRowViewModel, Button> _checkButtons = new();
        private readonly Dictionary<ActualReleaseRowViewModel, Button> _downloadButtons = new();

        private Button _checkAllButton = new();

        /// <summary>
        /// Открывает окно «Актуальные релизы».
        /// </summary>
        public ActualReleasesWindow()
        {
            Title = LocalizationManager.T("Updates.ActualReleasesTitle");
            Width = 900;
            Height = 600;
            MinWidth = 720;
            MinHeight = 440;
            FontSize = 13;
            CanResize = true;

            _viewModel = new ActualReleasesViewModel(OnCheckAll);
            _bases = LoadBases();
            BuildRows();
            foreach (var row in _viewModel.Rows)
                AddRow(row);
            Content = BuildRoot();
            RefreshCommands();
        }

        /// <summary>
        /// Показывает окно модально (синхронно). Открытая публичная обёртка над
        /// <see cref="ModalWindowBase.ShowDialogSync(Window?)"/>, чтобы диалог можно было
        /// вызывать из ViewModel (не наследника окна).
        /// </summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private static string T(string key) => LocalizationManager.T(key);

        /// <summary>Формирует строки из отслеживаемых конфигураций (предопределённых и пользовательских).
        /// Общий список типовых = встроенные + пользовательские из файла custom_config_types.json
        /// (единый загрузчик <see cref="Services.ICustomConfigTypesStore"/> — issue #321).</summary>
        private void BuildRows()
        {
            try
            {
                var all = _store.LoadAll();
                // Фильтр «Только в базах» (issue #375): при включённой галочке оставляем
                // конфигурации, присутствующие в списке баз (привязка или эвристика), а также
                // редакции, встречающиеся в версиях баз (если версии известны).
                var presentCodes = _onlyInBases
                    ? ActualReleasesBaseFilter.CollectPresentConfigCodes(all, _bases)
                    : null;
                var presentReds = _onlyInBases
                    ? ActualReleasesBaseFilter.CollectPresentEditionReds(all, _bases)
                    : null;
                foreach (var config in all)
                {
                    if (!config.IsTracked)
                        continue;
                    if (presentCodes is not null && !ActualReleasesBaseFilter.IsPresent(config, presentCodes))
                        continue;

                    var editions = config.Editions;
                    if (editions.Count == 0)
                    {
                        _viewModel.Rows.Add(new ActualReleaseRowViewModel(config, null, OnCheckRow, OnDownloadRow));
                    }
                    else
                    {
                        var presentRedsForConfig = presentReds is not null
                            && presentReds.TryGetValue(config.Code, out var reds)
                                ? reds
                                : null;
                        foreach (var edition in editions)
                        {
                            if (!ActualReleasesBaseFilter.IsEditionPresent(edition, presentRedsForConfig))
                                continue;
                            _viewModel.Rows.Add(new ActualReleaseRowViewModel(config, edition, OnCheckRow, OnDownloadRow));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Ошибка построения списка отслеживаемых конфигураций", ex);
            }
        }

        /// <summary>Загружает список баз для фильтра «Только в базах» (issue #375).</summary>
        private IReadOnlyList<Infobase> LoadBases()
        {
            try
            {
                return AppServices.GetRequiredService<IInfobaseRepository>().Load();
            }
            catch (Exception ex)
            {
                // Контейнер не настроен/ошибка чтения — фильтр работает по пустому списку.
                _logger.Warn("Не удалось загрузить список баз для фильтра «Только в базах»: " + ex.Message);
                return Array.Empty<Infobase>();
            }
        }

        /// <summary>Пересобирает строки списка с учётом текущего фильтра (issue #375).</summary>
        private void RebuildRows()
        {
            _rowsPanel.Children.Clear();
            _latestTexts.Clear();
            _statusTexts.Clear();
            _errorTexts.Clear();
            _progressPanels.Clear();
            _progressBars.Clear();
            _progressTexts.Clear();
            _checkButtons.Clear();
            _downloadButtons.Clear();
            _viewModel.Rows.Clear();
            BuildRows();
            foreach (var row in _viewModel.Rows)
                AddRow(row);
            RefreshCommands();
        }

        /// <summary>Выполняет точечную проверку одной строки.</summary>
        private async void OnCheckRow(ActualReleaseRowViewModel row)
        {
            if (row.IsChecking || row.IsDownloading)
                return;

            row.IsChecking = true;
            row.Error = string.Empty;
            try
            {
                var url = _updates.BuildUpdateUrl(row.Config, row.Edition, null);
                // issue #371: «семейство» версии — редакция конфигурации (например «3.0» для
                // Розницы): посторонние токены («2.2.10») с другим первым сегментом отсекаются;
                // учётная запись из настройки конфигурации передаётся сервису (пусто — «Основная»).
                var result = await Task.Run(() =>
                    _updates.CheckForUpdatesAsync(row.Name, string.Empty, url, CancellationToken.None,
                        row.Edition?.Red, row.Config?.AccountId));
                row.ApplyResult(result);
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка проверки конфигурации «{row.Name}»", ex);
                row.Status = ConfigUpdateStatus.Failed;
                row.Error = T("Updates.NetworkError");
            }
            finally
            {
                row.IsChecking = false;
                RefreshCommands();
            }
        }

        /// <summary>Выполняет пакетную проверку всех отслеживаемых конфигураций последовательно.</summary>
        private async void OnCheckAll()
        {
            if (_viewModel.IsCheckingAll)
                return;

            _viewModel.IsCheckingAll = true;
            RefreshCommands();
            try
            {
                foreach (var row in _viewModel.Rows.ToList())
                {
                    if (row.IsDownloading)
                        continue;

                    row.IsChecking = true;
                    row.Error = string.Empty;
                    try
                    {
                        var url = _updates.BuildUpdateUrl(row.Config, row.Edition, null);
                        // issue #371: «семейство» версии — редакция конфигурации (см. OnCheckRow).
                        var result = await Task.Run(() =>
                            _updates.CheckForUpdatesAsync(row.Name, string.Empty, url, CancellationToken.None,
                                row.Edition?.Red, row.Config?.AccountId));
                        row.ApplyResult(result);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Ошибка пакетной проверки конфигурации «{row.Name}»", ex);
                        row.Status = ConfigUpdateStatus.Failed;
                        row.Error = T("Updates.NetworkError");
                    }
                    finally
                    {
                        row.IsChecking = false;
                    }
                }
            }
            finally
            {
                _viewModel.IsCheckingAll = false;
                RefreshCommands();
            }
        }

        /// <summary>Загружает дистрибутив обновления по ссылке каталога релизов строки.</summary>
        private async void OnDownloadRow(ActualReleaseRowViewModel row)
        {
            if (!row.CanDownload || string.IsNullOrWhiteSpace(row.Url))
                return;

            var targetPath = _dialogs.SaveFileDialog(
                T("Updates.Download"),
                BuildDownloadFileName(row),
                "Архивы (*.zip;*.rar;*.7z)|*.zip;*.rar;*.7z|Все файлы (*.*)|*.*",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

            if (string.IsNullOrWhiteSpace(targetPath))
                return;

            row.IsDownloading = true;
            row.Progress = 0;
            UpdateRowProgress(row);
            try
            {
                var progress = new Progress<double>(v =>
                    Dispatcher.UIThread.Post(() => row.Progress = Math.Clamp(v, 0, 1)));
                var savedPath = await Task.Run(() =>
                    _updates.DownloadUpdateAsync(row.Url, targetPath, progress, accountId: row.Config?.AccountId));

                if (string.IsNullOrWhiteSpace(savedPath))
                {
                    _dialogs.ShowWarning(T("Updates.NetworkError"), T("Updates.ActualReleasesTitle"));
                }
                else
                {
                    row.Progress = 1;
                    UpdateRowProgress(row);
                    _dialogs.ShowInfo(string.Format(T("Updates.Loaded"), savedPath), T("Updates.ActualReleasesTitle"));
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка загрузки обновления «{row.Name}»", ex);
                _dialogs.ShowError(T("Updates.NetworkError"), T("Updates.ActualReleasesTitle"));
            }
            finally
            {
                row.IsDownloading = false;
                UpdateRowProgress(row);
                RefreshCommands();
            }
        }

        /// <summary>Обновляет доступность команд и состояние кнопок после изменения состава/состояния.</summary>
        private void RefreshCommands()
        {
            _viewModel.RefreshCommands();
            _checkAllButton.IsEnabled = _viewModel.CanCheckAll;
            foreach (var row in _viewModel.Rows)
            {
                if (_checkButtons.TryGetValue(row, out var check))
                    check.IsEnabled = !row.IsChecking && !row.IsDownloading;
                if (_downloadButtons.TryGetValue(row, out var download))
                    download.IsEnabled = row.CanDownload;
            }
        }

        private static string BuildDownloadFileName(ActualReleaseRowViewModel row)
        {
            var baseName = SanitizeFileName(row.Name);
            var latest = string.IsNullOrWhiteSpace(row.LatestVersion) ? "update" : row.LatestVersion;
            return $"{baseName}_{latest}.zip";
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Concat(name.Select(ch => invalid.Contains(ch) ? '_' : ch));
        }

        /// <summary>Обновляет текстовые ячейки и стиль статуса строки из модели.</summary>
        private void UpdateRow(ActualReleaseRowViewModel row)
        {
            if (_latestTexts.TryGetValue(row, out var latest))
                latest.Text = string.IsNullOrWhiteSpace(row.LatestVersion) ? "—" : row.LatestVersion;
            if (_statusTexts.TryGetValue(row, out var status))
            {
                status.Text = StatusTextLocalized(row.Status);
                ApplyStatusStyle(status, row);
            }
            if (_errorTexts.TryGetValue(row, out var error))
                error.Text = row.Status == ConfigUpdateStatus.Failed ? row.Error : string.Empty;
        }

        /// <summary>Обновляет индикатор прогресса строки.</summary>
        private void UpdateRowProgress(ActualReleaseRowViewModel row)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_progressPanels.TryGetValue(row, out var panel))
                    panel.IsVisible = row.IsDownloading;
                if (_progressBars.TryGetValue(row, out var bar))
                    bar.Value = Math.Clamp(row.Progress, 0, 1);
                if (_progressTexts.TryGetValue(row, out var text))
                    text.Text = $"{Math.Round(row.Progress * 100, 0):0} %";
            });
        }

        private void OnRowPropertyChanged(ActualReleaseRowViewModel row, string propertyName)
        {
            if (propertyName == nameof(row.Progress))
            {
                UpdateRowProgress(row);
            }
            else if (propertyName == nameof(row.LatestVersion)
                     || propertyName == nameof(row.Status)
                     || propertyName == nameof(row.Error))
            {
                UpdateRow(row);
            }
            else if (propertyName == nameof(row.IsChecking)
                     || propertyName == nameof(row.IsDownloading)
                     || propertyName == nameof(row.CanDownload))
            {
                RefreshCommands();
            }
        }

        /// <summary>Окрашивает текст статуса: акцент при новом релизе, красный при ошибке.</summary>
        private static void ApplyStatusStyle(TextBlock block, ActualReleaseRowViewModel row)
        {
            if (row.HasNewer)
            {
                block.Foreground = TryBrush("SuccessBrush") ?? new SolidColorBrush(Color.Parse("#16A34A"));
            }
            else if (row.Status == ConfigUpdateStatus.Failed)
            {
                block.Foreground = TryBrush("ErrorBrush") ?? new SolidColorBrush(Color.Parse("#DC2626"));
            }
            else
            {
                Themes.ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "TextPrimaryBrush");
            }
        }

        private static IBrush? TryBrush(string key)
        {
            if (Application.Current is not { } app || !app.TryFindResource(key, out var found))
                return null;
            return found as IBrush;
        }

        private static string StatusTextLocalized(ConfigUpdateStatus status)
        {
            return status switch
            {
                ConfigUpdateStatus.UpToDate => LocalizationManager.T("Updates.UpToDate"),
                ConfigUpdateStatus.NewerAvailable => LocalizationManager.T("Updates.NewerAvailable"),
                ConfigUpdateStatus.Unavailable => LocalizationManager.T("Updates.Unavailable"),
                ConfigUpdateStatus.Failed => LocalizationManager.T("Updates.CheckFailed"),
                _ => LocalizationManager.T("Updates.Status"),
            };
        }

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var title = new TextBlock
            {
                Text = T("Updates.ActualReleasesTitle"),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            // Карточка списка.
            var listBorder = new Border
            {
                Margin = new Thickness(0, 12, 0, 0),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };
            Themes.ThemeBrushes.Bind(listBorder, Border.BackgroundProperty, "CardBackgroundColorBrush");
            Themes.ThemeBrushes.Bind(listBorder, Border.BorderBrushProperty, "BorderColorBrush");

            var dock = new DockPanel { LastChildFill = true };

            // Панель инструментов.
            _checkAllButton = new Button { Content = T("Updates.BatchCheck"), Height = 32 };
            _checkAllButton.Styled(ControlThemes.ModernButton);
            _checkAllButton.Click += (_, _) => OnCheckAll();

            var toolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(8, 8, 8, 4)
            };
            toolbar.Children.Add(_checkAllButton);

            // Фильтр «Только в базах» (issue #375): по умолчанию включён. Подсказка
            // объясняет правила поиска конфигурации и сопоставления редакций.
            var onlyInBasesCheck = new CheckBox
            {
                Content = T("Updates.OnlyInBases"),
                IsChecked = _onlyInBases,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(onlyInBasesCheck, T("Updates.OnlyInBasesTooltip"));
            onlyInBasesCheck.Styled(ControlThemes.CacheCleanCheckBox);
            onlyInBasesCheck.Click += (_, _) =>
            {
                _onlyInBases = onlyInBasesCheck.IsChecked == true;
                RebuildRows();
            };
            toolbar.Children.Add(onlyInBasesCheck);

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

            var header = BuildHeaderGrid();
            DockPanel.SetDock(header, Dock.Top);
            dock.Children.Add(header);

            _rowsPanel.Margin = new Thickness(4, 2);
            var scroll = new ScrollViewer
            {
                Content = _rowsPanel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(4)
            };
            dock.Children.Add(scroll);

            listBorder.Child = dock;
            Grid.SetRow(listBorder, 1);
            grid.Children.Add(listBorder);

            // Нижняя панель: закрыть.
            var bottom = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            bottom.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var close = BuildCancelActionButton(140);
            close.Click += (_, _) => Close();
            Grid.SetColumn(close, 1);
            bottom.Children.Add(close);
            Grid.SetRow(bottom, 2);
            grid.Children.Add(bottom);

            return grid;
        }

        /// <summary>Заголовок таблицы.</summary>
        private Grid BuildHeaderGrid()
        {
            var grid = new Grid { Margin = new Thickness(8, 0, 8, 2) };
            ApplyColumns(grid);
            grid.Children.Add(MakeHeaderText(T("Updates.Name"), 0));
            grid.Children.Add(MakeHeaderText(T("Updates.LatestVersion"), 1));
            grid.Children.Add(MakeHeaderText(T("Updates.Status"), 2));
            return grid;
        }

        private static TextBlock MakeHeaderText(string text, int column)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Themes.ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetColumn(block, column);
            return block;
        }

        private void ApplyColumns(Grid grid)
        {
            grid.ColumnDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(3, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        }

        /// <summary>Строит визуальную строку для одной конфигурации.</summary>
        private void AddRow(ActualReleaseRowViewModel row)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };

            // Подсветка только нечётных строк (1-я, 3-я, 5-я…): индекс строки начинается с 0,
            // поэтому нечётной позиции соответствует чётный индекс. Hover подсвечивает текущую строку
            // (паритет с WPF RowStyle, см. ActualReleasesWindow.xaml — issue #264).
            var rowIndex = _rowsPanel.Children.Count;
            var oddRow = rowIndex % 2 == 0;
            // «Зебра» строк берёт тематическую кисть ZebraRowBrush (issue #26):
            // в тёмной теме это приглушённая #334155, а не светлая/белая полоса.
            var bandBrush = oddRow ? (TryBrush("ZebraRowBrush") ?? TryBrush("ItemHoverBrush") ?? Brushes.Transparent) : Brushes.Transparent;
            var hoverBrush = TryBrush("ItemSelectedBrush") ?? Brushes.Transparent;
            cell.Background = bandBrush;
            cell.PointerEntered += (_, _) => cell.Background = hoverBrush;
            cell.PointerExited += (_, _) => cell.Background = bandBrush;

            // Верхняя строка: имя, версия, статус, кнопки.
            var top = new Grid { Margin = new Thickness(8, 0, 8, 0) };
            ApplyColumns(top);

            var name = new TextBlock
            {
                Text = row.Name,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(name, 0);
            top.Children.Add(name);

            var latest = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(row.LatestVersion) ? "—" : row.LatestVersion,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(latest, 1);
            top.Children.Add(latest);

            var status = new TextBlock
            {
                Text = StatusTextLocalized(row.Status),
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            ApplyStatusStyle(status, row);
            Grid.SetColumn(status, 2);
            top.Children.Add(status);

            var check = new Button
            {
                Content = T("Updates.Check"),
                Padding = new Thickness(10, 4),
                Margin = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            check.Styled(ControlThemes.SelectAllButton);
            check.Click += (_, _) => OnCheckRow(row);
            Grid.SetColumn(check, 3);
            top.Children.Add(check);

            var download = new Button
            {
                Content = T("Updates.Download"),
                Padding = new Thickness(10, 4),
                Margin = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = row.CanDownload
            };
            download.Styled(ControlThemes.SelectAllButton);
            download.Click += (_, _) => OnDownloadRow(row);
            Grid.SetColumn(download, 4);
            top.Children.Add(download);

            cell.Children.Add(top);

            // Нижняя строка: индикатор прогресса + ошибка.
            var progressBar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 6 };
            var progressText = new TextBlock { FontSize = 12 };
            Themes.ThemeBrushes.Bind(progressText, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            var progressPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false, Margin = new Thickness(16, 2, 8, 0) };
            progressPanel.Children.Add(progressBar);
            progressPanel.Children.Add(progressText);
            cell.Children.Add(progressPanel);

            var error = new TextBlock
            {
                Text = row.Status == ConfigUpdateStatus.Failed ? row.Error : string.Empty,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(16, 0, 8, 0)
            };
            error.Foreground = TryBrush("ErrorBrush") ?? new SolidColorBrush(Color.Parse("#DC2626"));
            cell.Children.Add(error);

            _latestTexts[row] = latest;
            _statusTexts[row] = status;
            _errorTexts[row] = error;
            _progressPanels[row] = progressPanel;
            _progressBars[row] = progressBar;
            _progressTexts[row] = progressText;
            _checkButtons[row] = check;
            _downloadButtons[row] = download;

            row.PropertyChanged += (_, e) => OnRowPropertyChanged(row, e.PropertyName ?? string.Empty);

            _rowsPanel.Children.Add(cell);
        }
    }
}
#endif