using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Источник обновления конфигурации в массовом режиме (issue #85, v1.0.1.43):
/// файл поставки .cf (issue #106) или хранилище конфигурации базы.
/// </summary>
public enum MassLoadCfSource
{
    /// <summary>Файл поставки .cf — Конфигуратор /LoadCfg"path.cf" + /UpdateDBCfg (issue #106).</summary>
    CfFile,

    /// <summary>Хранилище конфигурации базы — Конфигуратор /ConfigurationRepositoryUpdateCfg
    /// + /UpdateDBCfg (<see cref="OneCLauncher.DesignerBatchOperation.RepositoryUpdate"/>).
    /// Адрес/логин/пароль хранилища берутся из свойств базы; у целей без настроенного
    /// хранилища валидация блокирует старт.</summary>
    Repository
}

/// <summary>
/// Шаг массовой загрузки конфигурации из .cf через Конфигуратор по одной базе
/// (issue #106). Порядок выполнения шагов по базе задаётся
/// <see cref="MassLoadCfPlanner.PlanSteps"/>.
/// </summary>
public enum MassLoadCfStep
{
    /// <summary>Подготовка: завершение сеансов и/или блокировки (rac / 1cv8.cdn).</summary>
    Prepare,

    /// <summary>Выгрузка базы в .dt перед загрузкой (Конфигуратор, /DumpIB).</summary>
    DumpDt,

    /// <summary>Загрузка конфигурации из .cf (Конфигуратор, /LoadCfg + /UpdateDBCfg).</summary>
    LoadCf,

    /// <summary>Обновление конфигурации из хранилища (Конфигуратор,
    /// /ConfigurationRepositoryUpdateCfg + /UpdateDBCfg; issue #85, v1.0.1.43).</summary>
    RepositoryUpdate,

    /// <summary>Снятие блокировок, установленных шагом <see cref="Prepare"/>.</summary>
    Restore
}

/// <summary>
/// Опции массовой загрузки конфигурации из .cf через Конфигуратор (issue #106,
/// автор aepkugesi): файл .cf, опциональная выгрузка базы в .dt перед загрузкой
/// («как в массовой выгрузке в dt» — тот же каталог/шаблон имени) и блок «Перед
/// загрузкой»: завершение сеансов, блокировка начала сеансов (с кодом разрешения)
/// и блокировка регламентных заданий. Код разрешения — секретоподобное значение:
/// в настройках приложения не сохраняется.
/// </summary>
public sealed class MassLoadCfOptions
{
    /// <summary>Файл .cf, загружаемый в каждую выбранную базу.</summary>
    public string CfFilePath { get; set; } = string.Empty;

    /// <summary>Источник обновления (issue #85, v1.0.1.43): файл .cf (по умолчанию,
    /// issue #106) или хранилище конфигурации.</summary>
    public MassLoadCfSource Source { get; set; } = MassLoadCfSource.CfFile;

    /// <summary>Запустить Предприятие монопольно (/M) после успешного обновления
    /// базы (issue #85, v1.0.1.43): применяется после снятия блокировок шага Restore;
    /// ошибка запуска — предупреждение в журнале, не ошибка обновления.</summary>
    public bool LaunchEnterpriseMonopol { get; set; }

    /// <summary>Выгрузить базу в .dt перед загрузкой (страховочная копия данных).</summary>
    public bool DumpDtBefore { get; set; }

    /// <summary>Каталог для .dt-выгрузки (как в массовой выгрузке в dt).</summary>
    public string DumpDirectory { get; set; } = string.Empty;

    /// <summary>Шаблон имени .dt-файла с токенами {Имя}/{Дата} (как в массовой выгрузке).</summary>
    public string DumpTemplate { get; set; } = BatchOperationsService.DefaultDumpTemplate;

    /// <summary>Завершить сеансы пользователей перед загрузкой (rac «session terminate»).</summary>
    public bool TerminateSessions { get; set; }

    /// <summary>Установить блокировку начала сеансов (rac «infobase update
    /// --sessions-deny=on» / файл 1cv8.cdn); после загрузки снимается.</summary>
    public bool DenyNewSessions { get; set; }

    /// <summary>Код разрешения для входа при блокировке сеансов (rac «--permission-code»
    /// и Конфигуратор «/UC»); null/пусто — не задан.</summary>
    public string? PermissionCode { get; set; }

    /// <summary>Блокировать регламентные задания (rac «infobase update
    /// --scheduled-jobs-deny=on»); после загрузки исходное состояние возвращается.</summary>
    public bool DenyScheduledJobs { get; set; }

    /// <summary>Включена ли хотя бы одна опция подготовки «Перед загрузкой».</summary>
    public bool HasPrepareOptions => TerminateSessions || DenyNewSessions || DenyScheduledJobs;

    /// <summary>Включена ли выгрузка в .dt перед загрузкой.</summary>
    public bool AnyDump => DumpDtBefore;

    /// <summary>Включена ли хотя бы одна опция (кроме самой загрузки).</summary>
    public bool AnyExtras => HasPrepareOptions || AnyDump;
}

/// <summary>
/// Чистая логика планирования массовой загрузки конфигурации из .cf через
/// Конфигуратор (issue #106, по образцу TestDeployPlanner): порядок шагов по базе,
/// формирование пути .dt по шаблону массовой выгрузки, маппинг опций в
/// <see cref="BatchOpsPreparationOptions"/> и пред-валидация параметров. Без
/// запуска процессов — покрыта юнит-тестами; выполнение — <see cref="MassLoadCfService"/>.
/// </summary>
public static class MassLoadCfPlanner
{
    /// <summary>
    /// Порядок шагов по одной базе (issue #106): подготовка (завершение сеансов,
    /// блокировки) → выгрузка .dt (страховочная копия уже при «тихой» базе) →
    /// загрузка конфигурации → снятие блокировок. Шаги, отключённые опциями,
    /// в план не попадают; <see cref="MassLoadCfStep.LoadCf"/> есть всегда.
    /// Internal — юнит-тесты.
    /// </summary>
    /// <summary>
    /// Разбор сохранённого источника обновления (настройка <c>BatchLoadCfSource</c>,
    /// issue #85, v1.0.1.43): «Repository» → хранилище, остальное (в т.ч. пусто) —
    /// файл .cf (дефолт issue #106).
    /// </summary>
    public static MassLoadCfSource ParseSource(string? value)
        => string.Equals(value?.Trim(), "Repository", StringComparison.OrdinalIgnoreCase)
            ? MassLoadCfSource.Repository
            : MassLoadCfSource.CfFile;

    /// <summary>Обратное преобразование <see cref="ParseSource"/> для сохранения в настройки.</summary>
    public static string SourceToString(MassLoadCfSource source)
        => source == MassLoadCfSource.Repository ? "Repository" : "CfFile";

    internal static IReadOnlyList<MassLoadCfStep> PlanSteps(MassLoadCfOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var steps = new List<MassLoadCfStep>();
        if (options.HasPrepareOptions)
            steps.Add(MassLoadCfStep.Prepare);
        if (options.DumpDtBefore)
            steps.Add(MassLoadCfStep.DumpDt);
        // Источник обновления (issue #85, v1.0.1.43): файл .cf (issue #106) или
        // хранилище конфигурации — шаг RepositoryUpdate вместо LoadCf.
        steps.Add(options.Source == MassLoadCfSource.Repository
            ? MassLoadCfStep.RepositoryUpdate
            : MassLoadCfStep.LoadCf);
        if (options.HasPrepareOptions)
            steps.Add(MassLoadCfStep.Restore);
        return steps;
    }

    /// <summary>
    /// Целевой путь .dt-файла для базы: тот же механизм шаблонов {Имя}/{Дата},
    /// что у массовой выгрузки в dt (issue #106: «выгрузку базы в файл *.dt
    /// как в массовой выгрузке в dt»). Делегирует
    /// <see cref="BatchOperationsService.BuildDumpFilePath"/>.
    /// </summary>
    public static string BuildDtPath(MassLoadCfOptions options, string infobaseName, DateTime? timestamp = null,
        string? timestampFormat = null)
        => BatchOperationsService.BuildDumpFilePath(
            options.DumpDirectory, infobaseName, BatchOperationKind.DumpDt,
            options.DumpTemplate, timestamp, timestampFormat);

    /// <summary>
    /// Маппинг опций окна в опции сервиса подготовки «Перед операцией»
    /// (issue #106 переиспользует <see cref="BatchOpsPreparationService"/>).
    /// В отличие от движка «Конфигуратор» массовой выгрузки (issue #92, v1.0.1.30),
    /// здесь блокировка начала сеансов СОВМЕСТИМА с операцией: Конфигуратор входит
    /// в заблокированную базу с кодом разрешения «/UC» (см.
    /// <see cref="MassLoadCfService"/>), поэтому <c>SessionDenyIncompatible</c>
    /// всегда false. Internal — юнит-тесты.
    /// </summary>
    internal static BatchOpsPreparationOptions ToPreparationOptions(MassLoadCfOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new BatchOpsPreparationOptions
        {
            TerminateSessions = options.TerminateSessions,
            DenyNewSessions = options.DenyNewSessions,
            PermissionCode = string.IsNullOrWhiteSpace(options.PermissionCode)
                ? null
                : options.PermissionCode!.Trim(),
            DenyScheduledJobs = options.DenyScheduledJobs,
            SessionDenyIncompatible = false
        };
    }

    /// <summary>
    /// Пред-валидация ДО старта прогона (issue #106): параметры (файл .cf,
    /// каталог .dt-выгрузки) и заведомо неподдерживаемые базы (веб-базы Конфигуратор
    /// не обслуживает; запущенные базы пропускаются в прогоне). Возвращает список
    /// сообщений об ошибках; пустой список — валидация пройдена.
    /// </summary>
    public static IReadOnlyList<string> Validate(MassLoadCfOptions? options, IEnumerable<Infobase>? targets)
    {
        var errors = new List<string>();
        if (options is null)
        {
            errors.Add(LocalizationManager.T("BatchOps.ValidationFailed"));
            return errors;
        }

        var repositorySource = options.Source == MassLoadCfSource.Repository;

        // Файл .cf требуется ТОЛЬКО для источника «Файл поставки»: для хранилища
        // конфигурации путь не нужен — адрес берётся из свойств базы (issue #85).
        var cfPath = options.CfFilePath?.Trim() ?? string.Empty;
        if (!repositorySource)
        {
            if (string.IsNullOrWhiteSpace(cfPath))
                errors.Add(LocalizationManager.T("BatchOps.NeedCfFile"));
            else if (!File.Exists(cfPath))
                errors.Add(LocalizationManager.T("BatchOps.CfNotFound"));
            else if (!string.Equals(Path.GetExtension(cfPath), ".cf", StringComparison.OrdinalIgnoreCase))
                errors.Add(LocalizationManager.T("MassLoadCf.NotCfFile"));
        }

        if (options.DumpDtBefore)
        {
            var dir = options.DumpDirectory?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(dir))
                errors.Add(LocalizationManager.T("BatchOps.NeedDirectory"));
            else if (!Directory.Exists(dir))
                errors.Add(LocalizationManager.T("BatchOps.DirectoryNotFound"));
        }

        foreach (var ib in targets?.Where(b => b is not null) ?? Enumerable.Empty<Infobase>())
        {
            if (ib.Connection?.Type == ConnectionType.WebServer)
                errors.Add(string.Format(
                    LocalizationManager.T("BatchOps.WebNotSupportedFormat"), ib.Name));

            // Источник «Хранилище конфигурации» (issue #85, v1.0.1.43): у каждой цели
            // должно быть настроено хранилище — иначе блокируем старт с перечнем баз
            // ДО запуска конфигураторов (понятная ошибка вместо сбоя каждой операции).
            if (repositorySource && !(ib.Repository?.HasServer ?? false))
                errors.Add(string.Format(
                    LocalizationManager.T("MassLoadCf.RepoNotConfiguredFormat"), ib.Name));
        }

        return errors;
    }
}
