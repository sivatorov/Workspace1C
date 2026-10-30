using System.Linq;

namespace Workspace1C.Models;

/// <summary>
/// Режим размещения тестовой копии (п.10 «Тестовый контур одной кнопкой», 1.0.0.92):
/// клиент-серверная ИБ на сервере 1С (<c>infobase create</c> через конфигуратор) или
/// файловая ИБ (каталог на диске).
/// </summary>
public enum TestDeployTargetMode
{
    /// <summary>Клиент-серверная тестовая ИБ на сервере 1С.</summary>
    Server,

    /// <summary>Файловая тестовая ИБ (каталог).</summary>
    File
}

/// <summary>
/// Шаг сценария «Развернуть тестовую копию» (чистая модель для мастера-предпросмотра,
/// журнала прогресса и юнит-тестов). Каждый шаг имеет стабильный ключ
/// (<see cref="Kind"/>) и ключ локализации заголовка (<see cref="TitleKey"/>).
/// </summary>
public enum TestDeployStepKind
{
    /// <summary>Выгрузка исходной базы в файл .dt (движок конфигуратора или ibcmd).</summary>
    Dump,

    /// <summary>Создание группы «Тест», если её ещё нет (только когда включено добавление в группу).</summary>
    EnsureGroup,

    /// <summary>Создание новой тестовой ИБ (серверная или файловая).</summary>
    Create,

    /// <summary>Загрузка .dt в существующую тестовую ИБ (обновление данных).</summary>
    Update,

    /// <summary>Загрузка .dt в новую тестовую ИБ (RestoreIB).</summary>
    Restore,

    /// <summary>Выключение регламентных заданий в копии (по умолчанию включено).</summary>
    DisableJobs,

    /// <summary>Применение отличающих признаков: заголовок «Тест: …» и/или цвет группы.</summary>
    Markers,

    /// <summary>Добавление тестовой базы в группу «Тест».</summary>
    AddToGroup
}

/// <summary>Шаг плана (модель без UI: ключ + локализованный заголовок).</summary>
public sealed class TestDeployStep
{
    /// <param name="kind">Стабильный ключ шага.</param>
    /// <param name="titleKey">Ключ локализации заголовка шага (TestDeploy.Step.*).</param>
    public TestDeployStep(TestDeployStepKind kind, string titleKey)
    {
        Kind = kind;
        TitleKey = titleKey;
    }

    public TestDeployStepKind Kind { get; }

    public string TitleKey { get; }
}

/// <summary>
/// Решение о группе «Тест»: существующая группа (путь для записи в базу) или
/// новая корневая группа, которую нужно добавить в список групп. Чистая логика
/// — в <see cref="Services.TestDeployPlanner"/>.
/// </summary>
public sealed class TestDeployGroupPlan
{
    /// <summary>Новая группа для добавления (null — группа уже существует или добавление отключено).</summary>
    public Group? NewGroup { get; init; }

    /// <summary>Путь группы (без разделителя — имя корневой группы).</summary>
    public string GroupPath { get; init; } = string.Empty;

    /// <summary>Признак: группа уже существовала в списке.</summary>
    public bool Exists { get; init; } = true;
}

/// <summary>
/// План сценария «Развернуть тестовую копию» (чистая модель): итоговое имя тестовой
/// базы, признак создания/обновления, шаги и решение по группе «Тест». Строится
/// <see cref="Services.TestDeployPlanner.BuildPlan"/> и служит предпросмотром в мастере.
/// </summary>
public sealed class TestDeployPlan
{
    /// <summary>Итоговое имя тестовой базы (с префиксом «Тест: », если включена смена заголовка).</summary>
    public string TargetName { get; init; } = string.Empty;

    /// <summary>Существующая тестовая база (обновление данных) или null — будет создана новая.</summary>
    public Infobase? ExistingTarget { get; init; }

    /// <summary>Нужно ли создавать новую тестовую ИБ (ExistingTarget == null).</summary>
    public bool CreateTarget => ExistingTarget is null;

    /// <summary>Путь к временному файлу выгрузки .dt.</summary>
    public string DumpFilePath { get; init; } = string.Empty;

    /// <summary>Решение по группе «Тест».</summary>
    public TestDeployGroupPlan GroupPlan { get; init; } = new();

