namespace Workspace1C;

/// <summary>
/// Цвет рамки окна в sRGB (0–255), независимый от UI-платформы. Используется чистой
/// политикой <see cref="WindowFramePolicy"/>: WPF (<c>System.Windows.Media.Color</c>) и
/// Avalonia (<c>Avalonia.Media.Color</c>) конвертируют свои цвета в этот тип на границе,
/// поэтому расчёты контраста не зависят от выбранного UI-фреймворка и покрываются
/// юнит-тестами без запуска окна (issue #126).
/// </summary>
public readonly record struct WindowFrameColor(byte R, byte G, byte B)
{
    /// <summary>Белый цвет (предел осветления при подборе контраста).</summary>
    public static WindowFrameColor White => new(255, 255, 255);

    /// <summary>Чёрный цвет (предел затемнения при подборе контраста).</summary>
    public static WindowFrameColor Black => new(0, 0, 0);

    /// <summary>Строка вида <c>#RRGGBB</c> (для журнала и диагностики).</summary>
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>
    /// Разбирает <c>#RRGGBB</c>/<c>#AARRGGBB</c>. При некорректном значении возвращает
    /// <paramref name="fallback"/> — цвета темы приходят из ресурсов и в норме валидны,
    /// но диагностика/отрисовка не должны падать из-за нестандартной схемы.
    /// </summary>
    public static WindowFrameColor FromHex(string? hex, WindowFrameColor fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return fallback;

        var value = hex.Trim();
        if (value.StartsWith('#'))
            value = value[1..];

        // #AARRGGBB — берём RGB-часть; #RRGGBB — как есть.
        if (value.Length == 8)
            value = value[2..];
        if (value.Length != 6)
            return fallback;

        if (byte.TryParse(value[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
            byte.TryParse(value.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
            byte.TryParse(value.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
            return new WindowFrameColor(r, g, b);

        return fallback;
    }
}

/// <summary>
/// Входы чистой политики видимости рамки окна (issue #126). Отделены от UI и Win32:
/// решение детерминированно и тестируется без реальной Windows-сессии.
/// </summary>
/// <param name="CompositionEnabled">Композиция DWM включена (<c>DwmIsCompositionEnabled</c>).</param>
/// <param name="OsRoundsWindows">ОС скругляет углы окон (Windows 11, build ≥ 22000).</param>
/// <param name="DwmStateKnown">Удалось прочитать фактическое состояние скругления DWM.</param>
/// <param name="DwmRounded">Значение состояния DWM, если оно известно.</param>
/// <param name="Maximized">Окно развёрнуто.</param>
/// <param name="RemoteSession">Сеанс RDP/удалённого рабочего стола (<c>SM_REMOTESESSION</c>).</param>
/// <param name="DpiScale">Масштаб DPI окна (1.0 = 96 DPI).</param>
public readonly record struct WindowFrameInputs(
    bool CompositionEnabled,
    bool OsRoundsWindows,
    bool DwmStateKnown,
    bool DwmRounded,
    bool Maximized,
    bool RemoteSession,
    double DpiScale);

/// <summary>
/// Решение политики видимости рамки: толщина, нужно ли усиливать контраст,
/// добавлять внешнюю линию тени и привязывать к пиксельной сетке.
/// </summary>
/// <param name="ThicknessDip">Толщина видимой рамки в DIP (обычно 1).</param>
/// <param name="StrengthenContrast">
/// DWM не рисует собственное ребро/тень — цвет рамки нужно поднять до контраста ≥ 3:1,
/// иначе линия физически есть, но невидима (issue #126).
/// </param>
/// <param name="AddOuterShadowLine">Добавить полупрозрачную линию тени рядом с рамкой.</param>
/// <param name="SnapToPixels">Привязать рамку к физическим пикселям (чёткая линия на любом DPI).</param>
public readonly record struct WindowFrameVisual(
    double ThicknessDip,
    bool StrengthenContrast,
    bool AddOuterShadowLine,
    bool SnapToPixels);

/// <summary>
/// Чистая политика видимости рамки окна (issue #126 «Рамки для окон 2»). Причина дефекта:
/// на Windows 10/RDP DWM не рисует собственное ребро/тень окна, и остаётся только
/// приложенная рамка 1 DIP цветом <c>BorderBrushColor</c> (в светлой теме #E2E8F0 на фоне
/// #F1F5F9 — контраст ~1.13:1): линия есть, но невидима. На Windows 11 ту же роль играет
/// системное скругление/тень, поэтому там поведение не меняется.
/// <para>
/// Класс не зависит от UI-платформы (без <c>#if WINDOWS</c>), поэтому используется и WPF,
/// и Avalonia, и покрывается юнит-тестами. Контраст считается по WCAG 2.x.
/// </para>
/// </summary>
public static class WindowFramePolicy
{
    /// <summary>
    /// Минимальный контраст рамки к фону содержимого (WCAG 2.x, уровень для
    /// нетекстовых элементов интерфейса — 3:1). Ниже этого линия сливается с фоном.
    /// </summary>
    public const double MinContrastRatio = 3.0;

    /// <summary>Толщина рамки в DIP (совпадает с историческим значением 1 px).</summary>
    public const double FrameThicknessDip = 1.0;

    /// <summary>
    /// Решает, как рисовать видимую рамку окна.
    /// <list type="bullet">
    /// <item>DWM рисует ребро окна (композиция включена, сеанс локальный и окно
    /// скруглено — фактическое состояние DWM, а при его недоступности оценка по
    /// версии ОС): рамка обычная 1 DIP без усиления контраста и без линии тени —
    /// поведение Windows 11 не меняется;</item>
    /// <item>иначе (Windows 10/RDP, композиция выключена, DWM сообщил «не скруглять») —
    /// контраст усиливается, добавляется линия тени, рамка привязывается к пикселям;</item>
    /// <item>в развёрнутом окне линия тени не нужна: окно прижато к краям рабочей
    /// области, и дополнительная линия только «утолщала» бы рамку.</item>
    /// </list>
    /// В удалённом сеансе (<paramref name="inputs"/>.RemoteSession) системному
    /// ребру/тени DWM не доверяем даже на Windows 11: клиент RDP может не рисовать
    /// их, и рамка снова «исчезает» — включается тот же фолбэк, что на Windows 10.
    /// </summary>
    public static WindowFrameVisual Resolve(WindowFrameInputs inputs)
    {
        bool dwmDrawsEdge = inputs.CompositionEnabled &&
                            !inputs.RemoteSession &&
                            (inputs.DwmStateKnown ? inputs.DwmRounded : inputs.OsRoundsWindows);
        if (dwmDrawsEdge)
        {
            return new WindowFrameVisual(
                ThicknessDip: FrameThicknessDip,
                StrengthenContrast: false,
                AddOuterShadowLine: false,
                SnapToPixels: false);
        }

        return new WindowFrameVisual(
            ThicknessDip: FrameThicknessDip,
            StrengthenContrast: true,
            AddOuterShadowLine: !inputs.Maximized,
            SnapToPixels: true);
    }

    /// <summary>
    /// Относительная яркость цвета по WCAG 2.x (0 — чёрный, 1 — белый).
    /// </summary>
    public static double RelativeLuminance(WindowFrameColor color)
    {
        static double Channel(byte value)
        {
            var s = value / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    /// <summary>
    /// Контрастное соотношение двух цветов по WCAG 2.x (1..21). Например, для пары
    /// #E2E8F0 / #F1F5F9 (рамка светлой темы на фоне содержимого) — около 1.13:1,
    /// то есть линия невидима; после <see cref="EnsureContrast"/> — не ниже 3:1.
    /// </summary>
    public static double ContrastRatio(WindowFrameColor a, WindowFrameColor b)
    {
        var lighter = Math.Max(RelativeLuminance(a), RelativeLuminance(b));
        var darker = Math.Min(RelativeLuminance(a), RelativeLuminance(b));
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// Детерминированно поднимает контраст цвета рамки к фону до
    /// <paramref name="minRatio"/> (по умолчанию 3:1), затемняя или осветляя его.
    /// Направление выбирается по тому, какой предел (чёрный/белый) контрастнее к фону;
    /// затем двоичным поиском находится минимальное смещение, дающее нужный контраст.
    /// <para>
    /// Идемпотентно: если исходный цвет уже даёт контраст ≥ <paramref name="minRatio"/>,
    /// возвращается он же без изменений (повторный вызов ничего не «темнит»).
    /// </para>
    /// </summary>
    public static WindowFrameColor EnsureContrast(
        WindowFrameColor frame, WindowFrameColor background, double minRatio = MinContrastRatio)
    {
        if (double.IsNaN(minRatio) || minRatio <= 1)
            minRatio = MinContrastRatio;

        if (ContrastRatio(frame, background) >= minRatio)
            return frame;

        // Направление: к чёрному или к белому — что контрастнее к фону.
        var towardWhite = ContrastRatio(WindowFrameColor.White, background) >=
                          ContrastRatio(WindowFrameColor.Black, background);
        var target = towardWhite ? WindowFrameColor.White : WindowFrameColor.Black;

        // Двоичный поиск минимального смещения к выбранному пределу: контраст к
        // неизменному фону монотонно растёт при удалении от него.
        double low = 0, high = 1;
        for (var i = 0; i < 24; i++)
        {
            var mid = (low + high) / 2;
            if (ContrastRatio(Blend(frame, target, mid), background) >= minRatio)
                high = mid;
            else
                low = mid;
        }

        var result = Blend(frame, target, high);
        // Страховка от округления каналов: предел гарантированно даёт контраст
        // не ниже 4.58:1 к любому фону, поэтому результат всегда видим.
        return ContrastRatio(result, background) >= minRatio ? result : target;
    }

    /// <summary>Линейное смешивание двух цветов: <paramref name="t"/> = 0 — исходный, 1 — целевой.</summary>
    private static WindowFrameColor Blend(WindowFrameColor from, WindowFrameColor to, double t)
    {
        static byte Mix(byte a, byte b, double k) =>
            (byte)Math.Clamp(Math.Round(a + (b - a) * k, MidpointRounding.AwayFromZero), 0, 255);

        return new WindowFrameColor(Mix(from.R, to.R, t), Mix(from.G, to.G, t), Mix(from.B, to.B, t));
    }
}
