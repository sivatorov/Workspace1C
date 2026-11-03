using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Параметры подключения к серверу 1С:Предприятие для команд rac (Remote Administration Client).
/// Адрес/порт/логин сохраняются в настройках приложения; <see cref="Password"/> живёт ТОЛЬКО
/// в памяти (вводится при каждом открытии окна монитора) и не пишется в журнал (см.
/// <see cref="SensitiveDataMasker.MaskRacPassword"/>).
/// </summary>
public sealed record RacConnectionParams
{
    /// <summary>Адрес сервера 1С (host или IP).</summary>
    public string Address { get; init; } = "localhost";

    /// <summary>
    /// Порт агента сервера 1С (ragent) — по умолчанию 1540; для сервера администрирования
    /// RAS — 1545.
    /// </summary>
    public int Port { get; init; } = IRacClient.DefaultPort;

    /// <summary>Логин администратора кластера (пустая строка — без аутентификации).</summary>
    public string User { get; init; } = string.Empty;

    /// <summary>Пароль администратора кластера. Только в памяти, не сериализуется.</summary>
    public string Password { get; init; } = string.Empty;
}

/// <summary>
/// Ошибка выполнения команды rac: не найден исполняемый файл, ненулевой код выхода,
/// превышен таймаут ожидания или ошибка запуска процесса.
/// </summary>
public sealed class RacClientException : Exception
{
    public RacClientException(string message) : base(message)
    {
    }

    public RacClientException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Клиент утилиты rac (Remote Administration Client) — сервисный слой встроенного монитора
/// серверов 1С (цикл 0.3.9.123–0.3.9.126). Чистый сервис — без UI-зависимостей.
/// Исполняемый файл rac ищется в каталоге bin установленной платформы через
/// <see cref="OneCPlatformLocator.FindRacExecutable"/>; команды выполняются прямым запуском
/// процесса без shell (ArgumentList) с захватом stdout/stderr и таймаутом.
/// </summary>
public interface IRacClient
{
    /// <summary>Порт агента сервера 1С (ragent) по умолчанию.</summary>
    public const int DefaultPort = 1540;

    /// <summary>Список кластеров сервера (команда «cluster list»).</summary>
    Task<IReadOnlyList<RacCluster>> GetClustersAsync(
        RacConnectionParams parameters, CancellationToken cancellationToken = default);

    /// <summary>Информация о кластере (команда «cluster info»); null, если вывод пуст/не распознан.</summary>
    Task<RacClusterInfo?> GetClusterInfoAsync(
        RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default);