    /// <summary>Последовательность шагов (для предпросмотра и журнала прогресса).</summary>
    public IReadOnlyList<TestDeployStep> Steps { get; init; } = Array.Empty<TestDeployStep>();
}

/// <summary>
/// Сценарий «Развернуть тестовую копию» (п.10, 1.0.0.92): параметры развёртывания
/// тестовой копии исходной базы — имя тестовой базы, режим (сервер/файл), параметры
/// сервера и СУБД, движок выгрузки, отключаемые регламенты, признаки отличия
/// (заголовок/цвет) и добавление в группу «Тест». Хранится списком в JSON-файле
/// <c>test_deploy.json</c> (см. Services.TestDeployStore) и используется заданием
/// планировщика (<see cref="ScheduledTaskKind.TestDeploy"/>).
/// </summary>
public class TestDeployScenario
{
    /// <summary>Идентификатор сценария (для задания по расписанию).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Наименование сценария для окна заданий (по умолчанию — имя тестовой базы).</summary>
    public string Name { get; set; } = string.Empty;

    // ---- Источник ----

    /// <summary>Идентификатор исходной базы (из списка баз приложения).</summary>
    public string SourceInfobaseId { get; set; } = string.Empty;

    /// <summary>Имя исходной базы на момент сохранения (для отображения в списке сценариев).</summary>
    public string SourceInfobaseName { get; set; } = string.Empty;

    // ---- Имя и режим тестовой базы ----

    /// <summary>
    /// Имя тестовой базы. Пустая строка — имя строится префиксом
    /// <see cref="Workspace1C.Services.TestDeployPlanner.DefaultTitlePrefix"/> + имя источника.
    /// </summary>
    public string TargetName { get; set; } = string.Empty;

    /// <summary>Режим размещения: клиент-серверная или файловая ИБ.</summary>
    public TestDeployTargetMode TargetMode { get; set; } = TestDeployTargetMode.Server;

    // ---- Параметры сервера (TargetMode == Server) ----

    /// <summary>Адрес сервера 1С (host[:port]) для новой базы.</summary>
    public string ServerAddress { get; set; } = string.Empty;

    /// <summary>Имя базы на сервере (Ref).</summary>
    public string DatabaseName { get; set; } = string.Empty;

    /// <summary>Тип СУБД (например, MSSQLServer, PostgreSQL).</summary>
    public string Dbms { get; set; } = "MSSQLServer";

    /// <summary>Сервер СУБД.</summary>
    public string DbServer { get; set; } = string.Empty;

    /// <summary>Имя базы данных на сервере СУБД.</summary>
    public string DbName { get; set; } = string.Empty;

    /// <summary>Пользователь СУБД.</summary>
    public string DbUser { get; set; } = string.Empty;

    /// <summary>
    /// Пароль пользователя СУБД. Хранится в JSON сценария (как и пароли сценариев
    /// резервирования); в журнал никогда не выводится.
    /// </summary>
    public string DbPassword { get; set; } = string.Empty;

    /// <summary>
    /// Учётные данные пользователя ИБ для выгрузки .dt (issue #79, последний комментарий
    /// 7OH: «А зачем мне отдельно вводить, если все данные вот они — уже введены выше?»).
    /// При <see cref="BackupCredential.UseInfobaseAuth"/> (по умолчанию) используется
    /// авторизация базы/общая авторизация приложения (как раньше); иначе — явные
    /// логин/пароль из мастера. Пароль хранится в JSON сценария (как DbPassword и пароли
    /// сценариев резервирования); в журнал никогда не выводится.
    /// </summary>
    public BackupCredential? Credential { get; set; }

    /// <summary>Создавать базу данных на сервере СУБД (CrSQLDB="Y").</summary>
    public bool CreateSqlDatabase { get; set; } = true;

    // ---- Параметры файлового режима (TargetMode == File) ----

    /// <summary>Каталог файловой тестовой базы.</summary>
    public string FilePath { get; set; } = string.Empty;

    // ---- Поведение ----

    /// <summary>Движок выгрузки .dt: конфигуратор или ibcmd (поле «Движок выгрузки»).</summary>
    public BackupEngine DumpEngine { get; set; } = BackupEngine.Designer;

    /// <summary>Выключать регламентные задания в тестовой копии (по умолчанию да).</summary>
    public bool DisableScheduledJobs { get; set; } = true;

