namespace Workspace1C.Themes;

/// <summary>
/// Единый источник метрик собственной полосы заголовка окна и системных кнопок
/// (issue #179, 1.0.1.128). Значения используются и WPF (<c>App.xaml</c>,
/// <c>MainWindow.xaml</c>, <c>WindowChromeHelper</c>), и Avalonia
/// (<c>UiMetrics</c>, <c>MainWindow.Avalonia</c>, <c>ModalWindowBase</c>).
/// <para>
/// До 1.0.1.128 главное окно жило с полосой 46 DIP (кнопки 34 + вертикальные
/// отступы 6+6), а дочерние окна — 30–34 DIP без правого отступа: кнопки
/// «свернуть/развернуть/закрыть» выглядели смещёнными вправо и вверх. Теперь
/// высота полосы главного окна равна высоте полосы диалогов, кнопки занимают
/// всю высоту полосы (вертикальное центрирование), а правый отступ одинаков
/// у всех окон (вариант из комментария 7OH: «рамку главного окна сделать такой
/// же, как у остальных», но «чтобы кнопки не были смещены»).
/// </para>
/// <para>
/// Файл собирается в обе платформенные сборки (без <c>#if</c>), чтобы метрики
/// нельзя было развести по платформам незаметно.
/// </para>
/// </summary>
public static class WindowChromeMetrics
{
    /// <summary>
    /// Высота полосы заголовка окна в обычном масштабе (DIP). Едина для главного
    /// окна и всех дочерних окон; ею же ограничена высота кнопок управления окном,
    /// поэтому кнопки занимают полосу целиком и центрируются по вертикали.
    /// </summary>
    public const double TitleBarHeight = 34;

    /// <summary>
    /// Высота кнопок управления окном (DIP). Равна <see cref="TitleBarHeight"/>:
    /// кнопка не «плавает» внутри полосы и не смещается вверх/вниз.
    /// </summary>
    public const double WindowButtonHeight = TitleBarHeight;

    /// <summary>
    /// Ширина кнопки управления окном (DIP), единая для главного окна и диалогов
    /// и не зависящая от темы (issue #179, 1.0.1.130). До этой правки ширина бралась
    /// из темы (<c>ThemeFeatureMap.Size</c>: 34/40/46), а высота — из метрики полосы,
    /// из-за чего кнопки выходили неквадратными и по-разному обрезались. Теперь
    /// ширина, высота и диаметр подложки — из одного источника.
    /// </summary>
    public const double WindowButtonWidth = 46;

    /// <summary>
    /// Диаметр круглой подложки кнопки управления окном (DIP). Едина для главного
    /// окна и диалогов (issue #179). Инвариант: не больше <see cref="WindowButtonWidth"/>
    /// и <see cref="WindowButtonHeight"/> — подложка всегда вписана в кнопку.
    /// </summary>
    public const double WindowButtonCircleDiameter = 30;

    /// <summary>Радиус подложки кнопки окна (половина диаметра) — для скруглений и масштаба.</summary>
    public const double WindowButtonCircleRadius = WindowButtonCircleDiameter / 2;

    /// <summary>
    /// Правый отступ группы кнопок управления окном от правого края полосы (DIP).
    /// Одинаков для главного окна и диалогов — кнопки не «прилипают» к краю.
    /// </summary>
    public const double WindowButtonRightInset = 12;

    /// <summary>
    /// Размер шрифта заголовка окна (DIP), общий для главного и дочерних окон
    /// (issue #179): стиль подписи шапки больше не расходится — везде полужирный
    /// шрифт одного размера. В компактном режиме уменьшается пропорционально.
    /// </summary>
    public const double TitleBarFontSize = 13;

    /// <summary>
    /// Левый внутренний отступ полосы заголовка (DIP): от края окна до заголовка.
    /// </summary>
    public const double TitleBarLeftInset = 12;

    /// <summary>
    /// Высота полосы заголовка главного окна в компактном режиме (issue #296).
    /// Полоса сжимается вместе с кнопками, но кнопки по-прежнему занимают её
    /// целиком и центрированы.
    /// </summary>
    public const double CompactTitleBarHeight = 30;

    /// <summary>Высота кнопок управления окном в компактном режиме (DIP).</summary>
    public const double CompactWindowButtonHeight = CompactTitleBarHeight;

    /// <summary>
    /// Ширина кнопки управления окном в компактном режиме (DIP). Сжимается вместе
    /// с полосой и высотой кнопки (issue #179/#296).
    /// </summary>
    public const double CompactWindowButtonWidth = 40;

    /// <summary>
    /// Диаметр подложки кнопки окна в компактном режиме (DIP). Инвариант:
    /// не больше <see cref="CompactWindowButtonWidth"/> и <see cref="CompactWindowButtonHeight"/> —
    /// круг остаётся вписанным в кнопку и центрируется в компактной полосе.
    /// </summary>
    public const double CompactWindowButtonCircleDiameter = 24;

    /// <summary>Боковой внутренний отступ полосы заголовка в компактном режиме (DIP).</summary>
    public const double CompactTitleBarSideInset = 8;

    /// <summary>
    /// Правый отступ группы кнопок окна в компактном режиме (DIP). Тот же боковой
    /// отступ, что и у полосы, — правый зазор не «прыгает» при включении компактного режима.
    /// </summary>
    public const double CompactWindowButtonRightInset = CompactTitleBarSideInset;
}
