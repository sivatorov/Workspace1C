using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Workspace1C.Services;

/// <summary>
/// Результат разбора конфига флагов диагностики <c>trace.json</c> (issue #347, кластер C):
/// признак валидности исходного JSON, словарь «имя флага → значение» и словарь числовых
/// параметров (например <c>CM_UPDATES_PREVIEW_LEN</c>, issue #371). Повреждённый JSON
/// трактуется как «все флаги выключены» (<see cref="IsValid"/>=false), файл при этом НЕ
/// перезаписывается (см. <see cref="TraceFlags"/>).
/// </summary>
internal readonly record struct TraceFlagsParseResult(
    bool IsValid,
    IReadOnlyDictionary<string, bool> Flags,
    IReadOnlyDictionary<string, int> IntValues);

/// <summary>
/// Чистое форматирование/разбор конфига отладочных флагов <c>trace.json</c>
/// (issue #347 «Сказ о trace.json»). Без файлового I/O и платформенных зависимостей —
/// вынесено из <see cref="TraceFlags"/>, чтобы покрыть юнит-тестами:
/// сериализация дефолтов, разбор значений (регистр имён безразличен), игнорирование
/// неизвестных флагов и решение о миграции старого JSONL-журнала.
/// <para>
/// <c>trace.json</c> лежит в каталоге данных АКТИВНОГО ПРОФИЛЯ (рядом с <c>settings.json</c>;
/// до выбора профиля — в корне <see cref="PlatformPaths.AppDataDirectory"/>, с переносом при
/// загрузке настроек профиля) — это JSON-КОНФИГ флагов, а не журнал. Формат (pretty-print,
/// стабильный порядок ключей <c>version</c>, <c>CM_COLUMNS</c>, <c>CM_MENUCLICK</c>,
/// <c>CM_MENUCLOSE</c>, <c>CM_REDIRECT</c>):
/// <code>
/// {
///   "version": 1,
///   "CM_COLUMNS": false,
///   "CM_MENUCLICK": false,
///   "CM_MENUCLOSE": false,
///   "CM_REDIRECT": false
/// }
/// </code>
/// Парсер устойчив и к компактной записи в одну строку (прежний формат). До 0.3.9.315 этот
/// файл был JSONL-журналом событий меню (первая строка <c>{"ts":…</c>) — при первом старте
/// новой версии такой файл переименовывается в <see cref="LegacyJsonlBackupFileName"/>
/// (история диагностики сохраняется), а сам журнал переезжает в
/// <see cref="MenuCloseLogFileName"/> (каталог логов <see cref="PlatformPaths.LogDirectory"/>)
/// при включённом флаге <c>CM_MENUCLOSE</c>.
/// </para>
/// </summary>
internal static class TraceFlagsFormat
{
    /// <summary>Версия схемы конфига флагов.</summary>
    public const int ConfigVersion = 1;

    /// <summary>
    /// Имя конфига флагов (issue #347): <c>trace.json</c> в каталоге данных активного профиля
    /// (тот же каталог, что settings.json; до выбора профиля — корень каталога данных).
    /// С 0.3.9.315 — JSON-объект флагов, а не журнал.
    /// </summary>
    public const string ConfigFileName = "trace.json";

    /// <summary>
    /// Основное имя журнала событий меню (issue #347, замечание 3): <c>trace_menuclose.json</c>
    /// в каталоге логов (<see cref="PlatformPaths.LogDirectory"/> — AppDataDirectory/logs).
    /// «Буква l в конце лишняя» (пользователь про .jsonl). Журнал больше НЕ лежит в
    /// <c>trace.json</c> (этот файл стал конфигом флагов); содержимое — JSON Lines
    /// (одна JSON-запись на строку), расширение .json.
    /// </summary>
    public const string MenuCloseLogFileName = "trace_menuclose.json";

    /// <summary>
    /// Имя резервной копии старого JSONL-журнала (issue #347): <c>trace_menuclose_legacy.json</c>.
    /// При первом старте 0.3.9.315 файл <c>trace.json</c>, первая строка которого начинается
    /// с <c>{"ts":</c> (JSONL-журнал 0.3.9.308–0.3.9.314), переименовывается в этот файл —
    /// история диагностики сохраняется, а <c>trace.json</c> становится конфигом флагов.
    /// </summary>
    public const string LegacyJsonlBackupFileName = "trace_menuclose_legacy.json";

