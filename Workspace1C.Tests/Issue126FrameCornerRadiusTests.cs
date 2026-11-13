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
        // Рамка-оверлей получает CornerRadius из чистой логики
        // WindowSizeMath.ComputeFrameCornerRadius (dwmRounded, maximized, радиус).
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
        Assert.Contains("ComputeFrameCornerRadius(rounded, maximized, DwmCornerRadius)", code, StringComparison.Ordinal);
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
}
