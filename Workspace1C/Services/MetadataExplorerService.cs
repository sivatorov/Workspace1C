using System.IO;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Оркестратор выгрузки конфигурации для «Обозревателя метаданных» (цикл 0.3.9.132–0.3.9.136):
/// переиспользует инфраструктуру сравнения конфигураций (0.3.9.99) — <c>CREATEINFOBASE</c>
/// с <c>/UseTemplate</c> для .cf и <c>DESIGNER /DumpConfigToFiles</c> через
/// <see cref="OneCLauncher.RunDesignerBatch"/> — но НЕ сравнивает и НЕ удаляет каталог выгрузки:
/// временная выгрузка живёт в <c>%TEMP%\cm_metaeplorer_<guid></c>, пока окно обозревателя
/// не вызовет <see cref="MetadataDump.Delete"/>. Частные копии паттернов
/// <c>ConfigurationDiffService.WaitForBatchAsync</c>/<c>TryDeleteDirectory</c>,
/// чтобы не менять семантику сервиса сравнения.
/// </summary>
public sealed class MetadataExplorerService : IMetadataExplorerService
{
    /// <summary>Логгер приложения (DI; в тестах — null или fake).</summary>
    private readonly IAppLogger? _logger;

    /// <summary>Таймаут ожидания одного запуска 1cv8 DESIGNER /DumpConfigToFiles (60 мин).</summary>
    private static readonly TimeSpan BatchTimeout = TimeSpan.FromMinutes(60);

    /// <summary>Таймаут создания временной ИБ из .cf (большие конфигурации, как в сравнении).</summary>
    private const int CreateInfoBaseTimeoutMs = 30 * 60 * 1000;

