using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #94 «Зависание закрытия окна Свойств на Linux»: FuncDataTemplate в
/// BuildIdTab (вкладка «Дополнительные поля») вызывался Avalonia с row == null
/// при переразборе контейнеров при закрытии окна → NRE обрывал цепочку закрытия.
/// Файловая проверка исходников (по образцу ButtonThemeBindingTests): шаблон
/// обязан иметь null-защиту и не обращаться к row до проверки.
/// </summary>
public sealed class ConnectionSettingsIdTemplateTests
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
    public void AdditionalFieldsTemplate_HasNullRowGuard()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ConnectionSettingsWindow.Avalonia.cs"));
        var templateStart = code.IndexOf("new FuncDataTemplate<AdditionalFieldRow>", System.StringComparison.Ordinal);
        Assert.True(templateStart > 0, "Шаблон AdditionalFieldRow не найден в ConnectionSettingsWindow.Avalonia.cs");
        // Берём фрагмент шаблона (до конца FuncDataTemplate-конструкции).
        var templateEnd = code.IndexOf("});", templateStart, System.StringComparison.Ordinal);
        Assert.True(templateEnd > templateStart, "Не удалось выделить тело шаблона");
        var template = code.Substring(templateStart, templateEnd - templateStart);

        Assert.Contains("row is null", template);
    }

    [Fact]
    public void AdditionalFieldsTemplate_DoesNotDereferenceRowBeforeGuard()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ConnectionSettingsWindow.Avalonia.cs"));
        var templateStart = code.IndexOf("new FuncDataTemplate<AdditionalFieldRow>", System.StringComparison.Ordinal);
        Assert.True(templateStart > 0, "Шаблон AdditionalFieldRow не найден в ConnectionSettingsWindow.Avalonia.cs");
        var guardIndex = code.IndexOf("row is null", templateStart, System.StringComparison.Ordinal);
        Assert.True(guardIndex > 0, "null-защита row отсутствует в шаблоне");

        // До защиты в шаблоне не должно быть обращения к полям row (row.Number / row.Value).
        var preamble = code.Substring(templateStart, guardIndex - templateStart);
        Assert.DoesNotContain("row.Number", preamble);
        Assert.DoesNotContain("row.Value", preamble);
    }
}
