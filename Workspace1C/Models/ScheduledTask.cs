using System.Linq;

namespace Workspace1C.Models;

/// <summary>
/// Тип задания по расписанию (issue #286).
/// </summary>
public enum ScheduledTaskKind
{
    /// <summary>Резервная копия ИБ по существующему сценарию резервирования.</summary>
    Backup,

    /// <summary>Обновление конфигурации ИБ из файла .cf (/LoadCfg + /UpdateDBCfg).</summary>
    UpdateConfig,

    /// <summary>Связка «Копия → обновление»: сначала резервная копия, затем обновление конфигурации.</summary>
    BackupThenUpdateConfig,

    /// <summary>Проверка целостности файловой ИБ (<c>chdbfl</c>), тихий запуск с ожиданием (0.3.9.87).</summary>
    CheckIntegrity,

    /// <summary>Обновление самого приложения через GitHub Releases (без диалога).</summary>
    UpdateApp,

    /// <summary>
    /// Проверка наличия обновления технологической платформы 1С в каталоге
    /// <c>releases.1c.ru</c> по выражению проверки («8.5.*», «8.3.27.*») — без
    /// установки, только уведомление о найденных версиях (issue #366).
    /// </summary>
    PlatformUpdateCheck,

    /// <summary>
    /// Проверка наличия обновления конфигурации 1С в каталоге
    /// <c>releases.1c.ru</c> для выбранной конфигурации: максимальная установленная
    /// версия по списку баз сравнивается с доступными релизами — без установки,
    /// только уведомление о версиях выше установленной (issue #367).
    /// </summary>
    ConfigurationUpdateCheck,

    /// <summary>
    /// Установка (обновление) расширения конфигурации из файла .cfe в базу
    /// (<c>/LoadCfg -Extension</c> + <c>/UpdateDBCfg -Extension</c>, выпуск 1.0.0.69).
    /// Путь к файлу — в <see cref="ScheduledTask.ConfigFilePath"/>, имя расширения —
    /// имя файла без расширения.
    /// </summary>
    InstallExtension,

    /// <summary>
    /// Выгрузка конфигурации базы в XML и коммит в Git по настройкам окна
    /// «Версионирование в Git» (выпуск 1.0.0.69).
    /// </summary>
    GitCommitConfig,

    /// <summary>
    /// Резервная копия клиент-серверной базы средствами СУБД (pg_dump / BACKUP DATABASE)
    /// по профилю окна «Резервные копии СУБД» (выпуск 1.0.0.69).
    /// </summary>
    DbmsBackup,

    /// <summary>
    /// Двусторонняя синхронизация своих настроек между компьютерами (п.9, 1.0.0.91):
    /// выполняется <see cref="Services.Sync.SettingsSyncRunner"/> по настройкам окна
    /// «Синхронизация настроек» (settings_sync.json). Дополнительный способ запуска
    /// к интервальному таймеру окна — задание планировщика в удобное время.
    /// </summary>
    SettingsSync,

    /// <summary>
    /// Развёртывание тестовой копии (п.10, 1.0.0.92): сценарий
    /// <see cref="TestDeployScenario"/> из <c>test_deploy.json</c>, ссылка — в
    /// <see cref="ScheduledTask.ScenarioId"/>. Например, «каждый понедельник обновлять
    /// тест из прода». Источник — база сценария, выполняет
    /// <see cref="Services.TestDeployService"/>.
    /// </summary>
    TestDeploy,

    /// <summary>
    /// Конвейер обслуживания (ПРО 2.x): имя конвейера — в
    /// <see cref="ScheduledTask.ScenarioId"/>, исполнитель регистрируется модулем
    /// <c>Pipelines</c> через <see cref="Services.SchedulerExecutors"/>.
    /// </summary>
    Pipeline,

    /// <summary>
    /// Автотесты в тестовом контуре (ПРО 2.x): Vanessa Automation / YAXUnit,
    /// исполнитель регистрируется модулем <c>AutoTests</c>.
    /// </summary>
    AutoTests,

    /// <summary>
    /// Снимок трендов производительности по ТЖ (ПРО 2.x): дневная сводка по базам,
    /// исполнитель регистрируется модулем <c>PerformanceTrends</c>.
    /// </summary>
    PerformanceSnapshot
}

/// <summary>
/// Канал уведомления заданий-проверок («Проверить наличие обновления платформы» —
/// issue #366, «Проверить наличие обновления конфигурации» — issue #367):
/// куда выводится сводка о найденных версиях.
/// </summary>
public enum PlatformUpdateNotifyChannel
{
    /// <summary>Сообщение в строке состояния главного окна приложения.</summary>
    MainWindow,

