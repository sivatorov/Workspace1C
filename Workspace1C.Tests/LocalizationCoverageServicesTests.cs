using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Покрытие локализации функционала служб/групп (issue #11, итерация 2, релиз 1.0.0.12).
/// <list type="bullet">
/// <item>каждый ключ <c>Services.*</c>/<c>Tray.*</c>, используемый в разметке/коде окон
/// служб, групп и трея, обязан существовать в словарях ru.json И en.json — иначе
/// интерфейс показывает «голый» ключ вместо текста;</item>
/// <item>наборы ключей <c>Services.*</c>/<c>Tray.*</c> в ru.json и en.json совпадают
/// (значения могут отличаться);</item>
/// <item>полные наборы ключей ru.json и en.json совпадают — защита от рассинхронизации
/// словарей впредь (те же файлы встраиваются в сборку как EmbeddedResource
/// Localization\Languages\*.json, см. Workspace1C.csproj).</item>
/// </list>
/// </summary>
public sealed class LocalizationCoverageServicesTests
{
    /// <summary>Возвращает корень репозитория (как в ConnectionReplaceLocalizationTests).</summary>
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

    /// <summary>Словари языка: <c>strings</c> из Languages/ru.json и Languages/en.json.</summary>
    private static HashSet<string> LoadLanguageKeys(string fileName)
    {
        var json = ReadProjectFile("Workspace1C", "Localization", "Languages", fileName);
        using var doc = JsonDocument.Parse(json);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in doc.RootElement.GetProperty("strings").EnumerateObject())
            keys.Add(property.Name);
        return keys;
    }

    /// <summary>
    /// Файлы окон служб/групп, их ViewModel, построителя трей-меню и точек входа
    /// (пункт меню «Управление службами»), где используются ключи Services.*/Tray.*.
    /// </summary>
    private static readonly string[] SourceFiles =
    {
        Path.Combine("Workspace1C", "Views", "ServicesWindow.xaml"),
        Path.Combine("Workspace1C", "Views", "ServicesWindow.xaml.cs"),
        Path.Combine("Workspace1C", "Views", "ServicesWindow.Avalonia.cs"),
        Path.Combine("Workspace1C", "Views", "ServicesPickerWindow.xaml"),
        Path.Combine("Workspace1C", "Views", "ServicesPickerWindow.xaml.cs"),
        Path.Combine("Workspace1C", "Views", "ServicesPickerWindow.Avalonia.cs"),
        Path.Combine("Workspace1C", "Views", "ServiceGroupEditWindow.xaml"),
        Path.Combine("Workspace1C", "Views", "ServiceGroupEditWindow.xaml.cs"),
        Path.Combine("Workspace1C", "Views", "ServiceGroupEditWindow.Avalonia.cs"),
        Path.Combine("Workspace1C", "ViewModels", "ServicesWindowViewModel.cs"),
        Path.Combine("Workspace1C", "ViewModels", "ServicesPickerViewModel.cs"),
        Path.Combine("Workspace1C", "Services", "ServiceTrayMenuBuilder.cs"),
        Path.Combine("Workspace1C", "Views", "MainWindow.Tray.cs"),
        Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tray.cs"),
        Path.Combine("Workspace1C", "Services", "UtilitiesMenuCatalog.cs"),
        Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml.cs"),
    };

    /// <summary>
    /// Ключи <c>Services.*</c>/<c>Tray.*</c>, упомянутые в файле: <c>{loc:Loc ...}</c> в
    /// XAML и строковые литералы в C# (<c>LocalizationManager.T(...)</c>, <c>Fmt(...)</c>,
    /// <c>CommandItem(...)</c>, switch-выражения статусов). В перечисленных файлах все
    /// такие литералы — ключи локализации (комментарии с именами классов вида
    /// <c>Services.ClassName</c> в эти файлы не попадают).
    /// </summary>
    private static HashSet<string> CollectServiceKeys(string content)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(content, @"\{loc:Loc\s+((?:Services|Tray)\.[A-Za-z0-9_.]+)\}"))
            keys.Add(m.Groups[1].Value);
        foreach (Match m in Regex.Matches(content, @"[""']((?:Services|Tray)\.[A-Za-z0-9_.]+)[""']"))
            keys.Add(m.Groups[1].Value);
        return keys;
    }

    [Fact]
    public void Services_Tray_EveryUsedKey_PresentInBothBuiltInDictionaries()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in SourceFiles)
            used.UnionWith(CollectServiceKeys(ReadProjectFile(file)));

        Assert.True(used.Count >= 55,
            "Ожидалось не менее 55 ключей Services.*/Tray.* в исходниках служб, найдено: " + used.Count);

        var ru = LoadLanguageKeys("ru.json");
        var en = LoadLanguageKeys("en.json");

        var missingInRu = used.Where(k => !ru.Contains(k)).ToList();
        var missingInEn = used.Where(k => !en.Contains(k)).ToList();

        Assert.True(missingInRu.Count == 0,
            "Ключи Services.*/Tray.*, отсутствующие во встроенном ru.json (issue #11): " +
            string.Join(", ", missingInRu));
        Assert.True(missingInEn.Count == 0,
            "Ключи Services.*/Tray.*, отсутствующие во встроенном en.json (issue #11): " +
            string.Join(", ", missingInEn));
    }

    [Fact]
    public void Services_Tray_DictionariesSameKeySet()
    {
        // Словари ru/en должны содержать одинаковый набор ключей служб/групп —
        // отсутствие ключа в одном из них означает «ключ вместо текста» в этом языке.
        var ru = LoadLanguageKeys("ru.json");
        var en = LoadLanguageKeys("en.json");

        var ruServiceKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in ru)
            if (key.StartsWith("Services.", StringComparison.Ordinal) ||
                key.StartsWith("Tray.", StringComparison.Ordinal))
                ruServiceKeys.Add(key);

        var enServiceKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in en)
            if (key.StartsWith("Services.", StringComparison.Ordinal) ||
                key.StartsWith("Tray.", StringComparison.Ordinal))
                enServiceKeys.Add(key);

        Assert.Equal(ruServiceKeys, enServiceKeys);
    }

    [Fact]
    public void Localization_DictionariesSameKeySet_AllKeys()
    {
        // Полные наборы ключей ru.json и en.json обязаны совпадать (значения могут
        // отличаться) — защита от рассинхронизации словарей впредь (issue #11, Задача 6).
        var ru = LoadLanguageKeys("ru.json");
        var en = LoadLanguageKeys("en.json");

        Assert.Equal(ru, en);
    }

    [Theory]
    [InlineData("Services.AddGroup")]
    [InlineData("Services.EditGroup")]
    [InlineData("Services.DeleteGroup")]
    [InlineData("Services.GroupAddHint")]
    [InlineData("Services.GroupEditHint")]
    [InlineData("Services.GroupDeleteHint")]
    [InlineData("Services.GroupName")]
    [InlineData("Services.MoveLeft")]
    [InlineData("Services.MoveRight")]
    [InlineData("Services.ManagedList")]
    [InlineData("Services.AllAvailable")]
    [InlineData("Services.Group.Status.AllRunning")]
    [InlineData("Services.Group.Status.Partial")]
    [InlineData("Services.Group.Status.None")]
    [InlineData("Services.Group.DeleteConfirm")]
    [InlineData("Tray.Services")]
    [InlineData("Services.TraySettings")]
    public void Services_GroupKeys_ArePresentInBothDictionaries(string key)
    {
        // Ключи групп служб и трея (итерация 2, issue #11) — каждый обязан быть и в
        // ru.json, и в en.json, иначе интерфейс покажет «голый» ключ вместо текста.
        var ru = LoadLanguageKeys("ru.json");
        var en = LoadLanguageKeys("en.json");
        Assert.Contains(key, ru);
        Assert.Contains(key, en);
    }
}