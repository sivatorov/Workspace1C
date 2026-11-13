using System;
using System.Collections.Generic;

namespace Workspace1C.Services;

/// <summary>
/// Направления (категории) логирования приложения (issue #80 «Настройка логирования»).
/// Каждая категория — отдельный флаг в конфиге <c>trace.json</c> (<see cref="TraceFlags"/>).
/// Правила (см. <see cref="TraceFlags.IsLogCategoryEnabled"/>):
/// <list type="bullet">
/// <item><description>по умолчанию ВСЕ категории выключены (как и общий выключатель
/// <see cref="MasterOn"/> «CM_ON»);</description></item>
/// <item><description>INFO-записи направления пишутся в общий журнал приложения
/// (<see cref="FileAppLogger"/>, <c>logs/app-YYYYMMDD.log</c>) только если категория
/// включена И включён общий выключатель <see cref="MasterOn"/>;</description></item>
/// <item><description>ОШИБКИ (ERROR) и ПРЕДУПРЕЖДЕНИЯ (WARN) не зависят от категорий и
/// пишутся всегда — важные сбои остаются видимыми по умолчанию.</description></item>
/// </list>
/// Изменения настроек категорий попадают в <c>trace.json</c> через
/// <see cref="TraceFlags.SaveCategorySettings"/> — файл перечитывается без перезапуска
/// приложения (конфиг уже умеет это делать, issue #347).
/// </summary>
public static class LogCategory
{
    /// <summary>Общий выключатель логирования по направлениям (issue #80): «CM_ON».
    /// Выключен по умолчанию. Пока он выключен — INFO-записи направлений в журнал
    /// не пишутся (даже если отдельная категория включена). Ошибки/предупреждения
    /// и диагностические флаги trace.json (CM_COLUMNS и др.) от него не зависят.</summary>
    public const string MasterOn = "CM_ON";

    /// <summary>CM_MAIN_WINDOW: запуск/завершение приложения, инициализация
    /// главного окна и MainViewModel.</summary>
    public const string MainWindow = "CM_MAIN_WINDOW";

    /// <summary>CM_RAC: события клиента RAC (оснастка/команды rac). Много данных —
    /// обычно включается только при настройке оснастки.</summary>
    public const string Rac = "CM_RAC";

    /// <summary>CM_BASE_RUN: запуск информационных баз (1С:Предприятие,
    /// Конфигуратор, тонкий/веб-клиент, ссылки, CLI/проводник).</summary>
    public const string BaseRun = "CM_BASE_RUN";

    /// <summary>CM_BACKUP: создание резервных копий (сценарии резервирования,
    /// выгрузка ibcmd, копирование и ротация копий).</summary>
    public const string Backup = "CM_BACKUP";

    /// <summary>CM_IBASES: импорт/экспорт списка баз в ibases.v8i.</summary>
    public const string Ibases = "CM_IBASES";

    /// <summary>CM_JOBS: задания по расписанию (планировщик, догоняющее
    /// выполнение, фоновые задания расписания).</summary>
    public const string Jobs = "CM_JOBS";

    /// <summary>CM_THEMES: применение тем оформления (отладочные записи
    /// переключения схемы/палитры).</summary>
    public const string Themes = "CM_THEMES";

    /// <summary>CM_PUBLISH: окно «Публикация на веб-сервере» (issue #63, комментарий
    /// 7OH 2026-10-28: «давайте отладку и тут включать подробную») — найденные
    /// инстансы Apache, include-граф конфигурации и файлы алиасов 1С в списке
    /// webinst, порядок fallback-ветки (почему инстансы не найдены/файл не попал
    /// в список). Выключено по умолчанию, как все категории.</summary>
    public const string Publish = "CM_PUBLISH";

    /// <summary>CM_CACHE: очистка кеша 1С (issue #109): проверенные корни кеша
    /// (включая дополнительные из настроек и легаси 1Cv82), число найденных
    /// каталогов кеша баз и результаты очистки — для диагностики «нулей».</summary>
    public const string Cache = "CM_CACHE";

    /// <summary>Все категории направлений вкладки «Логирование» (порядок вывода)
    /// — в терминах issue #80. Существовавшие до неё диагностические флаги
    /// (CM_COLUMNS, CM_MENUCLOSE и др.) выводятся отдельной группой «Диагностика».</summary>
    public static readonly string[] DirectionCategories =
    {
        MainWindow,
        Rac,
        BaseRun,
        Backup,
        Ibases,
        Jobs,
        Themes,
        Publish,
        Cache
    };

    /// <summary>
    /// Диагностические флаги trace.json, существовавшие до issue #80 (issue #347
    /// и следующие): выводятся во вкладке «Логирование» группой «Диагностика»,
    /// чтобы все места логирования были видны и управляемы в одном месте.
    /// </summary>
    public static readonly string[] DiagnosticCategories =
    {
        "CM_COLUMNS",
        "CM_MENUCLICK",
        "CM_MENUCLOSE",
        "CM_REDIRECT",
        "CM_TREESWITCH",
        "CM_TREESWITCH_FLUSH",
        "CM_REVEAL",
        "CM_CLUSTERIMPORT",
        "CM_UPDATES",
        // «Считать версию прошлой» (issue #129): виртуальное снижение версии для
        // проверки «Что нового» сразу, не дожидаясь выпуска релиза. Флаг хранится
        // в trace.json как остальные диагностические флаги и автоматически
        // сбрасывается после успешного обновления (EffectiveVersionService).
        "CM_PREV_VERSION"
    };

    /// <summary>
    /// Все известные категории конфига <c>trace.json</c> (направления + диагностика).
    /// Порядок: направления, затем диагностические флаги.
    /// </summary>
    public static readonly string[] AllCategories = Combine(DirectionCategories, DiagnosticCategories);

    /// <summary>Ключ локализации краткого названия категории (используется
    /// вкладкой «Логирование»). Ключи вида «Settings.Logging.Cat<Имя>».</summary>
    public static string TitleKey(string category) => "Settings.Logging.Cat" + NormalizeKey(category);

    /// <summary>Ключ локализации пояснения категории (используется вкладкой
    /// «Логирование»). Ключи вида «Settings.Logging.Cat<Имя>Desc».</summary>
    public static string DescriptionKey(string category) => TitleKey(category) + "Desc";

    private static string[] Combine(string[] first, string[] second)
    {
        var result = new string[first.Length + second.Length];
        Array.Copy(first, 0, result, 0, first.Length);
        Array.Copy(second, 0, result, first.Length, second.Length);
        return result;
    }

    private static string NormalizeKey(string category)
    {
        // CM_MAIN_WINDOW -> CatMainWindow; CM_TREESWITCH_FLUSH -> CatTreeswitchFlush.
        var text = (category ?? string.Empty).Trim();
        if (text.StartsWith("CM_", StringComparison.Ordinal))
            text = text.Substring(3);
        var parts = text.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new System.Text.StringBuilder();
        foreach (var part in parts)
        {
            var lower = part.ToLowerInvariant();
            if (lower.Length == 0)
                continue;
            sb.Append(char.ToUpperInvariant(lower[0])).Append(lower.Length > 1 ? lower.Substring(1) : string.Empty);
        }
        var result = sb.ToString();
        return result.Length > 0 ? result : "Unknown";
    }
}
