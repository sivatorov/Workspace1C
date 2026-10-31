using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #63 (последний комментарий 7OH от 2026-10-26): «Поле выбора сервера - надо
/// сделать шире, раза в полтора или по содержимому - обрезает надписи». ComboBox
/// «Веб-сервер» в окне публикации расширен с 200 до 400 в обоих построителях — WPF
/// <c>WebPublishWindow.cs</c> и Avalonia <c>WebPublishWindow.Avalonia.cs</c>. Подпись
/// инстанса также дополнена именем конфиг-файла (ключ <c>WebPublish.ApacheInstanceOptionWithConf</c>),
/// поэтому узкое поле действительно обрезало бы надписи.
/// </summary>
public sealed class WebPublishServerComboBoxWidthTests
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

    private static string ReadProjectFile(params string[] parts)
    {
        var relativePath = Path.Combine(parts);
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    [Fact]
    public void WpfWindow_ServerComboBox_IsWideEnough()
    {
        var source = ReadProjectFile("Workspace1C", "Views", "WebPublishWindow.cs");

        // Ширина ComboBox «Веб-сервер» увеличена (200 → 400 → 600, issue #63, хвост
        // 1.0.1.12: «раза в полтора или по содержимому»), надписи не обрезаются.
        Assert.Contains("nameof(WebPublishViewModel.ServerIndex), 600", source, StringComparison.Ordinal);
        Assert.DoesNotContain("nameof(WebPublishViewModel.ServerIndex), 200", source, StringComparison.Ordinal);
        Assert.DoesNotContain("nameof(WebPublishViewModel.ServerIndex), 400", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaWindow_ServerComboBox_IsWideEnough()
    {
        var source = ReadProjectFile("Workspace1C", "Views", "WebPublishWindow.Avalonia.cs");

        const string marker = "nameof(WebPublishViewModel.ServerIndex), 600";
        Assert.Contains(marker, source, StringComparison.Ordinal);
        Assert.DoesNotContain("nameof(WebPublishViewModel.ServerIndex), 200", source, StringComparison.Ordinal);
        Assert.DoesNotContain("nameof(WebPublishViewModel.ServerIndex), 400", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_InstanceOptionWithConf_ExistsInRuAndEn()
    {
        var ru = ReadProjectFile("Workspace1C", "Localization", "Languages", "ru.json");
        var en = ReadProjectFile("Workspace1C", "Localization", "Languages", "en.json");

        Assert.Contains("\"WebPublish.ApacheInstanceOptionWithConf\"", ru, StringComparison.Ordinal);
        Assert.Contains("\"WebPublish.ApacheInstanceOptionWithConf\"", en, StringComparison.Ordinal);
        // Формат-строка с тремя аргументами: {0} версия, {1} служба, {2} имя конфига.
        Assert.Contains("{2}", ru, StringComparison.Ordinal);
        Assert.Contains("{2}", en, StringComparison.Ordinal);
    }
}
