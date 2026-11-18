using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты-стражи релиза v1.0.1.85 (P0-2 аудита UI): пользовательские строки
/// локализации не должны содержать внутренних номеров задач вида «issue #NNN»
/// и «#NNN» — конечный пользователь видит их в подсказках и справке.
/// Технические пояснения остаются в комментариях кода, а не в UI-тексте.
/// </summary>
public sealed class Release100185Tests
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

    /// <summary>
    /// Строки словаря <c>strings</c> из Workspace1C/Localization/Languages/{lang}.json —
    /// так же, как их читают тесты покрытия локализации.
    /// </summary>
    private static Dictionary<string, string> LoadLanguageStrings(string fileName)
    {
        var path = Path.Combine(FindRepoRoot(), "Workspace1C", "Localization", "Languages", fileName);
        Assert.True(File.Exists(path), $"Файл локализации не найден: {path}");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var strings = doc.RootElement.GetProperty("strings");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in strings.EnumerateObject())
            result[property.Name] = property.Value.GetString() ?? string.Empty;
        return result;
    }

    [Theory]
    [InlineData("ru.json")]
    [InlineData("en.json")]
    public void Localization_Values_DoNotContainIssueReferences(string fileName)
    {
        // Регрессия P0-2 (v1.0.1.85): «(issue #79)», «issue #57/#236» и подобные
        // внутренние ссылки не должны попадать в подсказки и справку.
        var regex = new Regex(@"issue\s*#\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var offenders = LoadLanguageStrings(fileName)
            .Where(pair => regex.IsMatch(pair.Value))
            .Select(pair => $"{pair.Key}: {pair.Value}")
            .ToList();

        Assert.True(offenders.Count == 0,
            $"{fileName}: найдены внутренние номера задач в пользовательских строках:\n" +
            string.Join("\n", offenders));
    }

    [Theory]
    [InlineData("ru.json")]
    [InlineData("en.json")]
    public void Localization_Values_DoNotContainBareIssueNumbers(string fileName)
    {
        // Тот же дефект встречался и в сокращённом виде — «Show only running bases (#339)»,
        // поэтому дополнительно запрещаем «#NNN» без слова issue.
        var regex = new Regex(@"(?<![A-Za-z0-9])#\d+", RegexOptions.CultureInvariant);
        var offenders = LoadLanguageStrings(fileName)
            .Where(pair => regex.IsMatch(pair.Value))
            .Select(pair => $"{pair.Key}: {pair.Value}")
            .ToList();

        Assert.True(offenders.Count == 0,
            $"{fileName}: найдены внутренние номера задач в пользовательских строках:\n" +
            string.Join("\n", offenders));
    }

    [Fact]
    public void Localization_KeySets_Match()
    {
        // Правки строк не должны нарушить паритет ключей ru.json/en.json
        // (полная проверка наборов есть в LocalizationCoverageServicesTests,
        // здесь — страж именно релиза 1.0.1.85).
        var ru = LoadLanguageStrings("ru.json");
        var en = LoadLanguageStrings("en.json");

        Assert.Equal(ru.Count, en.Count);
        Assert.Empty(ru.Keys.Except(en.Keys, StringComparer.Ordinal));
        Assert.Empty(en.Keys.Except(ru.Keys, StringComparer.Ordinal));
    }
}
