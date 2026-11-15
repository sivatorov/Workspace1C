using System;
using System.IO;
using Workspace1C;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #126 «Рамки для окон 2», доработка 1.0.1.65: после появления рамки и
/// скругления остались «белые углы за пределами рамки» (RDP, Windows 10 22H2).
/// Причина: клип содержимого обрезает только содержимое, а <c>Window.Background</c>
/// заливает весь прямоугольник HWND — на Windows 10 DWM не обрезает окно по
/// скруглению, и четыре треугольника вне рамки остаются фоном темы.
/// <para>
/// Решение проверяется на трёх уровнях:
/// <list type="bullet">
/// <item>чистая логика <see cref="WindowSizeMath.ResolveWindowRegion"/> — нужен ли
/// регион окна и какой диаметр скругления (Win10/RDP, Win11, maximized,
/// выключенная композиция, opt-out, DPI 100/125/150%);</item>
/// <item>source-гварды WPF: <c>CreateRoundRectRgn</c>/<c>SetWindowRgn</c>, снятие
/// региона при maximized/Windows 11/opt-out, пересчёт на <c>WM_SIZE</c>,
/// прозрачный фон окна и подложка темы на контейнере содержимого, поля
/// диагностики <c>regionApplied</c>/<c>regionRadius</c>;</item>
/// <item>Avalonia: fallback «прямые углы» там, где прозрачность недоступна
/// (иначе белые треугольники появились бы и на Linux/в непрозрачном режиме).</item>
/// </list>
/// </para>
/// Существующие <c>Issue126WindowFrameTests</c>, <c>Issue126FrameCornerRadiusTests</c>
/// и <c>Issue126FrameVisibilityTests</c> не дублируются.
/// </summary>
public sealed class Issue126WindowRegionTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Views")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Workspace1C.Tests")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Корень репозитория не найден из тестового каталога.");
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    /// <summary>
    /// Приводит исходник к виду, устойчивому к форматированию: любые последовательности
    /// пробельных символов (включая переводы строк и отступы) становятся одним пробелом.
    /// </summary>
    private static string NormalizeWhitespace(string code)
    {
        var result = new System.Text.StringBuilder(code.Length);
        var inWhitespace = false;
        foreach (var ch in code)
        {
            if (char.IsWhiteSpace(ch))
            {
                inWhitespace = true;
                continue;
            }

            if (inWhitespace && result.Length > 0)
                result.Append(' ');
            inWhitespace = false;
            result.Append(ch);
        }

        return result.ToString();
    }

    // ===================== Чистая логика: ResolveWindowRegion =====================

    /// <summary>Системный радиус скругления DWM на Windows 11 (DIP), как в WindowChromeHelper.</summary>
    private const double SystemRadius = 8;

    /// <summary>Ровно входы RDP на Windows 10 из журнала frame-corner (issue #126).</summary>
    private static WindowSizeMath.FrameCornerRadiusResult RdpWin10Frame(
        bool maximized = false,
        bool compositionEnabled = true,
        bool shown = true) =>
        WindowSizeMath.ResolveFrameCornerRadius(new WindowSizeMath.FrameRoundingInputs(
            DwmStateKnown: false,
            DwmRounded: false,
            WindowShown: shown,
            Maximized: maximized,
            CompositionEnabled: compositionEnabled,
            OsRoundsWindows: false,
            ContentCornerRadius: WindowSizeMath.DefaultContentCornerRadius,
            SystemRadius: SystemRadius));

    /// <summary>Windows 11 с системным скруглением DWM.</summary>
    private static WindowSizeMath.FrameCornerRadiusResult Win11Frame(bool maximized = false) =>
        WindowSizeMath.ResolveFrameCornerRadius(new WindowSizeMath.FrameRoundingInputs(
            DwmStateKnown: true,
            DwmRounded: true,
            WindowShown: true,
            Maximized: maximized,
            CompositionEnabled: true,
            OsRoundsWindows: true,
            ContentCornerRadius: WindowSizeMath.DefaultContentCornerRadius,
            SystemRadius: SystemRadius));

    private static WindowSizeMath.WindowRegionResult Region(
        WindowSizeMath.FrameCornerRadiusResult frame,
        bool frameOptOut = false,
        double dpiScale = 1.0) =>
        WindowSizeMath.ResolveWindowRegion(new WindowSizeMath.WindowRegionInputs(
            Policy: frame.Policy,
            RadiusDip: frame.Radius,
            FrameOptOut: frameOptOut,
            DpiScale: dpiScale));

    [Fact]
    public void ResolveWindowRegion_RdpWin10_OwnRounding_AppliesRegion()
    {
        // Windows 10/RDP: DWM не скругляет окно, скругление рисуем мы сами
        // (policy=ContentRadius, радиус 10) — HWND нужно обрезать регионом,
        // иначе вне рамки остаются «белые углы».
        var frame = RdpWin10Frame();
        Assert.Equal(WindowSizeMath.FrameCornerPolicy.ContentRadius, frame.Policy);

        var region = Region(frame);

        Assert.True(region.Apply);
        Assert.Equal((int)Math.Ceiling(frame.Radius * 2), region.DiameterPixels);
        Assert.Equal(20, region.DiameterPixels);
    }

    [Fact]
    public void ResolveWindowRegion_Win11SystemRounding_NoRegion()
    {
        // Windows 11: DWM сам скругляет окно; регион сломал бы системную тень.
        var frame = Win11Frame();
        Assert.Equal(WindowSizeMath.FrameCornerPolicy.DwmRounded, frame.Policy);

        var region = Region(frame);

        Assert.False(region.Apply);
        Assert.Equal(0, region.DiameterPixels);
    }

    [Fact]
    public void ResolveWindowRegion_Maximized_NoRegion()
    {
        // В maximized углы прямые: регион не нужен и должен быть снят.
        var region = Region(RdpWin10Frame(maximized: true));

        Assert.False(region.Apply);
        Assert.Equal(0, region.DiameterPixels);
    }

    [Fact]
    public void ResolveWindowRegion_CompositionDisabledWithOwnRounding_AppliesRegion()
    {
        // Композиция выключена, но содержимое скругляем мы сами: DWM окно
        // не обрезает — регион нужен (та же логика, что и в RDP).
        var frame = RdpWin10Frame(compositionEnabled: false);
        Assert.Equal(WindowSizeMath.FrameCornerPolicy.ContentRadius, frame.Policy);

        var region = Region(frame);

        Assert.True(region.Apply);
        Assert.Equal(20, region.DiameterPixels);
    }

    [Fact]
    public void ResolveWindowRegion_CompositionDisabledWithoutOwnRounding_NoRegion()
    {
        // Композиция выключена и своего скругления нет — прямые углы, регион не нужен.
        var frame = WindowSizeMath.ResolveFrameCornerRadius(new WindowSizeMath.FrameRoundingInputs(
            DwmStateKnown: false,
            DwmRounded: false,
            WindowShown: true,
            Maximized: false,
            CompositionEnabled: false,
            OsRoundsWindows: false,
            ContentCornerRadius: 0,
            SystemRadius: SystemRadius));

        Assert.Equal(WindowSizeMath.FrameCornerPolicy.CompositionOff, frame.Policy);
        Assert.False(Region(frame).Apply);
    }

    [Fact]
    public void ResolveWindowRegion_FrameOptOut_NoRegion()
    {
        // Окно отказалось от рамки/скругления (ShowWindowFrame=false) — регион не нужен.
        var region = Region(RdpWin10Frame(), frameOptOut: true);

        Assert.False(region.Apply);
        Assert.Equal(0, region.DiameterPixels);
    }

    [Fact]
    public void ResolveWindowRegion_DwmDoNotRound_NoRegion()
    {
        // DWM явно сообщил «не скруглять»: рамка и содержимое прямоугольные.
        var frame = WindowSizeMath.ResolveFrameCornerRadius(new WindowSizeMath.FrameRoundingInputs(
            DwmStateKnown: true,
            DwmRounded: false,
            WindowShown: true,
            Maximized: false,
            CompositionEnabled: true,
            OsRoundsWindows: true,
            ContentCornerRadius: WindowSizeMath.DefaultContentCornerRadius,
            SystemRadius: SystemRadius));

        Assert.Equal(WindowSizeMath.FrameCornerPolicy.DwmDoNotRound, frame.Policy);
        Assert.False(Region(frame).Apply);
    }

    [Fact]
    public void ResolveWindowRegion_ZeroRadius_NoRegion()
    {
        // Радиус 0 — регион с нулевым диаметром не создаём.
        var region = WindowSizeMath.ResolveWindowRegion(new WindowSizeMath.WindowRegionInputs(
            Policy: WindowSizeMath.FrameCornerPolicy.ContentRadius,
            RadiusDip: 0,
            FrameOptOut: false,
            DpiScale: 1.0));

        Assert.False(region.Apply);
        Assert.Equal(0, region.DiameterPixels);
    }

    [Theory]
    [InlineData(1.0, 20)]
    [InlineData(1.25, 25)]
    [InlineData(1.5, 30)]
    public void ResolveWindowRegion_DiameterScalesWithDpi(double dpiScale, int expectedDiameter)
    {
        // Диаметр региона — в физических пикселях: radius(10) × scale × 2.
        // На 125%/150% регион не должен оказаться меньше фактического скругления.
        var region = Region(RdpWin10Frame(), dpiScale: dpiScale);

        Assert.True(region.Apply);
        Assert.Equal(expectedDiameter, region.DiameterPixels);
    }

    [Fact]
    public void ResolveWindowRegion_DiameterRoundsUp()
    {
        // Округление вверх: 7.3 × 1.25 × 2 = 18.25 → 19 физических пикселей.
        var region = WindowSizeMath.ResolveWindowRegion(new WindowSizeMath.WindowRegionInputs(
            Policy: WindowSizeMath.FrameCornerPolicy.ContentRadius,
            RadiusDip: 7.3,
            FrameOptOut: false,
            DpiScale: 1.25));

        Assert.True(region.Apply);
        Assert.Equal(19, region.DiameterPixels);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void ResolveWindowRegion_InvalidDpi_FallsBackTo100Percent(double dpiScale)
    {
        // Некорректный DPI не должен «обнулять» регион — считаем масштаб 1.0.
        var region = Region(RdpWin10Frame(), dpiScale: dpiScale);

        Assert.True(region.Apply);
        Assert.Equal(20, region.DiameterPixels);
    }

    // ===================== Source-гварды: WPF (WindowChromeHelper) =====================

    [Fact]
    public void WindowChromeHelper_Wpf_AppliesRoundedWindowRegion()
    {
        // Основное детерминированное решение #126: HWND обрезается по скруглённому
        // прямоугольнику тем же радиусом, что и рамка/клип содержимого.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));

        Assert.Contains("CreateRoundRectRgn", code, StringComparison.Ordinal);
        Assert.Contains("SetWindowRgn", code, StringComparison.Ordinal);
        Assert.Contains("WindowSizeMath.ResolveWindowRegion(new WindowSizeMath.WindowRegionInputs(", code,
            StringComparison.Ordinal);
        Assert.Contains("Policy: policy.Policy", code, StringComparison.Ordinal);
        Assert.Contains("RadiusDip: radius", code, StringComparison.Ordinal);
        Assert.Contains("bool regionApplied = ApplyWindowRegion(window, hwnd, region);", code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WindowChromeHelper_Wpf_RemovesRegionWhenNotNeeded()
    {
        // Windows 11 с системным скруглением, maximized, opt-out и прямые углы —
        // регион снимается, иначе пропала бы системная тень/анимация DWM.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));

        Assert.Contains("SetWindowRgn(hwnd, IntPtr.Zero, true)", code, StringComparison.Ordinal);
        Assert.Contains("if (!region.Apply)", code, StringComparison.Ordinal);
        Assert.Contains("FrameOptOut: !GetShowWindowFrame(window)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowChromeHelper_Wpf_ReappliesRegionOnResize()
    {
        // Регион задан в координатах HWND: после изменения размера его нужно
        // пересоздать, иначе углы обрежутся по прежнему размеру окна.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));

        Assert.Contains("private const int WmSize = 0x0005;", code, StringComparison.Ordinal);
        Assert.Contains("if (msg == WmSize)", code, StringComparison.Ordinal);
        Assert.Contains("RefreshFrameCornerRadius(window, \"size-changed\")", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowChromeHelper_Wpf_LogsRegionFields()
    {
        // По ZIP-диагностике (CM_THEMES, frame-corner) должно быть видно, что
        // регион применён и с каким радиусом/диаметром.
        var code = NormalizeWhitespace(
            ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs")));

        Assert.Contains("regionApplied=", code, StringComparison.Ordinal);
        Assert.Contains("regionRadius=", code, StringComparison.Ordinal);
        Assert.Contains("regionDiameter=", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowChromeHelper_Wpf_MovesThemeBackgroundToContentHost()
    {
        // Страховка от «белых углов»: подложка темы переносится на контейнер
        // содержимого (обрезан по скруглению), а фон окна в режиме собственного
        // скругления становится прозрачным.
        var code = NormalizeWhitespace(
            ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs")));

        Assert.Contains("if (GetContentBorder(window) is { } content)", code, StringComparison.Ordinal);
        Assert.Contains("content.Background = new SolidColorBrush(glassColor)", code, StringComparison.Ordinal);
        Assert.Contains("bool ownRounding = GetFrameTracking(window)?.OwnRounding == true;", code,
            StringComparison.Ordinal);
        Assert.Contains("ownRounding ? Colors.Transparent : glassColor", code, StringComparison.Ordinal);
    }

    // ===================== Source-гварды: Avalonia (fallback) =====================

    [Fact]
    public void Avalonia_Dialogs_DropRoundingWithoutTransparency()
    {
        // На Linux без прозрачности (X11 без композитора, виртуализация) скруглённая
        // подложка оставляла бы в углах непрозрачную заливку окна — «белые
        // треугольники». Fallback — прямые углы (вариант 7OH «убирать скругления»).
        var code = NormalizeWhitespace(
            ReadProjectFile(Path.Combine("Workspace1C", "Views", "ModalWindowBase.cs")));

        Assert.Contains("ShouldRenderOpaque ? 0 : WindowSizeMath.DefaultContentCornerRadius", code,
            StringComparison.Ordinal);
        Assert.Contains("CornerRadius = new CornerRadius(GlassCornerRadius)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_MainWindow_DropsRoundingWithoutTransparency()
    {
        // Главное окно Avalonia: тот же fallback — без прозрачности (системный
        // заголовок/непрозрачный режим) углы прямые, чтобы не было белых углов.
        var code = NormalizeWhitespace(
            ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs")));

        Assert.Contains("_useSystemTitleBar || _opaqueWindow ? 0 : WindowSizeMath.DefaultContentCornerRadius", code,
            StringComparison.Ordinal);
        Assert.Contains("CornerRadius = new CornerRadius(GlassCornerRadius)", code, StringComparison.Ordinal);
    }

    // ===================== Чистая политика — общая для платформ =====================

    [Fact]
    public void WindowSizeMath_ResolveWindowRegion_IsCrossPlatform()
    {
        // Политика региона — чистая логика без платформенных директив: покрывается
        // юнит-тестами и не зависит от UI-фреймворка.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Services", "WindowSizeMath.cs"));

        Assert.Contains("ResolveWindowRegion", code, StringComparison.Ordinal);
        Assert.DoesNotContain("#if WINDOWS", code, StringComparison.Ordinal);
    }
}