    /// <summary>Системное уведомление ОС (balloon трея на Windows, notify-send на Linux).</summary>
    SystemNotifications
}

/// <summary>
/// Единица измерения интервала повтора задания (issue #366/#367, комментарий 7OH:
/// «как в 1С — каждые х секунд, а дальше каждый сам поставит сколько надо —
/// час/два/10 минут»).
/// </summary>
public enum ScheduledTaskRepeatUnit
{
    /// <summary>Секунды.</summary>
    Seconds,

    /// <summary>Минуты.</summary>
    Minutes,

    /// <summary>Часы.</summary>
    Hours,

    /// <summary>Дни.</summary>
    Days
}

/// <summary>
/// Задание по расписанию: тип действия, целевая ИБ, время и дни недели запуска.
/// Хранится в отдельном JSON-файле (см. Services.ScheduledTaskStore).
/// Выполняется фоновым планировщиком, пока приложение запущено.
/// </summary>
public class ScheduledTask
{
    /// <summary>Идентификатор задания (GUID).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Наименование задания.</summary>
    public string Name { get; set; } = "";

    /// <summary>Тип выполняемого действия.</summary>
    public ScheduledTaskKind Kind { get; set; } = ScheduledTaskKind.Backup;

    /// <summary>Включено ли задание (выключенные пропускаются планировщиком).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Время запуска в формате «HH:mm». Невалидное значение трактуется как 00:00.
    /// </summary>
    public string Time { get; set; } = "02:00";

    /// <summary>
    /// Дни недели выполнения. Пустой список означает «ежедневно».
    /// </summary>
    public List<DayOfWeek> DaysOfWeek { get; set; } = new();

    /// <summary>Идентификатор сценария резервирования (для Backup / BackupThenUpdateConfig).</summary>
    public string? ScenarioId { get; set; }

    /// <summary>Идентификатор целевой ИБ (для Backup / UpdateConfig / BackupThenUpdateConfig).</summary>
    public string? InfobaseId { get; set; }

    /// <summary>Путь к файлу .cf с новой конфигурацией (для UpdateConfig / BackupThenUpdateConfig).</summary>
    public string? ConfigFilePath { get; set; }

    /// <summary>
    /// Выражение проверки платформы (для PlatformUpdateCheck, issue #366): сегменты —
    /// числа или «*», например «8.5.*» или «8.3.27.*». Пусто/невалидно — задание
    /// завершается ошибкой валидации.
    /// </summary>
    public string? PlatformCheckExpression { get; set; }

    /// <summary>
    /// Учитывать ли тестовые (предрелизные) сборки платформы при проверке
    /// (для PlatformUpdateCheck, issue #366). По умолчанию false.
    /// </summary>
    public bool PlatformCheckIncludeTestVersions { get; set; }

    /// <summary>
    /// Канал уведомления о результате проверки платформы (для PlatformUpdateCheck,
    /// issue #366). По умолчанию — строка состояния главного окна.
    /// </summary>
    public PlatformUpdateNotifyChannel PlatformNotifyChannel { get; set; } = PlatformUpdateNotifyChannel.MainWindow;

    /// <summary>
    /// Проверяемая конфигурация (для ConfigurationUpdateCheck, issue #367): имя
    /// конфигурации из списка баз (или введённое вручную). Пусто — задание
    /// завершается ошибкой валидации.
    /// </summary>
    public string? ConfigCheckConfiguration { get; set; }

    /// <summary>
    /// Канал уведомления о результате проверки конфигурации (для
    /// ConfigurationUpdateCheck, issue #367): общий enum каналов из issue #366.
    /// По умолчанию — строка состояния главного окна.
    /// </summary>
    public PlatformUpdateNotifyChannel ConfigCheckNotifyChannel { get; set; } = PlatformUpdateNotifyChannel.MainWindow;

    /// <summary>
    /// Повторять ли задание с интервалом (issue #366/#367): при включённом повторе
    /// задание выполняется каждые <see cref="RepeatIntervalValue"/>
    /// <see cref="RepeatIntervalUnit"/>, начиная с <see cref="Time"/> в рамках
    /// выбранных дней недели. По умолчанию выключено (одно выполнение в момент).
    /// </summary>
    public bool RepeatEnabled { get; set; }

    /// <summary>Значение интервала повтора (положительное число; по умолчанию 60).</summary>
    public int RepeatIntervalValue { get; set; } = 60;

    /// <summary>Единица интервала повтора (по умолчанию — минуты).</summary>
    public ScheduledTaskRepeatUnit RepeatIntervalUnit { get; set; } = ScheduledTaskRepeatUnit.Minutes;

