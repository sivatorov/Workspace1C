using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Разметочные тесты выделения тонкого клиента в окне «Сменить платформу»
/// (issue #54, комментарий ArtyoZ): в выпадающем списке платформ тонкие клиенты
/// помечаются локализованным суффиксом «(тонкий клиент)» (DisplayName — единый
/// форматтер, issue #33) и подсвечиваются оранжевым, как в настройках.
/// Тесты читают исходные XAML/код из репозитория (как Issue55WindowLayoutTests),
/// поэтому работают на обеих платформах без запуска UI.
/// </summary>
public sealed class Issue54ThinClientHighlightTests
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

    [Fact]
    public void PlatformChangeWindow_Wpf_ComboBoxBindsItems_AndHighlightsThinClient()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "PlatformChangeWindow.xaml"));

        // Список строится из элементов с признаком тонкого клиента, выбор привязан
        // к строке версии через SelectedValuePath.
        Assert.Contains("ItemsSource=\"{Binding InstalledPlatformItems}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedValuePath=\"Display\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedValue=\"{Binding SelectedPlatform, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);

        // Имя с локализованным суффиксом «(тонкий клиент)» + оранжевая подсветка.
        // Аудит UI 1.0.1.10, п. 2.10: фиксированный #F59E0B заменён кистью темы WarningBrush.
        Assert.Contains("Text=\"{Binding DisplayName}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DataTrigger Binding=\"{Binding IsThinClient}\" Value=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"{DynamicResource WarningBrush}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void PlatformChangeWindow_Avalonia_ComboBoxBindsItems_AndHighlightsThinClient()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "PlatformChangeWindow.Avalonia.cs"));

        // Список строится из элементов с признаком тонкого клиента.
        Assert.Contains("ItemsSource = _vm.InstalledPlatformItems", code, StringComparison.Ordinal);

        // Шаблон строки: имя с суффиксом + оранжевая подсветка тонкого клиента.
        Assert.Contains("FuncDataTemplate<PlatformVersionInfo>", code, StringComparison.Ordinal);
        Assert.Contains("item?.DisplayName", code, StringComparison.Ordinal);
        Assert.Contains("IsThinClient", code, StringComparison.Ordinal);
        Assert.Contains("#F59E0B", code, StringComparison.Ordinal);
    }
}
