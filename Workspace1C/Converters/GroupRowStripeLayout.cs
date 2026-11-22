namespace Workspace1C.Converters;

/// <summary>
/// Единый расчёт геометрии цветного вертикального разделителя строки-папки
/// (issue #145). Используется и WPF-конвертером (<see cref="LevelToThicknessConverter"/>),
/// и Avalonia-разметкой (<c>MainWindow.Avalonia.Tree.cs</c>), чтобы полоса имела
/// одинаковый отступ слева (от иконки свёртки) и справа (до иконки папки/текста).
/// <para>
/// Раскладка по горизонтали относительно правого края кнопки свёртки
/// (<c>expanderRight</c>):
/// <code>
/// expanderRight | Gap | StripeWidth | Gap | ContentLeft
/// </code>
/// Вертикальный отступ <see cref="VerticalInset"/> не даёт торцам полосы выходить
/// за скруглённые углы рамки строки.
/// </para>
/// </summary>
public static class GroupRowStripeLayout
{
    /// <summary>Ширина цветной полосы-разделителя (px).</summary>
    public const double StripeWidth = 3;

    /// <summary>Одинаковый горизонтальный зазор слева и справа от полосы (px).</summary>
    public const double Gap = 4;

    /// <summary>Вертикальный отступ полосы внутри скруглённой рамки строки (px).</summary>
    public const double VerticalInset = 4;

    /// <summary>Сдвиг содержимого (иконка папки/текст) от правого края кнопки свёртки.</summary>
    public const double ContentOffset = StripeWidth + 2 * Gap;

    /// <summary>
    /// Левое поле рамки строки группы в WPF (<c>Padding="8,3"</c>): фон/полоса/содержимое
    /// позиционируются внутри содержимого рамки, поэтому при абсолютном расчёте от края
    /// кнопки свёртки это поле вычитается. Avalonia задаёт левое поле заголовка равным
    /// <see cref="Gap"/> напрямую, поэтому там вычитать не нужно.
    /// </summary>
    public const double WpfRowPadding = 8;

    /// <summary>Левый край полосы относительно правого края кнопки свёртки.</summary>
    public static double StripeLeft(double expanderRight) => expanderRight + Gap;

    /// <summary>Левый край содержимого (иконка папки/текст) относительно правого края кнопки свёртки.</summary>
    public static double ContentLeft(double expanderRight) => expanderRight + ContentOffset;

    /// <summary>Зазор слева: от кнопки свёртки до полосы.</summary>
    public static double LeftGap(double expanderRight) => StripeLeft(expanderRight) - expanderRight;

    /// <summary>Зазор справа: от полосы до содержимого.</summary>
    public static double RightGap(double expanderRight)
        => ContentLeft(expanderRight) - (StripeLeft(expanderRight) + StripeWidth);
}
