using System.Text.Json;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #89 «Скачивание EXE на Линукс»: валидация скачанного файла обновления по
/// магическим байтам платформы (<see cref="UpdatePayload.ValidatePlatformMagic(ReadOnlySpan{byte}, bool)"/>,
/// файловые обёртки и <see cref="UpdatePayload.RejectWrongPlatformFile"/>) — Linux-сборка
/// обязана быть ELF (7F 45 4C 46), Windows — PE («MZ»). Неподходящий бинарник
/// не применяется и переименовывается в *.wrong-platform. Диагностика выбора ассета:
/// реальный список ассетов релиза v1.0.1.5 по платформам (GitHub API) фиксирует
/// поведение <see cref="GitHubReleaseService.FindAsset"/> (задокументировано планом 89.2).
/// </summary>
public sealed class Issue89PlatformValidationTests : IDisposable
{
    private readonly string _dir;

    public Issue89PlatformValidationTests()
    {
        _dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cm-issue89-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_dir, recursive: true); } catch { /* не критично */ }
    }

    private string WriteFile(string name, byte[] bytes)
    {
        var path = System.IO.Path.Combine(_dir, name);
        System.IO.File.WriteAllBytes(path, bytes);
        return path;
    }

    private static readonly byte[] ElfBytes = [0x7F, (byte)'E', (byte)'L', (byte)'F', 0x02, 0x01, 0x01, 0x00];
    private static readonly byte[] MzBytes = [0x4D, 0x5A, 0x90, 0x00, 1, 2, 3, 4];

    // ---- Чистая проверка первых байт (span) ----

    [Fact]
    public void ValidateSpan_ElfBytes_IsValidOnLinux()
    {
        Assert.Equal(UpdatePayload.UpdatePlatformValidation.Valid,
            UpdatePayload.ValidatePlatformMagic(ElfBytes, linux: true));
    }

    [Fact]
    public void ValidateSpan_ElfBytes_IsWrongPlatformOnWindows()
    {
        Assert.Equal(UpdatePayload.UpdatePlatformValidation.WrongPlatform,
            UpdatePayload.ValidatePlatformMagic(ElfBytes, linux: false));
    }

    [Fact]
    public void ValidateSpan_MzBytes_IsValidOnWindows()
    {
        Assert.Equal(UpdatePayload.UpdatePlatformValidation.Valid,
            UpdatePayload.ValidatePlatformMagic(MzBytes, linux: false));
    }

    [Fact]
    public void ValidateSpan_MzBytes_IsWrongPlatformOnLinux()
    {
        // Главный сценарий 7OH: EXE на Linux — «Обновление не может завершиться».
        Assert.Equal(UpdatePayload.UpdatePlatformValidation.WrongPlatform,
            UpdatePayload.ValidatePlatformMagic(MzBytes, linux: true));
    }

    [Fact]
    public void ValidateSpan_EmptyOrShort_IsUnknown()
    {
        Assert.Equal(UpdatePayload.UpdatePlatformValidation.Unknown,
            UpdatePayload.ValidatePlatformMagic(ReadOnlySpan<byte>.Empty, linux: true));
        Assert.Equal(UpdatePayload.UpdatePlatformValidation.Unknown,
            UpdatePayload.ValidatePlatformMagic([0x7F, (byte)'E'], linux: true));
        Assert.Equal(UpdatePayload.UpdatePlatformValidation.Unknown,
            UpdatePayload.ValidatePlatformMagic([0x4D], linux: false));
    }

    [Fact]
    public void ValidateSpan_Html_IsUnknown()
    {
        var html = System.Text.Encoding.UTF8.GetBytes("<!doctype html>");
        Assert.Equal(UpdatePayload.UpdatePlatformValidation.Unknown,
            UpdatePayload.ValidatePlatformMagic(html, linux: true));
    }

    // ---- Файловая обёртка (с проверкой размера) ----

    [Fact]
    public void ValidateFile_ElfFile_IsValidOnLinux()
    {
        var path = WriteFile("Workspace1C-linux-x64", ElfBytes);
        Assert.Equal(UpdatePayload.UpdatePlatformValidation.Valid,
            UpdatePayload.ValidatePlatformMagic(path, linux: true));
    }

    [Fact]
    public void ValidateFile_SizeMismatch_IsUnknown()
    {
        var path = WriteFile("Workspace1C-linux-x64", ElfBytes);
        // Заявленный размер ассета отличается от фактического — файл не принимается.
        Assert.Equal(UpdatePayload.UpdatePlatformValidation.Unknown,
            UpdatePayload.ValidatePlatformMagic(path, linux: true, expectedSize: 53_277_027));
    }

    [Fact]
    public void ValidateFile_SizeMatch_IsValid()
    {
        var path = WriteFile("Workspace1C-linux-x64", ElfBytes);
        Assert.Equal(UpdatePayload.UpdatePlatformValidation.Valid,
            UpdatePayload.ValidatePlatformMagic(path, linux: true, expectedSize: ElfBytes.Length));
    }

    [Fact]
    public void ValidateFile_MissingFile_IsUnknown()
    {
        var path = System.IO.Path.Combine(_dir, "missing.bin");
        Assert.Equal(UpdatePayload.UpdatePlatformValidation.Unknown,
            UpdatePayload.ValidatePlatformMagic(path, linux: true));
    }

    // ---- Карантин неподходящего файла ----

    [Fact]
    public void RejectWrongPlatform_ExeOnLinux_QuarantinesAndReturnsHint()
    {
        var path = WriteFile("Workspace1C.v1.0.1.6.new", MzBytes);

        var message = UpdatePayload.RejectWrongPlatformFile(path, linux: true, htmlUrl: "https://github.com/…/v1.0.1.6", logDir: _dir);

        Assert.NotNull(message);
        Assert.Contains("другой операционной системы", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("вручную", message, StringComparison.OrdinalIgnoreCase);
        // Ссылка на страницу выпуска подставлена в подсказку (план 89.1).
        Assert.Contains("v1.0.1.6", message, StringComparison.Ordinal);
        // Файл переименован в *.wrong-platform (карантин для диагностики).
        Assert.False(System.IO.File.Exists(path));
        Assert.True(System.IO.File.Exists(path + ".wrong-platform"));
        // Решение зафиксировано в журнале.
        var log = System.IO.Path.Combine(_dir, "update-platform-validation.log");
        Assert.True(System.IO.File.Exists(log));
        Assert.Contains("4D 5A", System.IO.File.ReadAllText(log), StringComparison.Ordinal);
    }

    [Fact]
    public void RejectWrongPlatform_ValidElfOnLinux_ReturnsNull()
    {
        var path = WriteFile("Workspace1C.v1.0.1.6.new", ElfBytes);

        var message = UpdatePayload.RejectWrongPlatformFile(path, linux: true, htmlUrl: null, logDir: _dir);

        Assert.Null(message);
        // Валидный файл НЕ переименовывается и НЕ удаляется.
        Assert.True(System.IO.File.Exists(path));
        Assert.False(System.IO.File.Exists(path + ".wrong-platform"));
    }

    // ---- Реальные ассеты релиза v1.0.1.5 (диагностика выбора ассета, план 89.2) ----

    private static JsonElement ParseAssets(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string RealV1015AssetsJson()
    {
        var linux = "{\"name\":\"Workspace1C-linux-x64\",\"size\":53277027," +
                    "\"browser_download_url\":\"https://github.com/sivatorov/Workspace1C/releases/download/v1.0.1.5/Workspace1C-linux-x64\"}";
        var windows = "{\"name\":\"Workspace1C.exe\",\"size\":84934435," +
                      "\"browser_download_url\":\"https://github.com/sivatorov/Workspace1C/releases/download/v1.0.1.5/Workspace1C.exe\"}";
        return $"{{\"assets\":[{linux},{windows}]}}";
    }

    [Fact]
    public void FindAsset_RealV1015Release_SelectsExactPlatformAsset()
    {
        var root = ParseAssets(RealV1015AssetsJson());
        var (url, size) = GitHubReleaseService.FindAsset(root);

#if !LINUX
        Assert.Contains("/Workspace1C.exe", url, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(84_934_435, size);
#else
        // Linux-сборка выбирает bare-бинарь, а не EXE (issue #89).
        Assert.Contains("/Workspace1C-linux-x64", url, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(53_277_027, size);
#endif
    }

    [Fact]
    public void FindAsset_LegacyAssetNameWithoutSuffix_KeepsIsPlatformAssetBehavior()
    {
        // Обратный кейс (план 89.2, пункт 3): Linux-ассет назван просто «Workspace1C»
        // (старое имя) — IsPlatformAsset сохраняет прежнее поведение.
#if !LINUX
        var root = ParseAssets("{\"assets\":[{\"name\":\"Workspace1C\",\"size\":10," +
            "\"browser_download_url\":\"https://example/Workspace1C\"}]}");
        var (url, size) = GitHubReleaseService.FindAsset(root);
        // Windows не берёт ассет без признаков win-x64/exe.
        Assert.Equal(string.Empty, url);
        Assert.Equal(0, size);
#else
        var root = ParseAssets("{\"assets\":[{\"name\":\"Workspace1C\",\"size\":10," +
            "\"browser_download_url\":\"https://example/Workspace1C\"}]}");
        var (url, size) = GitHubReleaseService.FindAsset(root);
        // Linux: точное имя AssetName («Workspace1C-linux-x64») не совпало, но проход 2
        // по IsPlatformAsset — прежнее поведение (не zip, не exe) допускает ассет.
        Assert.False(string.IsNullOrEmpty(url));
        Assert.Equal(10, size);
#endif
    }

    // ---- Карантин *.download-error при неизвестной ошибке загрузки (issue #89, 1.0.1.22) ----

    [Fact]
    public void QuarantineDownloadError_RenamesToDownloadError()
    {
        // Репорт 7OH (2026-11-02): после «Call from invalid thread» файлы исчезали —
        // catch удалял скачанный файл. Теперь он остаётся в карантине для проверки.
        var path = WriteFile("Workspace1C.v1.0.1.22.new", ElfBytes);

        var quarantined = UpdatePayload.QuarantineDownloadError(path);

        Assert.Equal(path + ".download-error", quarantined, StringComparer.OrdinalIgnoreCase);
        Assert.False(System.IO.File.Exists(path));
        Assert.True(System.IO.File.Exists(quarantined));
        Assert.Equal(ElfBytes, System.IO.File.ReadAllBytes(quarantined));
    }

    [Fact]
    public void QuarantineDownloadError_StaleQuarantine_ReplacedWithFreshFile()
    {
        // Повторная загрузка того же файла не накапливает мусор: прежний карантин
        // затирается свежим скачанным файлом.
        var path = WriteFile("Workspace1C.v1.0.1.22.new", ElfBytes);
        System.IO.File.WriteAllText(path + ".download-error", "stale");

        var quarantined = UpdatePayload.QuarantineDownloadError(path);

        Assert.True(System.IO.File.Exists(quarantined));
        Assert.Equal(ElfBytes, System.IO.File.ReadAllBytes(quarantined));
    }

    [Fact]
    public void QuarantineDownloadError_MissingFile_ReturnsPathWithoutThrow()
    {
        var path = System.IO.Path.Combine(_dir, "missing.new");

        Assert.Equal(path, UpdatePayload.QuarantineDownloadError(path));
    }

    // ---- Маршалинг индикатора загрузок (issue #89: «Call from invalid thread» на Linux) ----

    /// <summary>Контекст синхронизации-двойник: ставит коллбэки в очередь вместо выполнения.</summary>
    private sealed class ManualSyncContext : SynchronizationContext
    {
        public List<Action> Posted { get; } = new();
        public override void Post(SendOrPostCallback d, object? state) => Posted.Add(() => d(state));
    }

    [Fact]
    public void DispatchDownloadsUpdate_NoContext_RunsInline()
    {
        // Тесты/окружение без UI — прямой вызов (как раньше).
        var ran = false;
        MainViewModel.DispatchDownloadsUpdate(null, () => ran = true);
        Assert.True(ran);
    }

    [Fact]
    public void DispatchDownloadsUpdate_SameContext_RunsInline()
    {
        var ctx = new ManualSyncContext();
        SynchronizationContext.SetSynchronizationContext(ctx);
        try
        {
            var ran = false;
            MainViewModel.DispatchDownloadsUpdate(ctx, () => ran = true);
            Assert.True(ran);
            Assert.Empty(ctx.Posted);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(null);
        }
    }

    [Fact]
    public void DispatchDownloadsUpdate_ForeignContext_PostsToUiInsteadOfInline()
    {
        // Главный сценарий #89: событие приходит из потока загрузки — обновление
        // свойств VM НЕ выполняется инлайн (на Linux это «Call from invalid thread»),
        // а ставится в очередь захваченного UI-контекста.
        var ui = new ManualSyncContext();
        var ran = false;

        MainViewModel.DispatchDownloadsUpdate(ui, () => ran = true);

        Assert.False(ran);
        var posted = Assert.Single(ui.Posted);
        posted();
        Assert.True(ran);
    }
}