    /// <summary>Рабочие процессы кластера (команда «process list --cluster=...»).</summary>
    Task<IReadOnlyList<RacProcessInfo>> GetProcessesAsync(
        RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Сеансы кластера (команда «session list --cluster=...»). При заданном
    /// <paramref name="infobaseId"/> добавляется параметр «--infobase=...» — данные
    /// возвращаются только для выбранной информационной базы (issue #3, вкладка «Базы»).
    /// </summary>
    Task<IReadOnlyList<RacSessionInfo>> GetSessionsAsync(
        RacConnectionParams parameters, Guid clusterId, Guid? infobaseId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сеансы кластера с информацией о лицензиях (команда «session list --licenses
    /// --cluster=...», п.5 «Мониторинг лицензий», выпуск 1.0.0.87): у сеансов
    /// заполняются <see cref="RacSessionInfo.LicType"/>, <see cref="RacSessionInfo.MaxLicenses"/>
    /// и <see cref="RacSessionInfo.Licenses"/>. Реализация по умолчанию (тестовые
    /// заглушки) — пустой список.
    /// </summary>
    Task<IReadOnlyList<RacSessionInfo>> GetLicensedSessionsAsync(
        RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RacSessionInfo>>(Array.Empty<RacSessionInfo>());

    /// <summary>
    /// Соединения кластера (команда «connection list --cluster=...»). При заданном
    /// <paramref name="infobaseId"/> добавляется параметр «--infobase=...» (issue #3).
    /// </summary>
    Task<IReadOnlyList<RacConnectionInfo>> GetConnectionsAsync(
        RacConnectionParams parameters, Guid clusterId, Guid? infobaseId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Блокировки объектов данных кластера (команда «lock list --cluster=...»). При
    /// заданном <paramref name="infobaseId"/> добавляется параметр «--infobase=...» (issue #3).
    /// </summary>
    Task<IReadOnlyList<RacLockInfo>> GetLocksAsync(
        RacConnectionParams parameters, Guid clusterId, Guid? infobaseId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Информационные базы кластера (команда «infobase summary list --cluster=...»).
    /// Используется функцией импорта баз из кластера 1С (цикл 0.3.9.172–0.3.9.175).
    /// <paramref name="timeout"/> — per-call таймаут (issue #79, v1.0.1.28):
    /// null — стандартные 30 с; резолвер свойств базы передаёт 90 с.
    /// </summary>
    Task<IReadOnlyList<RacInfobaseSummary>> GetInfobasesAsync(
        RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default,
        TimeSpan? timeout = null);

    /// <summary>
    /// Регламентные задания кластера (команда «job list --cluster=...»).
    /// Вкладка «Регламентные задания» монитора серверов (цикл 0.3.9.176–0.3.9.179).
    /// </summary>
    Task<IReadOnlyList<RacJobInfo>> GetJobsAsync(
        RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Изменение параметров кластера (команда «cluster update --cluster=...», issue #324,
    /// C3: редактируемая «Информация о кластере»). Передаются ТОЛЬКО изменённые свойства
    /// (см. <see cref="RacClusterUpdate.IsEmpty"/>). Возвращает true при ExitCode 0;
    /// при неудаче — false и текст ошибки (включая stderr rac) в <see cref="LastActionError"/>.
    /// </summary>
    Task<bool> UpdateClusterAsync(
        RacConnectionParams parameters, Guid clusterId, RacClusterUpdate changes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Изменение состояния регламентного задания (команды «job pause/resume/disable/enable
    /// --cluster=... --job=...»). Возвращает true при ExitCode 0; при неудаче — false
    /// и текст ошибки (включая stderr rac) в <see cref="LastActionError"/>.
    /// </summary>
    Task<bool> SetJobStateAsync(
        RacConnectionParams parameters, Guid clusterId, Guid jobId, RacJobAction action,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Завершение сеанса пользователя (команда «session terminate --cluster=... [--process=...]
    /// --session=...»). Рабочий процесс <paramref name="processId"/> обязателен для rac 8.5.4
    /// (issue #69: «повторить — ошибка», «Ошибка разбора параметра: process»), в классических
    /// 8.3.x — опционален. Учётные данные администратора ИБ (<paramref name="infobaseUser"/>/
    /// <paramref name="infobasePassword"/>, issue #69, комментарий 7OH от 2026-10-27) нужны
    /// для защищённых баз: rac отклоняет «session terminate» с «Недостаточно прав пользователя
    /// на информационную базу», если креды не переданы аргументами <c>--infobase-user</c>/
    /// <c>--infobase-pwd</c>; пароль маскируется при журналировании. Возвращает true при
    /// ExitCode 0; при неудаче — false и текст ошибки (включая stderr rac) в
    /// <see cref="LastActionError"/>.
    /// </summary>
    Task<bool> TerminateSessionAsync(
        RacConnectionParams parameters, Guid clusterId, Guid sessionId, Guid? processId = null,
        string? infobaseUser = null, string? infobasePassword = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Разрыв соединения клиента (команда «connection disconnect --cluster=... [--process=...]
    /// --connection=...»). Рабочий процесс <paramref name="processId"/> обязателен для rac 8.5.4
    /// (issue #69: «повторить — ошибка», «Ошибка разбора параметра: process»), в классических
    /// 8.3.x — опционален. Учётные данные администратора ИБ (<paramref name="infobaseUser"/>/
    /// <paramref name="infobasePassword"/>, issue #69, комментарий 7OH от 2026-10-27) нужны
    /// для защищённых баз: rac отклоняет «connection disconnect» с «Недостаточно прав
    /// пользователя на информационную базу», если креды не переданы аргументами
    /// <c>--infobase-user</c>/<c>--infobase-pwd</c>; пароль маскируется при журналировании.
    /// Возвращает true при ExitCode 0; при неудаче — false и текст ошибки (включая stderr rac)
    /// в <see cref="LastActionError"/>.
    /// </summary>
    Task<bool> DisconnectConnectionAsync(
        RacConnectionParams parameters, Guid clusterId, Guid connectionId, Guid? processId = null,
        string? infobaseUser = null, string? infobasePassword = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Создание информационной базы в кластере (команда «infobase create --cluster=...
    /// --name=... --dbms=... --db-server=... --db-name=...», issue #3, вкладка «Базы»).
    /// Поля запроса передаются в rac; пароль СУБД маскируется при журналировании.
    /// Возвращает true при ExitCode 0; при неудаче — false и текст ошибки
    /// (включая stderr rac) в <see cref="LastActionError"/>.
    /// </summary>
    Task<bool> CreateInfobaseAsync(
        RacConnectionParams parameters, Guid clusterId, RacInfobaseCreateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Изменение свойств информационной базы в кластере (команда «infobase update
    /// --cluster=... --infobase=... --name=...», issue #3, кнопка «Изменить»).
    /// Поля запроса передаются в rac; пароль СУБД маскируется при журналировании.
    /// Возвращает true при ExitCode 0; при неудаче — false и текст ошибки
    /// (включая stderr rac) в <see cref="LastActionError"/>.
    /// </summary>
    Task<bool> UpdateInfobaseAsync(
        RacConnectionParams parameters, Guid clusterId, Guid infobaseId,
        RacInfobaseUpdateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаление информационной базы из кластера (команда «infobase drop --cluster=...
    /// --infobase=... [--infobase-user=U --infobase-pwd=P] [--drop-database]», issue #3,
    /// вкладка «Базы»): подтверждение и выбор варианта («просто удалить из кластера» /
    /// «удалить с удалением БД на сервере СУБД») выполняет диалог удаления; сюда
    /// передаётся итог — флаг <paramref name="dropDatabase"/>. Учётные данные
    /// администратора ИБ (<paramref name="infobaseUser"/>/<paramref name="infobasePassword"/>)
    /// нужны, если база защищена (issue #69): при повторной попытке удаления они
    /// передаются rac аргументами <c>--infobase-user</c>/<c>--infobase-pwd</c>; пароль
    /// маскируется при журналировании. Возвращает true при ExitCode 0; при неудаче —
    /// false и текст ошибки (включая stderr rac) в <see cref="LastActionError"/>.
    /// </summary>
    Task<bool> DropInfobaseAsync(
        RacConnectionParams parameters, Guid clusterId, Guid infobaseId,
        bool dropDatabase = false, string? infobaseUser = null,
        string? infobasePassword = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Полные свойства информационной базы (команда «infobase info», issue #3): нужны
    /// учётные данные администратора ИБ, если в базе есть пользователи. Возвращает null
    /// при ошибке; текст ошибки — в <see cref="LastActionError"/>. Реализация по умолчанию
    /// (для тестовых заглушек) — null.
    /// </summary>
    Task<RacInfobaseInfo?> GetInfobaseInfoAsync(
        RacConnectionParams parameters, Guid clusterId, Guid infobaseId,
        string? infobaseUser, string? infobasePassword, CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
        => Task.FromResult<RacInfobaseInfo?>(null);

    /// <summary>
    /// Текст последней ошибки действия (<see cref="TerminateSessionAsync"/> /
    /// <see cref="DisconnectConnectionAsync"/>): сообщение rac со stderr при неудаче,
    /// пустая строка при успехе или пока действие не выполнялось.
    /// </summary>
    string LastActionError { get; }
}