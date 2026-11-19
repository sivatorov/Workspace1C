using System.Text.RegularExpressions;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Согласованность StaticResource-ссылок окна настроек (issue #337).
/// В 0.3.9.247 кнопка «Управлять» справочника ИТС сослалась на
/// {StaticResource OutlineButtonStyle}, который не был объявлен ни в ресурсах окна,
/// ни в словарях приложения, — WPF ронял InitializeComponent с XamlParseException
/// при каждом открытии «Настроек». Тест гарантирует, что каждый ключ {StaticResource}
/// в SettingsWindow.xaml объявлен локально в окне или в словарях приложения
/// (App.xaml, Themes/LightTheme.xaml, Themes/DarkTheme.xaml, Themes/Icons.xaml).
/// </summary>
public sealed class SettingsWindowXamlResourcesTests
{
    /// <summary>Возвращает корень репозитория (каталог с проектами приложения и тестов).</summary>
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

    /// <summary>Собирает строковые ключи x:Key из фрагмента XAML.</summary>
    private static HashSet<string> CollectDefinedKeys(string xaml)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(xaml, @"x:Key=""([^""]+)"""))
            keys.Add(m.Groups[1].Value);
        return keys;
    }

    [Fact]
    public void SettingsWindow_EveryStaticResource_IsDefinedLocallyOrInAppDictionaries()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        // XML-комментарии не участвуют в разрешении ресурсов — исключаем их из анализа.
        xaml = Regex.Replace(xaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

        var usedKeys = Regex.Matches(xaml, @"\{StaticResource\s+([\w.]+)\}")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(usedKeys);

        var definedKeys = CollectDefinedKeys(xaml);
        foreach (var dictionary in new[]
                 {
                     Path.Combine("Workspace1C", "App.xaml"),
                     Path.Combine("Workspace1C", "Themes", "LightTheme.xaml"),
                     Path.Combine("Workspace1C", "Themes", "DarkTheme.xaml"),
                     Path.Combine("Workspace1C", "Themes", "Icons.xaml")
                 })
        {
            definedKeys.UnionWith(CollectDefinedKeys(ReadProjectFile(dictionary)));
        }

        var missing = usedKeys.Where(key => !definedKeys.Contains(key)).ToList();
        Assert.True(
            missing.Count == 0,
            "В SettingsWindow.xaml используются StaticResource-ресурсы, не объявленные ни в ресурсах окна, " +
            "ни в словарях приложения (App.xaml, Themes/LightTheme.xaml, Themes/DarkTheme.xaml, Themes/Icons.xaml): " +
            string.Join(", ", missing) +
            ". Такие ссылки роняют InitializeComponent с XamlParseException при открытии настроек (issue #337).");
    }

    [Fact]
    public void SettingsWindow_ButtonStyles_AreDefinedInTheme()
    {
        // Регрессия issue #337 + аудит UI P2-2: локальный OutlineButtonStyle удалён,
        // окно ссылается на канонический SecondaryButton из словарей темы.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        var localKeys = CollectDefinedKeys(xaml);
        Assert.DoesNotContain("OutlineButtonStyle", localKeys);
        Assert.Contains("SecondaryButton", xaml);

        foreach (var theme in new[] { "LightTheme.xaml", "DarkTheme.xaml" })
        {
            var themeKeys = CollectDefinedKeys(ReadProjectFile(Path.Combine("Workspace1C", "Themes", theme)));
            Assert.Contains("SecondaryButton", themeKeys);
            Assert.Contains("PrimaryButton", themeKeys);
        }
    }

    [Fact]
    public void ItsAccountCombos_DisplayMemberPath_Name_OnBothPlatforms()
    {
        // Страховка issue #333: в окне «Общие настройки» и в редакторе типовой конфигурации
        // ComboBox учётной записи ИТС обязан показывать ИМЯ записи, а не идентификатор/тип.
        // WPF: SettingsWindow.xaml (ItsAccountsCombo) и ConfigTypeEditWindow.xaml (AccountCombo).
        var settingsXaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        Assert.Matches(
            @"x:Name=""ItsAccountsCombo""[^>]*DisplayMemberPath=""Name""",
            settingsXaml);

        var editorXaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ConfigTypeEditWindow.xaml"));
        Assert.Matches(
            @"x:Name=""AccountCombo""[^>]*DisplayMemberPath=""Name""",
            editorXaml);

        // Avalonia: файлы *.Avalonia.cs задают DisplayMemberBinding на свойство Name пункта.
        var avaloniaSettings = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs"));
        Assert.Contains("nameof(ViewModels.ItsAccountSelectionItem.Name)", avaloniaSettings);

        var avaloniaEditor = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ConfigTypeEditWindow.Avalonia.cs"));
        Assert.Contains("nameof(ViewModels.ItsAccountSelectionItem.Name)", avaloniaEditor);
    }
}