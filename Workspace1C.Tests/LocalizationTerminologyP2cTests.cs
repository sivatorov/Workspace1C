using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Аудит UI P2c — тексты и терминология.
/// <list type="bullet">
/// <item>паритет ключей ru.json/en.json;</item>
/// <item>общие действия живут только в <c>Common.*</c>, доменные дубли удалены;</item>
/// <item>«OK» в русской локализации — кириллицей «ОК», в английской — латиницей «OK»;</item>
/// <item>в пользовательских строках нет эмодзи и текстовых глифов;</item>
/// <item>единые названия «1С:Предприятие» / «Конфигуратор» в подписях кнопок;</item>
/// <item>правило «ИБ» / «информационная база» и «Пользователь» / «Логин».</item>
/// </list>
/// </summary>
public sealed class LocalizationTerminologyP2cTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Localization")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Workspace1C.Tests")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Корень репозитория не найден из тестового каталога.");
    }

    private static Dictionary<string, string> LoadStrings(string languageFile)
    {
        var path = Path.Combine(FindRepoRoot(), "Workspace1C", "Localization", "Languages", languageFile);
        Assert.True(File.Exists(path), $"Файл локализации не найден: {path}");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in doc.RootElement.GetProperty("strings").EnumerateObject())
            result[property.Name] = property.Value.GetString() ?? string.Empty;
        return result;
    }

    private static Dictionary<string, string> Ru() => LoadStrings("ru.json");
    private static Dictionary<string, string> En() => LoadStrings("en.json");

    [Fact]
    public void RuAndEn_HaveSameKeySet()
    {
        var ru = Ru();
        var en = En();

        var onlyRu = ru.Keys.Except(en.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var onlyEn = en.Keys.Except(ru.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();

        Assert.True(onlyRu.Count == 0, "Ключи только в ru.json: " + string.Join(", ", onlyRu));
        Assert.True(onlyEn.Count == 0, "Ключи только в en.json: " + string.Join(", ", onlyEn));
    }

    [Theory]
    [InlineData("Updates.Cancel")]
    [InlineData("Updates.Close")]
    [InlineData("Updates.Save")]
    [InlineData("Services.Cancel")]
    [InlineData("Services.Save")]
    [InlineData("DeleteInfobase.Cancel")]
    [InlineData("RepositoryBrowser.Close")]
    public void CommonActions_HaveNoDomainDuplicates(string key)
    {
        var ru = Ru();
        var en = En();

        Assert.False(ru.ContainsKey(key), $"ru.json: доменный дубль {key} должен быть удалён в пользу Common.*");
        Assert.False(en.ContainsKey(key), $"en.json: доменный дубль {key} должен быть удалён в пользу Common.*");
    }

    [Fact]
    public void CommonActions_ExistInBothLanguages()
    {
        var ru = Ru();
        var en = En();

        foreach (var key in new[] { "Common.Ok", "Common.Cancel", "Common.Close", "Common.Save" })
        {
            Assert.True(ru.ContainsKey(key), $"ru.json: нет ключа {key}");
            Assert.True(en.ContainsKey(key), $"en.json: нет ключа {key}");
        }
    }

    [Fact]
    public void CommonOk_IsCyrillicInRussian_AndLatinInEnglish()
    {
        Assert.Equal("ОК", Ru()["Common.Ok"]);
        Assert.Equal("OK", En()["Common.Ok"]);
    }

    /// <summary>
    /// Запрещённые в пользовательских строках эмодзи и текстовые глифы.
    /// Типографские разделители «→», «↔» и маркер списка «•» разрешены.
    /// </summary>
    private static bool IsForbiddenGlyph(Rune rune)
    {
        var value = rune.Value;
        return value is >= 0x2500 and <= 0x25FF   // рамки и геометрические фигуры (★, ◂, ▲, ●)
            or >= 0x2600 and <= 0x27BF            // разные символы и dingbats (✓, ✗, ✅, ❌, ✎)
            or >= 0x2B00 and <= 0x2BFF            // дополнительные стрелки и символы
            or >= 0x1F000 and <= 0x1FAFF          // эмодзи (📌, 📁, 🖥)
            or 0xFE0F;                            // вариационный селектор эмодзи
    }

    [Theory]
    [InlineData("ru.json")]
    [InlineData("en.json")]
    public void UiStrings_ContainNoEmojiOrTextGlyphs(string languageFile)
    {
        var offenders = new List<string>();
        foreach (var (key, text) in LoadStrings(languageFile))
        {
            foreach (var rune in text.EnumerateRunes())
            {
                if (IsForbiddenGlyph(rune))
                {
                    offenders.Add($"{key}: {rune} (U+{rune.Value:X4})");
                    break;
                }
            }
        }

        Assert.True(offenders.Count == 0,
            $"{languageFile}: эмодзи/текстовые глифы в пользовательских строках: " + string.Join(", ", offenders));
    }

    [Fact]
    public void HelpStrings_UseWordsInsteadOfGlyphs()
    {
        var ru = Ru();
        var en = En();

        foreach (var key in new[]
                 {
                     "Main.BaseListHelp", "Main.TagFilterHelp", "Main.CurrentSessionHelp",
                     "Updates.ConfigTypesExplanation", "Updates.DefineVersionHint", "Updates.MatchCriteriaHint"
                 })
        {
            Assert.True(ru.ContainsKey(key), $"ru.json: нет ключа {key}");
            Assert.True(en.ContainsKey(key), $"en.json: нет ключа {key}");
        }

        Assert.Contains("звезд", ru["Main.BaseListHelp"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("закреплен", ru["Main.BaseListHelp"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("стрелк", ru["Main.BaseListHelp"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LaunchButtons_UseUnifiedProductName()
    {
        var ru = Ru();
        var en = En();

        // Кнопки запуска: «Запустить 1С:Предприятие» / «Запустить Конфигуратор».
        foreach (var key in new[] { "Main.LaunchEnterprise", "Main.LaunchEnterpriseShort", "Main.BatchRunEnterprise" })
            Assert.StartsWith("Запустить 1С:Предприятие", ru[key], StringComparison.Ordinal);

        foreach (var key in new[] { "Main.LaunchConfigurator", "Main.LaunchConfiguratorShort", "Main.BatchRunConfigurator" })
            Assert.StartsWith("Запустить Конфигуратор", ru[key], StringComparison.Ordinal);

        Assert.Contains("1С:Предприятие", ru["BatchLaunch.ConfirmEnterprise"], StringComparison.Ordinal);
        Assert.Contains("1С:Предприятие", ru["Main.LaunchEnterpriseTooltip"], StringComparison.Ordinal);
        Assert.Contains("Конфигуратор", ru["Main.LaunchConfiguratorSectionTooltip"], StringComparison.Ordinal);

        // Английская локализация использует те же сущности.
        foreach (var key in new[] { "Main.LaunchEnterprise", "Main.LaunchEnterpriseShort", "Main.BatchRunEnterprise" })
            Assert.StartsWith("Launch 1C:Enterprise", en[key], StringComparison.Ordinal);

        foreach (var key in new[] { "Main.LaunchConfigurator", "Main.LaunchConfiguratorShort", "Main.BatchRunConfigurator" })
            Assert.StartsWith("Launch Configurator", en[key], StringComparison.Ordinal);
    }

    [Fact]
    public void ProductName_IsNotStandaloneEnterprise()
    {
        var ru = Ru();

        // Короткие подписи/чипы и предложения — всегда «1С:Предприятие».
        Assert.Equal("1С:Предприятие", ru["Main.Enterprise"]);
        Assert.Equal("1С:Предприятие", ru["LongOperation.Mode.Enterprise"]);
        Assert.Equal("1С:Предприятие", ru["Running.PickerEnterprise"]);
        Assert.StartsWith("1С:Предприятие", ru["Main.BookmarksEnterprise"], StringComparison.Ordinal);
        Assert.DoesNotContain("(Конфигуратор или Предприятие)", ru["Settings.Columns.ActionsHoverOnlyHint"], StringComparison.Ordinal);
    }

    [Fact]
    public void InfobaseAbbreviation_FollowsRule()
    {
        var ru = Ru();

        // Узкие колонки — «ИБ».
        Assert.Contains("ИБ", ru["Maintenance.Size"], StringComparison.Ordinal);
        Assert.DoesNotContain("нформационная база", ru["Maintenance.Size"], StringComparison.Ordinal);
        Assert.Equal("ИБ", ru["ServerMonitor.Columns.Process.Infobases"]);
        Assert.Contains("ИБ", ru["Settings.Columns.Size"], StringComparison.Ordinal);

        // Широкая колонка и заголовки/диалоги — полная форма.
        Assert.Contains("нформационная база", ru["ServerMonitor.Columns.Session.Infobase"], StringComparison.Ordinal);
    }

    [Fact]
    public void CredentialsLabels_FollowUserVsLoginRule()
    {
        var ru = Ru();

        // Для 1С — «Пользователь».
        foreach (var key in new[]
                 {
                     "Connection.FieldUser", "Connection.FieldConfiguratorUser",
                     "ServerMonitor.Infobase.Props.AdminUser", "ServerMonitor.User",
                     "ServerMonitor.Servers.User", "ServerMonitor.Infobase.Drop.RetryLogin",
                     "Settings.CommonAuth.Login", "Connection.RepositoryLoginLabel",
                     "TestDeploy.CredentialsUser"
                 })
        {
            Assert.Contains("Пользователь", ru[key], StringComparison.Ordinal);
            Assert.DoesNotContain("Логин", ru[key], StringComparison.Ordinal);
        }

        // Для СУБД — «Логин».
        Assert.Contains("Логин", ru["Dbms.User"], StringComparison.Ordinal);
        Assert.DoesNotContain("Пользователь", ru["Dbms.User"], StringComparison.Ordinal);
    }
}
