#if LINUX
using System;
using System.Collections.Generic;
using System.IO;
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
    /// Диалог «Поиск потерянных и забытых баз 1С 8» (issue #247), Avalonia/Linux-версия
    /// WPF-окна <see cref="FindLostBasesWindow"/>. Выбор дисков/корней поиска, фоновое
    /// рекурсивное сканирование в поисках файлов <c>1Cv8.1CD</c> с кнопкой «Прекратить»,
    /// таблица найденных баз и добавление отсутствующих в списке приложения баз.
    /// </summary>
    public sealed class FindLostBasesWindow : ModalWindowBase
    {
        private readonly IReadOnlyList<Infobase> _infobases;
        private readonly Action<Infobase> _addBase;
        private readonly HashSet<string> _appFileDirs = new(FoundBaseRowViewModel.DirComparer);
        private readonly HashSet<string> _v8iFileDirs = new(FoundBaseRowViewModel.DirComparer);
        private readonly List<FoundBaseRowViewModel> _rows = new();
        private readonly List<CheckBox> _driveChecks = new();
        private readonly IDialogService _dialogs =
            AppServices.GetRequiredService<IDialogService>();
        private readonly Dictionary<FoundBaseRowViewModel, CheckBox> _checks = new();
        private readonly Dictionary<FoundBaseRowViewModel, TextBlock> _inAppTexts = new();
        private readonly Dictionary<FoundBaseRowViewModel, TextBlock> _inV8iTexts = new();
        private readonly Services.IAppLogger _logger =
            AppServices.GetRequiredService<Services.IAppLogger>();

        private readonly StackPanel _rowsPanel = new() { Margin = new Thickness(4, 2) };
        private readonly TextBlock _checkedHeaderText = new();
        private readonly TextBlock _progressText = new();
        private readonly TextBlock _summaryText = new();
        private CheckBox _addToAppCheck = new();
        private CheckBox _addToV8iCheck = new();
        private Button _searchButton = new();
        private Button _stopButton = new();
        private Button _addButton = new();
        private TextBox _folderPathBox = new();

        private CancellationTokenSource? _cts;

        /// <summary>Признак того, что хотя бы одна база была добавлена (для персиста в настройках).</summary>
        public bool DataChanged { get; private set; }

        /// <param name="infobases">Список баз приложения (для признака «В приложении»).</param>
        /// <param name="addBase">Обратный вызов добавления новой базы в список приложения.</param>
        public FindLostBasesWindow(IReadOnlyList<Infobase> infobases, Action<Infobase> addBase)
        {
            Title = LocalizationManager.T("FindLostBases.Title");
            Width = 920;
            Height = 620;
            MinWidth = 720;
            MinHeight = 480;
            FontSize = 13;
            CanResize = true;
            _infobases = infobases;
            _addBase = addBase;

            BuildAppFileDirs();
            BuildV8iFileDirs();

            Content = BuildRoot();
            PopulateDriveChecks();
            UpdateCheckedHeader();
            UpdateAddEnabled();
            Closing += (_, _) => _cts?.Cancel();
        }

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

        private void BuildAppFileDirs()
        {
            foreach (var ib in _infobases)
            {
                var dir = InfobaseMaintenanceService.GetFileBaseDirectory(ib);
                if (!string.IsNullOrWhiteSpace(dir))
                    _appFileDirs.Add(FoundBaseRowViewModel.NormalizeDir(dir));
            }
        }

        private void BuildV8iFileDirs()
        {
            var filePath = IbasesV8iImporter.FindDefaultPath();
            if (filePath is null)
                return;

            try
            {
                foreach (var ib in IbasesV8iImporter.ReadInfobases(filePath))
                {
                    if (ib.Connection.Type == ConnectionType.File
                        && !string.IsNullOrWhiteSpace(ib.Connection.FilePath))
                        _v8iFileDirs.Add(FoundBaseRowViewModel.NormalizeDir(ib.Connection.FilePath));
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Не удалось прочитать записи ibases.v8i для признака «В ibases.v8i»", ex);
            }
        }

        private void PopulateDriveChecks()
        {
            var roots = InfobaseDiskScanner.EnumerateSearchRoots();
            if (roots.Count == 0)
            {
                _searchButton.IsEnabled = false;
                return;
            }

            foreach (var root in roots)
            {
                var cb = new CheckBox
                {
                    Content = root,
                    Tag = root,
                    IsChecked = true,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 4, 6, 4)
                };
                _driveChecks.Add(cb);
                DrivesPanel().Children.Add(cb);
            }
        }

        /// <summary>
        /// Панель ввода произвольного каталога как дополнительного корня поиска:
        /// поле пути + кнопки «Обзор»/«Добавить». Enter в поле тоже добавляет каталог.
        /// </summary>
        private Control BuildFolderBar()
        {
            _folderPathBox = new TextBox
            {
                Watermark = LocalizationManager.T("FindLostBases.FolderPlaceholder"),
                VerticalContentAlignment = VerticalAlignment.Center,
                Height = 30
            };
            _folderPathBox.KeyDown += (_, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Enter)
                    OnAddFolder();
            };

            var browse = new Button
            {
                Content = LocalizationManager.T("FindLostBases.Button.Browse"),
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0)
            };
            browse.Styled(ControlThemes.SelectAllButton);
            browse.Click += (_, _) => OnBrowseFolder();

            var add = new Button
            {
                Content = LocalizationManager.T("FindLostBases.Button.AddFolder"),
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0)
            };
            add.Styled(ControlThemes.SelectAllButton);
            add.Click += (_, _) => OnAddFolder();

            var bar = new DockPanel { LastChildFill = true, Margin = new Thickness(8, 4) };
            DockPanel.SetDock(add, Dock.Right);
            bar.Children.Add(add);
            DockPanel.SetDock(browse, Dock.Right);
            bar.Children.Add(browse);
            bar.Children.Add(_folderPathBox);
            return bar;
        }

        /// <summary>Открывает диалог выбора каталога и подставляет путь в поле ввода.</summary>
        private void OnBrowseFolder()
        {
            var current = _folderPathBox.Text?.Trim() ?? string.Empty;
            var picked = _dialogs.OpenFolderDialog(
                LocalizationManager.T("FindLostBases.Button.Browse"),
                current.Length > 0 && Directory.Exists(current) ? current : null);
            if (!string.IsNullOrWhiteSpace(picked))
                _folderPathBox.Text = picked.Trim();
        }

        /// <summary>
        /// Добавляет введённый каталог как дополнительный корень поиска: проверяет его
        /// существование и отсутствие дубликата среди уже добавленных корней, затем
        /// добавляет отмеченный флажок в панель дисков (подхватывается GetSelectedRoots()).
        /// </summary>
        private void OnAddFolder()
        {
            var path = _folderPathBox.Text?.Trim() ?? string.Empty;
            if (path.Length == 0)
                return;

            if (!Directory.Exists(path))
            {
                _summaryText.Text = string.Format(
                    LocalizationManager.T("FindLostBases.FolderNotExists"), path);
                return;
            }

            var normalized = FoundBaseRowViewModel.NormalizeDir(path);
            var comparer = FoundBaseRowViewModel.DirComparer;
            var isDuplicate = _driveChecks.Any(c =>
                c.Tag is string s && comparer.Equals(normalized, FoundBaseRowViewModel.NormalizeDir(s)));
            if (isDuplicate)
            {
                _summaryText.Text = LocalizationManager.T("FindLostBases.FolderDuplicate");
                return;
            }

            var cb = new CheckBox
            {
                Content = path,
                Tag = path,
                IsChecked = true,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 4, 6, 4)
            };
            _driveChecks.Add(cb);
            DrivesPanel().Children.Add(cb);
            _folderPathBox.Clear();
            _summaryText.Text = string.Empty;
        }

        /// <summary>WrapPanel корней поиска — хранится в поле для заполнения флажками.</summary>
        private readonly WrapPanel _drivesWrap = new() { Margin = new Thickness(8, 6) };

        private WrapPanel DrivesPanel() => _drivesWrap;

        private List<string> GetSelectedRoots()
            => _driveChecks.Where(c => c.IsChecked == true && c.Tag is string s && !string.IsNullOrWhiteSpace(s))
                .Select(c => (string)c.Tag!).ToList();

        private void UpdateCheckedHeader()
        {
            _checkedHeaderText.Text = string.Format(
                LocalizationManager.T("FindLostBases.Column.CheckedHeaderFormat"),
                _rows.Count(r => r.IsChecked));
        }

        private void UpdateAddEnabled()
        {
            var anyDestination = _addToAppCheck.IsChecked == true || _addToV8iCheck.IsChecked == true;
            _addButton.IsEnabled = anyDestination && _rows.Any(r => r.IsChecked && !r.InApp);
        }

        private void SetDriveChecksEnabled(bool enabled)
        {
            foreach (var cb in _driveChecks)
                cb.IsEnabled = enabled;
        }

        private void OnStopClick() => _cts?.Cancel();

        private void OnScanProgress(int found, int dirs)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _progressText.Text = string.Format(
                    LocalizationManager.T("FindLostBases.ProgressFormat"), found, dirs);
            });
        }

        /// <summary>
        /// Переносит накопленные в буфере найденные базы в таблицу (на UI-потоке).
        /// Вызывается таймером пакетного обновления (~100 мс) и по завершении сканирования,
        /// чтобы строки появлялись инкрементально и не терялись при отмене.
        /// </summary>
        private void FlushFoundRows(object gate, List<FoundBaseRowViewModel> pending)
        {
            List<FoundBaseRowViewModel>? batch = null;
            lock (gate)
            {
                if (pending.Count > 0)
                {
                    batch = new List<FoundBaseRowViewModel>(pending);
                    pending.Clear();
                }
            }

            if (batch is null || batch.Count == 0)
                return;

            foreach (var row in batch)
            {
                _rows.Add(row);
                AddRow(row);
            }

            _summaryText.Text = string.Format(
                LocalizationManager.T("FindLostBases.FoundFormat"), _rows.Count);
            UpdateCheckedHeader();
            UpdateAddEnabled();
        }

        private async void OnSearchClick()
        {
            var roots = GetSelectedRoots();
            if (roots.Count == 0)
            {
                _summaryText.Text = LocalizationManager.T("FindLostBases.NoneSelected");
                return;
            }

            _searchButton.IsEnabled = false;
            _stopButton.IsEnabled = true;
            SetDriveChecksEnabled(false);
            _rows.Clear();
            _checks.Clear();
            _inAppTexts.Clear();
            _inV8iTexts.Clear();
            _rowsPanel.Children.Clear();
            _progressText.Text = string.Empty;
            _summaryText.Text = string.Empty;
            UpdateCheckedHeader();
            UpdateAddEnabled();

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            // Буфер найденных строк + таймер пакетного обновления таблицы (~100 мс):
            // строки появляются инкрементально по мере сканирования, а не разом в конце.
            var gate = new object();
            var pending = new List<FoundBaseRowViewModel>();
            var flushTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            flushTimer.Tick += (_, _) => FlushFoundRows(gate, pending);
            flushTimer.Start();

            try
            {
                await Task.Run(() => InfobaseDiskScanner.Scan(roots, token, OnScanProgress,
                    found =>
                    {
                        var row = new FoundBaseRowViewModel(found)
                        {
                            InApp = _appFileDirs.Contains(FoundBaseRowViewModel.NormalizeDir(found.DirectoryPath)),
                            InIbasesV8i = _v8iFileDirs.Contains(FoundBaseRowViewModel.NormalizeDir(found.DirectoryPath))
                        };
                        lock (gate)
                            pending.Add(row);
                    }), token);

                FlushFoundRows(gate, pending);
                _summaryText.Text = string.Format(
                    LocalizationManager.T("FindLostBases.FoundFormat"), _rows.Count);
                UpdateCheckedHeader();
                UpdateAddEnabled();
            }
            catch (OperationCanceledException)
            {
                FlushFoundRows(gate, pending);
                _summaryText.Text = LocalizationManager.T("FindLostBases.Stopped");
                UpdateCheckedHeader();
                UpdateAddEnabled();
            }
            catch (Exception ex)
            {
                FlushFoundRows(gate, pending);
                _logger.Error("Ошибка сканирования дисков в поисках баз 1С", ex);
                _summaryText.Text = string.Format(LocalizationManager.T("FindLostBases.ErrorFormat"), ex.Message);
            }
            finally
            {
                flushTimer.Stop();
                _cts.Dispose();
                _cts = null;
                _searchButton.IsEnabled = true;
                _stopButton.IsEnabled = false;
                SetDriveChecksEnabled(true);
                _progressText.Text = string.Empty;
            }
        }

        private void OnAddClick()
        {
            var addToApp = _addToAppCheck.IsChecked == true;
            var addToV8i = _addToV8iCheck.IsChecked == true;

            if (!addToApp && !addToV8i)
            {
                _summaryText.Text = T("FindLostBases.NoneDestination");
                return;
            }

            var checkedRows = _rows.Where(r => r.IsChecked).ToList();
            var toAdd = checkedRows.Where(r => !r.InApp).ToList();
            var alreadyInList = checkedRows.Count(r => r.InApp);

            var addedApp = 0;
            var forV8i = new List<Infobase>();
            var rowsForV8i = new List<FoundBaseRowViewModel>();

            foreach (var row in toAdd)
            {
                var ib = new Infobase
                {
                    Name = row.Name,
                    Group = string.Empty,
                    Id = Guid.NewGuid().ToString("D"),
                    Connection = new ConnectionSettings
                    {
                        Type = ConnectionType.File,
                        FilePath = row.Base.DirectoryPath
                    }
                };

                if (addToApp)
                {
                    _addBase(ib);
                    addedApp++;

                    var dirKey = FoundBaseRowViewModel.NormalizeDir(row.Base.DirectoryPath);
                    _appFileDirs.Add(dirKey);
                    row.InApp = true;
                    DataChanged = true;
                }

                if (addToV8i)
                {
                    forV8i.Add(ib);
                    rowsForV8i.Add(row);
                }
            }

            var addedV8i = 0;
            var v8iMessage = string.Empty;
            if (addToV8i && forV8i.Count > 0)
            {
                var v8iPath = IbasesV8iImporter.FindDefaultPath();
                if (v8iPath is null)
                {
                    v8iMessage = T("FindLostBases.NoV8iPath");
                }
                else
                {
                    try
                    {
                        IbasesV8iExporter.AddInfobasesToFile(v8iPath, forV8i, new List<Group>());
                        addedV8i = forV8i.Count;
                        for (var i = 0; i < forV8i.Count; i++)
                        {
                            var row = rowsForV8i[i];
                            row.InIbasesV8i = true;
                            _v8iFileDirs.Add(FoundBaseRowViewModel.NormalizeDir(row.Base.DirectoryPath));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error("Ошибка записи выбранных баз в ibases.v8i", ex);
                        v8iMessage = string.Format(T("FindLostBases.ErrorFormat"), ex.Message);
                    }
                }
            }

            // Снимаем отметки со строк, добавленных хотя бы в одно из назначений.
            foreach (var row in toAdd)
            {
                if (row.InApp || rowsForV8i.Contains(row))
                {
                    row.IsChecked = false;
                    if (_checks.TryGetValue(row, out var check))
                        check.IsChecked = false;
                }
            }

            // Обновляем признаки «В приложении» / «В ibases.v8i» на строках.
            foreach (var row in toAdd)
                UpdateRow(row);

            _summaryText.Text = string.IsNullOrEmpty(v8iMessage)
                ? string.Format(T("FindLostBases.AddedFormatSplit"), addedApp, addedV8i, alreadyInList)
                : v8iMessage;
            UpdateCheckedHeader();
            UpdateAddEnabled();
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
                Text = LocalizationManager.T("FindLostBases.Title"),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            // Панель выбора дисков + кнопки Найти базы / Прекратить.
            var drivesBorder = new Border
            {
                Margin = new Thickness(0, 12, 0, 0),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };
            Themes.ThemeBrushes.Bind(drivesBorder, Border.BackgroundProperty, "CardBackgroundColorBrush");
            Themes.ThemeBrushes.Bind(drivesBorder, Border.BorderBrushProperty, "BorderColorBrush");

            var drivesDock = new DockPanel { LastChildFill = true };

            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            var checkAll = new Button { Content = LocalizationManager.T("FindLostBases.Drive.All") };
            checkAll.Styled(ControlThemes.SelectAllButton);
            ToolTip.SetTip(checkAll, LocalizationManager.T("FindLostBases.Drive.AllTooltip"));
            checkAll.Click += (_, _) => _driveChecks.ForEach(c => c.IsChecked = true);

            var uncheckAll = new Button { Content = LocalizationManager.T("FindLostBases.Drive.None") };
            uncheckAll.Styled(ControlThemes.SelectAllButton);
            ToolTip.SetTip(uncheckAll, LocalizationManager.T("FindLostBases.Drive.NoneTooltip"));
            uncheckAll.Click += (_, _) => _driveChecks.ForEach(c => c.IsChecked = false);

            toolbar.Children.Add(checkAll);
            toolbar.Children.Add(uncheckAll);
            // Разделитель — на кисти границы темы (аудит UI, п. 2.9) вместо Brushes.Gray.
            var separator = new Border { Width = 1, Height = 18 };
            ThemeBrushes.Bind(separator, Border.BackgroundProperty, "BorderColorBrush");
            toolbar.Children.Add(separator);

            _stopButton = new Button
            {
                Content = LocalizationManager.T("FindLostBases.Button.Stop"),
                Width = 120,
                Height = 34,
                IsEnabled = false
            };
            ToolTip.SetTip(_stopButton, LocalizationManager.T("FindLostBases.Button.StopTooltip"));
            // Остановка поиска — опасное действие: DangerButton (issue #26).
            _stopButton.Styled(ControlThemes.DangerButton);
            _stopButton.Click += (_, _) => OnStopClick();

            _searchButton = new Button
            {
                Content = LocalizationManager.T("FindLostBases.Button.Search"),
                Width = 180,
                Height = 34,
                IsDefault = true
            };
            _searchButton.Styled(ControlThemes.DialogConfirmButton);
            _searchButton.Click += (_, _) => OnSearchClick();

            // Разделитель — на кисти границы темы (аудит UI, п. 2.9) вместо Brushes.Gray.
            var separator2 = new Border { Width = 1, Height = 18 };
            ThemeBrushes.Bind(separator2, Border.BackgroundProperty, "BorderColorBrush");
            toolbar.Children.Add(separator2);
            toolbar.Children.Add(_stopButton);
            toolbar.Children.Add(_searchButton);

            var toolbarBorder = new Border
            {
                Padding = new Thickness(8, 4),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = Brushes.Transparent,
                Child = toolbar
            };
            Themes.ThemeBrushes.Bind(toolbarBorder, Border.BorderBrushProperty, "BorderColorBrush");
            DockPanel.SetDock(toolbarBorder, Dock.Top);
            drivesDock.Children.Add(toolbarBorder);

            var folderBar = BuildFolderBar();
            DockPanel.SetDock(folderBar, Dock.Top);
            drivesDock.Children.Add(folderBar);

            DockPanel.SetDock(_drivesWrap, Dock.Bottom);
            drivesDock.Children.Add(_drivesWrap);

            drivesBorder.Child = drivesDock;
            Grid.SetRow(drivesBorder, 1);
            grid.Children.Add(drivesBorder);

            // Таблица найденных баз.
            var listBorder = new Border
            {
                Margin = new Thickness(0, 12, 0, 0),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };
            Themes.ThemeBrushes.Bind(listBorder, Border.BackgroundProperty, "CardBackgroundColorBrush");
            Themes.ThemeBrushes.Bind(listBorder, Border.BorderBrushProperty, "BorderColorBrush");

            var dock = new DockPanel { LastChildFill = true };

            var foundToolbar = BuildFoundToolbar();
            DockPanel.SetDock(foundToolbar, Dock.Top);
            dock.Children.Add(foundToolbar);

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

            // Нижняя панель.
            var bottom = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            bottom.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var leftStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };

            _addToAppCheck = new CheckBox { Content = LocalizationManager.T("FindLostBases.AddToApp"), IsChecked = true };
            _addToAppCheck.Styled(ControlThemes.CacheCleanCheckBox);
            _addToAppCheck.IsCheckedChanged += (_, _) => UpdateAddEnabled();

            _addToV8iCheck = new CheckBox { Content = LocalizationManager.T("FindLostBases.AddToV8i"), IsChecked = true, Margin = new Thickness(16, 0, 0, 0) };
            _addToV8iCheck.Styled(ControlThemes.CacheCleanCheckBox);
            _addToV8iCheck.IsCheckedChanged += (_, _) => UpdateAddEnabled();

            var destRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            destRow.Children.Add(_addToAppCheck);
            destRow.Children.Add(_addToV8iCheck);
            leftStack.Children.Add(destRow);

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

            _addButton = new Button
            {
                Content = LocalizationManager.T("FindLostBases.Button.Add"),
                Width = 200,
                Height = 36,
                IsEnabled = false
            };
            _addButton.Styled(ControlThemes.DialogConfirmButton);
            _addButton.Click += (_, _) => OnAddClick();

            var cancel = BuildCancelActionButton(140);
            var rightPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Children = { _addButton, cancel }
            };
            Grid.SetColumn(rightPanel, 1);
            bottom.Children.Add(rightPanel);

            Grid.SetRow(bottom, 3);
            grid.Children.Add(bottom);

            return grid;
        }

        /// <summary>Панель «Отметить все / Снять все» для найденных баз.</summary>
        private Control BuildFoundToolbar()
        {
            var toolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(8, 6, 8, 2)
            };

            var checkAll = new Button { Content = LocalizationManager.T("FindLostBases.FoundAll") };
            checkAll.Styled(ControlThemes.SelectAllButton);
            ToolTip.SetTip(checkAll, LocalizationManager.T("FindLostBases.FoundAllTooltip"));
            checkAll.Click += (_, _) => OnFoundCheckAll();

            var uncheckAll = new Button { Content = LocalizationManager.T("FindLostBases.FoundNone") };
            uncheckAll.Styled(ControlThemes.SelectAllButton);
            ToolTip.SetTip(uncheckAll, LocalizationManager.T("FindLostBases.FoundNoneTooltip"));
            uncheckAll.Click += (_, _) => OnFoundCheckNone();

            toolbar.Children.Add(checkAll);
            toolbar.Children.Add(uncheckAll);
            return toolbar;
        }

        /// <summary>Отмечает все найденные базы, доступные для добавления (не в приложении).</summary>
        private void OnFoundCheckAll()
        {
            foreach (var r in _rows)
            {
                if (!r.NotInApp) continue;
                r.IsChecked = true;
                if (_checks.TryGetValue(r, out var check))
                    check.IsChecked = true;
            }
            UpdateCheckedHeader();
            UpdateAddEnabled();
        }

        /// <summary>Снимает отметки со всех найденных баз, доступных для добавления.</summary>
        private void OnFoundCheckNone()
        {
            foreach (var r in _rows)
            {
                if (!r.NotInApp) continue;
                r.IsChecked = false;
                if (_checks.TryGetValue(r, out var check))
                    check.IsChecked = false;
            }
            UpdateCheckedHeader();
            UpdateAddEnabled();
        }

        private Grid BuildHeaderGrid()
        {
            var grid = new Grid { Margin = new Thickness(8, 0, 8, 2) };
            ApplyColumns(grid);

            _checkedHeaderText.FontSize = 12;
            _checkedHeaderText.FontWeight = FontWeight.SemiBold;
            BindSecondary(_checkedHeaderText);
            Grid.SetColumn(_checkedHeaderText, 0);
            grid.Children.Add(_checkedHeaderText);

            AddHeader(grid, 1, "FindLostBases.Column.Name");
            AddHeader(grid, 2, "FindLostBases.Column.Path");
            AddHeader(grid, 3, "FindLostBases.Column.Size");
            AddHeader(grid, 4, "FindLostBases.Column.Modified");
            AddHeader(grid, 5, "FindLostBases.Column.InApp");
            AddHeader(grid, 6, "FindLostBases.Column.InV8i");

            return grid;
        }

        private static void AddHeader(Grid grid, int column, string key)
        {
            var h = MakeHeaderText(LocalizationManager.T(key));
            BindSecondary(h);
            Grid.SetColumn(h, column);
            grid.Children.Add(h);
        }

        private void ApplyColumns(Grid grid)
        {
            grid.ColumnDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));               // 0 — флажок
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star))); // 1 — имя
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(3, GridUnitType.Star))); // 2 — путь
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));               // 3 — размер
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));               // 4 — дата
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));               // 5 — в приложении
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));               // 6 — в ibases.v8i
        }

        private void AddRow(FoundBaseRowViewModel row)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            ApplyColumns(grid);

            var check = new CheckBox
            {
                IsChecked = row.IsChecked,
                IsEnabled = row.NotInApp,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };
            check.Styled(ControlThemes.CacheCleanCheckBox);
            check.IsCheckedChanged += (_, _) =>
            {
                row.IsChecked = check.IsChecked == true;
                UpdateCheckedHeader();
                UpdateAddEnabled();
            };
            Grid.SetColumn(check, 0);
            grid.Children.Add(check);

            AddText(grid, 1, row.Name, allowEllipsis: true);
            AddText(grid, 2, row.Path, allowEllipsis: true);

            var size = new TextBlock
            {
                Text = row.SizeText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(size, 3);
            grid.Children.Add(size);

            var modified = new TextBlock
            {
                Text = row.LastWriteText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(modified, 4);
            grid.Children.Add(modified);

            var inApp = MakeFlagDot();
            ApplyFlagStyle(inApp, row.InApp, "#16A34A", "#94A3B8");
            ToolTip.SetTip(inApp, LocalizationManager.T("FindLostBases.AlreadyInAppHint"));
            Grid.SetColumn(inApp, 5);
            grid.Children.Add(inApp);

            var inV8i = MakeFlagDot();
            ApplyFlagStyle(inV8i, row.InIbasesV8i, "#8B5CF6", "#94A3B8");
            ToolTip.SetTip(inV8i, LocalizationManager.T("FindLostBases.InV8iHint"));
            Grid.SetColumn(inV8i, 6);
            grid.Children.Add(inV8i);

            _checks[row] = check;
            _inAppTexts[row] = inApp;
            _inV8iTexts[row] = inV8i;
            _rowsPanel.Children.Add(grid);
        }

        private static TextBlock MakeFlagDot()
            => new()
            {
                Text = "●",
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };

        private static void ApplyFlagStyle(TextBlock block, bool on, string onColor, string offColor)
            => block.Foreground = Brush.Parse(on ? onColor : offColor);

        private static void AddText(Grid grid, int column, string text, bool allowEllipsis)
        {
            var block = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            if (allowEllipsis)
                block.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(block, column);
            grid.Children.Add(block);
        }

        private void UpdateRow(FoundBaseRowViewModel row)
        {
            if (_inAppTexts.TryGetValue(row, out var inApp))
                ApplyFlagStyle(inApp, row.InApp, "#16A34A", "#94A3B8");
            if (_inV8iTexts.TryGetValue(row, out var inV8i))
                ApplyFlagStyle(inV8i, row.InIbasesV8i, "#8B5CF6", "#94A3B8");
            if (_checks.TryGetValue(row, out var check))
                check.IsEnabled = row.NotInApp;
        }
    }
}
#endif