    /// <summary>Менять заголовок тестовой базы на «Тест: <имя>», чтобы не путать с рабочей.</summary>
    public bool ChangeTitle { get; set; } = true;

    /// <summary>
    /// Признак «цвет»: новой группе «Тест» задаётся отличительный цвет
    /// (<see cref="TestGroupColor"/>), чтобы тестовые базы бросались в глаза в списке.
    /// </summary>
    public bool ChangeColor { get; set; } = true;

    /// <summary>Добавлять тестовую базу в группу «Тест» (автосоздание группы).</summary>
    public bool AddToTestGroup { get; set; } = true;

    /// <summary>Имя группы «Тест» (корневая; может отличаться от стандартного «Тест»).</summary>
    public string TestGroupName { get; set; } = "Тест";

    /// <summary>Цвет группы «Тест» (#RRGGBB) при её автосоздании.</summary>
    public string TestGroupColor { get; set; } = "#D97706";

    /// <summary>
    /// Повторное развёртывание: если тестовая база уже есть в списке — обновить её
    /// данными (.dt загружается в неё), если нет — создать. Управляется флагом:
    /// да (по умолчанию) — обновлять существующую, нет — останавливаться с ошибкой,
    /// чтобы случайно не затереть чужую базу, и создать можно только если её нет.
    /// </summary>
    public bool UpdateExisting { get; set; } = true;

    // ---- Расписание (удобный шаблон для мастера) ----

    /// <summary>Создавать задание «Развернуть тестовую копию» в планировщике.</summary>
    public bool UseSchedule { get; set; }

    /// <summary>Время запуска задания «HH:mm».</summary>
    public string ScheduleTime { get; set; } = "06:00";

    /// <summary>Дни недели задания (пусто — ежедневно).</summary>
    public List<DayOfWeek> ScheduleDaysOfWeek { get; set; } = new();

    // ---- Последний запуск ----

    /// <summary>Момент последнего выполнения (локальное время; null — ещё не выполнялся).</summary>
    public DateTime? LastRunAt { get; set; }

    /// <summary>Успешно ли завершился последний запуск.</summary>
    public bool LastRunSuccess { get; set; }

    /// <summary>Сообщение о результате последнего запуска (путь/ошибка, без секретов).</summary>
    public string? LastRunMessage { get; set; }

    /// <summary>Проверка совпадения сценария с исходной базой.</summary>
    public bool MatchesSource(string? infobaseId) =>
        !string.IsNullOrWhiteSpace(infobaseId) &&
        string.Equals(SourceInfobaseId, infobaseId, StringComparison.Ordinal);
}

/// <summary>
/// Результат выполнения сценария «Развернуть тестовую копию» (см.
/// <see cref="Services.TestDeployService"/>): целевая запись списка (с применёнными
/// признаками: имя, группа), журнал шагов и предупреждения. Секреты в журнал
/// не попадают.
/// </summary>
public sealed class TestDeployResult
{
    /// <summary>Успешно ли завершено развёртывание.</summary>
    public bool Success { get; set; }

    /// <summary>Понятное сообщение об ошибке (локаль, без секретов).</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Целевая тестовая запись: существующая (обновлена) или новая (создана).
    /// Вызывающий добавляет/обновляет её в списке и сохраняет.
    /// </summary>
    public Infobase? Target { get; set; }

    /// <summary>Тестовая ИБ создана заново (была отсутствующей).</summary>
    public bool TargetCreated { get; set; }

    /// <summary>Тестовая ИБ существовала и обновлена данными (.dt).</summary>
    public bool TargetUpdated { get; set; }

    /// <summary>Путь группы «Тест» (пусто — добавление отключено или группа не тронута).</summary>
    public string? GroupPath { get; set; }

    /// <summary>Группа «Тест» была создана (новую группу добавляет вызывающий).</summary>
    public bool GroupCreated { get; set; }

    /// <summary>Журнал прогресса (локальные строки шагов).</summary>
    public List<string> Log { get; } = new();

    /// <summary>Предупреждения (например, регламентные задания не удалось выключить).</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>Шаги плана (для предпросмотра/журнала).</summary>
    public IReadOnlyList<TestDeployStep>? Steps { get; set; }
}