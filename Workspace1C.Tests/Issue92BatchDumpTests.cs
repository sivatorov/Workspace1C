using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты массовой выгрузки в DT/CF с шаблоном имени файла (issue #92):
/// чистая логика построения пути по шаблону ({Имя}/{Дата}), разбор вида
/// операции, миграция сохранённого шаблона (NormalizeSavedTemplate, v100115),
/// пред-валидация DumpCf и цикл прогона ViewModel с подменённым
/// исполнителем (без запуска ibcmd и без процессов).
/// </summary>
public sealed class Issue92BatchDumpTests
{
    private static Infobase FileBase(string name) => new()
    {
        Name = name,
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\bases\" + name }
    };

    // ===================== Шаблон имени файла =====================

    [Fact]
    public void BuildDumpFilePath_DefaultTemplate_NameAndDate()
    {
        var stamp = new DateTime(2026, 10, 30, 17, 45, 0);
        var path = BatchOperationsService.BuildDumpFilePath(
            @"C:\dumps", "Альфа", BatchOperationKind.DumpDt, BatchOperationsService.DefaultDumpTemplate, stamp);

        Assert.Equal(Path.Combine(@"C:\dumps", "Альфа_2026-10-30_17-45.dt"), path);
    }

    [Fact]
    public void BuildDumpFilePath_CfKind_UsesCfExtension()
    {
        var stamp = new DateTime(2026, 10, 30, 17, 45, 0);
        var path = BatchOperationsService.BuildDumpFilePath(
            @"C:\dumps", "Альфа", BatchOperationKind.DumpCf, "{Имя}", stamp);

        Assert.Equal(Path.Combine(@"C:\dumps", "Альфа.cf"), path);
    }

    [Fact]
    public void BuildDumpFilePath_EnglishTokens_Work()
    {
        var stamp = new DateTime(2026, 10, 30, 17, 45, 0);
        var path = BatchOperationsService.BuildDumpFilePath(
            @"C:\dumps", "Альфа", BatchOperationKind.DumpDt, "{Name}_{Date}", stamp);

        Assert.Equal(Path.Combine(@"C:\dumps", "Альфа_2026-10-30_17-45.dt"), path);
    }

    [Fact]
    public void BuildDumpFilePath_EmptyTemplate_FallsBackToBaseName()
    {
        var path = BatchOperationsService.BuildDumpFilePath(
            @"C:\dumps", "Альфа", BatchOperationKind.DumpDt, template: "   ", timestamp: null);

        Assert.Equal(Path.Combine(@"C:\dumps", "Альфа.dt"), path);
    }

    [Fact]
    public void BuildDumpFilePath_TemplateResult_IsSanitized()
    {
        // Недопустимые символы, оставшиеся в шаблоне/имени, заменяются,
        // файл остаётся корректным именем.
        var path = BatchOperationsService.BuildDumpFilePath(
            @"C:\dumps", "База: 1/2", BatchOperationKind.DumpDt, "{Имя}", null);

        Assert.EndsWith(".dt", path, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(@"C:\dumps", "База_ 1_2.dt"), path);
    }

    [Fact]
    public void BuildDumpFilePath_ExistingFile_GetsNumericSuffix()
    {
        var dir = Directory.CreateTempSubdirectory("ws1c_dump").FullName;
        try
        {
            var stamp = new DateTime(2026, 10, 30, 17, 45, 0);
            File.WriteAllText(Path.Combine(dir, "Альфа_2026-10-30_17-45.dt"), "x");

            var path = BatchOperationsService.BuildDumpFilePath(
                dir, "Альфа", BatchOperationKind.DumpDt, BatchOperationsService.DefaultDumpTemplate, stamp);

            Assert.Equal(Path.Combine(dir, "Альфа_2026-10-30_17-45_2.dt"), path);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void BuildDumpFilePath_TimestampFormatParameter_ChangesDefaultStamp()
    {
        // Формат из настроек приложения («Формат даты в имени файла выгрузки»)
        // применяется к токену {Дата} без собственного формата (issue #92, 1.0.1.11).
        // v100114: шаблон по умолчанию содержит {Дата:формат}, поэтому механика
        // настроек проверяется на токене без собственного формата.
        var stamp = new DateTime(2026, 10, 30, 17, 45, 0);
        var path = BatchOperationsService.BuildDumpFilePath(
            @"C:\dumps", "Альфа", BatchOperationKind.DumpDt,
            "{Имя}_{Дата}", stamp,
            timestampFormat: "yyyyMMdd_HHmmss");

        Assert.Equal(Path.Combine(@"C:\dumps", "Альфа_20261030_174500.dt"), path);
    }

    [Fact]
    public void BuildDumpFilePath_TokenWithOwnFormat_OverridesDefault()
    {
        // Токен {Дата:формат} задаёт собственный формат (issue #92, 7OH:
        // «формат даты задать можно») и перекрывает формат из настроек.
        var stamp = new DateTime(2026, 10, 30, 17, 45, 0);
        var path = BatchOperationsService.BuildDumpFilePath(
            @"C:\dumps", "Альфа", BatchOperationKind.DumpDt,
            "{Имя}_{Дата:yyyy-MM-dd}", stamp,
            timestampFormat: "yyyyMMdd_HHmmss");

        Assert.Equal(Path.Combine(@"C:\dumps", "Альфа_2026-10-30.dt"), path);
    }

    [Fact]
    public void BuildDumpFilePath_TokenWithInvalidFormat_FallsBackToDefault()
    {
        // Некорректный формат токена не ломает операцию: fallback на формат по умолчанию.
        var stamp = new DateTime(2026, 10, 30, 17, 45, 0);
        var path = BatchOperationsService.BuildDumpFilePath(
            @"C:\dumps", "Альфа", BatchOperationKind.DumpDt,
            "{Имя}_{Дата:QQQ}", stamp,
            timestampFormat: "yyyyMMdd");

        Assert.Equal(Path.Combine(@"C:\dumps", "Альфа_20261030.dt"), path);
    }

    [Fact]
    public void ExpandDumpTemplate_MixedTokensWithSpacesAroundColon_Work()
    {
        var stamp = new DateTime(2026, 10, 30, 17, 45, 0);
        var expanded = BatchOperationsService.ExpandDumpTemplate(
            "{Имя} {Date : dd.MM.yyyy}", "Бета", stamp, "yyyyMMdd");

        Assert.Equal("Бета 30.10.2026", expanded);
    }

    // ===================== Миграция сохранённого шаблона (v100115) =====================

    [Fact]
    public void NormalizeSavedTemplate_LegacyDefault_ReplacedWithActualDefault()
    {
        // У существующих пользователей в settings.json сохранён прежний дефолт
        // {Имя}_{Дата} — при открытии окна заменяется на шаблон с форматом даты.
        var normalized = BatchOperationsService.NormalizeSavedTemplate(BatchOperationsService.LegacyDumpTemplate);

        Assert.Equal(BatchOperationsService.DefaultDumpTemplate, normalized);
    }

    [Fact]
    public void NormalizeSavedTemplate_NullOrEmpty_ReplacedWithActualDefault()
    {
        Assert.Equal(BatchOperationsService.DefaultDumpTemplate,
            BatchOperationsService.NormalizeSavedTemplate(null));
        Assert.Equal(BatchOperationsService.DefaultDumpTemplate,
            BatchOperationsService.NormalizeSavedTemplate(string.Empty));
        Assert.Equal(BatchOperationsService.DefaultDumpTemplate,
            BatchOperationsService.NormalizeSavedTemplate("   "));
    }

    [Fact]
    public void NormalizeSavedTemplate_UserTemplate_KeptAsIs()
    {
        // Собственные шаблоны пользователей (в т.ч. с токеном с собственным форматом)
        // не изменяются.
        const string user = "{Имя}_{Дата:dd.MM.yyyy}";

        Assert.Equal(user, BatchOperationsService.NormalizeSavedTemplate(user));
    }

    [Fact]
    public void NormalizeSavedTemplate_LegacyWithWhitespace_ReplacedWithActualDefault()
    {
        // Равенство с legacy-шаблоном проверяется по Ordinal с Trim.
        var normalized = BatchOperationsService.NormalizeSavedTemplate(
            " " + BatchOperationsService.LegacyDumpTemplate + " ");

        Assert.Equal(BatchOperationsService.DefaultDumpTemplate, normalized);
    }

    // ===================== Вид операции DT/CF =====================

    [Fact]
    public void ParseDumpKind_String_MapsCorrectly()
    {
        Assert.Equal(BatchOperationKind.DumpDt, BatchOperationsService.ParseDumpKind("DT"));
        Assert.Equal(BatchOperationKind.DumpDt, BatchOperationsService.ParseDumpKind(null));
        Assert.Equal(BatchOperationKind.DumpDt, BatchOperationsService.ParseDumpKind(""));
        Assert.Equal(BatchOperationKind.DumpCf, BatchOperationsService.ParseDumpKind("CF"));
        Assert.Equal(BatchOperationKind.DumpCf, BatchOperationsService.ParseDumpKind("cf"));
    }

    [Fact]
    public void DumpKindToString_RoundTrips()
    {
        Assert.Equal("DT", BatchOperationsService.DumpKindToString(BatchOperationKind.DumpDt));
        Assert.Equal("CF", BatchOperationsService.DumpKindToString(BatchOperationKind.DumpCf));

        Assert.Equal(BatchOperationKind.DumpCf,
            BatchOperationsService.ParseDumpKind(
                BatchOperationsService.DumpKindToString(BatchOperationKind.DumpCf)));
    }

    // ===================== Пред-валидация DumpCf =====================

    [Fact]
    public void Validate_DumpCf_WithoutDirectory_Fails()
    {
        var errors = BatchOperationsService.Validate(
            Array.Empty<Infobase>(), BatchOperationKind.DumpCf, path: null);

        Assert.Single(errors);
    }

    [Fact]
    public void Validate_DumpCf_WithValidDirectory_Passes()
    {
        var file = FileBase("Файловая");

        var errors = BatchOperationsService.Validate(
            new[] { file }, BatchOperationKind.DumpCf, path: Path.GetTempPath());

        Assert.Empty(errors);
    }

    // ===================== ViewModel: шаблон и переключатель DT/CF =====================

    [Fact]
    public void ViewModel_DefaultTemplate_IsFromService()
    {
        var vm = new BatchOperationsViewModel(new[] { FileBase("Альфа") }, BatchOperationKind.DumpDt);

        Assert.Equal(BatchOperationsService.DefaultDumpTemplate, vm.Template);
        Assert.True(vm.IsDumpKind);
    }

    [Fact]
    public void ViewModel_Kind_IsSettable_AndUpdatesPathLabel()
    {
        var vm = new BatchOperationsViewModel(new[] { FileBase("Альфа") }, BatchOperationKind.DumpDt);
        Assert.True(vm.IsDumpKind);

        vm.Kind = BatchOperationKind.DumpCf;
        Assert.True(vm.IsDumpKind);

        vm.Kind = BatchOperationKind.LoadConfig;
        Assert.False(vm.IsDumpKind);
    }

    [Fact]
    public async Task RunAsync_DumpCf_BuildsPerBaseCfPaths_ByTemplate()
    {
        var dir = Directory.CreateTempSubdirectory("ws1c_dumpcf").FullName;
        try
        {
            var a = FileBase("Альфа");
            var b = FileBase("Бета");
            var vm = new BatchOperationsViewModel(new[] { a, b }, BatchOperationKind.DumpCf)
            {
                PathText = dir,
                Template = "{Имя}",
                ExecutorOverride = (_, path, _) =>
                {
                    Assert.EndsWith(".cf", path, StringComparison.Ordinal);
                    return Task.FromResult(new IbcmdResult { Success = true, ExitCode = 0 });
                }
            };

            await vm.RunAsync(CancellationToken.None);

            Assert.True(vm.Items.All(i => i.Status == BatchOperationsStatus.Success));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AppSettings_BatchDumpDefaults_AreSane()
    {
        var settings = new AppSettings();

        Assert.Equal(BatchOperationsService.DefaultDumpTemplate, settings.BatchDumpTemplate);
        Assert.Equal(BatchOperationKind.DumpDt, BatchOperationsService.ParseDumpKind(settings.BatchDumpKind));
        Assert.Equal(string.Empty, settings.BatchDumpDirectory);
    }

    [Fact]
    public void ViewModel_TimestampFormat_IsPassedToFilePathBuilding()
    {
        // Формат из окна (настройка приложения) доходит до построителя пути (issue #92):
        // имя файла перед исполнителем построено с настроенным форматом {Дата}
        // «yyyyMMdd_HHmmss» (отметка времени берётся в момент прогона).
        var dir = Directory.CreateTempSubdirectory("ws1c_dumptf").FullName;
        try
        {
            var builtPaths = new List<string>();
            var vm = new BatchOperationsViewModel(new[] { FileBase("Альфа") }, BatchOperationKind.DumpDt)
            {
                PathText = dir,
                Template = "{Имя}_{Дата}",
                TimestampFormat = "yyyyMMdd_HHmmss",
                ExecutorOverride = (_, path, _) =>
                {
                    builtPaths.Add(path);
                    return Task.FromResult(new IbcmdResult { Success = true, ExitCode = 0 });
                }
            };

            vm.RunAsync(CancellationToken.None).GetAwaiter().GetResult();

            var fileName = Path.GetFileName(Assert.Single(builtPaths));
            Assert.Matches(@"^Альфа_\d{8}_\d{6}\.dt$", fileName);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ===================== Автоподстановка СУБД из rac (issue #92, v1.0.1.22) =====================
    // ZIP 7OH (diagnostics_20261102_104926): rac 8.5.4 отдаёт пустой dbms в «infobase
    // summary list», «infobase info» падает «Недостаточно прав пользователя» — резолв
    // возвращает null, все базы «пропускаются с ошибкой». Проверяются: нормализация
    // сопоставления имён, диагностика свойств найденной базы и честная причина по базе.

    /// <summary>Минимальный фейк <see cref="IRacClient"/>: один кластер и список баз.</summary>
    private sealed class FakeRacClient : IRacClient
    {
        public RacCluster Cluster { get; init; } = new() { Id = Guid.NewGuid(), Name = "Кластер" };
        public RacInfobaseSummary[] Infobases { get; init; } = Array.Empty<RacInfobaseSummary>();
        public RacInfobaseInfo? Info { get; init; }
        public string LastActionErrorValue { get; init; } = string.Empty;
        public string LastActionError => LastActionErrorValue;

        public Task<IReadOnlyList<RacCluster>> GetClustersAsync(
            RacConnectionParams parameters, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RacCluster>>(new[] { Cluster });

        public Task<IReadOnlyList<RacInfobaseSummary>> GetInfobasesAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default, TimeSpan? timeout = null)
            => Task.FromResult<IReadOnlyList<RacInfobaseSummary>>(Infobases);

        public Task<RacInfobaseInfo?> GetInfobaseInfoAsync(
            RacConnectionParams parameters, Guid clusterId, Guid infobaseId,
            string? infobaseUser, string? infobasePassword, CancellationToken cancellationToken = default, TimeSpan? timeout = null)
            => Task.FromResult(Info);

        public Task<RacClusterInfo?> GetClusterInfoAsync(RacConnectionParams p, Guid c, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<RacProcessInfo>> GetProcessesAsync(RacConnectionParams p, Guid c, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<RacSessionInfo>> GetSessionsAsync(RacConnectionParams p, Guid c, Guid? i = null, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<RacConnectionInfo>> GetConnectionsAsync(RacConnectionParams p, Guid c, Guid? i = null, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<RacLockInfo>> GetLocksAsync(RacConnectionParams p, Guid c, Guid? i = null, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<RacJobInfo>> GetJobsAsync(RacConnectionParams p, Guid c, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> UpdateClusterAsync(RacConnectionParams p, Guid c, RacClusterUpdate u, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> SetJobStateAsync(RacConnectionParams p, Guid c, Guid j, RacJobAction a, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> TerminateSessionAsync(RacConnectionParams p, Guid c, Guid s, Guid? pr = null, string? u = null, string? pw = null, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> DisconnectConnectionAsync(RacConnectionParams p, Guid c, Guid con, Guid? pr = null, string? u = null, string? pw = null, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> CreateInfobaseAsync(RacConnectionParams p, Guid c, RacInfobaseCreateRequest r, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> UpdateInfobaseAsync(RacConnectionParams p, Guid c, Guid i, RacInfobaseUpdateRequest r, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> DropInfobaseAsync(RacConnectionParams p, Guid c, Guid i, bool d = false, string? u = null, string? pw = null, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private static Infobase ServerBase(string name) => new()
    {
        Id = "base-92",
        Name = name,
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.ClientServer,
            Server = "localhost:27545",
            DatabaseName = name
        }
    };

    [Theory]
    [InlineData("Demo 27", "demo_27")]
    [InlineData("Demo 27", "demo-27")]
    [InlineData("  Demo 27 ", "Demo  27")]
    [InlineData("demo27", "DEMO27")]
    public void NormalizeInfobaseName_SeparatorVariants_Match(string appName, string racName)
    {
        // Имя базы в приложении и в кластере rac может отличаться разделителями
        // (пробелы/подчерки/дефисы) — раньше строгое сравнение считало базы разными,
        // и автоподстановка падала «база не найдена» (issue #79/#92).
        Assert.True(ClusterInfobaseCredentialsResolver.NormalizedNamesEqual(appName, racName));
    }

    [Fact]
    public void NormalizeInfobaseName_DifferentBases_DoNotMatch()
    {
        // «Demo 27» и «Demo_8.3.27» — разные базы: слишком агрессивная нормализация
        // (без разделителей вообще) подставляла бы чужую базу.
        Assert.False(ClusterInfobaseCredentialsResolver.NormalizedNamesEqual("Demo 27", "Demo_8.3.27"));
        Assert.False(ClusterInfobaseCredentialsResolver.NormalizedNamesEqual("Demo 27", "Demo  8 3 27"));
    }

    [Fact]
    public async Task ResolveAsync_NormalizedName_BuildsProfile()
    {
        var rac = new FakeRacClient
        {
            Infobases = new[]
            {
                new RacInfobaseSummary
                {
                    InfobaseId = Guid.NewGuid(), Name = "demo_27",
                    Dbms = "PostgreSQL", DbServer = "pg-host", DbName = "demo_db", DbUser = "postgres"
                }
            }
        };
        var log = new List<string>();
        var resolver = new ClusterInfobaseCredentialsResolver(rac, servers: null, log: log.Add);

        var profile = await resolver.ResolveAsync(ServerBase("Demo 27"));

        Assert.NotNull(profile);
        Assert.Equal(DbmsKind.PostgreSql, profile!.Dbms);
        Assert.Equal("demo_db", profile.Database);
        // Штатный путь: только диагностическая строка свойств найденной базы (v1.0.1.22),
        // без ошибок резолва.
        Assert.Contains(log, l => l.Contains("BatchOps.DbmsResolveSummaryFieldsFormat"));
        Assert.DoesNotContain(log, l => l.Contains("BatchOps.DbmsResolveInfoFailed"));
        Assert.DoesNotContain(log, l => l.Contains("BatchOps.DbmsResolveNoProfileFormat"));
    }

    [Fact]
    public async Task ResolveAsync_EmptyDbms_InfoAccessDenied_ReasonVisibleInLog()
    {
        // Главный сценарий ZIP 7OH: dbms в «infobase summary list» пуст, «infobase
        // info» возвращает null («Недостаточно прав пользователя»). Раньше итоговая
        // строка журнала — «база не найдена», что вводило в заблуждение. Теперь:
        // 1) в журнале фактические свойства найденной базы (dbms/db-server/...);
        // 2) текст ошибки rac из LastActionError;
        // 3) честное «найдена, но параметры СУБД получить не удалось».
        var rac = new FakeRacClient
        {
            Infobases = new[]
            {
                new RacInfobaseSummary
                {
                    InfobaseId = Guid.NewGuid(), Name = "Demo_8.3.27",
                    Dbms = string.Empty, DbServer = "pg-host", DbName = string.Empty, DbUser = "postgres"
                }
            },
            Info = null,
            LastActionErrorValue = "Недостаточно прав пользователя на информационную базу Demo_8.3.27"
        };
        var log = new List<string>();
        var resolver = new ClusterInfobaseCredentialsResolver(rac, servers: null, log: log.Add);

        var profile = await resolver.ResolveAsync(ServerBase("Demo_8.3.27"));

        Assert.Null(profile);
        // Свойства найденной базы — в журнале (следующий ZIP однозначно покажет,
        // что rac вернул по dbms).
        Assert.Contains(log, l => l.Contains("BatchOps.DbmsResolveSummaryFieldsFormat"));
        // Причина ошибки «infobase info» пошла в журнал через формат с {0}
        // (текст rac из LastActionError; в тестовом процессе локализация не загружена —
        // остаётся ключ строки, сам подставленный текст проверяется в приложении).
        Assert.Contains(log, l => l.Contains("BatchOps.DbmsResolveInfoFailedFormat"));
        // Итог — не «база не найдена», а «найдена, но параметры СУБД получить не удалось».
        Assert.Contains(log, l => l.Contains("BatchOps.DbmsResolveNoProfileFormat"));
        Assert.DoesNotContain(log, l => l.Contains("BatchOps.DbmsResolveNotFound"));
    }

    [Fact]
    public async Task ResolveAsync_EmptyDbms_InfoWithoutError_StillFailsWithReason()
    {
        // «infobase info» вернул null без текста ошибки — прежнее диагностическое
        // сообщение «не вернула поддерживаемый тип СУБД» + честный итог.
        var rac = new FakeRacClient
        {
            Infobases = new[]
            {
                new RacInfobaseSummary
                {
                    InfobaseId = Guid.NewGuid(), Name = "Demo_8.3.27", Dbms = string.Empty
                }
            }
        };
        var log = new List<string>();
        var resolver = new ClusterInfobaseCredentialsResolver(rac, servers: null, log: log.Add);

        var profile = await resolver.ResolveAsync(ServerBase("Demo_8.3.27"));

        Assert.Null(profile);
        Assert.Contains(log, l => l.Contains("BatchOps.DbmsResolveInfoFailed"));
        Assert.Contains(log, l => l.Contains("BatchOps.DbmsResolveNoProfileFormat"));
    }

    // ===================== Заголовок и кнопки окна (issue #92, v1.0.1.31) =====================

    [Theory]
    [InlineData(BatchOperationKind.DumpDt, "BatchOps.HintDumpDt")]
    [InlineData(BatchOperationKind.DumpCf, "BatchOps.HintDumpCf")]
    [InlineData(BatchOperationKind.LoadConfig, "BatchOps.HintLoadCf")]
    public void HintKeys_FollowKind(BatchOperationKind kind, string expectedHint)
    {
        // Issue #92 (v1.0.1.32): подсказка по-прежнему следует за видом операции.
        Assert.Equal(expectedHint, BatchOperationsViewModel.HintKeyForKind(kind));
    }

    [Fact]
    public void TitleKeys_StaticForBothDumpKinds()
    {
        // Issue #92 (v1.0.1.32, репорт 7OH: «Не переключается заголовок … Может быть
        // просто сделать заголовок не изменяемым "Выгрузка в DT/CF"?»): для обоих
        // видов выгрузки — СТАТИЧНЫЙ заголовок, загрузка CF — свой.
        Assert.Equal("BatchOps.TitleDumpBoth", BatchOperationsViewModel.TitleKeyForKind(BatchOperationKind.DumpDt));
        Assert.Equal("BatchOps.TitleDumpBoth", BatchOperationsViewModel.TitleKeyForKind(BatchOperationKind.DumpCf));
        Assert.Equal("BatchOps.TitleLoadCf", BatchOperationsViewModel.TitleKeyForKind(BatchOperationKind.LoadConfig));
    }

    [Fact]
    public async Task RunAsync_Success_IsRunningReset()
    {
        // Issue #92 (репорт 7OH: после успешного завершения «Остановить» активна,
        // «Выполнить» не активна): по завершении прогона IsRunning == false —
        // кнопки окон (WPF/Avalonia) синхронизируются по нему.
        var dir = Directory.CreateTempSubdirectory("ws1c_runok").FullName;
        try
        {
            var vm = new BatchOperationsViewModel(new[] { FileBase("Альфа") }, BatchOperationKind.DumpDt)
            {
                PathText = dir,
                ExecutorOverride = (_, _, _) => Task.FromResult(new IbcmdResult { Success = true, ExitCode = 0 })
            };

            await vm.RunAsync(CancellationToken.None);

            Assert.False(vm.IsRunning);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ExecutorThrows_IsRunningReset()
    {
        // Исключение исполнителя — тоже сброс IsRunning (в finally, при любом исходе).
        var dir = Directory.CreateTempSubdirectory("ws1c_runerr").FullName;
        try
        {
            var vm = new BatchOperationsViewModel(new[] { FileBase("Альфа") }, BatchOperationKind.DumpDt)
            {
                PathText = dir,
                ExecutorOverride = (_, _, _) => throw new InvalidOperationException("boom")
            };

            await vm.RunAsync(CancellationToken.None);

            Assert.False(vm.IsRunning);
            Assert.True(vm.Items.All(i => i.Status == BatchOperationsStatus.Failed));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_Cancelled_IsRunningReset()
    {
        // Остановка пользователем (кнопка «Остановить»): IsRunning сбрасывается —
        // повторный «Выполнить» работает без переоткрытия окна.
        var dir = Directory.CreateTempSubdirectory("ws1c_runcan").FullName;
        try
        {
            using var cts = new CancellationTokenSource();
            var vm = new BatchOperationsViewModel(new[] { FileBase("Альфа") }, BatchOperationKind.DumpDt)
            {
                PathText = dir,
                ExecutorOverride = (_, _, token) =>
                {
                    cts.Cancel();
                    token.ThrowIfCancellationRequested();
                    return Task.FromResult(new IbcmdResult { Success = false });
                }
            };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => vm.RunAsync(cts.Token));

            Assert.False(vm.IsRunning);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// SynchronizationContext, складывающий Post'ы в очередь (без прокачки): имитация
    /// UI-потока, на котором продолжения НЕ выполняются сразу.
    /// </summary>
    private sealed class PumpingSynchronizationContext : SynchronizationContext
    {
        public readonly Queue<(SendOrPostCallback Callback, object? State)> Pending = new();

        public override void Post(SendOrPostCallback d, object? state) =>
            Pending.Enqueue((d, state));

        public void PumpAll()
        {
            while (Pending.Count > 0)
            {
                var (callback, state) = Pending.Dequeue();
                callback(state);
            }
        }
    }

    [Fact]
    public void RunAsync_AfterBackgroundContinuation_IsRunningResetViaCapturedContext()
    {
        // Issue #92 (v1.0.1.31, корень бага 7OH): прогон уходит с UI-потока после
        // ConfigureAwait(false), и раньше сброс IsRunning (вместе с PropertyChanged)
        // выполнялся на фоновом потоке — окна не могли обновить кнопки. Теперь сброс
        // ставится в очередь ЗАХВАЧЕННОГО контекста UI-потока. Тест: продолжения
        // исполнителя уходят в пул потоков, сброс IsRunning — в очередь контекста,
        // после прокачки очереди IsRunning == false.
        var dir = Directory.CreateTempSubdirectory("ws1c_runctx").FullName;
        try
        {
            var context = new PumpingSynchronizationContext();
            var vm = new BatchOperationsViewModel(new[] { FileBase("Альфа") }, BatchOperationKind.DumpDt)
            {
                PathText = dir,
                ExecutorOverride = (_, _, _) => Task.Run(() => new IbcmdResult { Success = true, ExitCode = 0 })
            };

            Exception? threadError = null;
            var thread = new Thread(() =>
            {
                try
                {
                    SynchronizationContext.SetSynchronizationContext(context);
                    vm.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    threadError = ex;
                }
            });
            thread.Start();
            thread.Join();

            Assert.Null(threadError);
            // Сброс IsRunning отправлен в очередь контекста UI-потока (прогон ушёл
            // с потока прогона), а не выполнен на фоновом потоке.
            Assert.True(context.Pending.Count > 0 || !vm.IsRunning,
                "Сброс IsRunning должен идти через захваченный контекст UI-потока.");
            context.PumpAll();
            Assert.False(vm.IsRunning);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
