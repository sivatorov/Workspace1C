using System;
using System.Collections.Generic;
using System.IO;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #129 «Считать версию прошлой» (v1.0.1.59): диагностический флаг
/// <c>CM_PREV_VERSION</c> вкладки «Логирование» — приложение виртуально считает
/// себя ПРЕДЫДУЩЕЙ версией, чтобы «Что нового» и сводка релиза были проверяемы
/// СРАЗУ, не дожидаясь следующего выпуска.
/// <list type="bullet">
/// <item>снижение версии: из git-тегов (чистая функция выбора предыдущего тега)
/// и fallback «минус 1 сборка»;</item>
/// <item>чтение/запись флага в <c>trace.json</c> через <see cref="TraceFlags"/>
/// (переживает перезапуск, как остальные флаги вкладки «Логирование»);</item>
/// <item>эффективная версия в <see cref="WhatsNewService"/> (ShouldShow/MarkShown)
/// и автосброс флага после успешного обновления
/// (<see cref="EffectiveVersionService.ResetPrevVersionFlag"/>);</item>
/// <item>source-гварды точек сброса в UpdateService (WPF и Avalonia) и
/// локализации ru/en.</item>
/// </list>
/// Тесты меняют глобальное состояние <see cref="TraceFlags"/> и точку подмены
/// git-запроса, поэтому изолированы коллекцией TraceFlagsState (последовательный прогон).
/// </summary>
[Collection("TraceFlagsState")]
public sealed class Issue129PrevVersionTests : IDisposable
{
    private readonly string _dir;
    private readonly Func<string?>? _previousGitProvider;

