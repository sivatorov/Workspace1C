#if WINDOWS
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C;

/// <summary>
/// Диалог выбора типа очищаемого кэша 1С и набора информационных баз,
/// для которых нужно выполнить очистку.
/// </summary>
public partial class CacheCleanWindow : Window
{
    private readonly List<Infobase> _infobases;

    // Базы, предвыбранные при открытии окна (issue #105): открытие «Очистить кэш»
    // для выбранной группы — базы группы отмечены по умолчанию.
    private readonly HashSet<Infobase> _preselected;
    private readonly Dictionary<CheckBox, Infobase> _baseChecks = new();
    private readonly Dictionary<CheckBox, Grid> _baseRows = new();
    private readonly Dictionary<Infobase, TextBlock> _programSizeTexts = new();
    private readonly Dictionary<Infobase, TextBlock> _userSizeTexts = new();
    private readonly List<Grid> _rows = new();

    // Строка и её флажок для базы, отмеченной по умолчанию (issue #198): если окно
    // открыто для конкретной базы, список прокручивается к ней, чтобы она была видна
    // и в фокусе; при открытии без предзаполнения (например, по папке) — не заполнены.
    private Grid? _defaultRow;
    private CheckBox? _defaultCheck;

    // Ширина изменяемых колонок списка.
    private const double DefaultProgramWidth = 130;
    private const double DefaultUserWidth = 130;
    private const double MinColumnWidth = 40;

    private readonly IInfobaseRepository _repository =
        AppServices.GetRequiredService<IInfobaseRepository>();

    private double _nameColumnWidth;             // 0 — колонка «База» растягивается
    private double _programColumnWidth = DefaultProgramWidth;
    private double _userColumnWidth = DefaultUserWidth;

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

