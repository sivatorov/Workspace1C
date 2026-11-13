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
    /// Радиус скругления собственного содержимого окон (DIP), единый для WPF и
    /// Avalonia (issue #126). Совпадает с радиусом «стеклянного» корня Avalonia
    /// (<c>UiMetrics.RadiusLg</c>). Используется как радиус рамки, когда состояние
    /// DWM неизвестно (RDP/Windows 10), но композиция включена: рамка повторяет
    /// скругление содержимого, а не остаётся квадратной вокруг скруглённых углов.
    /// </summary>
    public const double DefaultContentCornerRadius = 10;

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

    /// <summary>
    /// Решает, считать ли окно скруглённым для целей отрисовки рамки (issue #126,
    /// доработка 1.0.1.61). Главная причина «квадратной рамки при скруглённых углах»:
    /// <c>ApplyFrameCornerRadius</c> вызывается ДО показа окна, и DWM в этот момент
    /// часто сообщает <c>DWMWCS_DO_NOT_ROUND</c> (окно ещё не скомпоновано), хотя
    /// после показа скругляет его. Поэтому состояние DWM считается достоверным только
    /// для уже показанного окна; до показа используется оценка по версии ОС
    /// (Windows 11+ скругляет обычные окна), а фактическое состояние перечитывается
    /// после <c>Loaded</c>/<c>ContentRendered</c> и отложенным повтором.
    /// Класс не зависит от UI-платформы — покрыт юнит-тестами
    /// (Issue126FrameCornerRadiusTests).
    /// </summary>
    /// <param name="dwmStateKnown">
    /// Удалось ли прочитать <c>DWMWA_WINDOW_CORNER_STATE</c> (DWM ответил известным
    /// состоянием <c>DWMWCS_ROUNDED</c>/<c>DWMWCS_DO_NOT_ROUND</c>).
    /// </param>
    /// <param name="dwmRounded">Значение состояния DWM, если оно известно.</param>
    /// <param name="windowShown">
    /// Окно реально показано (есть HWND, <c>IsVisible</c> и <c>IsWindowVisible</c>).
    /// </param>
    /// <param name="osRoundsWindows">
    /// ОС скругляет углы обычных окон (Windows 11, build >= 22000).
    /// </param>
    /// <returns><c>true</c> — рамку и верхние углы шапки нужно скруглять.</returns>
    public static bool ResolveFrameRounding(
        bool dwmStateKnown, bool dwmRounded, bool windowShown, bool osRoundsWindows)
    {
        // Окно ещё не показано: состояние DWM недостоверно (часто DO_NOT_ROUND у
        // невидимого окна). Берём оценку по ОС, после показа — уточняем.
        if (!windowShown)
            return osRoundsWindows;
        if (dwmStateKnown)
            return dwmRounded;
        // Показанное окно, но DWM не ответил (RDP/отключённая композиция) — оценка по ОС.
        return osRoundsWindows;
    }

    /// <summary>
    /// Причина выбранного радиуса рамки (issue #126). Попадает в журнал
    /// (<c>policy=…</c>), чтобы по диагностике было видно, почему рамка скруглена
    /// или осталась прямоугольной.
    /// </summary>
    public enum FrameCornerPolicy
    {
        /// <summary>Углы прямые: композиция выключена или своего скругления нет.</summary>
        CompositionOff,
        /// <summary>Окно развёрнуто — DWM рисует прямые углы.</summary>
        Maximized,
        /// <summary>DWM сообщил, что окно скруглено — рамка по системному радиусу.</summary>
        DwmRounded,
        /// <summary>DWM явно сообщил «не скруглять» — рамка прямоугольная.</summary>
        DwmDoNotRound,
        /// <summary>
        /// Состояние DWM неизвестно (RDP/Windows 10), композиция включена — рамка
        /// повторяет радиус собственного скруглённого содержимого приложения.
        /// </summary>
        ContentRadius
    }

    /// <summary>
    /// Входы чистой политики скругления рамки (issue #126). Отделены от UI/DWM,
    /// поэтому решение детерминированно тестируется без реальной Windows-сессии.
    /// </summary>
    /// <param name="DwmStateKnown">Удалось прочитать состояние скругления DWM.</param>
    /// <param name="DwmRounded">Значение состояния DWM, если оно известно.</param>
    /// <param name="WindowShown">Окно реально показано (есть HWND и WS_VISIBLE).</param>
    /// <param name="Maximized">Окно развёрнуто.</param>
    /// <param name="CompositionEnabled">Композиция DWM включена (DwmIsCompositionEnabled).</param>
    /// <param name="OsRoundsWindows">ОС скругляет окна (Windows 11, build >= 22000).</param>
    /// <param name="ContentCornerRadius">Радиус собственного скругления содержимого (DIP).</param>
    /// <param name="SystemRadius">Системный радиус скругления DWM (DIP, ~8).</param>
    public readonly record struct FrameRoundingInputs(
        bool DwmStateKnown,
        bool DwmRounded,
        bool WindowShown,
        bool Maximized,
        bool CompositionEnabled,
        bool OsRoundsWindows,
        double ContentCornerRadius,
        double SystemRadius);

    /// <summary>Результат политики: радиус рамки и причина решения.</summary>
    public readonly record struct FrameCornerRadiusResult(double Radius, FrameCornerPolicy Policy);

    /// <summary>
    /// Единая политика скругления видимой рамки окна (issue #126), общая для WPF и
    /// Avalonia. Правила (в порядке приоритета):
    /// <list type="bullet">
    /// <item>развёрнутое окно — прямые углы;</item>
    /// <item>показанное окно с достоверным состоянием DWM — следуем ему
    /// (пользователь/система могли отключить скругление);</item>
    /// <item>ОС скругляет окна (Windows 11) — системный радиус;</item>
    /// <item>приложение само рисует скруглённое содержимое (RDP/Windows 10, а также
    /// выключенная композиция при собственном скруглении) — радиус содержимого,
    /// чтобы рамка не оставалась квадратной вокруг скруглённых углов;</item>
    /// <item>своего скругления нет — прямые углы (не «угадываем» скругление).</item>
    /// </list>
    /// <para>
    /// Состояние DWM учитывается только при включённой композиции: без композиции
    /// DWM не клипует окно, поэтому доверять его ответу нельзя. Это и есть случай
    /// RDP на Windows 10: атрибут <c>DWMWA_WINDOW_CORNER_STATE</c> недоступен
    /// (<c>dwmState=unknown</c>), <c>osRounds=false</c>, но содержимое приложения
    /// скруглено — рамка должна повторять его радиус.
    /// </para>
    /// </summary>
    public static FrameCornerRadiusResult ResolveFrameCornerRadius(FrameRoundingInputs inputs)
    {
        if (inputs.Maximized)
            return new(0, FrameCornerPolicy.Maximized);

        double systemRadius = inputs.SystemRadius > 0 ? inputs.SystemRadius : 0;
        double contentRadius = inputs.ContentCornerRadius > 0 ? inputs.ContentCornerRadius : 0;

        // Показанное окно с достоверным состоянием DWM: следуем фактическому
        // состоянию. При выключенной композиции ответ DWM недостоверен — пропускаем.
        if (inputs.CompositionEnabled && inputs.WindowShown && inputs.DwmStateKnown)
        {
            if (inputs.DwmRounded && systemRadius > 0)
                return new(systemRadius, FrameCornerPolicy.DwmRounded);
            return new(0, FrameCornerPolicy.DwmDoNotRound);
        }

        // Windows 11 (в том числе до показа окна, когда состояние DWM ещё
        // недостоверно): системный радиус — поведение 1.0.1.61 без регрессии.
        if (inputs.CompositionEnabled && inputs.OsRoundsWindows && systemRadius > 0)
            return new(systemRadius, FrameCornerPolicy.DwmRounded);

        // DWM не сообщил состояние (RDP/Windows 10) либо композиция выключена, но
        // приложение само рисует скруглённое содержимое — повторяем его радиус.
        // Если своего скругления нет, ниже вернём прямые углы.
        if (contentRadius > 0)
            return new(contentRadius, FrameCornerPolicy.ContentRadius);

        // Композиция есть/нет, но своего скругления нет — углы последовательно прямые.
        return new(0, FrameCornerPolicy.CompositionOff);
    }
}