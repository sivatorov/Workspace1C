using System;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #116 (комментарий 7OH от 17.11.2026 14:30): режим «Загружать с предложением
/// установки» должен догонять новые версии. Если доступна версия новее уже скачанной —
/// файл скачивается в фоне, а старый удаляется ТОЛЬКО после успешной загрузки.
/// <list type="bullet">
/// <item>чистые тесты <see cref="UpdateRedownloadPolicy"/>: перекачиваем только более
/// новую версию, не откатываемся на старую/ту же;</item>
/// <item>старый файл остаётся при неудаче и удаляется после успеха;</item>
/// <item>source-гварды: периодическая проверка не блокируется найденным релизом в
/// режиме DownloadAskInstall, параллельные загрузки не запускаются, значок
/// «готово к установке» относится к доступному релизу.</item>
/// </list>
/// </summary>
public sealed class Issue116UpdateRedownloadTests
{
    private const string WpfService = "Workspace1C/Services/UpdateService.cs";
    private const string AvaloniaService = "Workspace1C/Services/UpdateService.Avalonia.cs";

    private static ReleaseInfo Release(string tag) => new()
    {
        TagName = tag,
        Body = "notes",
        HtmlUrl = "https://example.org/r"
    };

    // ===================== Чистое правило перекачивания =====================

    [Theory]
    [InlineData("v1.0.1.90", "v1.0.1.89", true)]
    [InlineData("v1.0.1.89", "v1.0.1.89", false)]
    [InlineData("v1.0.1.88", "v1.0.1.89", false)]
    public void ShouldDownload_OnlyWhenCandidateIsNewer(
        string candidateTag, string readyTag, bool expected)
    {
        var candidate = Release(candidateTag);
        var ready = Release(readyTag);

        Assert.Equal(expected, UpdateRedownloadPolicy.ShouldDownload(candidate, ready, true));
    }

    [Fact]
    public void ShouldDownload_WhenNothingReadyOrFileMissing()
    {
        var candidate = Release("v1.0.1.90");

        Assert.True(UpdateRedownloadPolicy.ShouldDownload(candidate, null, false));
        // Файл готового релиза исчез (уборка temp) — ту же версию можно скачать заново.
        Assert.True(UpdateRedownloadPolicy.ShouldDownload(
            Release("v1.0.1.89"), Release("v1.0.1.89"), readyFileExists: false));
        Assert.False(UpdateRedownloadPolicy.ShouldDownload(null, Release("v1.0.1.89"), true));
    }

    [Fact]
    public void ShouldCleanup_OnlyForSuccessfulDifferentFile()
    {
        Assert.False(UpdateRedownloadPolicy.ShouldCleanup(null, "new"));
        Assert.False(UpdateRedownloadPolicy.ShouldCleanup("old", null));
        Assert.False(UpdateRedownloadPolicy.ShouldCleanup("old", string.Empty));
        Assert.False(UpdateRedownloadPolicy.ShouldCleanup("same", "same"));
        Assert.True(UpdateRedownloadPolicy.ShouldCleanup("old", "new"));
    }

    [Fact]
    public void StaleFile_IsKeptOnFailure_AndRemovedAfterSuccess()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cm-issue116-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var oldFile = Path.Combine(dir, "Workspace1C.1.0.1.89.new.exe");
            var newFile = Path.Combine(dir, "Workspace1C.1.0.1.90.new.exe");
            File.WriteAllText(oldFile, "old");
            File.WriteAllText(newFile, "new");

            // Неудача загрузки: нового пути нет — старый файл не трогаем.
            var failedPath = (string?)null;
            if (UpdateRedownloadPolicy.ShouldCleanup(oldFile, failedPath))
                UpdateDownloadCleanup.RemoveStaleDownload(oldFile, failedPath!);
            Assert.True(File.Exists(oldFile), "При неудаче старый файл должен остаться.");