    // Диагностика и показ ошибок (issues #195, #202): расчёт размера кеша выполняется
    // в фоновых потоках и может завершиться сбоем доступа к файловой системе или иным
    // исключением — окно обязано остаться открытым, а сбой — попасть в журнал и на экран.
    private readonly IAppLogger _logger = AppServices.GetRequiredService<IAppLogger>();
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
    /// <param name="preselected">Базы, отмеченные по умолчанию ДО открытия (issue #105:
    /// открытие «Очистить кэш» для выбранной группы — базы группы предвыбраны).</param>
    public CacheCleanWindow(IEnumerable<Infobase> infobases, OneCCacheKind initialKind,
        Infobase? defaultSelected = null, IEnumerable<Infobase>? preselected = null)
    {
        InitializeComponent();
        _infobases = infobases.ToList();
        _preselected = preselected is null
            ? new HashSet<Infobase>()
            : new HashSet<Infobase>(preselected);

        LoadColumnWidths();
        BuildBasesList(defaultSelected);
        BuildHeaderGrid();

        ProgramCacheCheck.IsChecked = initialKind.HasFlag(OneCCacheKind.Program);
        UserCacheCheck.IsChecked = initialKind.HasFlag(OneCCacheKind.User);

        UpdateCount();
        UpdateCleanEnabled();

        Loaded += async (_, _) =>
        {
            // Прокрутка выполняется после разметки окна: без этого ScrollIntoView
            // не знает видимую область и ничего не прокрутит (issue #198).
            ScrollToDefault();
            await RefreshCacheSizesAsync();
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
    public bool CleanOrphans => OrphanCacheCheck.IsChecked == true;

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
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = ColLength(0, _nameColumnWidth), MinWidth = MinColumnWidth });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_programColumnWidth), MinWidth = MinColumnWidth });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_userColumnWidth), MinWidth = MinColumnWidth });
    }

    /// <summary>Строит закреплённую шапку списка с зонами захвата для изменения ширины колонок.</summary>
    private void BuildHeaderGrid()
    {
        BasesHeaderGrid.Children.Clear();
        ApplyColumns(BasesHeaderGrid);

        var secondaryBrush = (Brush)FindResource("TextSecondaryBrush");
        BuildHeaderText(BasesHeaderGrid, LocalizationManager.T("CacheClean.ColumnBase"), 0, HorizontalAlignment.Left, secondaryBrush);
        BuildHeaderText(BasesHeaderGrid, LocalizationManager.T("CacheClean.ColumnProgramSize"), 1, HorizontalAlignment.Right, secondaryBrush);
        BuildHeaderText(BasesHeaderGrid, LocalizationManager.T("CacheClean.ColumnUserSize"), 2, HorizontalAlignment.Right, secondaryBrush);

        for (var col = 0; col < 3; col++)
            BasesHeaderGrid.Children.Add(BuildResizeGrip(col));

        UpdateHeaderIndicators();
    }

    /// <summary>Создаёт зону захвата на правой границе колонки (как в главном окне).</summary>
    private Border BuildResizeGrip(int column)
    {
        var grip = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Width = 8,
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeWE,
            ToolTip = LocalizationManager.T("CacheClean.ResizeColumnTooltip")
        };
        Grid.SetZIndex(grip, 1);
        Grid.SetColumn(grip, column);
        grip.MouseDown += OnResize_MouseDown;
        grip.MouseMove += OnResize_MouseMove;
        grip.MouseUp += OnResize_MouseUp;
        return grip;
    }

    private void OnResize_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border grip)
            return;

        var column = Grid.GetColumn(grip);
        if (column < 0 || column >= BasesHeaderGrid.ColumnDefinitions.Count)
            return;

        _resizeColumn = column;
        _resizeStartWidth = BasesHeaderGrid.ColumnDefinitions[column].ActualWidth;
        _resizeStartX = e.GetPosition(this).X;
        _isResizing = true;
        grip.CaptureMouse();
        e.Handled = true;
    }

    private void OnResize_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isResizing || _resizeColumn < 0 || sender is not UIElement element || !element.IsMouseCaptured)
            return;

        var newWidth = _resizeStartWidth + (e.GetPosition(this).X - _resizeStartX);
        if (newWidth < MinColumnWidth)
            newWidth = MinColumnWidth;

        SetColumnWidth(_resizeColumn, newWidth);
    }

    private void OnResize_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement element)
            element.ReleaseMouseCapture();

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

        BasesHeaderGrid.ColumnDefinitions[column].Width = ColLength(column, width);

        foreach (var row in _rows)
            row.ColumnDefinitions[column].Width = ColLength(column, width);
    }

    private void BuildBasesList(Infobase? defaultSelected)
    {
        BasesPanel.Children.Clear();
        _baseChecks.Clear();
        _baseRows.Clear();
        _programSizeTexts.Clear();
        _userSizeTexts.Clear();
        _rows.Clear();

        var checkStyle = (Style)FindResource("ModernMaterialCheckBox");
        var secondaryBrush = (Brush)FindResource("TextSecondaryBrush");

        foreach (var ib in _infobases)
        {
            var row = new Grid { Margin = new Thickness(4, 2, 4, 2) };
            ApplyColumns(row);

            var check = new CheckBox
            {
                Content = ib.Name,
                IsChecked = ReferenceEquals(ib, defaultSelected) || _preselected.Contains(ib),
                VerticalContentAlignment = VerticalAlignment.Center,
                Style = checkStyle,
                ToolTip = string.IsNullOrWhiteSpace(ib.ConnectionPathDisplay)
                    ? ib.Name
                    : ib.ConnectionPathDisplay
            };
            check.Checked += OnBaseChecked;
            check.Unchecked += OnBaseChecked;
            Grid.SetColumn(check, 0);
            row.Children.Add(check);

            // Запоминаем строку базы, предзаполненной по умолчанию, чтобы после
            // разметки прокрутить к ней список (issue #198).
            if (ReferenceEquals(ib, defaultSelected))
            {
                _defaultRow = row;
                _defaultCheck = check;
            }

            var programSize = BuildSizeText(secondaryBrush);
            Grid.SetColumn(programSize, 1);
            row.Children.Add(programSize);

            var userSize = BuildSizeText(secondaryBrush);
            Grid.SetColumn(userSize, 2);
            row.Children.Add(userSize);

            _baseChecks[check] = ib;
            _baseRows[check] = row;
            _rowByBase[ib] = row;
            _programSizeTexts[ib] = programSize;
            _userSizeTexts[ib] = userSize;
            _rows.Add(row);
            BasesPanel.Children.Add(row);
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
        // BringIntoView поднимает строку к видимой области внутри ScrollViewer
        // списка баз, как это уже делается для групп и строк в других окнах.
        _defaultRow.BringIntoView();
        _defaultCheck?.Focus();
    }

    /// <summary>
    /// Формирует кликабельный заголовок колонки списка баз. Клик по нему сортирует список
    /// по этой колонке, повторный клик меняет направление (issue #215).
    /// </summary>
    private void BuildHeaderText(Grid grid, string text, int column, HorizontalAlignment align, Brush brush)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = brush,
            HorizontalAlignment = align,
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Cursor = Cursors.Hand
        };
        block.MouseLeftButtonDown += (_, _) => OnHeaderClick(column);
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
        _headerTexts.Add(block);
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
                BasesPanel.Children.Remove(row);
                BasesPanel.Children.Insert(index, row);
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
    private static TextBlock BuildSizeText(Brush brush)
    {
        return new TextBlock
        {
            Text = "…",
            FontSize = 12,
            Foreground = brush,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
    }

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var check in _baseChecks.Keys)
            check.IsChecked = true;
        UpdateCount();
        UpdateCleanEnabled();
    }

    private void OnClearSelectionClick(object sender, RoutedEventArgs e)
    {
        foreach (var check in _baseChecks.Keys)
            check.IsChecked = false;
        UpdateCount();
        UpdateCleanEnabled();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        foreach (var kv in _baseChecks)
        {
            var visible = query.Length == 0
                || kv.Value.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (kv.Value.ConnectionPathDisplay?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);
            if (_baseRows.TryGetValue(kv.Key, out var row))
                row.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnBaseChecked(object sender, RoutedEventArgs e)
    {
        UpdateCount();
        UpdateCleanEnabled();
    }

    private void OnCacheKindChanged(object sender, RoutedEventArgs e)
    {
        UpdateCleanEnabled();
        // Подпись «Остатки» должна соответствовать выбранному типу кеша: раньше размер
        // всегда считался по обоим типам, а очистка шла по отмеченным галкам (issue #178).
        _ = RefreshOrphanSizeAsync();
    }

    /// <summary>
    /// Вычисляет и отображает размеры программного и пользовательского кеша.
    /// Итоговые строки показывают честный общий объём кэша в корнях (issue #109а),
    /// включая несопоставленные базе каталоги; строки по базам — только кэш этих баз.
    /// Расчёт выполняется в фоновом потоке, чтобы не блокировать интерфейс.
    /// </summary>
    private async Task RefreshCacheSizesAsync()
    {
        ProgramCacheSizeText.Text = "…";
        UserCacheSizeText.Text = "…";
        foreach (var t in _programSizeTexts.Values) t.Text = "…";
        foreach (var t in _userSizeTexts.Values) t.Text = "…";

        // Тип кеша читается ДО Task.Run: CurrentKind обращается к IsChecked переключателей,
        // а свойства элементов WPF доступны только из потока интерфейса. Вызов внутри лямбды
        // упал бы в потоке пула с InvalidOperationException, и при каждом открытии окна все
        // размеры оставались бы многоточиями (issue #212). Так же сделано в RefreshOrphanSizeAsync.
        var kind = CurrentKind();

        // Этапы считаются независимо, и каждый присваивает свой текст сразу по готовности,
        // чтобы отказ на одном шаге (например, orphan) не стирал уже посчитанные program/user
        // и не прерывал цикл по базам. Каждый этап логирует собственную ошибку (issue #212).
        try
        {
            // Честный общий объём (issue #109а): все каталоги корней, включая
            // несопоставленные базе, — иначе «15 ГБ» на диске показывались «15 МБ».
            var program = await Task.Run(() => OneCCacheCleaner.GetRootTotalSize(OneCCacheKind.Program));
            ProgramCacheSizeText.Text = FormatSize(program);
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка расчёта размера программного кеша", ex);
            ShowSizeError();
        }

        try
        {
            // Честный общий объём (issue #109а) — см. комментарий к блоку «программный».
            var user = await Task.Run(() => OneCCacheCleaner.GetRootTotalSize(OneCCacheKind.User));
            UserCacheSizeText.Text = FormatSize(user);
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка расчёта размера пользовательского кеша", ex);
            ShowSizeError();
        }

        try
        {
            var orphans = await Task.Run(() => OneCCacheCleaner.GetOrphanSize(kind, _infobases));
            OrphanCacheSizeText.Text = FormatSize(orphans);
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
        if (ProgramCacheCheck.IsChecked == true)
            kind |= OneCCacheKind.Program;
        if (UserCacheCheck.IsChecked == true)
            kind |= OneCCacheKind.User;
        return kind;
    }

    /// <summary>
    /// Пересчитывает размер «остатков» кеша по текущему выбору типа кеша, чтобы подпись
    /// соответствовала тому, что реально будет очищено (issue #178).
    /// </summary>
    private async Task RefreshOrphanSizeAsync()
    {
        var kind = CurrentKind();
        OrphanCacheSizeText.Text = "…";
        try
        {
            var orphans = await Task.Run(() => OneCCacheCleaner.GetOrphanSize(kind, _infobases));
            OrphanCacheSizeText.Text = FormatSize(orphans);
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка расчёта размера остатков кеша", ex);
            ShowSizeError();
        }
    }

    /// <summary>
    /// Форматирует размер в байтах в человекочитаемый вид с локализованными единицами.
    /// </summary>
    private static string FormatSize(long bytes)
    {
        // Единицы из локализации; если ключ пуст или задан мусором, остаёмся на байтах,
        // чтобы не упасть с IndexOutOfRangeException на units[0].
        var units = LocalizationManager.T("CacheClean.SizeUnits")
            .Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
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

    private void UpdateCount()
    {
        var selected = _baseChecks.Count(kv => kv.Key.IsChecked == true);
        var total = _baseChecks.Count;
        BasesCountText.Text = string.Format(LocalizationManager.T("CacheClean.CountSelected"), selected, total);
    }

    private void UpdateCleanEnabled()
    {
        var hasType = ProgramCacheCheck.IsChecked == true || UserCacheCheck.IsChecked == true;
        var hasBases = _baseChecks.Any(kv => kv.Key.IsChecked == true);
        var hasOrphans = OrphanCacheCheck.IsChecked == true;
        CleanButton.IsEnabled = hasType && (hasBases || hasOrphans);
    }

    private void OnClean_Click(object sender, RoutedEventArgs e)
    {
        var kind = OneCCacheKind.None;
        if (ProgramCacheCheck.IsChecked == true)
            kind |= OneCCacheKind.Program;
        if (UserCacheCheck.IsChecked == true)
            kind |= OneCCacheKind.User;

        SelectedCacheKind = kind;
        SelectedInfobases = _baseChecks
            .Where(kv => kv.Key.IsChecked == true)
            .Select(kv => kv.Value)
            .ToList();

        DialogResult = true;
    }
}
#endif