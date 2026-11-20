using System.Collections.Generic;

namespace Workspace1C.Services.Licensing;

/// <summary>
/// Функции редакции ПРО (2.x, docs/pro/PRO-EDITION.md). Единый перечень: по нему строятся
/// отметки «ПРО» в меню, проверки в коде и текст окна «Лицензия».
/// Значения сохраняются только в памяти — порядок и имена можно менять без миграций.
/// </summary>
public enum ProFeature
{
    // ---- Инструменты администратора, бесплатные в 1.0.x ----

    /// <summary>Мониторинг серверов и оповещения.</summary>
    ServerAlerts,

    /// <summary>Команды Telegram-бота (/status, /tasks, /run, /alerts).</summary>
    TelegramBot,

    /// <summary>MCP-сервер для ИИ-агентов (<c>--mcp</c>).</summary>
    Mcp,

    /// <summary>Миграция баз между кластерами 1С.</summary>
    ClusterMigration,

    /// <summary>Тестовый контур: «Развернуть тестовую копию».</summary>
    TestDeploy,

    /// <summary>Массовая загрузка конфигурации из .cf по списку баз.</summary>
    MassLoadCf,

    // ---- Новые функции 2.x (модули из приватного репозитория Workspace1C.Pro) ----

    /// <summary>Конвейеры обслуживания из готовых шагов.</summary>
    Pipelines,

    /// <summary>Консоль запросов по нескольким базам.</summary>
    MultiBaseQuery,

    /// <summary>Сравнение окружения двух баз (пользователи, роли, расширения, регламенты).</summary>
    EnvironmentDiff,

    /// <summary>Автотесты (Vanessa Automation / YAXUnit) в тестовом контуре.</summary>
    AutoTests,

    /// <summary>Тренды производительности по технологическому журналу.</summary>
    PerformanceTrends,

    /// <summary>Журнал аудита действий программы.</summary>
    AuditLog,

    /// <summary>Резервные копии в S3-совместимые хранилища и шифрование копий.</summary>
    CloudBackup
}

/// <summary>Метаданные функций ПРО: ключи локализации названий.</summary>
public static class ProFeatures
{
    private static readonly HashSet<ProFeature> ImplementedFeatures = new();
    private static readonly object Sync = new();

    /// <summary>Все функции ПРО в порядке показа в окне «Лицензия».</summary>
    public static IReadOnlyList<ProFeature> All { get; } = (ProFeature[])System.Enum.GetValues(typeof(ProFeature));

    /// <summary>Ключ локализации названия функции: <c>Pro.Feature.&lt;Имя&gt;</c>.</summary>
    public static string TitleKey(ProFeature feature) => "Pro.Feature." + feature;

    /// <summary>
    /// Функция реализована в публичном коде (была в 1.0.x). Остальные существуют только
    /// в официальной сборке с модулями из приватного репозитория.
    /// </summary>
    public static bool IsInPublicCore(ProFeature feature) => feature <= ProFeature.MassLoadCf;

    /// <summary>
    /// Отмечает функцию как реализованную. Вызывается модулем ПРО при регистрации
    /// (<c>RegisterProModules</c>): в бесплатной сборке без модуля функция остаётся
    /// «в разработке». Повторный вызов идемпотентен.
    /// </summary>
    public static void MarkImplemented(ProFeature feature)
    {
        lock (Sync)
            ImplementedFeatures.Add(feature);
    }

    /// <summary>Сбрасывает реестр реализованных функций (тесты).</summary>
    internal static void ResetImplemented()
    {
        lock (Sync)
            ImplementedFeatures.Clear();
    }

    /// <summary>
    /// Функция реализована: публичное ядро (было в 1.0.x) или модуль ПРО,
    /// отметивший себя через <see cref="MarkImplemented"/>. Не влияет на
    /// <see cref="EditionService.Has"/>: реализованность и доступность по лицензии —
    /// разные признаки.
    /// </summary>
    public static bool IsImplemented(ProFeature feature)
    {
        if (IsInPublicCore(feature))
            return true;
        lock (Sync)
            return ImplementedFeatures.Contains(feature);
    }

    /// <summary>
    /// Ключ локализации статуса функции: «доступна» (реализована) или
    /// «в разработке» (пока только в перечне).
    /// </summary>
    public static string StatusKey(ProFeature feature) =>
        IsImplemented(feature) ? "Pro.FeatureStatus.Available" : "Pro.FeatureStatus.InDevelopment";

    /// <summary>
    /// Ссылка на раздел «ПРО-версия» README репозитория (справочная кнопка
    /// «Подробнее в README» окна «Как купить ПРО-версию»).
    /// </summary>
    public const string BuyUrl = "https://github.com/sivatorov/Workspace1C#-про-версия";

    /// <summary>
    /// Telegram-канал проекта (контакты покупки: кнопка «Открыть Telegram»
    /// окна «Как купить ПРО-версию»). Личных реквизитов оплаты в приложении нет —
    /// оплата обсуждается в канале.
    /// </summary>
    public const string TelegramUrl = "https://t.me/Workspace1C";

    /// <summary>
    /// Issues репозитория (контакты покупки: кнопка «Открыть Issues»
    /// окна «Как купить ПРО-версию»).
    /// </summary>
    public const string IssuesUrl = "https://github.com/sivatorov/Workspace1C/issues";
}