    /// <param name="logger">Логгер приложения для диагностики сбоев «Загрузить» (issue #365); null — без лога (тесты).</param>
    public MetadataExplorerService(IAppLogger? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<MetadataDump> DumpFromBaseAsync(
        Infobase infobase,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (infobase is null)
            throw new MetadataExplorerException(LocalizationManager.T("MetadataExplorer.Errors.NoBase"));

        return RunAsync(dumpRoot =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(LocalizationManager.T("MetadataExplorer.Status.DumpStage"));
            return DumpAndReadHeader(infobase, dumpRoot, progress, cancellationToken);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<MetadataDump> DumpFromCfAsync(
        string cfPath,
        string platformVersion,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(dumpRoot =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(cfPath) || !File.Exists(cfPath))
                throw new MetadataExplorerException(LocalizationManager.T("MetadataExplorer.Errors.CfNotExists"));
            if (string.IsNullOrWhiteSpace(platformVersion))
                throw new MetadataExplorerException(LocalizationManager.T("MetadataExplorer.Errors.NoPlatform"));

            // Распаковка .cf во временную файловую ИБ (CREATEINFOBASE с /UseTemplate),
            // затем выгрузка конфигурации этой ИБ /DumpConfigToFiles.
            var ibDir = Path.Combine(dumpRoot, "ib");
            var dumpDir = Path.Combine(dumpRoot, "dump");

            progress?.Report(LocalizationManager.T("MetadataExplorer.Status.CreateTempStage"));
            var created = OneCLauncher.CreateInfoBase(
                platformVersion,
                isFile: true,
                filePath: ibDir,
                server: null,
                databaseName: null,
                templatePath: cfPath,
                timeoutMs: CreateInfoBaseTimeoutMs);
            if (!created.Ok)
            {
                var message = created.Error ?? LocalizationManager.T("MetadataExplorer.Errors.LoadFailed");
                _logger?.Error(
                    $"Создание временной базы для обозревателя метаданных не удалось (cf='{cfPath}'): {message}",
                    new MetadataExplorerException(message));
                throw new MetadataExplorerException(message);
            }

            var tempBase = CreateTempBase(platformVersion, ibDir);
            return DumpAndReadHeader(tempBase, dumpDir, progress, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>
    /// Запускает <c>/DumpConfigToFiles</c> для базы, ждёт завершения по событию
    /// <see cref="OneCLauncher.DesignerBatchCompleted"/> и читает заголовок конфигурации.
    /// Каталог выгрузки остаётся на диске (владелец удалит через <see cref="MetadataDump.Delete"/>).
    /// </summary>
    private MetadataDump DumpAndReadHeader(
        Infobase infobase,
        string dumpDir,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Каталог создаёт сам RunDesignerBatch (ветка DumpConfigToFiles), но подстраховываемся.
        try { Directory.CreateDirectory(dumpDir); } catch { /* создаст RunDesignerBatch */ }

        progress?.Report(LocalizationManager.T("MetadataExplorer.Status.DumpStage"));
        var started = OneCLauncher.RunDesignerBatch(
            infobase,
            OneCLauncher.DesignerBatchOperation.DumpConfigToFiles,
            dumpDir,
            credential: null,
            repositoryVersion: null,
            repositoryReportBegin: null,
            repositoryReportEnd: null,
            permissionCode: null,
            out var startError);
        if (!started)
        {
            // Реальная причина отказа запуска (нет 1cv8.exe, блокировка, каталог/процесс) —
            // см. RunDesignerBatch(... out startError) — issue #365: раньше UI получал
            // generic-фразу «Не удалось загрузить данные обозревателя» без деталей.
            var message = startError ?? LocalizationManager.T("MetadataExplorer.Errors.LoadFailed");
            _logger?.Error(
                $"Не удалось запустить выгрузку конфигурации обозревателя метаданных (база='{infobase.Name}', этап=Dump): {message}",
                new MetadataExplorerException(message));
            throw new MetadataExplorerException(message);
        }

        var info = WaitForBatchAsync(
            OneCLauncher.DesignerBatchOperation.DumpConfigToFiles,
            dumpDir,
            BatchTimeout,
            cancellationToken).GetAwaiter().GetResult();

        if (info is null)
        {
            var message = LocalizationManager.T("MetadataExplorer.Errors.LoadTimeout");
            _logger?.Error(
                $"Превышено время ожидания выгрузки конфигурации обозревателя метаданных (база='{infobase.Name}', этап=Dump).",
                new MetadataExplorerException(message));
            throw new MetadataExplorerException(message);
        }
        if (!info.Success)
        {
            var message = info.ErrorMessage ?? LocalizationManager.T("MetadataExplorer.Errors.LoadFailed");
            _logger?.Error(
                $"Выгрузка конфигурации обозревателя метаданных завершилась ошибкой (база='{infobase.Name}', этап=Dump): {message}",
                new MetadataExplorerException(message));
            throw new MetadataExplorerException(message);
        }

        var (name, version) = MetadataXmlParser.ParseConfigurationHeader(dumpDir);
        return new MetadataDump(dumpDir, name, version);
    }

    /// <summary>Собирает Infobase, указывающую на временную файловую ИБ (как в сравнении).</summary>
    private static Infobase CreateTempBase(string platformVersion, string ibDir)
        => new()
        {
            Name = "cm_metaeplorer_temp",
            PlatformVersion = platformVersion,
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.File,
                FilePath = ibDir
            }
        };

    /// <summary>
    /// Выполняет операцию выгрузки в свежем каталоге <c>%TEMP%\cm_metaeplorer_<guid></c>;
    /// при исключении каталог удаляется (мусор не копим), при успехе — остаётся до
    /// <see cref="MetadataDump.Delete"/>.
    /// </summary>
    private static async Task<MetadataDump> RunAsync(
        Func<string, MetadataDump> operation,
        CancellationToken cancellationToken)
    {
        var dumpRoot = Path.Combine(Path.GetTempPath(), "cm_metaeplorer_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dumpRoot);
            return await Task.Run(() => operation(dumpRoot), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryDeleteDirectory(dumpRoot);
            throw;
        }
        catch
        {
            TryDeleteDirectory(dumpRoot);
            throw;
        }
    }

    /// <summary>
    /// Ожидает завершения конкретной пакетной операции DESIGNER через событие
    /// <see cref="OneCLauncher.DesignerBatchCompleted"/> с таймаутом и отменой
    /// (приватная копия паттерна <c>ConfigurationDiffService.WaitForBatchAsync</c>).
    /// </summary>
    private static async Task<OneCLauncher.DesignerBatchInfo?> WaitForBatchAsync(
        OneCLauncher.DesignerBatchOperation operation,
        string outputPath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        var tcs = new TaskCompletionSource<OneCLauncher.DesignerBatchInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<OneCLauncher.DesignerBatchInfo> handler = (_, info) =>
        {
            if (info.Operation == operation &&
                string.Equals(info.OutputPath, outputPath, StringComparison.OrdinalIgnoreCase))
            {
                tcs.TrySetResult(info);
            }
        };
        OneCLauncher.DesignerBatchCompleted += handler;
        try
        {
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.InfiniteTimeSpan, cts.Token)).ConfigureAwait(false);
            return completed == tcs.Task ? tcs.Task.Result : null;
        }
        finally
        {
            OneCLauncher.DesignerBatchCompleted -= handler;
        }
    }

    /// <summary>Рекурсивно удаляет каталог; ошибки игнорируются (лучше осиротевший каталог, чем падение).</summary>
    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Занят процессом/антивирусом — попытка при следующем запуске очистится сама.
        }
    }
}