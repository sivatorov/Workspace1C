using System;
using System.Collections.Generic;
using System.IO;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #136 «Access to the path is denied» (v1.0.1.78): при установке в
/// <c>C:\Program Files</c> сохранение настроек интерфейса падало с
/// <c>UnauthorizedAccessException</c> на каждом сворачивании группы, и глобальный
/// обработчик показывал «Ошибка интерфейса» на каждое действие.
/// <list type="bullet">
/// <item><see cref="PlatformPaths"/> однократно выбирает каталог данных с проверкой
/// записи и переходит на резервный (<c>%LOCALAPPDATA%</c> / <c>$XDG_DATA_HOME</c> / temp);</item>
/// <item><see cref="InfobaseRepository"/> при недоступности <c>File.Replace</c>/<c>File.Move</c>
/// пишет резервным способом (копирование, затем прямая запись);</item>
/// <item><c>SaveSettings</c>/<c>SaveSettingsAsync</c> не выбрасывают ошибку наружу и
/// сообщают о ней в журнал один раз, а <c>Save</c>/<c>SaveGroups</c> сохраняют прежнюю
/// семантику (ошибка видна).</item>
/// </list>
/// Тесты кроссплатформенные: используют только временные каталоги и не пишут
/// в реальный <c>%APPDATA%</c>.
/// </summary>
public sealed class Issue136DataDirectoryFallbackTests
{
    // ---------- PlatformPaths: выбор каталога ----------

    [Fact]
    public void ResolveDataDirectory_UsesWritablePreferredDirectory()
    {
        var preferred = CreateTempDirectory("preferred");
        var fallback = CreateTempDirectory("fallback");
        try
        {
            var resolution = PlatformPaths.ResolveDataDirectory(preferred, new[] { fallback });

            Assert.Equal(preferred, resolution.DirectoryPath);
            Assert.False(resolution.IsFallback);
            Assert.Null(resolution.Reason);
        }
        finally
        {
            DeleteDirectory(preferred);
            DeleteDirectory(fallback);
        }
    }

