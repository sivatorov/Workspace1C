namespace Workspace1C;

/// <summary>
/// Чистые расчёты размеров/позиции окон (регресс «пустого незакрываемого окна»,
/// issues #308/#305). Авторазмер через <c>SizeToContent=Height</c> оказался хрупким:
/// при модальном показе окно могло схлопываться в пустой прямоугольник с «пропавшими»
/// надписями. Вместо него окна вычисляют нужную высоту явно (по измерению контента) и
/// клампят её в <c>[MinHeight, MaxHeight]</c>, а затем поднимают окно, если его низ
/// вышел за нижний край рабочей области. Класс не зависит от UI-платформы —
/// Windows/WPF и Linux/Avalonia используют одну логику; покрыт юнит-тестами.
/// </summary>
public static class WindowSizeMath
{
    /// <summary>
    /// Высота окна по желаемой высоте контента с ограничениями окна.
    /// Значения <c><= 0</c> означают «ограничение не задано».
    /// </summary>
    public static double ClampHeight(double desiredContent, double min, double max)
    {
        var result = desiredContent;
        if (min > 0 && result < min)
            result = min;
        if (max > 0 && result > max)
            result = max;
        return result;
    }

    /// <summary>
    /// Вертикальная позиция окна: если низ окна выходит за нижний край рабочей области,
    /// окно поднимается (но не выше верхнего края). Возвращает новое значение <c>Top</c>.
    /// При некорректной рабочей области возвращает исходное значение.
    /// </summary>
    public static double FitTop(double top, double height, double workTop, double workBottom)
    {
        if (workBottom <= workTop)
            return top;
        if (top + height > workBottom)
            return Math.Max(workTop, workBottom - height);
        return top;
    }

    /// <summary>
    /// Горизонтальная позиция окна: если правый край окна выходит за правый край рабочей
    /// области, окно сдвигается влево (но не левее левого края). Возвращает новое значение
    /// <c>Left</c>. Зеркально <see cref="FitTop"/> (issue #3, «Серверы 1С» уезжает за экран).
    /// При некорректной рабочей области возвращает исходное значение.
    /// </summary>
    public static double FitLeft(double left, double width, double workLeft, double workRight)
    {
        if (workRight <= workLeft)
            return left;
        if (left + width > workRight)
            return Math.Max(workLeft, workRight - width);
        return left;
    }

    /// <summary>
    /// Ограничение размера окна рабочим максимумом (например, шириной рабочей области
    /// экрана с запасом): если <paramref name="value"/> больше <paramref name="max"/> —
    /// возвращает <paramref name="max"/>, иначе исходное значение. Значения
    /// <c><= 0</c> означают «не задано» (value не ограничивается, max — без лимита).
    /// По образцу <see cref="ClampHeight"/> (issue #3).
    /// </summary>
    public static double ClampToWorkArea(double value, double max)
    {
        var result = value;
        if (max > 0 && result > max)
            result = max;
        return result;
    }

    /// <summary>
    /// Границы maximized-окна (в физических пикселях монитора) так, чтобы окно легло
    /// строго на рабочую область <paramref name="work"/> монитора
    /// <paramref name="monitor"/> (без панели задач/доков). Положение — смещение
    /// верхнего левого угла рабочей области относительно верхнего левого угла
    /// монитора; размер — сама рабочая область. Верно для панели задач с любой
    /// стороны и для мониторов со смещёнными/отрицательными координатами
    /// (issue #368: максимизация второстепенных окон уходила за панель задач).
    /// Прямоугольники задаются как <c>(Left, Top, Right, Bottom)</c> — как Win32 RECT.
    /// </summary>
    public static void ComputeMaximizedBounds(
        (int Left, int Top, int Right, int Bottom) monitor,
        (int Left, int Top, int Right, int Bottom) work,
        out int x, out int y, out int width, out int height)
    {
        x = work.Left - monitor.Left;
        y = work.Top - monitor.Top;
        width = work.Right - work.Left;
        height = work.Bottom - work.Top;
    }

    /// <summary>
    /// Радиус скругления видимой рамки окна, согласованный с системным скруглением
    /// углов DWM (issue #126): на Windows 11 DWM скругляет углы окна, и рамка должна
    /// рисоваться ПО скруглению (Border.CornerRadius = системному радиусу), иначе
    /// прямоугольная линия рамки «срезается» скруглённым клипом окна и по углам
    /// выглядит неаккуратно. Максимизированное окно DWM рисует с прямыми углами —
    /// радиус обнуляется; на Windows 10 (или когда DWM сообщает DWMWCS_DO_NOT_ROUND)
    /// скругления нет — рамка остаётся прямоугольной, как раньше. Класс не зависит
    /// от UI-платформы, поэтому чистая логика покрыта юнит-тестами
    /// (Issue126FrameCornerRadiusTests).
    /// </summary>
    /// <param name="dwmRounded">
    /// Фактическое состояние скругления DWM (DWMWA_WINDOW_CORNER_STATE = DWMWCS_ROUNDED).
    /// </param>
    /// <param name="maximized">Окно развёрнуто (DWM рисует maximized-окно с прямыми углами).</param>
    /// <param name="systemRadius">Системный радиус скругления в DIP (~8, совпадает с клипом DWM).</param>
    /// <returns>Радиус CornerRadius рамки и верхних углов полосы заголовка (DIP).</returns>
    public static double ComputeFrameCornerRadius(bool dwmRounded, bool maximized, double systemRadius)
    {
        if (maximized || !dwmRounded || systemRadius <= 0)
            return 0;
        return systemRadius;
    }
}