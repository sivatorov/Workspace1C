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