    public Issue129PrevVersionTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "w1c_prevver_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        // Изолируем TraceFlags от реального конфига пользователя и от env.
        TraceFlags.ConfigDirectoryOverride = _dir;
        TraceFlags.EnvReader = _ => null;
        TraceFlags.ResetCacheForTesting();
        // Подменяем git-запрос (по умолчанию каждый тест ставит свой провайдер явно).
        _previousGitProvider = EffectiveVersionService.GitTagsProvider;
    }

    public void Dispose()
    {
        EffectiveVersionService.GitTagsProvider = _previousGitProvider;
        EffectiveVersionService.ResetGitCacheForTesting();
        TraceFlags.ConfigDirectoryOverride = null;
        TraceFlags.ResetCacheForTesting();
        try { Directory.Delete(_dir, true); }
        catch { /* временный каталог теста */ }
    }

    // --------------------------------------------------------------
    // Fallback: снижение версии на одну сборку
    // --------------------------------------------------------------

    [Fact]
    public void DecrementLastPart_LowersRevisionByOne()
    {
        // Каноничный случай из issue #129: 1.0.1.60 → 1.0.1.59.
        Assert.Equal("1.0.1.59", EffectiveVersionService.DecrementLastPart("1.0.1.60"));
        Assert.Equal("2.0.0.1", EffectiveVersionService.DecrementLastPart("2.0.0.2"));
    }

    [Theory]
    [InlineData("1.0.1.0")]     // последняя часть 0 — снижать нечем
    [InlineData("1.0.1")]       // не 4-частная версия
    [InlineData("")]            // пустая строка
    [InlineData("abc")]         // мусор вместо версии
    [InlineData("1.0.1.59.7")]  // 5 частей — не версия
    public void DecrementLastPart_InvalidInputs_ReturnNull(string version)
    {
        Assert.Null(EffectiveVersionService.DecrementLastPart(version));
        Assert.Null(EffectiveVersionService.DecrementLastPart(null));
    }

    // --------------------------------------------------------------
    // Выбор предыдущего тега из вывода git tag
    // --------------------------------------------------------------

    [Fact]
    public void PickPreviousFromTags_TakesGreatestTagBelowCurrent_StripVPrefix()
    {
        // Текущая версия ещё не тегнута (1.0.1.60): предыдущая — последний
        // выпущенный тег v1.0.1.59. Префикс «v» убирается (в «Что нового»
        // показывается номер версии, а не имя тега).
        const string tags = "v1.0.1.58\nv1.0.1.59\nv1.0.1.57\nv1.0.0.99";
        Assert.Equal("1.0.1.59", EffectiveVersionService.PickPreviousFromTags(tags, "1.0.1.60"));
    }

    [Fact]
    public void PickPreviousFromTags_CurrentVersionTagged_TakesTagBelowIt()
    {
        // Текущая версия уже тегнута: предыдущая — тег СТРОГО меньше текущей.
        const string tags = "v1.0.1.59\nv1.0.1.58\nv1.0.1.57";
        Assert.Equal("1.0.1.58", EffectiveVersionService.PickPreviousFromTags(tags, "1.0.1.59"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\n\n")]
    public void PickPreviousFromTags_EmptyOutput_ReturnsNull(string? tags)
    {
        Assert.Null(EffectiveVersionService.PickPreviousFromTags(tags, "1.0.1.59"));
    }

    [Fact]
    public void PickPreviousFromTags_IgnoresNonVersionTags_AndTagsAboveCurrent()
    {
        // Теги, не похожие на версию, и «будущие» теги (>= текущей) игнорируются.
        const string tags = "v2.0.0.0\nsivatorov-readme\nv1.0.1.59";
        Assert.Equal("1.0.1.59", EffectiveVersionService.PickPreviousFromTags(tags, "1.0.1.60"));
        // Нет тега ниже текущей — null (сработает fallback «минус 1 сборка»).
        Assert.Null(EffectiveVersionService.PickPreviousFromTags("v1.0.1.60\nv1.0.1.61", "1.0.1.60"));
    }

    // --------------------------------------------------------------
    // Чтение/запись флага в trace.json (переживание перезапуска)
    // --------------------------------------------------------------

    [Fact]
    public void PrevVersionFlag_PersistedInTraceJson_AndSurvivesCacheReset()
    {
        var path = Path.Combine(_dir, TraceFlagsFormat.ConfigFileName);
        Assert.False(EffectiveVersionService.IsPrevVersionModeEnabled(), "По умолчанию флаг выключен.");

        // Включение через вкладку «Логирование» (SaveCategorySettings, как у всех категорий).
        Assert.True(TraceFlags.SaveCategorySettings(new Dictionary<string, bool>
        {
            [TraceFlags.PrevVersionFlag] = true
        }));
        Assert.True(File.Exists(path), "trace.json должен быть создан.");
        Assert.Contains("\"CM_PREV_VERSION\": true", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.True(EffectiveVersionService.IsPrevVersionModeEnabled());

        // Переживание перезапуска: полный сброс кэша (как при новом старте приложения)
        // и перечитывание файла с диска.
        TraceFlags.ResetCacheForTesting();
        Assert.True(EffectiveVersionService.IsPrevVersionModeEnabled());
    }

    [Fact]
    public void ResetPrevVersionFlag_WritesFalse_AndKeepsOtherFlags()
    {
        // Включаем флаг и ещё одну категорию (проверяем, что сброс не трогает соседей).
        TraceFlags.SaveCategorySettings(new Dictionary<string, bool>
        {
            [TraceFlags.PrevVersionFlag] = true,
            [TraceFlags.ColumnsFlag] = true
        });
        Assert.True(EffectiveVersionService.IsPrevVersionModeEnabled());

        // Автосброс после успешного обновления.
        EffectiveVersionService.ResetPrevVersionFlag();

        Assert.False(EffectiveVersionService.IsPrevVersionModeEnabled(), "Флаг должен быть сброшен.");
        Assert.True(TraceFlags.IsEnabled(TraceFlags.ColumnsFlag), "Соседние флаги сброс не трогает.");
        Assert.Contains("\"CM_PREV_VERSION\": false",
            File.ReadAllText(Path.Combine(_dir, TraceFlagsFormat.ConfigFileName)), StringComparison.Ordinal);
    }

    // --------------------------------------------------------------
    // Эффективная версия (ShouldShow/MarkShown)
    // --------------------------------------------------------------

    [Fact]
    public void GetEffectiveVersion_FlagOff_ReturnsCurrent()
    {
        Assert.False(EffectiveVersionService.IsPrevVersionModeEnabled());
        Assert.Equal("1.0.1.60", EffectiveVersionService.GetEffectiveVersion("1.0.1.60"));
    }

    [Fact]
    public void GetEffectiveVersion_FlagOn_UsesPreviousGitTag()
    {
        EnablePrevVersionFlag();
        EffectiveVersionService.GitTagsProvider = () => "v1.0.1.58\nv1.0.1.59";
        EffectiveVersionService.ResetGitCacheForTesting();

        Assert.Equal("1.0.1.59", EffectiveVersionService.GetEffectiveVersion("1.0.1.60"));
    }

    [Fact]
    public void GetEffectiveVersion_FlagOn_GitUnavailable_DecrementsLastPart()
    {
        EnablePrevVersionFlag();
        // git недоступен (упакованная сборка / нет git в PATH) — fallback «минус 1 сборка».
        EffectiveVersionService.GitTagsProvider = () => null;
        EffectiveVersionService.ResetGitCacheForTesting();

        Assert.Equal("1.0.1.59", EffectiveVersionService.GetEffectiveVersion("1.0.1.60"));
    }

    // --------------------------------------------------------------
    // Единый источник истины для проверки обновлений (issue #129)
    // --------------------------------------------------------------

    [Fact]
    public void CurrentForUpdates_FlagOff_EqualsRealBuild()
    {
        // Флаг выключен — решать об обновлении нужно по реальной сборке.
        Assert.False(EffectiveVersionService.IsPrevVersionModeEnabled());
        Assert.Equal(VersionInfo.Display(), EffectiveVersionService.CurrentForUpdates);
        Assert.False(EffectiveVersionService.IsDowngraded);
    }

    [Fact]
    public void CurrentForUpdates_FlagOn_EqualsEffectiveVersion()
    {
        EnablePrevVersionFlag();
        EffectiveVersionService.GitTagsProvider = () => "v1.0.1.58\nv1.0.1.59";
        EffectiveVersionService.ResetGitCacheForTesting();

        Assert.Equal("1.0.1.59", EffectiveVersionService.CurrentForUpdates);
        Assert.True(EffectiveVersionService.IsDowngraded);
    }

    [Fact]
    public void UpdateDecision_UsesEffectiveVersion_WhenDowngraded()
    {
        // Ключевой сценарий issue #129: релиз равен РЕАЛЬНОЙ сборке.
        var real = VersionInfo.Display();
        var sameRelease = new ReleaseInfo { TagName = "v" + real };

        // Флаг выключен — «обновлений нет» (как и было до исправления).
        Assert.False(GitHubReleaseService.IsNewerThan(
            sameRelease, EffectiveVersionService.CurrentForUpdates));

        // Флаг включён и git даёт предыдущую версию — та же сборка СЧИТАЕТСЯ
        // доступным обновлением (сравнение идёт с эффективной версией).
        EnablePrevVersionFlag();
        EffectiveVersionService.GitTagsProvider = () => "v1.0.0.1";
        EffectiveVersionService.ResetGitCacheForTesting();

        Assert.Equal("1.0.0.1", EffectiveVersionService.CurrentForUpdates);
        Assert.True(GitHubReleaseService.IsNewerThan(
            sameRelease, EffectiveVersionService.CurrentForUpdates));
    }

    [Fact]
    public void FormatVersionForDisplay_NoDowngrade_ReturnsRealVersion()
    {
        const string format = "{0} (prev: {1})";
        // Версии совпадают (флаг выключен либо снижать нечем) — только реальная сборка.
        Assert.Equal("1.0.1.60",
            EffectiveVersionService.FormatVersionForDisplay("1.0.1.60", "1.0.1.60", format));
        Assert.Equal("1.0.1.60",
            EffectiveVersionService.FormatVersionForDisplay("1.0.1.60", null, format));
        Assert.Equal("1.0.1.60",
            EffectiveVersionService.FormatVersionForDisplay(" 1.0.1.60 ", "", format));
    }

    [Fact]
    public void FormatVersionForDisplay_Downgraded_ShowsRealAndEffective()
    {
        Assert.Equal("1.0.1.60 (prev: 1.0.1.59)",
            EffectiveVersionService.FormatVersionForDisplay("1.0.1.60", "1.0.1.59", "{0} (prev: {1})"));
        // Пустой шаблон — нейтральный fallback без падения.
        Assert.Equal("1.0.1.60 (1.0.1.59)",
            EffectiveVersionService.FormatVersionForDisplay("1.0.1.60", "1.0.1.59", null));
    }

    [Fact]
    public void ShouldOfferInsteadOfAutoInstall_OnlyForSameRealBuildWhenDowngraded()
    {
        var real = VersionInfo.Display();
        var sameRelease = new ReleaseInfo { TagName = "v" + real };
        var newerRelease = new ReleaseInfo { TagName = "v9.9.9.9" };

        // Флаг выключен — обычная автоустановка (ограничения нет).
        Assert.False(EffectiveVersionService.ShouldOfferInsteadOfAutoInstall(sameRelease));

        EnablePrevVersionFlag();
        EffectiveVersionService.GitTagsProvider = () => "v1.0.0.1";
        EffectiveVersionService.ResetGitCacheForTesting();

        // Релиз совпадает с РЕАЛЬНОЙ сборкой — только предлагать, не ставить молча.
        Assert.True(EffectiveVersionService.ShouldOfferInsteadOfAutoInstall(sameRelease));
        // Реально более новый релиз ставится автоматически, как раньше.
        Assert.False(EffectiveVersionService.ShouldOfferInsteadOfAutoInstall(newerRelease));
        Assert.False(EffectiveVersionService.ShouldOfferInsteadOfAutoInstall(null));
    }

    [Fact]
    public void ShouldShowAndMarkShown_UseEffectiveVersion()
    {
        EnablePrevVersionFlag();
        EffectiveVersionService.GitTagsProvider = () => "v1.0.1.58\nv1.0.1.59";
        EffectiveVersionService.ResetGitCacheForTesting();

        // При флаге «Считать версию прошлой» эффективная версия НЕ совпадает
        // с реальной (текущей): git-теги дают прошлую либо fallback «−1 сборка».
        var expected = EffectiveVersionService.GetEffectiveVersion(WhatsNewService.CurrentVersion);
        Assert.NotEqual(WhatsNewService.CurrentVersion, expected);

        // MarkShown запоминает ЭФФЕКТИВНУЮ версию (issue #129), а не реальную сборку,
        // поэтому сразу после показа диалог больше не появляется.
        var settings = new AppSettings { ShowWhatsNewOnStart = true };
        WhatsNewService.MarkShown(settings);
        Assert.Equal(expected, settings.LastWhatsNewVersion);
        Assert.False(WhatsNewService.ShouldShow(settings));
    }

    [Fact]
    public void ResetPrevVersionFlag_IsSilentWhenConfigUnavailable()
    {
        // trace.json недоступен (каталог только для чтения и т.п.) — сброс НЕ бросает:
        // обновление важнее флага (issue #129: «если лог-файл недоступен — не падать»).
        EnablePrevVersionFlag();
        TraceFlags.ConfigDirectoryOverride = Path.Combine(_dir, "missing_dir_for_reset");
        TraceFlags.ResetCacheForTesting();
        try
        {
            EffectiveVersionService.ResetPrevVersionFlag();
        }
        finally
        {
            TraceFlags.ConfigDirectoryOverride = _dir;
            TraceFlags.ResetCacheForTesting();
        }
    }

    // --------------------------------------------------------------
    // Мгновенное применение без перезапуска (issue #129, часть 2)
    // --------------------------------------------------------------

    [Fact]
    public void SettingsSaved_FlagEnabled_ReevaluatesAvailabilityWithoutRestart()
    {
        // Релиз равен РЕАЛЬНОЙ сборке — именно его проверка обновлений видит при
        // выключенном флаге и отвергает как «не новее» (дефект из комментария @7OH).
        var real = VersionInfo.Display();
        var sameRelease = new ReleaseInfo { TagName = "v" + real };

        Assert.False(EffectiveVersionService.IsPrevVersionModeEnabled());
        var before = UpdateAvailabilityLogic.Reevaluate(
            sameRelease, EffectiveVersionService.CurrentForUpdates, skipped: false);
        Assert.Null(before.Release);
        Assert.False(before.NeedsNetworkCheck);

        var savedEvents = new List<TraceSettingsSavedEventArgs>();
        void Handler(TraceSettingsSavedEventArgs args) => savedEvents.Add(args);
        TraceFlags.SettingsSaved += Handler;
        try
        {
            EnablePrevVersionFlag();
        }
        finally
        {
            TraceFlags.SettingsSaved -= Handler;
        }

        // Событие поднято — главное окно и UpdateService пересчитывают состояние СРАЗУ.
        var args = Assert.Single(savedEvents);
        Assert.True(args.Contains(TraceFlags.PrevVersionFlag));

        // Пересчёт без перезапуска: та же сборка становится доступным обновлением.
        EffectiveVersionService.GitTagsProvider = () => "v1.0.0.1";
        EffectiveVersionService.ResetGitCacheForTesting();
        Assert.Equal("1.0.0.1", EffectiveVersionService.CurrentForUpdates);

        var after = UpdateAvailabilityLogic.Reevaluate(
            sameRelease, EffectiveVersionService.CurrentForUpdates, skipped: false);
        Assert.Same(sameRelease, after.Release);
        Assert.False(after.NeedsNetworkCheck);
    }

    [Fact]
    public void SettingsSaved_FlagDisabled_ReleaseIsNotNewer()
    {
        EnablePrevVersionFlag();
        EffectiveVersionService.GitTagsProvider = () => "v1.0.0.1";
        EffectiveVersionService.ResetGitCacheForTesting();

        var real = VersionInfo.Display();
        var sameRelease = new ReleaseInfo { TagName = "v" + real };
        Assert.NotNull(UpdateAvailabilityLogic.Reevaluate(
            sameRelease, EffectiveVersionService.CurrentForUpdates, skipped: false).Release);

        // Выключение флага тем же путём (вкладка «Логирование») возвращает состояние
        // «не новее» — снова без перезапуска.
        Assert.True(TraceFlags.SaveCategorySettings(new Dictionary<string, bool>
        {
            [TraceFlags.PrevVersionFlag] = false
        }));
        Assert.Equal(real, EffectiveVersionService.CurrentForUpdates);

        var after = UpdateAvailabilityLogic.Reevaluate(
            sameRelease, EffectiveVersionService.CurrentForUpdates, skipped: false);
        Assert.Null(after.Release);
        Assert.False(after.NeedsNetworkCheck);
    }

    private void EnablePrevVersionFlag()
    {
        Assert.True(TraceFlags.SaveCategorySettings(new Dictionary<string, bool>
        {
            [TraceFlags.PrevVersionFlag] = true
        }));
    }

    // --------------------------------------------------------------
    // Source-гварды точек сброса и локализации
    // --------------------------------------------------------------

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
    public void UpdateChecks_CompareWithEffectiveVersion_NotWithRealBuild()
    {
        // Source-гвард issue #129: все решения об обновлении идут через единый
        // EffectiveVersionService.CurrentForUpdates, прямых сравнений с реальной
        // сборкой в этих местах быть не должно.
        foreach (var relativePath in new[]
                 {
                     Path.Combine("Workspace1C", "Services", "UpdateService.cs"),
                     Path.Combine("Workspace1C", "Services", "UpdateService.Avalonia.cs"),
                     Path.Combine("Workspace1C", "Services", "SchedulerService.cs")
                 })
        {
            var source = ReadProjectFile(relativePath);
            Assert.Contains("EffectiveVersionService.CurrentForUpdates", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IsNewerThan(release, VersionInfo.Display())", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FooterAndUpdateDialogs_ShowEffectiveVersion()
    {
        // Подпись версии в подвале и диалоги обновления показывают реальную сборку
        // и эффективную версию (issue #129), а не «голую» реальную версию.
        var wpfFooter = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Updates.cs"));
        Assert.Contains("EffectiveVersionService.FormatVersionForDisplay", wpfFooter, StringComparison.Ordinal);
        Assert.DoesNotContain("FooterVersionText.Text = \"v\" + VersionInfo.Display()",
            wpfFooter, StringComparison.Ordinal);

        var avaloniaFooter = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
        Assert.Contains("EffectiveVersionService.FormatVersionForDisplay", avaloniaFooter, StringComparison.Ordinal);

        var wpfDialog = ReadProjectFile(Path.Combine("Workspace1C", "Services", "UpdateAvailableWindow.xaml.cs"));
        Assert.Contains("EffectiveVersionService.FormatVersionForDisplay", wpfDialog, StringComparison.Ordinal);

        var avaloniaDialog = ReadProjectFile(Path.Combine("Workspace1C", "Services", "UpdateService.Avalonia.cs"));
        Assert.Contains("EffectiveVersionService.FormatVersionForDisplay", avaloniaDialog, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_HasEffectiveVersionKeys_InBothLanguages()
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var json = File.ReadAllText(Path.Combine(
                FindRepoRoot(), "Workspace1C", "Localization", "Languages", lang + ".json"));
            Assert.Contains("\"Common.VersionEffective\":", json, StringComparison.Ordinal);
            Assert.Contains("\"Update.AvailableDowngraded\":", json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void UpdateServices_ResetPrevVersionFlagAfterSuccessfulUpdate()
    {
        // WPF: оба пути успешного обновления (готовый файл и диалог) сбрасывают флаг.
        var wpf = ReadProjectFile(Path.Combine("Workspace1C", "Services", "UpdateService.cs"));
        Assert.Contains("EffectiveVersionService.ResetPrevVersionFlag();", wpf, StringComparison.Ordinal);
        // Avalonia: путь успешного обновления сбрасывает флаг.
        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Services", "UpdateService.Avalonia.cs"));
        Assert.Contains("EffectiveVersionService.ResetPrevVersionFlag();", avalonia, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_HasPrevVersionKeys_InBothLanguages()
    {
        // Ключи генерируются LogCategory.TitleKey/DescriptionKey («CM_PREV_VERSION» → CatPrevVersion).
        var title = LogCategory.TitleKey(TraceFlags.PrevVersionFlag);
        var description = LogCategory.DescriptionKey(TraceFlags.PrevVersionFlag);
        Assert.Equal("Settings.Logging.CatPrevVersion", title);
        Assert.Equal("Settings.Logging.CatPrevVersionDesc", description);

        foreach (var lang in new[] { "ru", "en" })
        {
            var json = File.ReadAllText(Path.Combine(
                FindRepoRoot(), "Workspace1C", "Localization", "Languages", lang + ".json"));
            Assert.Contains($"\"{title}\":", json, StringComparison.Ordinal);
            Assert.Contains($"\"{description}\":", json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WhatsNewWindows_UseEffectiveVersion()
    {
        // Обе платформы показывают в «Что нового» ЭФФЕКТИВНУЮ версию (issue #129).
        var wpf = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WhatsNewWindow.xaml.cs"));
        Assert.Contains("WhatsNewService.EffectiveVersion", wpf, StringComparison.Ordinal);

        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WhatsNewWindow.Avalonia.cs"));
        Assert.Contains("WhatsNewService.EffectiveVersion", avalonia, StringComparison.Ordinal);
    }
}
