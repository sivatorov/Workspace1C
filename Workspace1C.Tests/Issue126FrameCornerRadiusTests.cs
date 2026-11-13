using System;
using System.IO;
using Workspace1C;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #126 «Рамки для окон 2», доработка 1.0.1.59: рамка внутренних окон должна
/// повторять системное скругление углов. На Windows 11 DWM скругляет углы окна
/// (DWMWA_WINDOW_CORNER_STATE = DWMWCS_ROUNDED), и прямоугольная рамка-оверлей
/// «срезалась» скруглённым клипом окна — по углам линия обрывалась, что выглядело
/// неаккуратно. Решение: Border.CornerRadius рамки синхронизируется с фактическим
/// состоянием DWM через <see cref="WindowSizeMath.ComputeFrameCornerRadius"/>.
/// Проверяются: чистая логика расчёта радиуса (все границы состояний) и
/// source-гварды правок <c>WindowChromeHelper</c>.
/// </summary>
public sealed class Issue126FrameCornerRadiusTests
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

    // ---------- Чистая логика расчёта радиуса (WindowSizeMath) ----------

    /// <summary>Системный радиус скругления DWM на Windows 11 (DIP), как в WindowChromeHelper.</summary>
    private const double SystemRadius = 8;

    [Fact]
    public void ComputeFrameCornerRadius_Win11RoundedWindow_EqualsSystemRadius()
    {
        // Обычное (не maximized) окно на Windows 11: DWMWCS_ROUNDED — рамка рисуется
        // по скруглению с системным радиусом, линия не «срезается» клипом окна.
        Assert.Equal(SystemRadius,
            WindowSizeMath.ComputeFrameCornerRadius(dwmRounded: true, maximized: false, SystemRadius));
    }

    [Fact]
    public void ComputeFrameCornerRadius_Maximized_IsZero()
    {
        // Максимизированное окно DWM рисует с прямыми углами: скруглённая рамка
        // оставила бы «недозалитые» углы по краям рабочей области.
        Assert.Equal(0,
            WindowSizeMath.ComputeFrameCornerRadius(dwmRounded: true, maximized: true, SystemRadius));
    }

    [Fact]
    public void ComputeFrameCornerRadius_Win10_NoRounding_IsZero()
    {
        // Windows 10 (build < 22000): системного скругления нет — рамка остаётся
        // прямоугольной, как до доработки 1.0.1.59.
        Assert.Equal(0,
            WindowSizeMath.ComputeFrameCornerRadius(dwmRounded: false, maximized: false, SystemRadius));
    }

    [Fact]
    public void ComputeFrameCornerRadius_DwmDoNotRound_IsZero()
    {
        // DWM сообщает DWMWCS_DO_NOT_ROUND (скругление отключено пользователем/системой):
        // рамка не должна скругляться «наугад», чтобы не вылезать за прямоугольный клип.
        Assert.Equal(0,
            WindowSizeMath.ComputeFrameCornerRadius(dwmRounded: false, maximized: false, SystemRadius));
    }

    [Fact]
    public void ComputeFrameCornerRadius_CustomRadius_IsUsed()
    {
        // Кастомный системный радиус (например, если DWM изменит скругление):
        // логика возвращает его как есть, не привязываясь к литералу 8.
        Assert.Equal(6.5,
            WindowSizeMath.ComputeFrameCornerRadius(dwmRounded: true, maximized: false, 6.5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ComputeFrameCornerRadius_NonPositiveRadius_IsZero(double systemRadius)
    {
        // Некорректный (неположительный) радиус — рамка прямоугольная, а не «ломаная».
        Assert.Equal(0,
            WindowSizeMath.ComputeFrameCornerRadius(dwmRounded: true, maximized: false, systemRadius));
    }

    [Fact]
    public void ComputeFrameCornerRadius_MaximizedWinsOverCustomRadius()
    {
        // Максимизация сильнее любого радиуса: даже при кастомном скруглении
        // maximized-окно получает прямые углы.
        Assert.Equal(0,
            WindowSizeMath.ComputeFrameCornerRadius(dwmRounded: true, maximized: true, 6.5));
    }

    // ---------- Состояние DWM достоверно только после показа окна (1.0.1.61) ----------

    [Fact]
    public void ResolveFrameRounding_HiddenWindow_IgnoresDwmDoNotRound_OnWin11()
    {
        // Исходный дефект #126: ApplyFrameCornerRadius вызывался ДО показа окна, DWM
        // сообщал DWMWCS_DO_NOT_ROUND (окно ещё не скомпоновано), радиус получался 0,
        // а после показа DWM скруглял углы — «скругление с квадратной рамкой».
        // Для невидимого окна состояние DWM недостоверно: берём оценку по версии ОС.
        Assert.True(WindowSizeMath.ResolveFrameRounding(
            dwmStateKnown: true, dwmRounded: false, windowShown: false, osRoundsWindows: true));
    }

    [Fact]
    public void ResolveFrameRounding_HiddenWindow_OnWin10_NoRounding()
    {
        // Windows 10 (build < 22000) не скругляет окна: даже для невидимого окна
        // рамка остаётся прямоугольной, как раньше.
        Assert.False(WindowSizeMath.ResolveFrameRounding(
            dwmStateKnown: true, dwmRounded: false, windowShown: false, osRoundsWindows: false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveFrameRounding_ShownWindow_UsesActualDwmState(bool dwmRounded)
    {
        // Показанное окно: фактическое состояние DWM важнее оценки по версии ОС
        // (пользователь/система могли отключить скругление, RDP, maximized).
        Assert.Equal(dwmRounded, WindowSizeMath.ResolveFrameRounding(
            dwmStateKnown: true,
            dwmRounded: dwmRounded,
            windowShown: true,
            osRoundsWindows: !dwmRounded));
    }

    [Fact]
    public void ResolveFrameRounding_ShownWindow_UnknownDwm_FallsBackToOs()
    {
        // Показанное окно, но DWM не ответил (RDP/отключённая композиция) —
        // остаётся оценка по версии ОС, исключений нет.
        Assert.True(WindowSizeMath.ResolveFrameRounding(
            dwmStateKnown: false, dwmRounded: false, windowShown: true, osRoundsWindows: true));
        Assert.False(WindowSizeMath.ResolveFrameRounding(
            dwmStateKnown: false, dwmRounded: true, windowShown: true, osRoundsWindows: false));
    }

    [Fact]
    public void ResolveFrameRounding_HiddenWindowOnWin11_GivesSystemRadius()
    {
        // Комбинация «оценка до показа» + расчёт радиуса: при первом показе на
        // Windows 11 рамка уже скруглена системным радиусом — без «прыжка» рамки.
        var rounded = WindowSizeMath.ResolveFrameRounding(
            dwmStateKnown: true, dwmRounded: false, windowShown: false, osRoundsWindows: true);
        Assert.Equal(SystemRadius,
            WindowSizeMath.ComputeFrameCornerRadius(rounded, maximized: false, SystemRadius));
    }

    // ---------- Source-гварды: WindowChromeHelper (WPF) ----------

    [Fact]
    public void WindowChromeHelper_Wpf_ReadsActualDwmCornerState()
    {
        // Скругление определяется по фактическому состоянию DWM
        // (DWMWA_WINDOW_CORNER_STATE, DWMWCS_ROUNDED/DO_NOT_ROUND), а не «наугад»;
        // при недоступности атрибута — оценка по версии ОС (build < 22000 — без скругления).
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
        Assert.Contains("DwmGetWindowAttribute", code, StringComparison.Ordinal);
        Assert.Contains("DwmWindowCornerState", code, StringComparison.Ordinal);
        Assert.Contains("DwmStateRounded", code, StringComparison.Ordinal);
        Assert.Contains("Build < 22000", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowChromeHelper_Wpf_FrameUsesComputedCornerRadius()
    {
        // Рамка-оверлей получает CornerRadius из единой чистой политики
        // WindowSizeMath.ResolveFrameCornerRadius (issue #126, доработка 1.0.1.62).
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
        Assert.Contains("WindowSizeMath.ResolveFrameCornerRadius(new WindowSizeMath.FrameRoundingInputs(", code,
            StringComparison.Ordinal);
        Assert.Contains("var radius = policy.Radius;", code, StringComparison.Ordinal);
        Assert.Contains("frame.CornerRadius = new CornerRadius(radius)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowChromeHelper_Wpf_FrameRadiusFollowsWindowStateChanges()
    {
        // Радиус пересчитывается не только при показе окна, но и при
        // максимизации/восстановлении (StateChanged) — рамка не остаётся
        // скруглённой у maximized-окна с прямыми углами DWM.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
        var first = code.IndexOf("ApplyFrameCornerRadius(window);", StringComparison.Ordinal);
        var second = code.IndexOf("ApplyFrameCornerRadius(window);", first + 1, StringComparison.Ordinal);
        Assert.True(first >= 0, "ApplyFrameCornerRadius не вызывается после показа окна.");
        Assert.True(second > first, "ApplyFrameCornerRadius не вызывается в StateChanged-хуке.");
        Assert.Contains("window.StateChanged", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowChromeHelper_Wpf_TitleBarRadiusFollowsFrame()
    {
        // Полоса заголовка повторяет геометрию верхних углов окна: обновляется тем же
        // механизмом (radius, radius, 0, 0) — при maximized углы шапки прямые (issue #135).
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
        Assert.Contains("SetTitleBarBorder(window, bar)", code, StringComparison.Ordinal);
        Assert.Contains("new CornerRadius(radius, radius, 0, 0)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowChromeHelper_Wpf_FrameRecolorAndOptOutPreserved()
    {
        // Функционал 1.0.1.58 не потерян: перекраска рамки из темы (DynamicResource
        // BorderBrushColor) и opt-out через ShowWindowFrame=false сохранены.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
        Assert.Contains("Border.BorderBrushProperty, \"BorderBrushColor\"", code, StringComparison.Ordinal);
        Assert.Contains("if (!GetShowWindowFrame(window))", code, StringComparison.Ordinal);
        Assert.Contains("BorderThickness = new Thickness(1)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowSizeMath_ComputeFrameCornerRadius_IsCrossPlatform()
    {
        // Чистая логика живёт в платформо-независимом WindowSizeMath (как
        // ComputeMaximizedBounds): файл без #if WINDOWS — доступен и тестам.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Services", "WindowSizeMath.cs"));
        Assert.DoesNotContain("#if WINDOWS", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowSizeMath_ResolveFrameRounding_IsCrossPlatform()
    {
        // Решение «доверять ли состоянию DWM» — тоже чистая логика без UI-зависимостей,
        // поэтому покрывается юнит-тестами и используется WPF-слоем.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Services", "WindowSizeMath.cs"));
        Assert.Contains("ResolveFrameRounding", code, StringComparison.Ordinal);
        Assert.DoesNotContain("#if WINDOWS", code, StringComparison.Ordinal);
    }

    // ---------- Source-гварды: пересчёт радиуса после показа окна (1.0.1.61) ----------

    [Fact]
    public void WindowChromeHelper_Wpf_RecomputesRadiusAfterShow()
    {
        // Радиус пересчитывается после фактического показа окна (ContentRendered),
        // при активации и смене DPI — а не только один раз до показа.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
        Assert.Contains("window.ContentRendered", code, StringComparison.Ordinal);
        Assert.Contains("window.Activated", code, StringComparison.Ordinal);
        Assert.Contains("window.DpiChanged", code, StringComparison.Ordinal);
        Assert.Contains("ScheduleFrameCornerRadiusRefresh", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowChromeHelper_Wpf_DeferredRepeatAfterShow()
    {
        // Отложенный повтор: один кадр (DispatcherPriority.Loaded) и ~200 мс
        // (DispatcherTimer) — страховка для медленного применения скругления DWM.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
        Assert.Contains("DispatcherPriority.Loaded", code, StringComparison.Ordinal);
        Assert.Contains("DispatcherTimer", code, StringComparison.Ordinal);
        Assert.Contains("FrameCornerDeferredDelayMs", code, StringComparison.Ordinal);
        Assert.Contains("Interval = TimeSpan.FromMilliseconds(FrameCornerDeferredDelayMs)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowChromeHelper_Wpf_HandlesCompositionAndThemeChange()
    {
        // Смена системной темы и режима композиции (RDP, включение/выключение эффектов)
        // не должны оставлять рамку в прежнем радиусе: обрабатываются оба сообщения.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
        Assert.Contains("WmThemeChanged", code, StringComparison.Ordinal);
        Assert.Contains("WmDwmCompositionChanged", code, StringComparison.Ordinal);
        Assert.Contains("HookFrameCornerMessages", code, StringComparison.Ordinal);
        Assert.Contains("composition-changed", code, StringComparison.Ordinal);
        Assert.Contains("theme-changed", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowChromeHelper_Wpf_UsesOsEstimateUntilWindowShown()
    {
        // До показа окна (нет HWND/окно невидимо) состояние DWM не используется:
        // политика получает WindowShown=false и принимает решение по версии ОС.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
        Assert.Contains("IsWindowShown(window, out var hwnd)", code, StringComparison.Ordinal);
        Assert.Contains("WindowShown: shown", code, StringComparison.Ordinal);
        Assert.Contains("OsRoundsWindows: osRounds", code, StringComparison.Ordinal);
        Assert.Contains("IsWindowVisible(hwnd)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowChromeHelper_Wpf_AppliesRadiusOnlyWhenChanged()
    {
        // Повторный пересчёт не переустанавливает CornerRadius без реального
        // изменения радиуса — иначе рамка мерцала бы на Activated/DPI.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
        Assert.Contains("LastRadius", code, StringComparison.Ordinal);
        Assert.Contains("Math.Abs(previous.Value - radius) > 0.01", code, StringComparison.Ordinal);
        Assert.Contains("if (changed)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowChromeHelper_Wpf_LogsFrameCornerDiagnostics()
    {
        // Диагностика: тип окна, HWND, состояние DWM/композиции, радиус содержимого,
        // вычисленный радиус, принятая политика и причина пересчёта — попадают в
        // журнал (категория CM_THEMES / LogCategory.Themes). По этим полям видно,
        // почему рамка скруглена или осталась квадратной (RDP/Windows 10).
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
        Assert.Contains("LogFrameCornerRadius", code, StringComparison.Ordinal);
        Assert.Contains("LogCategory.Themes", code, StringComparison.Ordinal);
        Assert.Contains("frame-corner reason=", code, StringComparison.Ordinal);
        Assert.Contains("dwmState=", code, StringComparison.Ordinal);
        Assert.Contains("compositionEnabled=", code, StringComparison.Ordinal);
        Assert.Contains("contentRadius=", code, StringComparison.Ordinal);
        Assert.Contains("policy=", code, StringComparison.Ordinal);
        Assert.Contains("frameOptOut=", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowChromeHelper_Wpf_ExposesAttachForOwnChromeWindows()
    {
        // Окна, оформленные собственными средствами (главное окно), не проходят через
        // Apply — для них есть публичный AttachFrameCornerTracking с тем же механизмом.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
        Assert.Contains("public static void AttachFrameCornerTracking(Window window, Border frame, Border? titleBar = null)",
            code, StringComparison.Ordinal);
        Assert.Contains("RefreshFrameCornerRadius(window, \"state-changed\")", code, StringComparison.Ordinal);
    }

    // ---------- Единая политика скругления: RDP/Windows 10 (1.0.1.62) ----------

    /// <summary>
    /// Входы «RDP на Windows 10»: DWM не сообщает состояние (атрибут не поддерживается),
    /// ОС окна не скругляет, композиция включена, приложение рисует своё скругление.
    /// </summary>
    private static WindowSizeMath.FrameRoundingInputs RdpWin10Inputs(
        bool compositionEnabled = true,
        bool maximized = false,
        bool shown = true,
        double contentRadius = WindowSizeMath.DefaultContentCornerRadius) =>
        new(
            DwmStateKnown: false,
            DwmRounded: false,
            WindowShown: shown,
            Maximized: maximized,
            CompositionEnabled: compositionEnabled,
            OsRoundsWindows: false,
            ContentCornerRadius: contentRadius,
            SystemRadius: SystemRadius);

    [Fact]
    public void ResolveFrameCornerRadius_RdpWin10_UnknownDwm_UsesContentRadius()
    {
        // Дефект #126 в RDP на Windows 10: dwmState=unknown, osRounds=false,
        // radius=0 — рамка квадратная вокруг скруглённых углов. Теперь при
        // неизвестном DWM и включённой композиции рамка повторяет радиус
        // собственного скруглённого содержимого.
        var result = WindowSizeMath.ResolveFrameCornerRadius(RdpWin10Inputs());

        Assert.Equal(WindowSizeMath.DefaultContentCornerRadius, result.Radius);
        Assert.Equal(WindowSizeMath.FrameCornerPolicy.ContentRadius, result.Policy);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResolveFrameCornerRadius_RdpWin10_ContentRadiusBeforeAndAfterShow(bool shown)
    {
        // Радиус не «прыгает» между первым расчётом (окно ещё не показано) и
        // повторным после показа: в обоих случаях рамка скруглена одинаково.
        var result = WindowSizeMath.ResolveFrameCornerRadius(RdpWin10Inputs(shown: shown));
        Assert.Equal(WindowSizeMath.DefaultContentCornerRadius, result.Radius);
    }

    [Fact]
    public void ResolveFrameCornerRadius_CompositionDisabledWithoutOwnRounding_IsZero()
    {
        // Композиция DWM выключена и своего скругления у содержимого нет — рамку
        // не скругляем «наугад»: углы последовательно прямые (правило issue #126:
        // «композиция выключена и своего скругления нет → 0»).
        var result = WindowSizeMath.ResolveFrameCornerRadius(
            RdpWin10Inputs(compositionEnabled: false, contentRadius: 0));

        Assert.Equal(0, result.Radius);
        Assert.Equal(WindowSizeMath.FrameCornerPolicy.CompositionOff, result.Policy);
    }

    [Fact]
    public void ResolveFrameCornerRadius_CompositionDisabledWithOwnRounding_FollowsContent()
    {
        // Композиция выключена, но приложение само рисует скруглённое содержимое:
        // рамка повторяет его радиус — иначе получится «скруглённое содержимое в
        // квадратной рамке» (тот же дефект #126, что и в RDP на Windows 10).
        var result = WindowSizeMath.ResolveFrameCornerRadius(
            RdpWin10Inputs(compositionEnabled: false));

        Assert.Equal(WindowSizeMath.DefaultContentCornerRadius, result.Radius);
        Assert.Equal(WindowSizeMath.FrameCornerPolicy.ContentRadius, result.Policy);
    }

    [Fact]
    public void ResolveFrameCornerRadius_OwnChromeWithoutContentRadius_IsZeroOnWin10()
    {
        // Окно с собственным каркасом (главное окно) своего скругления не имеет:
        // на Windows 10/RDP рамка остаётся прямой, как и содержимое, — явный
        // opt-out от «фантомного» скругления вокруг прямоугольных углов.
        var result = WindowSizeMath.ResolveFrameCornerRadius(
            RdpWin10Inputs(contentRadius: 0));

        Assert.Equal(0, result.Radius);
        Assert.Equal(WindowSizeMath.FrameCornerPolicy.CompositionOff, result.Policy);
    }

    [Fact]
    public void ResolveFrameCornerRadius_Maximized_IsZero()
    {
        // Максимизированное окно — прямые углы при любой композиции и радиусе.
        var result = WindowSizeMath.ResolveFrameCornerRadius(RdpWin10Inputs(maximized: true));

        Assert.Equal(0, result.Radius);
        Assert.Equal(WindowSizeMath.FrameCornerPolicy.Maximized, result.Policy);
    }

    [Fact]
    public void ResolveFrameCornerRadius_DwmDoNotRound_IsZero()
    {
        // DWM явно сообщил «не скруглять» — рамка прямоугольная, даже на Windows 11
        // и при собственном радиусе содержимого.
        var result = WindowSizeMath.ResolveFrameCornerRadius(new WindowSizeMath.FrameRoundingInputs(
            DwmStateKnown: true,
            DwmRounded: false,
            WindowShown: true,
            Maximized: false,
            CompositionEnabled: true,
            OsRoundsWindows: true,
            ContentCornerRadius: WindowSizeMath.DefaultContentCornerRadius,
            SystemRadius: SystemRadius));

        Assert.Equal(0, result.Radius);
        Assert.Equal(WindowSizeMath.FrameCornerPolicy.DwmDoNotRound, result.Policy);
    }

    [Fact]
    public void ResolveFrameCornerRadius_Win11Rounded_NoRegression()
    {
        // Windows 11 со скруглением DWM: поведение 1.0.1.61 не изменилось —
        // рамка по системному радиусу DWM (8), а не по радиусу содержимого.
        var result = WindowSizeMath.ResolveFrameCornerRadius(new WindowSizeMath.FrameRoundingInputs(
            DwmStateKnown: true,
            DwmRounded: true,
            WindowShown: true,
            Maximized: false,
            CompositionEnabled: true,
            OsRoundsWindows: true,
            ContentCornerRadius: WindowSizeMath.DefaultContentCornerRadius,
            SystemRadius: SystemRadius));

        Assert.Equal(SystemRadius, result.Radius);
        Assert.Equal(WindowSizeMath.FrameCornerPolicy.DwmRounded, result.Policy);
    }

    [Fact]
    public void ResolveFrameCornerRadius_Win11BeforeShow_UsesSystemRadius()
    {
        // До показа окна на Windows 11 DWM ещё не отдаёт состояние: рамка сразу
        // получает системный радиус (без «прыжка» 0 → 8 после ContentRendered).
        var result = WindowSizeMath.ResolveFrameCornerRadius(new WindowSizeMath.FrameRoundingInputs(
            DwmStateKnown: true,
            DwmRounded: false,
            WindowShown: false,
            Maximized: false,
            CompositionEnabled: true,
            OsRoundsWindows: true,
            ContentCornerRadius: WindowSizeMath.DefaultContentCornerRadius,
            SystemRadius: SystemRadius));

        Assert.Equal(SystemRadius, result.Radius);
        Assert.Equal(WindowSizeMath.FrameCornerPolicy.DwmRounded, result.Policy);
    }

    [Fact]
    public void ResolveFrameCornerRadius_Win10WithoutContentRadius_IsZero()
    {
        // Windows 10/RDP, своего скругления нет — прямые углы (нет «фантомного»
        // скругления там, где содержимое прямоугольное).
        var result = WindowSizeMath.ResolveFrameCornerRadius(RdpWin10Inputs(contentRadius: 0));

        Assert.Equal(0, result.Radius);
        Assert.Equal(WindowSizeMath.FrameCornerPolicy.CompositionOff, result.Policy);
    }

    [Fact]
    public void WindowSizeMath_DefaultContentCornerRadius_MatchesAvaloniaGlass()
    {
        // Единый радиус содержимого: WPF-фолбэк (RDP/Win10) и «стеклянный» корень
        // Avalonia используют одну константу — рамка и содержимое совпадают.
        Assert.Equal(10, WindowSizeMath.DefaultContentCornerRadius);

        var main = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
        Assert.Contains("WindowSizeMath.DefaultContentCornerRadius", main, StringComparison.Ordinal);

        var modal = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ModalWindowBase.cs"));
        Assert.Contains("WindowSizeMath.DefaultContentCornerRadius", modal, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowChromeHelper_Wpf_DetectsDwmComposition()
    {
        // Политика учитывает состояние композиции DWM: при выключенных эффектах
        // системного скругления нет, рамку не скругляем.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
        Assert.Contains("DwmIsCompositionEnabled", code, StringComparison.Ordinal);
        Assert.Contains("TryGetDwmCompositionEnabled()", code, StringComparison.Ordinal);
        Assert.Contains("CompositionEnabled: compositionEnabled", code, StringComparison.Ordinal);
        Assert.Contains("FrameContentCornerRadius = WindowSizeMath.DefaultContentCornerRadius", code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WindowChromeHelper_Wpf_RoundsContentWithFrameRadius()
    {
        // Рамка и содержимое диалога совпадают (issue #126): содержимое обёрнуто в
        // скруглённый контейнер, радиус и клип синхронизируются с радиусом рамки.
        // Без этого на Windows 10/RDP скруглённая рамка обрамляла бы прямоугольное
        // содержимое — обратная сторона того же дефекта.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
        Assert.Contains("ContentBorderProperty", code, StringComparison.Ordinal);
        Assert.Contains("ApplyRoundedContentClip(content, radius)", code, StringComparison.Ordinal);
        Assert.Contains("content.CornerRadius = new CornerRadius(radius)", code, StringComparison.Ordinal);
        Assert.Contains("GetContentBorder(window) is not null ? FrameContentCornerRadius : 0", code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WindowChromeHelper_Wpf_LogsActualContentRadius()
    {
        // В лог frame-corner пишется фактический радиус содержимого (0 у окон с
        // собственным каркасом) вместе с compositionEnabled и policy — по
        // ZIP-диагностике видно, почему радиус рамки 0 (issue #126).
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
        Assert.Contains("compositionEnabled={compositionEnabled}", code, StringComparison.Ordinal);
        Assert.Contains("contentRadius={contentRadius:0.#}", code, StringComparison.Ordinal);
        Assert.Contains("policy={policy}", code, StringComparison.Ordinal);
    }
}