    /// <summary>
    /// Прежнее имя файла трассировки версии 0.3.9.306 (<c>menuclose_trace.json</c>).
    /// Если такой файл уже существует рядом с настройками — журнал продолжает
    /// дописываться в него (непрерывность диагностики), иначе используется
    /// <see cref="MenuCloseLogFileName"/>.
    /// </summary>
    public const string LegacyFileName = "menuclose_trace.json";

    /// <summary>
    /// Известные имена флагов (в верхнем регистре). Порядок определяет стабильную
    /// последовательность ключей в сериализованном конфиге. Неизвестные имена при разборе
    /// игнорируются — устойчивость к будущим версиям (появляется новый флаг — старые
    /// сборки не ломаются, новая сборка добавит его в этот список).
    /// </summary>
    /// <remarks>
    /// С 1.0.0.104 (issue #80 «Настройка логирования») добавлены направления
    /// <c>CM_ON</c> (общий выключатель), <c>CM_MAIN_WINDOW</c>, <c>CM_RAC</c>,
    /// <c>CM_BASE_RUN</c>, <c>CM_BACKUP</c>, <c>CM_IBASES</c>, <c>CM_JOBS</c>,
    /// <c>CM_THEMES</c> и флаг <c>CM_UPDATES</c> (существовал в коде, но не
    /// сериализовался в trace.json). Группы: направления закрепляются после
    /// диагностических флагов, чтобы старый порядок ключей профилей не «прыгал».
    /// </remarks>
    public static readonly string[] KnownFlags =
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
        "CM_ON",
        "CM_MAIN_WINDOW",
        "CM_RAC",
        "CM_BASE_RUN",
        "CM_BACKUP",
        "CM_IBASES",
        "CM_JOBS",
        "CM_THEMES",
        "CM_PUBLISH",
        // Очистка кеша 1С (issue #109, v1.0.1.40): диагностика «нулей».
        "CM_CACHE"
    };

    /// <summary>
    /// Числовой параметр диагностики обновлений (issue #371): длина превью HTML в журнале
    /// <c>CM_UPDATES</c> (сколько символов тела ответа писать в лог). Пользователь просил
    /// «больше 500 символов» и хранить значение в <c>trace.json</c> — параметр читается
    /// оттуда (<see cref="TraceFlags.GetInt"/>), дефолт 500–2000.
    /// </summary>
    public const string UpdatesPreviewLenKey = "CM_UPDATES_PREVIEW_LEN";

    /// <summary>Пустой набор флагов (все выключены) — используется при повреждённом конфиге.</summary>
    public static readonly IReadOnlyDictionary<string, bool> EmptyFlags =
        new Dictionary<string, bool>(StringComparer.Ordinal);

    /// <summary>Пустой набор числовых параметров — используется при повреждённом конфиге.</summary>
    public static readonly IReadOnlyDictionary<string, int> EmptyIntValues =
        new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// Сериализует конфиг флагов в JSON с ПЕРЕНОСАМИ СТРОК И ОТСТУПАМИ (pretty-print,
    /// issue #347, замечание 1) и стабильным порядком ключей: <c>version</c>, затем флаги
    /// в порядке <see cref="KnownFlags"/>. Парсер (<see cref="Parse"/>) устойчив к обоим
    /// форматам записи — и к этому, и к компактному однострочному. Флаги, отсутствующие
    /// в <paramref name="flags"/>, выводятся как <c>false</c> (по умолчанию).
    /// </summary>
    public static string Serialize(IReadOnlyDictionary<string, bool>? flags)
    {
        var sb = new StringBuilder(160);
        sb.Append('{').Append('\n');
        sb.Append("  \"version\": ").Append(ConfigVersion);
        foreach (var flag in KnownFlags)
        {
            var enabled = flags is not null && flags.TryGetValue(flag, out var value) && value;
            sb.Append(",\n  \"").Append(flag).Append("\": ").Append(enabled ? "true" : "false");
        }
        sb.Append('\n').Append('}');
        return sb.ToString();
    }

    /// <summary>Конфиг с дефолтными значениями: все флаги <c>false</c>, версия схемы 1.</summary>
    public static string SerializeDefaults() => Serialize(null);

    /// <summary>
    /// Разбирает JSON-конфиг флагов. Регистр имён безразличен; неизвестные имена
    /// игнорируются; значения не-булева типа игнорируются (флаг остаётся выключенным).
    /// Повреждённый JSON (или не-JSON) не бросает исключение — возвращается
    /// <see cref="TraceFlagsParseResult.IsValid"/>=false с пустым набором флагов.
    /// </summary>
    public static TraceFlagsParseResult Parse(string? json)
    {
        var flags = new Dictionary<string, bool>(StringComparer.Ordinal);
        var intValues = new Dictionary<string, int>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json))
            return new TraceFlagsParseResult(false, flags, intValues);

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return new TraceFlagsParseResult(false, flags, intValues);

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                var name = property.Name.Trim().ToUpperInvariant();

                // Числовой параметр (например CM_UPDATES_PREVIEW_LEN, issue #371):
                // положительное целое — сохраняем; иначе параметр игнорируется (дефолт).
                if (name == UpdatesPreviewLenKey)
                {
                    if (property.Value.ValueKind == JsonValueKind.Number
                        && property.Value.TryGetInt32(out var intValue)
                        && intValue > 0)
                        intValues[name] = intValue;
                    continue;
                }

                if (Array.IndexOf(KnownFlags, name) < 0)
                    continue; // неизвестный флаг — игнорируем (устойчивость к будущим версиям)
                if (property.Value.ValueKind == JsonValueKind.True)
                    flags[name] = true;
                else if (property.Value.ValueKind == JsonValueKind.False)
                    flags[name] = false;
                // Не булево значение — флаг остаётся выключенным (дефолт).
            }
            return new TraceFlagsParseResult(true, flags, intValues);
        }
        catch (JsonException)
        {
            // Повреждённый JSON: все флаги выключены, файл не перезаписывается
            // (решение принимает TraceFlags, здесь — только признак валидности).
            return new TraceFlagsParseResult(false, flags, intValues);
        }
    }

    /// <summary>
    /// Регистронезависимое чтение значения флага из разобранного словаря.
    /// Отсутствующий/неизвестный флаг — <c>false</c> (по умолчанию выключен).
    /// </summary>
    public static bool IsEnabled(IReadOnlyDictionary<string, bool>? flags, string name)
    {
        if (flags is null || string.IsNullOrWhiteSpace(name))
            return false;
        return flags.TryGetValue(name.Trim().ToUpperInvariant(), out var enabled) && enabled;
    }

    /// <summary>
    /// Регистронезависимое чтение числового параметра из разобранного словаря
    /// (например <c>CM_UPDATES_PREVIEW_LEN</c>, issue #371). Отсутствующий/нечисловой
    /// параметр — <c>false</c> (вызывающий использует дефолт).
    /// </summary>
    public static bool TryGetInt(IReadOnlyDictionary<string, int>? values, string name, out int value)
    {
        value = 0;
        if (values is null || string.IsNullOrWhiteSpace(name))
            return false;
        return values.TryGetValue(name.Trim().ToUpperInvariant(), out value);
    }

    /// <summary>
    /// Решение о миграции старого JSONL-журнала (issue #347): если первая непустая строка
    /// файла <c>trace.json</c> начинается с <c>{"ts":</c> — файл является прежним журналом
    /// 0.3.9.308–0.3.9.314 и должен быть переименован в <see cref="LegacyJsonlBackupFileName"/>.
    /// Конфиг-формат (JSON-объект) таким признаком не обладает — миграция не выполняется.
    /// </summary>
    public static bool ShouldMigrateLegacyJsonl(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return false;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;
            return line.StartsWith("{\"ts\":", StringComparison.Ordinal);
        }
        return false;
    }

    /// <summary>
    /// Выбор имени файла журнала событий меню: при наличии legacy-файла
    /// <c>menuclose_trace.json</c> от 0.3.9.306 журнал дописывается в него (непрерывность
    /// диагностики), иначе — основной <c>trace_menuclose.json</c> в каталоге логов
    /// (<see cref="PlatformPaths.LogDirectory"/>). Чистая функция выбора имени для
    /// юнит-тестов (полный путь строит <see cref="MenuCloseTrace"/>).
    /// </summary>
    public static string ResolveMenuCloseFileName(bool legacyExists)
        => legacyExists ? LegacyFileName : MenuCloseLogFileName;
}