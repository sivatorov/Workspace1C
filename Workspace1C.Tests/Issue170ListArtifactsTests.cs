using System;
using System.IO;
using Workspace1C;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #170 «Видеоартефакты при отображении списка баз» — регрессия между
/// 1.0.1.121 и 1.0.1.122.
/// <para>
/// Причина регрессии: добавленная в 1.0.1.122 политика толщины стеклянной рамки
/// (<see cref="WindowGlassFramePolicy"/>) обнуляла <c>GlassFrameThickness</c> при
/// прижатии окна к <b>верхней</b> границе рабочей области. Типичное положение окна
/// (<c>Top = 0</c>) почти всегда попадало под это правило, acrylic/mica backdrop
/// отключался, а пересчёт рамки на каждом <c>SizeChanged</c>/<c>LocationChanged</c>
/// заставлял DWM пересоздавать стекло. Под полупрозрачной подложкой окна
/// (alpha ~0xE8) и полупрозрачным фоном списка оставались смазанные/неперерисованные
/// участки — «видеоартефакты».
/// </para>
/// <para>
/// Проверяется: (1) политика не возвращает «артефактную» конфигурацию для окна,
/// прижатого только к верхней границе; (2) список баз в WPF и Avalonia рисуется на
/// непрозрачной подложке; (3) правки #172 (maximized) и #126 (обычное состояние)
/// не затронуты.
/// </para>
/// </summary>
public sealed class Issue170ListArtifactsTests
{
    private static readonly (double Left, double Top, double Width, double Height) Work =
        (0, 0, 1920, 1040);

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

    // ---------- Политика: «артефактная» конфигурация не возвращается ----------

    [Fact]
    public void Resolve_TopFlushOnly_KeepsExtendedFrame_NoArtifacts()
    {
        // Окно прижато только к верхней границе (Top = 0) — типичное положение.
        // Рамка должна остаться расширенной (-1): acrylic продолжает подкладываться
        // под полупрозрачное окно, DWM не пересоздаёт стекло.
        var thickness = WindowGlassFramePolicy.Resolve(
            maximized: false,
            window: (100, 0, 1200, 700),
            workArea: Work);

        Assert.Equal(WindowGlassFramePolicy.ExtendedThickness, thickness);
        Assert.NotEqual(WindowGlassFramePolicy.NoneThickness, thickness);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void ResolveState_TopFlushOnly_KeepsExtendedFrameAtAnyDpi(double dpiScale)
    {
        // На 100%/125%/150% прижатие к верхней границе не должно обнулять рамку.
        var state = WindowGlassFramePolicy.ResolveState(
            maximized: false,
            window: (100, 0, 1200, 700),
            workArea: Work,
            dpiScale: dpiScale);

        Assert.Equal(WindowGlassFramePolicy.ExtendedThickness, state.GlassFrameThickness);
        Assert.Equal(WindowGlassFramePolicy.ResizeBorderThicknessDip, state.ResizeBorderThickness);
        Assert.Null(state.CornerRadiusOverride);
    }

    [Fact]
    public void Resolve_FullHeightWindow_StillZeroFrame_NoBottomStripe()
    {
        // Дефект 1.0.1.122 (прозрачная полоса внизу) не возвращается: окно на всю
        // высоту рабочей области по-прежнему получает GlassFrameThickness = 0.
        var thickness = WindowGlassFramePolicy.Resolve(
            maximized: false,
            window: (0, 0, 1920, 1040),
            workArea: Work);

        Assert.Equal(WindowGlassFramePolicy.NoneThickness, thickness);
    }

    [Fact]
    public void ResolveState_MaximizedStillZeroFrame_172NotBroken()
    {
        // Правки #172 (maximized на HiDPI) не затронуты.
        var state = WindowGlassFramePolicy.ResolveState(
            maximized: true,
            window: (-8, -8, 1936, 1056),
            workArea: Work,
            dpiScale: 1.25);

        Assert.Equal(0, state.GlassFrameThickness);
        Assert.Equal(0, state.ResizeBorderThickness);
        Assert.Equal(0d, state.CornerRadiusOverride);
    }

    // ---------- WPF: список баз на непрозрачной подложке ----------

    [Fact]
    public void Wpf_MainTree_HasOpaqueCardBackground()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));

        // Находим объявление списка баз и проверяем его фон.
        var treeStart = xaml.IndexOf("<controls:LeveledTreeView x:Name=\"MainTree\"",
            StringComparison.Ordinal);
        Assert.True(treeStart >= 0, "В MainWindow.xaml не найден список баз MainTree.");

        var treeEnd = xaml.IndexOf(">", treeStart, StringComparison.Ordinal);
        Assert.True(treeEnd > treeStart, "Не удалось выделить открывающий тег MainTree.");
        var treeTag = xaml.Substring(treeStart, treeEnd - treeStart);

        Assert.Contains("Background=\"{DynamicResource CardBackgroundBrush}\"", treeTag,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"Transparent\"", treeTag, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_ListScrollViewer_BackgroundIsOpaqueCardBrush()
    {
        // Неявный стиль ScrollViewer (внутренний ScrollViewer списка баз) должен
        // закрашивать область непрозрачной кистью карточки — иначе при прокрутке
        // под строками просвечивает полупрозрачное окно/backdrop.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));

        Assert.Contains("<Style TargetType=\"ScrollViewer\">", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Background\" Value=\"{DynamicResource CardBackgroundBrush}\"/>",
            xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_ExtendedGlassFrameStillDeclared_AcyclicPreserved()
    {
        // Расширенная стеклянная рамка (acrylic) должна оставаться в разметке:
        // политика лишь переключает её толщину, а не отключает эффект навсегда.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));

        Assert.Contains("GlassFrameThickness=\"-1\"", xaml, StringComparison.Ordinal);
    }

    // ---------- Avalonia: список баз на непрозрачной подложке ----------

    [Fact]
    public void Avalonia_TreeBackgroundIsOpaque_NoGlassAlpha()
    {
        // В Avalonia фон списка баз раньше был полупрозрачным (WithAlpha(brush,
        // GlassBackgroundAlpha)) — та же причина смазывания строк. Теперь кисть
        // темы применяется как есть (непрозрачная).
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));

        Assert.Contains("_tree.Background = brush", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_tree.Background = ThemeBrushes.WithAlpha(brush, GlassBackgroundAlpha)",
            code, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_GlassBackgroundStillUsedForWindow()
    {
        // Стеклянный эффект окна в Avalonia сохраняется (только фон списка стал
        // непрозрачным): константа alpha по-прежнему применяется к окну.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));

        Assert.Contains("GlassBackgroundAlpha", code, StringComparison.Ordinal);
        Assert.Contains("ApplyGlassBackground", code, StringComparison.Ordinal);
    }

    // ---------- Source-guard: пересчёт рамки сохраняет защиту от перерисовки ----------

    [Fact]
    public void Wpf_FrameUpdate_AppliesOnlyOnChange()
    {
        // Пересчёт на SizeChanged/LocationChanged не должен каждый раз присваивать
        // WindowChrome.GlassFrameThickness: лишние присваивания заставляют DWM
        // пересоздавать стекло (источник артефактов). Значение применяется только
        // при фактическом изменении.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml.cs"));

        Assert.Contains("if (chrome.GlassFrameThickness != glass)", code, StringComparison.Ordinal);
        Assert.Contains("WindowGlassFramePolicy.ResolveState", code, StringComparison.Ordinal);
    }
}
