using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Страж-тесты v1.0.1.50 (issues #107, #116, #103 — доводка после v1.0.1.49):
/// <list type="bullet">
/// <item>#107 п.1 — markdown-рендер «Что нового» красится кистями темы (обе платформы);</item>
/// <item>#107 п.2 — кнопка «Пропустить эту версию» не фиксируется Width (обе платформы);</item>
/// <item>#116 п.1 — окно «Что нового» не вкладывает скролл-контейнеры;</item>
/// <item>#116 п.2 — периодичность проверки обновлений на отдельной строке настроек;</item>
/// <item>#103 — маркер отладки — кружок, а не квадрат.</item>
/// </list>
/// </summary>
public sealed class V100150IssuesTests
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

    // ===================== Issue #107 п.1: кисти темы в markdown-рендере =====================

    [Fact]
    public void MarkdownRenderer_BindsThemeBrushes_OnBothPlatforms()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Services", "MarkdownRenderer.cs"));

        // WPF-ветка: FlowDocument получает кисть темы, ссылки — AccentBrush.
        Assert.Contains("TryFindResource(\"TextPrimaryBrush\")", source);
        Assert.Contains("doc.Foreground = primary", source);
        Assert.Contains("ThemeBrush(\"AccentBrush\")", source);

        // Avalonia-ветка: TextBlock'и привязаны к TextPrimaryBrush через ThemeBrushes.Bind.
        var bindCount = System.Text.RegularExpressions.Regex.Matches(
            source, "ThemeBrushes\\.Bind\\([^,]+, TextBlock\\.ForegroundProperty, \"TextPrimaryBrush\"\\)").Count;
        Assert.True(bindCount >= 5,
            $"Ожидалось ≥5 привязок TextPrimaryBrush в Avalonia-ветке рендера, найдено {bindCount}.");
    }

    [Fact]
    public void UpdateAvailableWindow_BodyViewer_SetsThemeForeground()
    {
        // Диалог обновления («Что нового» до начала обновления) — тоже потребитель
        // рендера; базовый Foreground контрола тематизирован (issue #107).
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Services", "UpdateAvailableWindow.xaml"));
        Assert.Contains("BodyViewer", xaml);
        Assert.Contains("DynamicResource TextSecondaryBrush", xaml);
    }

    // ===================== Issue #107 п.2: «Пропустить эту версию» не обрезается =====================

    [Fact]
    public void SkipVersionButton_UsesMinWidth_OnBothPlatforms()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Services", "UpdateAvailableWindow.xaml"));
        Assert.Contains("SkipVersionButton", xaml);
        // Кнопка пропуска не должна фиксироваться Width — только MinWidth.
        Assert.DoesNotContain("SkipVersionButton\" Width=", xaml);
        Assert.Contains("SkipVersionButton\" MinWidth=", xaml);
    }

    // ===================== Issue #116 п.1: один скролл-контейнер в «Что нового» =====================

    [Fact]
    public void WhatsNewWindow_Wpf_HasNoNestedScrollContainers()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WhatsNewWindow.xaml"));
        // В разметке нет постоянного внешнего ScrollViewer-тега: хост пуст, код кладёт
        // ровно один скролл-контейнер (ScrollViewer или FlowDocumentScrollViewer).
        Assert.DoesNotContain("<ScrollViewer", xaml);
        Assert.Contains("NotesHost", xaml);

        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WhatsNewWindow.xaml.cs"));
        // Оба контейнера скроллятся по вертикали; вертикальная прокрутка не отключается
        // (раньше отключение у FlowDocumentScrollViewer перехватывало колесо мыши).
        Assert.Contains("VerticalScrollBarVisibility = ScrollBarVisibility.Auto", code);
        Assert.DoesNotContain("VerticalScrollBarVisibility = ScrollBarVisibility.Disabled", code);
    }

    [Fact]
    public void WhatsNewWindow_Avalonia_ScrollViewer_HasExplicitScrollBarVisibility()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WhatsNewWindow.Avalonia.cs"));
        Assert.Contains("VerticalScrollBarVisibility = ScrollBarVisibility.Auto", code);
    }

    // ===================== Issue #116 п.2: периодичность — отдельная строка =====================

    [Fact]
    public void SettingsWindow_UpdateIntervalRow_IsOwnLine_OnBothPlatforms()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        // v1.0.1.54 (issue #116): фиксированный ComboBox заменён парой «значение +
        // единица» (UpdateIntervalValueBox + UpdateIntervalUnitCombo).
        Assert.Contains("UpdateIntervalValueBox", xaml);
        Assert.Contains("UpdateIntervalUnitCombo", xaml);
        // Ряд периода не совмещён в одном StackPanel с UpdateModeComboBox.
        var modeIndex = xaml.IndexOf("UpdateModeComboBox", StringComparison.Ordinal);
        var intervalIndex = xaml.IndexOf("UpdateIntervalValueBox", StringComparison.Ordinal);
        Assert.True(modeIndex >= 0 && intervalIndex > modeIndex);
        var between = xaml[modeIndex..intervalIndex];
        Assert.Contains("</StackPanel>", between);

        // Avalonia: ряд интервала кладётся в вертикальный контейнер отдельным ребёнком.
        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs"));
        Assert.Contains("settingsBehavior.Children.Add(updateIntervalRow)", avalonia);
    }

    // ===================== Issue #103: маркер отладки — кружок =====================

    // v1.0.1.54: отдельный кружок-маркер отладки удалён на обеих платформах —
    // цвет значка Предприятия теперь сам кодирует отладку (синий при /DEBUG,
    // оранжевый без). Актуальные сторож-проверки — в Issue103EnterpriseGlyphTests
    // (Wpf_DebugMarkerEllipse_Removed / Avalonia_DebugMarkerEllipse_Removed).
}