    /// <summary>
    /// Уведомлять ли при ОТСУТСТВИИ обновлений платформы (issue #366): при выключенной
    /// опции задание с обновлённой «до актуальной» версией не показывает пустое окно/
    /// уведомление — результат всё равно записывается в <see cref="LastRunMessage"/>.
    /// По умолчанию false.
    /// </summary>
    public bool PlatformCheckNotifyWhenNoUpdates { get; set; }

    /// <summary>
    /// Уведомлять ли при ОТСУТСТВИИ обновлений конфигурации (issue #367): при выключенной
    /// опции задание без доступных версий не уведомляет (результат пишется в
    /// <see cref="LastRunMessage"/>). По умолчанию false.
    /// </summary>
    public bool ConfigCheckNotifyWhenNoUpdates { get; set; }

    /// <summary>
    /// Момент последнего запуска задания (null — ещё не выполнялось).
    /// Локальное время, используется для отображения в списке заданий.
    /// </summary>
    public DateTime? LastRunAt { get; set; }

    /// <summary>
    /// Момент последнего запуска задания в UTC (функция №7 «догоняющее выполнение»).
    /// Дублирует <see cref="LastRunAt"/> в UTC, чтобы сравнивать с плановым временем
    /// без зависимости от часового пояса машины. null — ещё не выполнялось; у старых
    /// сохранённых заданий поле отсутствует, и догоняние опирается на LastRunAt.
    /// </summary>
    public DateTime? LastRunUtc { get; set; }

    /// <summary>Успешно ли завершился последний запуск.</summary>
    public bool LastRunSuccess { get; set; }

    /// <summary>Сообщение о результате последнего запуска (путь/ошибка).</summary>
    public string? LastRunMessage { get; set; }

    /// <summary>Подходит ли день недели под расписание (пустой список — любой день).</summary>
    public bool MatchesDay(DayOfWeek day) => DaysOfWeek.Count == 0 || DaysOfWeek.Contains(day);

    /// <summary>Время запуска как TimeSpan («HH:mm»), при ошибке парсинга — 00:00.</summary>
    public TimeSpan GetTime() => TimeSpan.TryParse(Time, out var t) ? t : TimeSpan.Zero;

    /// <summary>Интервал повтора как <see cref="TimeSpan"/> (при выключенном повторе — нулевой).</summary>
    public TimeSpan GetRepeatInterval() => GetRepeatInterval(RepeatIntervalValue, RepeatIntervalUnit);

    /// <summary>Интервал повтора из значения и единицы; значение ≤ 0 — нулевой интервал.</summary>
    public static TimeSpan GetRepeatInterval(int value, ScheduledTaskRepeatUnit unit)
    {
        if (value <= 0)
            return TimeSpan.Zero;
        return unit switch
        {
            ScheduledTaskRepeatUnit.Seconds => TimeSpan.FromSeconds(value),
            ScheduledTaskRepeatUnit.Hours => TimeSpan.FromHours(value),
            ScheduledTaskRepeatUnit.Days => TimeSpan.FromDays(value),
            _ => TimeSpan.FromMinutes(value)
        };
    }

    /// <summary>
    /// Создаёт копию задания с новым <see cref="Id"/> (issue #370): копируются все
    /// настройки задания, история последнего запуска (<see cref="LastRunAt"/>,
    /// <see cref="LastRunUtc"/>, <see cref="LastRunSuccess"/>,
    /// <see cref="LastRunMessage"/>) сбрасывается — копия ещё не выполнялась.
    /// Имя задаётся вызывающим (например, «{оригинал} — копия»).
    /// </summary>
    public ScheduledTask CreateCopy(string name)
    {
        return new ScheduledTask
        {
            Name = name,
            Enabled = Enabled,
            Kind = Kind,
            Time = Time,
            DaysOfWeek = new List<DayOfWeek>(DaysOfWeek),
            ScenarioId = ScenarioId,
            InfobaseId = InfobaseId,
            ConfigFilePath = ConfigFilePath,
            PlatformCheckExpression = PlatformCheckExpression,
            PlatformCheckIncludeTestVersions = PlatformCheckIncludeTestVersions,
            PlatformNotifyChannel = PlatformNotifyChannel,
            ConfigCheckConfiguration = ConfigCheckConfiguration,
            ConfigCheckNotifyChannel = ConfigCheckNotifyChannel,
            RepeatEnabled = RepeatEnabled,
            RepeatIntervalValue = RepeatIntervalValue,
            RepeatIntervalUnit = RepeatIntervalUnit,
            PlatformCheckNotifyWhenNoUpdates = PlatformCheckNotifyWhenNoUpdates,
            ConfigCheckNotifyWhenNoUpdates = ConfigCheckNotifyWhenNoUpdates
        };
    }
}