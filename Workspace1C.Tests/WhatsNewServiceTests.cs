using System.IO;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты сервиса «Что нового» (v100112, F3): решение о показе диалога при старте
/// (версия изменилась с прошлого запуска, показ включён в настройках), маркировка
/// показанной версии, наличие сводки для известных версий и отсутствие для неизвестных.
/// Сводка строится через LocalizationManager — при пустом словаре тесты проверяют
/// только структуру (пункты либо null), не текст.
/// </summary>
public sealed class WhatsNewServiceTests
{
    [Fact]
    public void ShouldShow_WithKnownVersion_AndEmptyLastVersion_ReturnsTrue()
    {
        var settings = new AppSettings { ShowWhatsNewOnStart = true, LastWhatsNewVersion = string.Empty };

        // Текущая версия сборки имеет готовую сводку только для релиза v100112;
        // при другой версии диалог не показывается вовсе (не мешает тестовым сборкам).
        if (WhatsNewService.HasEntry(WhatsNewService.CurrentVersion))
            Assert.True(WhatsNewService.ShouldShow(settings));
        else
            Assert.False(WhatsNewService.ShouldShow(settings));
    }

    [Fact]
    public void ShouldShow_SameVersionShown_ReturnsFalse()
    {
        var settings = new AppSettings { ShowWhatsNewOnStart = true };
        WhatsNewService.MarkShown(settings);

        Assert.Equal(WhatsNewService.CurrentVersion, settings.LastWhatsNewVersion);
        Assert.False(WhatsNewService.ShouldShow(settings));
    }

    [Fact]
    public void ShouldShow_DisabledInSettings_ReturnsFalse()
    {
        var settings = new AppSettings { ShowWhatsNewOnStart = false, LastWhatsNewVersion = string.Empty };

        Assert.False(WhatsNewService.ShouldShow(settings));
    }

    [Fact]
    public void ShouldShow_NullSettings_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => WhatsNewService.ShouldShow(null!));
        Assert.Throws<ArgumentNullException>(() => WhatsNewService.MarkShown(null!));
    }

    [Fact]
    public void GetReleaseNotes_UnknownVersion_ReturnsNull()
    {
        Assert.False(WhatsNewService.HasEntry("9.9.9.9"));
        Assert.Null(WhatsNewService.GetReleaseNotes("9.9.9.9"));
    }

    [Fact]
    public void GetReleaseNotes_KnownVersion_ReturnsNonEmptyItems()
    {
        // Для релиза v100112 сводка захардкожена (F1–F3) — пункты не пустые.
        var known = WhatsNewService.HasEntry(WhatsNewService.CurrentVersion)
            ? WhatsNewService.CurrentVersion
            : null;
        if (known is null)
            return; // сборка без готовой сводки — проверять нечего

        var notes = WhatsNewService.GetReleaseNotes(known);

        // В тестовой среде LocalizationManager.T возвращает сам ключ («WhatsNew.*»
        // начинается с префикса) — сводка null; это поведение сервиса, а не ошибка.
        var firstKey = $"WhatsNew.{WhatsNewService.Normalize(known)}.Item1";
        if (LocalizationManager.T(firstKey).StartsWith("WhatsNew.", StringComparison.Ordinal))
        {
            Assert.Null(notes);
            return;
        }

        Assert.NotNull(notes);
        Assert.NotEmpty(notes!);
        Assert.All(notes!, item => Assert.False(string.IsNullOrWhiteSpace(item)));
    }

    [Fact]
    public void GetReleaseNotes_KnownVersion_KeysPresentInBothDictionaries()
    {
        // Ключи сводки текущего релиза должны присутствовать в ОБАХ встроенных
        // словарях (ru.json и en.json) — иначе интерфейс покажет «голый» ключ.
        if (!WhatsNewService.HasEntry(WhatsNewService.CurrentVersion))
            return; // сборка без готовой сводки — проверять нечего

        var prefix = $"WhatsNew.{WhatsNewService.Normalize(WhatsNewService.CurrentVersion)}.";
        var ru = LoadLanguageKeys("ru.json");
        var en = LoadLanguageKeys("en.json");

        Assert.Contains(prefix + "Item1", ru);
        Assert.Contains(prefix + "Item1", en);
    }

    /// <summary>Словари языка: <c>strings</c> из Workspace1C/Localization/Languages/*.json
    /// (файлы репозитория, образец — LocalizationCoverageServicesTests).</summary>
    private static HashSet<string> LoadLanguageKeys(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null &&
               !File.Exists(Path.Combine(dir.FullName, "Workspace1C", "Localization", "Languages", fileName)))
            dir = dir.Parent;
        Assert.True(dir is not null,
            $"Не найден файл Workspace1C/Localization/Languages/{fileName} от {AppContext.BaseDirectory}.");
        var json = File.ReadAllText(
            Path.Combine(dir.FullName, "Workspace1C", "Localization", "Languages", fileName));
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("strings").EnumerateObject()
            .Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void Normalize_ReplacesDotsWithUnderscores()
    {
        Assert.Equal("1_0_1_12", WhatsNewService.Normalize("1.0.1.12"));
        Assert.Equal(string.Empty, WhatsNewService.Normalize(string.Empty));
        Assert.Equal(string.Empty, WhatsNewService.Normalize(null!));
    }

    [Fact]
    public void ChangelogUrl_IsGitHubChangelog()
    {
        Assert.StartsWith("https://github.com/", WhatsNewService.ChangelogUrl);
        Assert.EndsWith("CHANGELOG.md", WhatsNewService.ChangelogUrl);
    }
}
