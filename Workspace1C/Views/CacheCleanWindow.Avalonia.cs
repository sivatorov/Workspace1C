#if LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.Primitives;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Themes;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C
{
    /// <summary>
    /// Диалог выбора типа очищаемого кэша 1С и набора информационных баз, для которых
    /// нужно выполнить очистку. Avalonia/Linux-версия WPF-окна <see cref="CacheCleanWindow"/>.
    /// </summary>
    public class CacheCleanWindow : ModalWindowBase
    {
        private readonly List<Infobase> _infobases;
        private readonly Dictionary<CheckBox, Infobase> _baseChecks = new();
        private readonly Dictionary<CheckBox, Grid> _baseRows = new();
        private readonly Dictionary<Infobase, TextBlock> _programSizeTexts = new();
        private readonly Dictionary<Infobase, TextBlock> _userSizeTexts = new();

        private readonly TextBlock _programCacheSizeText = new();
        private readonly TextBlock _userCacheSizeText = new();
        private readonly TextBlock _orphanCacheSizeText = new();
        private readonly CheckBox _programCacheCheck = new();
        private readonly CheckBox _userCacheCheck = new();
        private readonly CheckBox _orphanCacheCheck = new();
        private readonly TextBox _searchBox = new() { Padding = new Thickness(10, 7), Watermark = LocalizationManager.T("CacheClean.SearchBase") };
        // Отступ панели строк как в разметке (CacheCleanWindow.xaml:183).
        private readonly StackPanel _basesPanel = new() { Margin = new Thickness(4, 2) };
        private readonly TextBlock _basesCountText = new();
        private readonly Button _cleanButton = new() { IsDefault = true };

        // Ширина изменяемых колонок списка.
        private const double DefaultProgramWidth = 130;
        private const double DefaultUserWidth = 130;
        private const double MinColumnWidth = 40;

        private readonly Services.IInfobaseRepository _repository =
            AppServices.GetRequiredService<Services.IInfobaseRepository>();

        private double _nameColumnWidth;             // 0 — колонка «База» растягивается
        private double _programColumnWidth = DefaultProgramWidth;
        private double _userColumnWidth = DefaultUserWidth;
        private Grid? _headerGrid;
        private readonly List<Grid> _rows = new();

        // Сортировка колонок списка (issue #215): -1 — естественный порядок списка, иначе
        // индекс активной колонки сортировки (0 — имя, 1 — программный, 2 — пользовательский).
        private int _sortColumn = -1;
        private bool _sortDescending;
        private readonly Dictionary<Infobase, Grid> _rowByBase = new();
        // Фактические размеры кеша в байтах для корректной сортировки по колонкам размера
        // (сортируем по байтам, а не по отформатированному тексту «1,2 ГБ»).
        private readonly Dictionary<Infobase, long> _programBytes = new();
        private readonly Dictionary<Infobase, long> _userBytes = new();
        // Заголовки колонок, чтобы обновлять на них стрелку-индикатор направления сортировки.
        private readonly List<TextBlock> _headerTexts = new();

        // Прокрутка списка и строка базы, отмеченной по умолчанию (issue #198): если окно
        // открыто для конкретной базы, список прокручивается к ней, чтобы она была видна
        // и в фокусе; при открытии без предзаполнения (например, по папке) — не заполнены.
        private ScrollViewer? _basesScroll;
        private Grid? _defaultRow;
        private CheckBox? _defaultCheck;

        // Диагностика и показ ошибок (issues #195, #202): расчёт размера кеша выполняется
        // в фоновых потоках и может завершиться сбоем доступа к файловой системе или иным
        // исключением — окно обязано остаться открытым, а сбой — попасть в журнал и на экран.
        private readonly Services.IAppLogger _logger = AppServices.GetRequiredService<Services.IAppLogger>();
        private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

        // Показываем сообщение об ошибке расчёта размера только один раз за время жизни
        // окна, чтобы не спамить модальными окнами при сбое сразу нескольких расчётов.
        private bool _sizeErrorReported;

        // Состояние перетаскивания разделителя (как в главном окне).
        private bool _isResizing;
        private int _resizeColumn = -1;
        private double _resizeStartWidth;
        private double _resizeStartX;

        /// <param name="infobases">Все доступные информационные базы.</param>
        /// <param name="initialKind">Изначально выбранный тип кэша.</param>
        /// <param name="defaultSelected">База, выбранная по умолчанию (например, выделенная в главном окне).</param>
        public CacheCleanWindow(IEnumerable<Infobase> infobases, OneCCacheKind initialKind, Infobase? defaultSelected = null)
        {
            Title = LocalizationManager.T("CacheClean.Title");
            Width = 580;
            Height = 540;
            MinWidth = 480;
            MinHeight = 440;
            // Кегль окна из разметки (CacheCleanWindow.xaml:13): подписи флажков
            // и кнопок выбора берут его по наследству.
            FontSize = 13;
            CanResize = true;

            _infobases = infobases.ToList();

            // Подпись флажка это просто текст, размер кеша стоит рядом отдельной
            // подписью (CacheCleanWindow.xaml:119-124), а не внутри флажка.
            _programCacheCheck.Content = LocalizationManager.T("CacheClean.ProgramCache");
            _userCacheCheck.Content = LocalizationManager.T("CacheClean.UserCache");
            _orphanCacheCheck.Content = LocalizationManager.T("CacheClean.OrphanCache");
            foreach (var check in new[] { _programCacheCheck, _userCacheCheck, _orphanCacheCheck })
                check.Styled(ControlThemes.CacheCleanCheckBox);
            foreach (var size in new[] { _programCacheSizeText, _userCacheSizeText, _orphanCacheSizeText })
                PrepareSizeText(size);
            ToolTip.SetTip(_orphanCacheCheck, LocalizationManager.T("CacheClean.OrphanCacheTooltip"));

            _programCacheCheck.IsChecked = initialKind.HasFlag(OneCCacheKind.Program);
            _userCacheCheck.IsChecked = initialKind.HasFlag(OneCCacheKind.User);
            _programCacheCheck.IsCheckedChanged += (_, _) => OnCacheKindChanged();
            _userCacheCheck.IsCheckedChanged += (_, _) => OnCacheKindChanged();
            _orphanCacheCheck.IsCheckedChanged += (_, _) => UpdateCleanEnabled();
            _searchBox.TextChanged += (_, _) => OnSearchTextChanged();

            LoadColumnWidths();

            BuildBasesList(defaultSelected);
            UpdateCount();
            UpdateCleanEnabled();

            // Внешней прокрутки нет, как и в разметке: список занимает всё
            // свободное место звёздной строкой и тянется вместе с окном, а нижняя
            // панель прижата к низу. Минимальная высота окна 440 не даёт ей
            // обрезаться (CacheCleanWindow.xaml:9 и 105).
            Content = BuildRoot();

            Opened += (_, _) =>
            {
                // Прокрутка выполняется после показа окна, когда известна видимая
                // область ScrollViewer: иначе BringIntoView не сдвинет список (issue #198).
                ScrollToDefault();
                RefreshCacheSizes();
            };
            Closing += (_, _) => SaveColumnWidths();
        }

        /// <summary>Загружает сохранённые ширины колонок из настроек приложения.</summary>
        private void LoadColumnWidths()
        {
            try
            {
                var settings = _repository.LoadSettings();
                _nameColumnWidth = settings.CacheCleanBaseColumnWidth;
                if (settings.CacheCleanProgramColumnWidth > 0)
                    _programColumnWidth = settings.CacheCleanProgramColumnWidth;
                if (settings.CacheCleanUserColumnWidth > 0)
                    _userColumnWidth = settings.CacheCleanUserColumnWidth;
            }
            catch
            {
                // Игнорируем ошибки загрузки — используем значения по умолчанию.
            }
        }

        /// <summary>Сохраняет текущие ширины колонок в настройки приложения.</summary>
        private void SaveColumnWidths()
        {
            try
            {
                var settings = _repository.LoadSettings();
                settings.CacheCleanBaseColumnWidth = _nameColumnWidth;
                settings.CacheCleanProgramColumnWidth = _programColumnWidth;
                settings.CacheCleanUserColumnWidth = _userColumnWidth;
                _repository.SaveSettings(settings);
            }
            catch
            {
                // Игнорируем ошибки сохранения.
            }
        }

        /// <summary>
        /// Сообщает о сбое расчёта размера кеша. Сбой не должен ронять окно (issues #195, #202),
        /// поэтому здесь — лог в журнал и понятное сообщение пользователю, а не молчаливое
        /// проглатывание исключения.
        /// </summary>
        private void ShowSizeError()
        {
            if (_sizeErrorReported)
                return;
            _sizeErrorReported = true;

            try
            {
                _dialogs.ShowError(
                    LocalizationManager.T("CacheClean.SizeError"),
                    LocalizationManager.T("Main.CacheErrorTitle"));
            }
            catch
            {
                // Сбой показа диалога тоже не должен уронить окно очистки.
            }
        }

        /// <summary>Тип кэша, выбранный пользователем.</summary>
        public OneCCacheKind SelectedCacheKind { get; private set; } = OneCCacheKind.None;

        /// <summary>Список баз, выбранных для очистки.</summary>
        public IReadOnlyList<Infobase> SelectedInfobases { get; private set; } = Array.Empty<Infobase>();

        /// <summary>Признак того, что нужно дополнительно очистить «остатки» кеша от удалённых баз.</summary>
        public bool CleanOrphans { get; private set; }

        /// <summary>
        /// Показывает окно модально (синхронно) и возвращает результат диалога.
        /// Открытая публичная обёртка над <see cref="ModalWindowBase.ShowDialogSync()"/>,
        /// чтобы диалог можно было вызывать из ViewModel (не наследника окна).
        /// </summary>
        /// <returns>True, если пользователь подтвердил очистку.</returns>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        /// <summary>
        /// Подпись размера кеша рядом с флажком: кегль 12 и вторичный цвет темы,
        /// как в разметке (CacheCleanWindow.xaml:123).
        /// </summary>
        private static void PrepareSizeText(TextBlock sizeText)
        {
            sizeText.VerticalAlignment = VerticalAlignment.Center;
            sizeText.FontSize = 12;
            sizeText.Text = "…";
            Themes.ThemeBrushes.Bind(sizeText, TextBlock.ForegroundProperty, "TextSecondaryBrush");
        }

        /// <summary>
        /// Содержимое кнопки выбора: цветной значок 16 и подпись с отступом 6
        /// (CacheCleanWindow.xaml:159).
        /// </summary>
        private static Control SelectAllContent(string iconKey, string colorHex, string text)
        {
            var icon = IconHelper.MakeIcon(iconKey, 16, Themes.ThemeBrushes.FromSpec(colorHex));
            icon.VerticalAlignment = VerticalAlignment.Center;
            var caption = new TextBlock
            {
                Text = text,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { icon, caption }
            };
        }

        /// <summary>
        /// Вычисляет и отображает размеры программного и пользовательского кеша.
        /// Расчёт выполняется в фоновом потоке, чтобы не блокировать интерфейс.
        /// </summary>
        private async void RefreshCacheSizes()
        {
            _programCacheSizeText.Text = "…";
            _userCacheSizeText.Text = "…";
            _orphanCacheSizeText.Text = "…";
            foreach (var t in _programSizeTexts.Values) t.Text = "…";
            foreach (var t in _userSizeTexts.Values) t.Text = "…";

            // Тип кеша читается до Task.Run: CurrentKind обращается к IsChecked
            // переключателей, а свойства элементов Avalonia доступны только из
            // потока интерфейса, иначе вызов падает с «Call from invalid thread»
            // и размеры остаются незаполненными. Так же сделано в RefreshOrphanSize.
            var kind = CurrentKind();

            // Обработчик Opened, который это вызывает, — async void: любое выброшенное
            // здесь исключение ушло бы в контекст синхронизации UI и уронило приложение
            // при открытии окна (issues #195, #202). Каждый этап ловит и логирует свою
            // ошибку отдельно, чтобы отказ на одном шаге не стирал уже посчитанные размеры
            // и не прерывал цикл по базам (issue #212).
            try
            {
                var program = await Task.Run(() => OneCCacheCleaner.GetSize(OneCCacheKind.Program, _infobases));
                _programCacheSizeText.Text = FormatSize(program);
            }
            catch (Exception ex)
            {
                _logger.Error("Ошибка расчёта размера программного кеша", ex);
                ShowSizeError();
            }

            try
            {
                var user = await Task.Run(() => OneCCacheCleaner.GetSize(OneCCacheKind.User, _infobases));
                _userCacheSizeText.Text = FormatSize(user);
            }
            catch (Exception ex)
            {
                _logger.Error("Ошибка расчёта размера пользовательского кеша", ex);
                ShowSizeError();
            }

            try
            {
                var orphans = await Task.Run(() => OneCCacheCleaner.GetOrphanSize(kind, _infobases));
                _orphanCacheSizeText.Text = FormatSize(orphans);
            }
            catch (Exception ex)
            {
                _logger.Error("Ошибка расчёта размера остатков кеша", ex);
                ShowSizeError();
            }

            foreach (var ib in _infobases)
            {
                try
                {
                    var p = await Task.Run(() => OneCCacheCleaner.GetSize(ib, OneCCacheKind.Program));
                    _programBytes[ib] = p;
                    if (_programSizeTexts.TryGetValue(ib, out var pt)) pt.Text = FormatSize(p);
                }
                catch (Exception ex)
                {
                    _logger.Error("Ошибка расчёта размера программного кеша по базе", ex);
                }

                try
                {
                    var u = await Task.Run(() => OneCCacheCleaner.GetSize(ib, OneCCacheKind.User));
                    _userBytes[ib] = u;
                    if (_userSizeTexts.TryGetValue(ib, out var ut)) ut.Text = FormatSize(u);
                }
                catch (Exception ex)
                {
                    _logger.Error("Ошибка расчёта размера пользовательского кеша по базе", ex);
                }
            }
        }

        /// <summary>Возвращает тип кеша, выбранный в данный момент (по галкам программного/пользовательского).</summary>
        private OneCCacheKind CurrentKind()
        {
            var kind = OneCCacheKind.None;
            if (_programCacheCheck.IsChecked == true)
                kind |= OneCCacheKind.Program;
            if (_userCacheCheck.IsChecked == true)
                kind |= OneCCacheKind.User;
            return kind;
        }

        /// <summary>
        /// Пересчитывает размер «остатков» кеша по текущему выбору типа кеша, чтобы подпись
        /// соответствовала тому, что реально будет очищено (issue #178).
        /// </summary>
        private async void RefreshOrphanSize()
        {
            var kind = CurrentKind();
            _orphanCacheSizeText.Text = "…";
            try
            {
                var orphans = await Task.Run(() => OneCCacheCleaner.GetOrphanSize(kind, _infobases));
                _orphanCacheSizeText.Text = FormatSize(orphans);
            }
            catch (Exception ex)
            {
                _logger.Error("Ошибка расчёта размера остатков кеша", ex);
                ShowSizeError();
            }
        }

        private void OnCacheKindChanged()
        {
            UpdateCleanEnabled();
            RefreshOrphanSize();
        }

        /// <summary>
        /// Форматирует размер в байтах в человекочитаемый вид с локализованными единицами.
        /// </summary>
        private static string FormatSize(long bytes)
        {
            // Единицы из локализации; если ключ пуст или задан мусором, остаёмся на байтах,
            // чтобы не упасть с IndexOutOfRangeException на units[0].
            var units = LocalizationManager.T("CacheClean.SizeUnits")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (units.Length == 0)
                units = new[] { "B" };

            double value = bytes;
            var index = 0;
            while (value >= 1024 && index < units.Length - 1)
            {
                value /= 1024;
                index++;
            }

            var number = index == 0 ? value.ToString("0") : value.ToString("0.0");
            return $"{number} {units[index]}";
        }

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            // Список растягивается на всё свободное место (CacheCleanWindow.xaml:105).
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var title = new TextBlock
            {
                Text = LocalizationManager.T("CacheClean.TitleHeader"),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            var description = new TextBlock
            {
                Text = LocalizationManager.T("CacheClean.Subtitle"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            };
            Themes.ThemeBrushes.Bind(description, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetRow(description, 1);
            grid.Children.Add(description);

            var typeLabel = new TextBlock
            {
                Text = LocalizationManager.T("CacheClean.CacheTypeLabel"),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 16, 0, 6)
            };
            Grid.SetRow(typeLabel, 2);
            grid.Children.Add(typeLabel);

            // Размеры кеша стоят отдельными подписями рядом с флажками, а между
            // парами вертикальный разделитель (CacheCleanWindow.xaml:118-131).
            var typePanel = new StackPanel { Orientation = Orientation.Horizontal };
            ToolTip.SetTip(_programCacheCheck, "%LOCALAPPDATA%\\1C\\1cv8…");
            ToolTip.SetTip(_userCacheCheck, "%APPDATA%\\1C\\1cv8…");
            _programCacheCheck.Margin = new Thickness(0, 0, 8, 0);
            _programCacheSizeText.Margin = new Thickness(2, 0, 18, 0);
            _userCacheSizeText.Margin = new Thickness(2, 0, 0, 0);
            typePanel.Children.Add(_programCacheCheck);
            typePanel.Children.Add(_programCacheSizeText);

            var typeSeparator = new Border
            {
                Width = 1,
                Margin = new Thickness(4, 0, 20, 0),
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Themes.ThemeBrushes.Bind(typeSeparator, Border.BackgroundProperty, "BorderColorBrush");
            typePanel.Children.Add(typeSeparator);

            typePanel.Children.Add(_userCacheCheck);
            typePanel.Children.Add(_userCacheSizeText);
            Grid.SetRow(typePanel, 3);
            grid.Children.Add(typePanel);

            // Список баз
            var basesBorder = new Border
            {
                Margin = new Thickness(0, 12, 0, 0),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };
            // Фон и рамка карточки: в разметке это CardBackgroundBrush
            // и BorderBrushColor (CacheCleanWindow.xaml:136-137). Без привязки
            // карточка сливалась с фоном окна, а рамка была невидимой, и серого
            // блока от поля поиска до списка не получалось.
            Themes.ThemeBrushes.Bind(basesBorder, Border.BackgroundProperty, "CardBackgroundColorBrush");
            Themes.ThemeBrushes.Bind(basesBorder, Border.BorderBrushProperty, "BorderColorBrush");

            var dock = new DockPanel { LastChildFill = true };

            // Вид поля Windows-сторона сняла с живого окна (задание 15): своего
            // стиля в разметке нет, применяется неявный MaterialDesignTextBox,
            // а он даёт прозрачную подложку и одну черту снизу цветом #7F000000.
            // Фон под полем это фон самой карточки списка, отдельной заливки нет.
            _searchBox.Background = Brushes.Transparent;
            _searchBox.BorderThickness = new Thickness(0, 0, 0, 1);
            Themes.ThemeBrushes.Bind(_searchBox, TextBox.BorderBrushProperty, "BorderColorBrush");
            _searchBox.FontSize = 13;
            _searchBox.VerticalContentAlignment = VerticalAlignment.Center;
            _searchBox.Margin = new Thickness(8, 8, 8, 2);
            DockPanel.SetDock(_searchBox, Dock.Top);
            dock.Children.Add(_searchBox);

            var toolbar = new Grid();
            toolbar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            toolbar.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            var toolbarButtons = new StackPanel { Orientation = Orientation.Horizontal };
            var selectAll = new Button
            {
                Content = SelectAllContent("IconCheckboxMultipleMarked", "@SuccessBrush", LocalizationManager.T("CacheClean.SelectAll"))
            };
            selectAll.Styled(ControlThemes.SelectAllButton);
            ToolTip.SetTip(selectAll, LocalizationManager.T("CacheClean.SelectAllTooltip"));
            selectAll.Click += (_, _) => { foreach (var check in _baseChecks.Keys) check.IsChecked = true; UpdateCount(); UpdateCleanEnabled(); };
            var clearAll = new Button
            {
                Content = SelectAllContent("IconCheckboxMultipleBlankOutline", "@ErrorBrush", LocalizationManager.T("CacheClean.ClearAll")),
                Margin = new Thickness(16, 0, 0, 0)
            };
            clearAll.Styled(ControlThemes.SelectAllButton);
            ToolTip.SetTip(clearAll, LocalizationManager.T("CacheClean.ClearAllTooltip"));
            clearAll.Click += (_, _) => { foreach (var check in _baseChecks.Keys) check.IsChecked = false; UpdateCount(); UpdateCleanEnabled(); };
            toolbarButtons.Children.Add(selectAll);
            toolbarButtons.Children.Add(clearAll);
            toolbar.Children.Add(toolbarButtons);

            // Кегль и цвет как у заголовков колонок рядом: счётчик встал с ними
            // в один ряд, и умолчания выбивались из строя.
            _basesCountText.VerticalAlignment = VerticalAlignment.Center;
            _basesCountText.HorizontalAlignment = HorizontalAlignment.Right;
            _basesCountText.TextTrimming = TextTrimming.CharacterEllipsis;
            _basesCountText.FontSize = 12;
            // Цвет из темы, как в оригинале: постоянный серый мимо палитры
            // тёмной схемы, где вторичный текст светлее фона.
            Themes.ThemeBrushes.Bind(_basesCountText, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetColumn(_basesCountText, 1);
            toolbar.Children.Add(_basesCountText);
            // Панель отделена от списка чертой снизу и имеет отступ 8,4
            // (CacheCleanWindow.xaml:149).
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

            // Закреплённая шапка списка (остаётся вверху при прокрутке).
            _headerGrid = BuildHeaderGrid();
            DockPanel.SetDock(_headerGrid, Dock.Top);
            dock.Children.Add(_headerGrid);

            _basesScroll = new ScrollViewer
            {
                Content = _basesPanel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(4)
            };
            dock.Children.Add(_basesScroll);

            basesBorder.Child = dock;
            Grid.SetRow(basesBorder, 4);
            grid.Children.Add(basesBorder);

            // Нижняя панель: счётчик + кнопки
            var bottom = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            bottom.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var leftPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Center
            };
            // Размер подписью под флажком, а не рядом: при ширине окна по умолчанию
            // соседняя строка с размером обрезалась кнопкой очистки (issue #178).
            _orphanCacheSizeText.Margin = new Thickness(24, 2, 0, 0);
            _orphanCacheSizeText.TextWrapping = TextWrapping.Wrap;
            _orphanCacheSizeText.TextTrimming = TextTrimming.CharacterEllipsis;
            leftPanel.Children.Add(_orphanCacheCheck);
            leftPanel.Children.Add(_orphanCacheSizeText);
            Grid.SetColumn(leftPanel, 0);
            bottom.Children.Add(leftPanel);

            // Оформление и порядок по разметке (CacheCleanWindow.xaml:205):
            // зелёная очистка шириной 180 слева, красная отмена справа, зазор 10.
            // Пока нечего чистить, кнопка недоступна и приглушена, как там же.
            _cleanButton.Styled(ControlThemes.DialogConfirmButton);
            _cleanButton.Classes.Add("dimmed");
            _cleanButton.Width = 180;
            _cleanButton.Height = 36;
            var cleanCaption = new TextBlock
            {
                Text = LocalizationManager.T("CacheClean.Clean"),
                VerticalAlignment = VerticalAlignment.Center
            };
            _cleanButton.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                // Значок на акцентной кнопке — контрастный TextOnAccentBrush (issue #26).
                Children = { IconHelper.MakeIcon("IconBroom", 16, "TextOnAccentBrush"), cleanCaption }
            };
            RegisterConfirmCaption(cleanCaption, "CacheClean.Clean");
            _cleanButton.Click += (_, _) => OnClean_Click();

            var rightPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Children = { _cleanButton, BuildCancelActionButton(140) }
            };
            Grid.SetColumn(rightPanel, 1);
            bottom.Children.Add(rightPanel);

            Grid.SetRow(bottom, 5);
            grid.Children.Add(bottom);

            return grid;
        }

        /// <summary>
        /// Возвращает GridLength для колонки: «База» (индекс 0) при нулевой ширине
        /// растягивается на всё свободное место, остальные — фиксированной ширины.
        /// </summary>
        private static GridLength ColLength(int column, double width)
            => column == 0 && width <= 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width);

        /// <summary>
        /// Применяет к сетке общую раскладку колонок: 0 — имя базы, 1 — программный, 2 — пользовательский.
        /// </summary>
        private void ApplyColumns(Grid grid)
        {
            grid.ColumnDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition(ColLength(0, _nameColumnWidth)) { MinWidth = MinColumnWidth });
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(_programColumnWidth)) { MinWidth = MinColumnWidth });
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(_userColumnWidth)) { MinWidth = MinColumnWidth });
        }

        /// <summary>Строит закреплённую шапку списка с зонами захвата для изменения ширины колонок.</summary>
        private Grid BuildHeaderGrid()
        {
            var grid = new Grid { Margin = new Thickness(8, 0, 8, 2) };
            ApplyColumns(grid);

            grid.Children.Add(BuildHeaderText(LocalizationManager.T("CacheClean.ColumnBase"), HorizontalAlignment.Left, 0));
            grid.Children.Add(BuildHeaderText(LocalizationManager.T("CacheClean.ColumnProgramSize"), HorizontalAlignment.Right, 1));
            grid.Children.Add(BuildHeaderText(LocalizationManager.T("CacheClean.ColumnUserSize"), HorizontalAlignment.Right, 2));

            // Зоны захвата для изменения ширины каждой колонки (как в главном окне).
            for (var col = 0; col < 3; col++)
                grid.Children.Add(BuildResizeGrip(col));

            UpdateHeaderIndicators();
            return grid;
        }

        /// <summary>Создаёт зону захвата на правой границе колонки (тонкая полоса + широкий захват).</summary>
        private Border BuildResizeGrip(int column)
        {
            var grip = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                Width = 8,
                ZIndex = 1,
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.SizeWestEast)
            };
            ToolTip.SetTip(grip, LocalizationManager.T("CacheClean.ResizeColumnTooltip"));
            Grid.SetColumn(grip, column);
            grip.PointerPressed += OnResize_PointerPressed;
            grip.PointerMoved += OnResize_PointerMoved;
            grip.PointerReleased += OnResize_PointerReleased;
            return grip;
        }

        private void OnResize_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Border grip || _headerGrid is null)
                return;

            var column = Grid.GetColumn(grip);
            if (column < 0 || column >= _headerGrid.ColumnDefinitions.Count)
                return;

            _resizeColumn = column;
            _resizeStartWidth = _headerGrid.ColumnDefinitions[column].ActualWidth;
            _resizeStartX = e.GetPosition(this).X;
            _isResizing = true;
            e.Pointer.Capture(grip);
            e.Handled = true;
        }

        private void OnResize_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isResizing || _resizeColumn < 0 || _headerGrid is null)
                return;

            var newWidth = _resizeStartWidth + (e.GetPosition(this).X - _resizeStartX);
            if (newWidth < MinColumnWidth)
                newWidth = MinColumnWidth;

            SetColumnWidth(_resizeColumn, newWidth);
        }

        private void OnResize_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (sender is Border)
                e.Pointer.Capture(null);

            if (_isResizing)
            {
                _isResizing = false;
                _resizeColumn = -1;
                SaveColumnWidths();
            }
        }

        /// <summary>
        /// Применяет новую ширину только к целевой колонке — и в шапке, и во всех строках
        /// (заголовок и данные изменяются синхронно, как в главном окне).
        /// </summary>
        private void SetColumnWidth(int column, double width)
        {
            width = Math.Max(MinColumnWidth, width);

            if (column == 0) _nameColumnWidth = width;
            else if (column == 1) _programColumnWidth = width;
            else if (column == 2) _userColumnWidth = width;

            if (_headerGrid is not null)
                _headerGrid.ColumnDefinitions[column].Width = ColLength(column, width);

            foreach (var row in _rows)
                row.ColumnDefinitions[column].Width = ColLength(column, width);
        }

        private void BuildBasesList(Infobase? defaultSelected)
        {
            _basesPanel.Children.Clear();
            _baseChecks.Clear();
            _baseRows.Clear();
            _programSizeTexts.Clear();
            _userSizeTexts.Clear();
            _rows.Clear();

            foreach (var ib in _infobases)
            {
                var row = new Grid { Margin = new Thickness(4, 2, 4, 2) };
                ApplyColumns(row);

                var check = new CheckBox
                {
                    Content = ib.Name,
                    IsChecked = ReferenceEquals(ib, defaultSelected),
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                // Тот же местный флажок, что и у типов кеша: в версии для Windows
                // стиль назначается строкам списка кодом (CacheCleanWindow.xaml.cs:232).
                check.Styled(ControlThemes.CacheCleanCheckBox);
                ToolTip.SetTip(check, string.IsNullOrWhiteSpace(ib.ConnectionPathDisplay) ? ib.Name : ib.ConnectionPathDisplay);
                check.IsCheckedChanged += (_, _) => OnBaseChecked();
                Grid.SetColumn(check, 0);
                row.Children.Add(check);

                // Запоминаем строку базы, предзаполненной по умолчанию, чтобы после
                // показа окна прокрутить к ней список (issue #198).
                if (ReferenceEquals(ib, defaultSelected))
                {
                    _defaultRow = row;
                    _defaultCheck = check;
                }

                var programSize = BuildSizeText();
                Grid.SetColumn(programSize, 1);
                row.Children.Add(programSize);

                var userSize = BuildSizeText();
                Grid.SetColumn(userSize, 2);
                row.Children.Add(userSize);

                _baseChecks[check] = ib;
                _baseRows[check] = row;
                _rowByBase[ib] = row;
                _programSizeTexts[ib] = programSize;
                _userSizeTexts[ib] = userSize;
                _rows.Add(row);
                _basesPanel.Children.Add(row);
            }
        }

        /// <summary>
        /// Прокручивает список к базе, отмеченной по умолчанию, и ставит на неё фокус
        /// (issue #198). Если конкретная база не задана (например, окно открыто по папке
        /// без предзаполнения) — ничего не делает, и список остаётся в исходной позиции.
        /// </summary>
        private void ScrollToDefault()
        {
            if (_defaultRow is null)
                return;
            _defaultRow.BringIntoView();
            _defaultCheck?.Focus();
        }

        /// <summary>
        /// Формирует кликабельный заголовок колонки списка баз. Клик по нему сортирует список
        /// по этой колонке, повторный клик меняет направление (issue #215).
        /// </summary>
        private TextBlock BuildHeaderText(string text, HorizontalAlignment align, int column)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = align,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            // Цвет из темы, а не постоянный серый: в тёмной схеме вторичный
            // текст светлее фона (CacheCleanWindow.xaml.cs:134).
            Themes.ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            block.PointerPressed += (_, _) => OnHeaderClick(column);
            Grid.SetColumn(block, column);
            _headerTexts.Add(block);
            return block;
        }

        /// <summary>Обрабатывает клик по заголовку колонки: задаёт сортировку и направление (issue #215).</summary>
        private void OnHeaderClick(int column)
        {
            if (_sortColumn == column)
            {
                // Повторный клик по той же колонке — меняем направление, как в 1С.
                _sortDescending = !_sortDescending;
            }
            else
            {
                _sortColumn = column;
                _sortDescending = false;
            }
            SortRows();
            UpdateHeaderIndicators();
        }

        /// <summary>
        /// Переупорядочивает строки списка по выбранной колонке. Колонки размера сортируются
        /// по фактическим байтам, а не по отформатированному тексту. Ещё не посчитанные размеры
        /// уходят в конец независимо от направления (issue #215).
        /// </summary>
        private void SortRows()
        {
            if (_sortColumn < 0)
                return;

            var desc = _sortDescending;
            var ordered = _infobases.ToList();
            ordered.Sort((a, b) =>
            {
                if (_sortColumn == 1)
                    return CompareSize(_programBytes, a, b, desc);
                if (_sortColumn == 2)
                    return CompareSize(_userBytes, a, b, desc);

                var cmp = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                return desc ? -cmp : cmp;
            });

            var index = 0;
            foreach (var ib in ordered)
            {
                if (_rowByBase.TryGetValue(ib, out var row))
                {
                    _basesPanel.Children.Remove(row);
                    _basesPanel.Children.Insert(index, row);
                }
                index++;
            }
        }

        /// <summary>
        /// Сравнивает две базы по фактическому размеру кеша в байтах. Неизвестный (ещё не
        /// посчитанный) размер всегда идёт в конец, независимо от направления сортировки.
        /// </summary>
        private static int CompareSize(Dictionary<Infobase, long> bytes, Infobase a, Infobase b, bool desc)
        {
            var av = bytes.TryGetValue(a, out var ax) ? ax : (long?)null;
            var bv = bytes.TryGetValue(b, out var bx) ? bx : (long?)null;
            if (av is null && bv is null) return 0;
            if (av is null) return 1;
            if (bv is null) return -1;
            var cmp = av.Value.CompareTo(bv.Value);
            return desc ? -cmp : cmp;
        }

        /// <summary>Обновляет стрелки-индикаторы направления сортировки в заголовках (issue #215).</summary>
        private void UpdateHeaderIndicators()
        {
            var labels = new[]
            {
                LocalizationManager.T("CacheClean.ColumnBase"),
                LocalizationManager.T("CacheClean.ColumnProgramSize"),
                LocalizationManager.T("CacheClean.ColumnUserSize")
            };
            for (var i = 0; i < _headerTexts.Count && i < labels.Length; i++)
            {
                var suffix = i == _sortColumn ? (_sortDescending ? " ▼" : " ▲") : string.Empty;
                _headerTexts[i].Text = labels[i] + suffix;
            }
        }

        /// <summary>Формирует поле отображения размера кеша базы.</summary>
        private static TextBlock BuildSizeText()
        {
            var size = new TextBlock
            {
                Text = "…",
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Themes.ThemeBrushes.Bind(size, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            return size;
        }

        private void OnSearchTextChanged()
        {
            var query = _searchBox.Text?.Trim() ?? string.Empty;
            foreach (var kv in _baseChecks)
            {
                var visible = query.Length == 0
                    || kv.Value.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || (kv.Value.ConnectionPathDisplay?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);
                if (_baseRows.TryGetValue(kv.Key, out var row))
                    row.IsVisible = visible;
            }
        }

        private void OnBaseChecked()
        {
            UpdateCount();
            UpdateCleanEnabled();
        }

        private void UpdateCount()
        {
            var selected = _baseChecks.Count(kv => kv.Key.IsChecked == true);
            var total = _baseChecks.Count;
            _basesCountText.Text = string.Format(LocalizationManager.T("CacheClean.CountSelected"), selected, total);
        }

        private void UpdateCleanEnabled()
        {
            var hasType = _programCacheCheck.IsChecked == true || _userCacheCheck.IsChecked == true;
            var hasBases = _baseChecks.Any(kv => kv.Key.IsChecked == true);
            var hasOrphans = _orphanCacheCheck.IsChecked == true;
            _cleanButton.IsEnabled = hasType && (hasBases || hasOrphans);
        }

        private void OnClean_Click()
        {
            var kind = OneCCacheKind.None;
            if (_programCacheCheck.IsChecked == true)
                kind |= OneCCacheKind.Program;
            if (_userCacheCheck.IsChecked == true)
                kind |= OneCCacheKind.User;

            CleanOrphans = _orphanCacheCheck.IsChecked == true;
            SelectedCacheKind = kind;
            SelectedInfobases = _baseChecks
                .Where(kv => kv.Key.IsChecked == true)
                .Select(kv => kv.Value)
                .ToList();

            DialogResult = true;
            Close();
        }
    }
}
#endif