    [Fact]
    public void ResolveDataDirectory_CreatesMissingPreferredDirectory()
    {
        var root = CreateTempDirectory("nested");
        var preferred = Path.Combine(root, "a", "b", "data");
        try
        {
            var resolution = PlatformPaths.ResolveDataDirectory(
                preferred, new[] { Path.Combine(root, "fallback") });

            Assert.Equal(preferred, resolution.DirectoryPath);
            Assert.False(resolution.IsFallback);
            Assert.True(Directory.Exists(preferred));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveDataDirectory_FallsBack_WhenPreferredIsChildOfFile()
    {
        var root = CreateTempDirectory("blocked");
        var blockingFile = Path.Combine(root, "blocking.txt");
        File.WriteAllText(blockingFile, "not a directory");
        // Путь-«ребёнок» файла: создать каталог невозможно ни на Windows, ни на Linux.
        var preferred = Path.Combine(blockingFile, "data");
        var fallback = Path.Combine(root, "fallback-data");
        try
        {
            var resolution = PlatformPaths.ResolveDataDirectory(preferred, new[] { fallback });

            Assert.Equal(fallback, resolution.DirectoryPath);
            Assert.True(resolution.IsFallback);
            Assert.NotNull(resolution.Reason);
            Assert.Contains("недоступен", resolution.Reason);
            Assert.True(Directory.Exists(fallback));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveDataDirectory_UsesSecondFallback_WhenFirstIsUnavailable()
    {
        var root = CreateTempDirectory("second");
        var blockingFile = Path.Combine(root, "blocking.txt");
        File.WriteAllText(blockingFile, "not a directory");
        var preferred = Path.Combine(blockingFile, "data");
        var firstFallback = Path.Combine(blockingFile, "local");
        var secondFallback = Path.Combine(root, "temp-data");
        try
        {
            var resolution = PlatformPaths.ResolveDataDirectory(
                preferred, new[] { firstFallback, secondFallback });

            Assert.Equal(secondFallback, resolution.DirectoryPath);
            Assert.True(resolution.IsFallback);
            Assert.NotNull(resolution.Reason);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveDataDirectory_ReturnsPreferredWithoutThrowing_WhenNothingIsWritable()
    {
        var root = CreateTempDirectory("nothing");
        var blockingFile = Path.Combine(root, "blocking.txt");
        File.WriteAllText(blockingFile, "not a directory");
        var preferred = Path.Combine(blockingFile, "data");
        var fallback = Path.Combine(blockingFile, "fallback");
        try
        {
            var resolution = PlatformPaths.ResolveDataDirectory(preferred, new[] { fallback });

            // Все кандидаты недоступны: исключения нет, вызывающий код обработает
            // ошибку записи сам (SaveSettings — тихо, Save — с пробросом).
            Assert.Equal(preferred, resolution.DirectoryPath);
            Assert.True(resolution.IsFallback);
            Assert.NotNull(resolution.Reason);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void IsWritableDirectory_DetectsAccessibleAndBlockedPaths()
    {
        var root = CreateTempDirectory("probe");
        var blockingFile = Path.Combine(root, "blocking.txt");
        File.WriteAllText(blockingFile, "not a directory");
        try
        {
            Assert.True(PlatformPaths.IsWritableDirectory(root, out var noError));
            Assert.Null(noError);

            Assert.False(PlatformPaths.IsWritableDirectory(
                Path.Combine(blockingFile, "data"), out var error));
            Assert.False(string.IsNullOrWhiteSpace(error));

            Assert.False(PlatformPaths.IsWritableDirectory(null, out _));
            Assert.False(PlatformPaths.IsWritableDirectory("   ", out _));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void BuildFallbackDirectories_ContainsLocalDataAndTempCandidates()
    {
        var candidates = PlatformPaths.BuildFallbackDirectories();

        Assert.NotEmpty(candidates);
        Assert.All(candidates, candidate =>
            Assert.EndsWith(
                PlatformPaths.AppFolderName,
                candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));

        // Последний резерв — <temp>/Workspace1C (issue #136).
        var tempCandidate = Path.Combine(Path.GetTempPath(), PlatformPaths.AppFolderName);
        Assert.Contains(candidates, candidate =>
            string.Equals(
                Path.GetFullPath(candidate),
                Path.GetFullPath(tempCandidate),
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AppDataDirectory_UsesFallback_AndLogDirectoryFollowsIt()
    {
        var root = CreateTempDirectory("cache");
        var blockingFile = Path.Combine(root, "blocking.txt");
        File.WriteAllText(blockingFile, "not a directory");
        var preferred = Path.Combine(blockingFile, "data");
        var fallback = Path.Combine(root, "fallback");
        try
        {
            // Заполняем кэш явным результатом: реальный %APPDATA% не затрагивается.
            PlatformPaths.ResolveDataDirectoryForTests(preferred, new[] { fallback });

            Assert.Equal(fallback, PlatformPaths.AppDataDirectory);
            Assert.True(PlatformPaths.IsFallbackActive);
            Assert.NotNull(PlatformPaths.FallbackReason);
            Assert.Equal(Path.Combine(fallback, "logs"), PlatformPaths.LogDirectory);
        }
        finally
        {
            PlatformPaths.ResetDataDirectoryCacheForTests();
            DeleteDirectory(root);
        }
    }

    // ---------- InfobaseRepository: запись ----------

    [Fact]
    public void SaveSettings_DoesNotThrow_WhenDataDirectoryIsUnavailable()
    {
        var root = CreateTempDirectory("save-dir");
        var blockingFile = Path.Combine(root, "blocking.txt");
        File.WriteAllText(blockingFile, "not a directory");
        var repository = new InfobaseRepository(directory: Path.Combine(blockingFile, "data"));
        try
        {
            var settings = new AppSettings { Language = "ru" };

            // Повторные действия пользователя (сворачивание группы) не должны
            // выбрасывать исключение и показывать «Ошибка интерфейса» (issue #136).
            repository.SaveSettings(settings);
            repository.SaveSettings(settings);

            Assert.Equal(1, settings.SchemaVersion);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void SaveSettings_DoesNotThrow_WhenTargetFileIsDirectory()
    {
        var directory = CreateTempDirectory("save-target");
        Directory.CreateDirectory(Path.Combine(directory, "settings.json"));
        var repository = new InfobaseRepository(directory: directory);
        try
        {
            var exception = Record.Exception(() =>
                repository.SaveSettings(new AppSettings { Language = "en" }));

            Assert.Null(exception);
            // Временный файл убирается в finally, даже если все способы записи не удались.
            Assert.False(File.Exists(Path.Combine(directory, "settings.json.tmp")));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void SaveSettings_PersistsAndReloadsSettings()
    {
        var directory = CreateTempDirectory("persist");
        var repository = new InfobaseRepository(directory: directory);
        try
        {
            repository.SaveSettings(new AppSettings { Language = "ru", Theme = "dark" });

            var loaded = repository.LoadSettings();
            Assert.Equal("ru", loaded.Language);
            Assert.Equal("dark", loaded.Theme);
            Assert.Equal(1, loaded.SchemaVersion);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void SaveSettings_FallbackWriteOverwritesExistingContent()
    {
        var directory = CreateTempDirectory("fallback-write");
        var repository = new InfobaseRepository(directory: directory);
        try
        {
            repository.SaveSettings(new AppSettings { Language = "ru", Theme = "light" });

            // Имитируем недоступность File.Replace/File.Move: должна сработать
            // резервная запись (копирование временного файла поверх целевого).
            InfobaseRepository.ForceFallbackWriteForTests = true;
            repository.SaveSettings(new AppSettings { Language = "en", Theme = "dark" });

            var loaded = repository.LoadSettings();
            Assert.Equal("en", loaded.Language);
            Assert.Equal("dark", loaded.Theme);
            Assert.False(File.Exists(Path.Combine(directory, "settings.json.tmp")));
        }
        finally
        {
            InfobaseRepository.ForceFallbackWriteForTests = false;
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task SaveSettingsAsync_DoesNotThrow_WhenDataDirectoryIsUnavailable()
    {
        var root = CreateTempDirectory("saveasync-dir");
        var blockingFile = Path.Combine(root, "blocking.txt");
        File.WriteAllText(blockingFile, "not a directory");
        var repository = new InfobaseRepository(directory: Path.Combine(blockingFile, "data"));
        try
        {
            var exception = await Record.ExceptionAsync(() =>
                repository.SaveSettingsAsync(new AppSettings { Language = "ru" }));

            Assert.Null(exception);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SaveSettingsAsync_FallbackWritePersistsSettings()
    {
        var directory = CreateTempDirectory("saveasync-fallback");
        var repository = new InfobaseRepository(directory: directory);
        try
        {
            repository.SaveSettings(new AppSettings { Language = "ru" });

            InfobaseRepository.ForceFallbackWriteForTests = true;
            await repository.SaveSettingsAsync(new AppSettings { Language = "en", Theme = "dark" });

            var loaded = repository.LoadSettings();
            Assert.Equal("en", loaded.Language);
            Assert.Equal("dark", loaded.Theme);
            Assert.False(File.Exists(Path.Combine(directory, "settings.json.tmp")));
        }
        finally
        {
            InfobaseRepository.ForceFallbackWriteForTests = false;
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Save_StillThrows_WhenTargetFileIsDirectory()
    {
        var directory = CreateTempDirectory("save-strict");
        Directory.CreateDirectory(Path.Combine(directory, "infobases.json"));
        var repository = new InfobaseRepository(directory: directory);
        try
        {
            // Данные баз и групп — не вспомогательные: ошибка должна быть видна.
            Assert.ThrowsAny<Exception>(() => repository.Save(new List<Infobase>()));
            Assert.False(File.Exists(Path.Combine(directory, "infobases.json.tmp")));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    // ---------- Source-гварды ----------

    [Fact]
    public void PlatformPaths_Source_ContainsFallbackGuards()
    {
        var source = ReadProjectFile("Workspace1C", "Services", "PlatformPaths.cs");

        Assert.Contains("PreferredDataDirectory", source);
        Assert.Contains("IsFallbackActive", source);
        Assert.Contains("FallbackReason", source);
        Assert.Contains("LocalApplicationData", source);
        Assert.Contains("XDG_DATA_HOME", source);
        Assert.Contains("GetTempPath", source);
        Assert.Contains("IsWritableDirectory", source);
    }

    [Fact]
    public void Repository_Source_ContainsReserveWrite()
    {
        var source = ReadProjectFile("Workspace1C", "Services", "InfobaseRepository.cs");

        Assert.Contains("File.Copy(tempPath, targetPath, overwrite: true)", source);
        Assert.Contains("File.WriteAllText(targetPath, content)", source);
        Assert.Contains("LogSettingsSaveFailureOnce", source);
        Assert.Contains("TryDeleteTempFile", source);
    }

    // ---------- Вспомогательные методы ----------

    private static string CreateTempDirectory(string prefix)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "w1c_issue136_" + prefix + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Временный каталог — не критично, если не удалился сразу.
        }
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var relativePath = Path.Combine(parts);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var fullPath = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(fullPath))
                return File.ReadAllText(fullPath);
            directory = directory.Parent;
        }

        Assert.Fail($"Файл проекта не найден: {relativePath}");
        return string.Empty;
    }
}
