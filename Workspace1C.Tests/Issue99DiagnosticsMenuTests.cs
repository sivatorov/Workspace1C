using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #99 «Сбор диагностики» (предложения 7OH): пункт «Собрать диагностику» в
/// меню «Утилиты» → «Информация» (быстрый доступ к сбору ZIP без окна настроек) и
/// перестроенное окно «О программе» (нижние кнопки в одну колонку, кликабельная
/// спонсорская картинка справа и крупнее, без «надоедливого скролла»).
/// v1.0.1.23: настраиваемый хоткей реализован (AppSettings.HotkeyCollectDiagnostics,
/// по умолчанию пусто — решение 7OH «можно не ставить ничего по умолчанию»).
/// </summary>
public sealed class Issue99DiagnosticsMenuTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    // ===================== Каталог меню «Утилиты» =====================

    [Fact]
    public void UtilitiesMenuCatalog_CollectDiagnostics_InInformationGroup()
    {
        var item = UtilitiesMenuCatalog.Find("CollectDiagnostics");
        Assert.NotNull(item);
        Assert.Equal(UtilitiesMenuCommandKind.CollectDiagnostics, item!.CommandKind);
        Assert.Equal(UtilitiesMenuCatalog.GroupInformation, item.DefaultGroup);
        // Иконки заданы для обеих платформ (единый источник пар каталога).
        Assert.False(string.IsNullOrWhiteSpace(item.WpfIconKind));
        Assert.False(string.IsNullOrWhiteSpace(item.AvaloniaIconKey));
        // v1.0.1.23: подпись хоткея пункта берётся из AppSettings.HotkeyCollectDiagnostics.
        Assert.Equal("HotkeyCollectDiagnostics", item.HotkeyKey);
    }

    [Fact]
    public void UtilitiesMenuCatalog_CollectDiagnostics_MarkedAsNew()
    {
        // При сохранённой пользовательской компоновке пункт попадает в «Новинки программы».
        Assert.True(UtilitiesMenuCatalog.Find("CollectDiagnostics")!.IsNew);
    }

    [Fact]
    public void UtilitiesMenuLayouts_Default_IncludesCollectDiagnostics()
    {
        // Пункт доступен всем сразу: включён в стандартную компоновку подменю
        // «Информация» (для сохранённых пользовательских компоновок он попадает
        // в «Новинки программы» — см. UtilitiesMenuResolver).
        var layout = UtilitiesMenuLayouts.Default();
        var ids = new System.Collections.Generic.HashSet<string>();
        UtilitiesMenuResolver.CollectItemIds(layout.Root, ids);
        Assert.Contains("CollectDiagnostics", ids);
    }

    // ===================== Локализация ru/en =====================

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void Localization_UtilitiesCollectDiagnostics_Present(string language)
    {
        var json = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", language + ".json"));
        using var doc = JsonDocument.Parse(json);
        // Строки лежат в корневом объекте "strings" (ru.json/en.json: code/name/strings).
        Assert.True(
            doc.RootElement.TryGetProperty("strings", out var strings) &&
            strings.TryGetProperty("Utilities.CollectDiagnostics", out var value) &&
            value.GetString() is { Length: > 0 },
            $"{language}.json: ключ Utilities.CollectDiagnostics отсутствует или пуст.");
    }

    // ===================== Привязка команды в меню платформ =====================

    [Fact]
    public void WpfMenu_HandlesCollectDiagnosticsCommand()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.UtilitiesMenu.cs"));
        Assert.Contains("case UtilitiesMenuCommandKind.CollectDiagnostics:", source, StringComparison.Ordinal);
        // Действие — реальный сбор ZIP (тот же, что на вкладке «О программе»).
        Assert.Contains("DiagnosticsBundleService.CreateBundleAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaMenu_HandlesCollectDiagnosticsCommand()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        Assert.Contains("case UtilitiesMenuCommandKind.CollectDiagnostics:", source, StringComparison.Ordinal);
        Assert.Contains("DiagnosticsBundleService.CreateBundleAsync", source, StringComparison.Ordinal);
    }

    // ===================== Окно «О программе» (обе платформы) =====================

    [Fact]
    public void WpfAboutTab_ButtonsInOneColumn_ImageOnRightLarger()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        // Кнопки — в вертикальной колонке (issue #99), а не в горизонтальном ряду.
        Assert.Contains("x:Name=\"AboutButtonsColumn\"", xaml, StringComparison.Ordinal);
        // Картинка справа (Grid.Column="1") и крупнее прежних 240.
        Assert.Contains("x:Name=\"AboutDonatImage\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"380\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaAboutTab_ButtonsInOneColumn_ImageOnRightLarger()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs"));
        // Вкладка строит двухколоночный корень; картинка — колонка 1, крупнее 240.
        Assert.Contains("aboutRoot.Children.Add(panel)", code, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(donatImage, 1)", code, StringComparison.Ordinal);
        Assert.Contains("MaxWidth = 380", code, StringComparison.Ordinal);
        // Кнопки остаются в вертикальном StackPanel левой колонки (одна под другой).
        Assert.Contains("panel.Children.Add(copyButton)", code, StringComparison.Ordinal);
        Assert.Contains("panel.Children.Add(diagButton)", code, StringComparison.Ordinal);
    }
}