            // Успех: новый файл скачан — только теперь удаляем старый.
            if (UpdateRedownloadPolicy.ShouldCleanup(oldFile, newFile))
                UpdateDownloadCleanup.RemoveStaleDownload(oldFile, newFile);
            Assert.False(File.Exists(oldFile), "После успеха старый файл должен быть удалён.");
            Assert.True(File.Exists(newFile), "Новый файл должен остаться.");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp */ }
        }
    }

    // ===================== Периодическая проверка =====================

    [Theory]
    [InlineData(WpfService)]
    [InlineData(AvaloniaService)]
    public void PeriodicCheck_ContinuesInDownloadAskInstall(string file)
    {
        var source = ReadProjectFile(file);
        var periodic = ExtractMethod(source, "private async void RunPeriodicCheck()");

        // В режиме «Загружать с предложением установки» найденный/скачанный релиз
        // больше не блокирует следующие проверки — иначе новые версии не замечаются.
        Assert.Contains("Mode == AppUpdateMode.Manual", periodic, StringComparison.Ordinal);
        Assert.Contains(
            "Mode != AppUpdateMode.DownloadAskInstall", periodic, StringComparison.Ordinal);
        Assert.Contains("CheckForUpdatesAsync()", periodic, StringComparison.Ordinal);
    }

    // ===================== Перекачивание без параллельных загрузок =====================

    [Theory]
    [InlineData(WpfService)]
    [InlineData(AvaloniaService)]
    public void BackgroundDownload_UsesNewerOnlyPolicy(string file)
    {
        var source = ReadProjectFile(file);
        var start = ExtractMethod(source, "public void StartBackgroundDownload(ReleaseInfo release)");

        Assert.Contains(
            "UpdateRedownloadPolicy.ShouldDownload(release, ReadyRelease, IsReadyFor(ReadyRelease))",
            start, StringComparison.Ordinal);
        // Одна загрузка за раз: при занятом слоте запоминаем самый новый релиз.
        Assert.Contains("_backgroundDownloadBusy", start, StringComparison.Ordinal);
        Assert.Contains("_pendingBackgroundDownload", start, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(WpfService)]
    [InlineData(AvaloniaService)]
    public void BackgroundDownload_CleansOldFileOnlyAfterSuccess(string file)
    {
        var source = ReadProjectFile(file);

        // Проверка «путь не получен — выходим» стоит ДО удаления старого файла.
        var nullCheck = source.IndexOf("if (path is null)", StringComparison.Ordinal);
        var cleanup = source.IndexOf(
            "UpdateDownloadCleanup.RemoveStaleDownload(previousReadyPath, path);",
            StringComparison.Ordinal);
        Assert.True(nullCheck >= 0, "Не найден guard неудачной загрузки (path is null).");
        Assert.True(cleanup > nullCheck, "Удаление старого файла должно идти после успешной загрузки.");

        // Удаление выполняется только по правилу «есть новый файл, путь отличается».
        Assert.Contains(
            "UpdateRedownloadPolicy.ShouldCleanup(previousReadyPath, path)",
            source, StringComparison.Ordinal);
        Assert.Contains("SetReadyState(release, path);", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(WpfService)]
    [InlineData(AvaloniaService)]
    public void BackgroundDownload_StartsPendingNewerReleaseAfterCurrent(string file)
    {
        var source = ReadProjectFile(file);

        Assert.Contains("ref _pendingBackgroundDownload, null", source, StringComparison.Ordinal);
        Assert.Contains("StartBackgroundDownload(pending);", source, StringComparison.Ordinal);
    }

    // ===================== Значок «готово к установке» =====================

    [Fact]
    public void Footer_ReadyState_RefersToAvailableRelease()
    {
        // Пока в фоне качается более новая версия, старый скачанный файл не должен
        // выглядеть как «готов к установке»: зелёная галочка — только для доступного релиза.
        var wpf = ReadProjectFile("Workspace1C/Views/MainWindow.Updates.cs");
        Assert.Contains(
            "var isReady = updateService.IsReadyFor(updateService.AvailableRelease);",
            wpf, StringComparison.Ordinal);

        var avalonia = ReadProjectFile("Workspace1C/Views/MainWindow.Avalonia.cs");
        Assert.Contains(
            "var isReady = updateService.IsReadyFor(updateService.AvailableRelease);",
            avalonia, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(WpfService)]
    [InlineData(AvaloniaService)]
    public void SkippedVersion_IsStillFilteredBeforeDownload(string file)
    {
        // Пропущенные версии не трогаем: проверка IsSkipped остаётся до ветвления
        // по режимам, то есть до StartBackgroundDownload.
        var source = ReadProjectFile(file);
        var skipped = source.IndexOf("if (IsSkipped(release))", StringComparison.Ordinal);
        var branch = source.IndexOf("switch (Mode)", StringComparison.Ordinal);

        Assert.True(skipped >= 0, "Не найдена проверка пропущенной версии.");
        Assert.True(branch > skipped, "Проверка IsSkipped должна идти до выбора режима.");
    }

    // ===================== Вспомогательное =====================

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CHANGELOG.md")))
                return dir.FullName;
            dir = dir.Parent;
        }

        Assert.Fail("Корень репозитория не найден (CHANGELOG.md)");
        return string.Empty;
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(
            FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    /// <summary>Вырезает тело метода от сигнатуры до парной закрывающей скобки.</summary>
    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Метод не найден: {signature}");
        var open = source.IndexOf('{', start);
        Assert.True(open > start, $"Тело метода не найдено: {signature}");

        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[start..(i + 1)];
            }
        }

        Assert.Fail($"Конец метода не найден: {signature}");
        return string.Empty;
    }
}
