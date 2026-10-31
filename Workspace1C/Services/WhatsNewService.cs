using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Диалог «Что нового» после обновления (v100112, F3): решает, показывать ли при старте
/// компактный диалог со сводкой изменений релиза, и помечает показанную версию в
/// настройках (<see cref="AppSettings.LastWhatsNewVersion"/>). Сводка — краткий
/// захардкоженный текст текущего релиза (словарь версия → текст), локализованный
/// ключами <c>WhatsNew.<версия без точек>.*</c>; для версии без готовой сводки
/// диалог не показывается (не мешает при ручных/тестовых сборках). Показ отключается
/// настройкой <see cref="AppSettings.ShowWhatsNewOnStart"/>. Чистый сервис — без
/// UI-зависимостей.
/// </summary>
public static class WhatsNewService
{
    /// <summary>Ссылка на CHANGELOG репозитория (кнопка диалога «Открыть CHANGELOG на GitHub»).</summary>
    public const string ChangelogUrl = "https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md";

    /// <summary>Текущая версия приложения (без суффикса хеша, см. <see cref="VersionInfo.Display"/>).</summary>
    public static string CurrentVersion => VersionInfo.Display();

    /// <summary>
    /// Нужно ли показать диалог «Что нового» при старте: включено в настройках, версия
    /// изменилась с прошлого показа (<paramref name="settings"/>.<see cref="AppSettings.LastWhatsNewVersion"/>)
    /// И для текущей версии есть готовая сводка (<see cref="HasEntry"/>).
    /// </summary>
    public static bool ShouldShow(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.ShowWhatsNewOnStart)
            return false;
        if (string.Equals(settings.LastWhatsNewVersion, CurrentVersion, StringComparison.Ordinal))
            return false;
        return HasEntry(CurrentVersion);
    }

    /// <summary>Помечает текущую версию как «диалог уже показан» (без записи файла — сохраняет вызывающий).</summary>
    public static void MarkShown(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.LastWhatsNewVersion = CurrentVersion;
    }

    /// <summary>Есть ли захардкоженная сводка изменений для версии <paramref name="version"/>.</summary>
    public static bool HasEntry(string version) => KnownVersions.Contains(Normalize(version));

    /// <summary>
    /// Краткая сводка изменений версии (локализованные пункты через ключи
    /// <c>WhatsNew.<версия без точек>.ItemN</c>); null — для версии нет сводки.
    /// </summary>
    public static IReadOnlyList<string>? GetReleaseNotes(string version)
    {
        var normalized = Normalize(version);
        if (!KnownVersions.Contains(normalized))
            return null;
        var items = new List<string>();
        for (var i = 1; i <= MaxItems; i++)
        {
            var text = Localization.LocalizationManager.T($"WhatsNew.{normalized}.Item{i}");
            if (string.IsNullOrWhiteSpace(text) || text.StartsWith("WhatsNew.", StringComparison.Ordinal))
                break;
            items.Add(text);
        }
        return items.Count > 0 ? items : null;
    }

    /// <summary>Версия в ключе локализации: точки заменяются подчёркиваниями («1.0.1.12» → «1_0_1_12»).</summary>
    internal static string Normalize(string version) =>
        (version ?? string.Empty).Trim().Replace('.', '_');

    /// <summary>Версии с готовой сводкой «Что нового» (в порядке выпуска).</summary>
    private static readonly HashSet<string> KnownVersions = new(StringComparer.Ordinal)
    {
        // Релиз v100112 (план plan-v100112.md, раздел «Фичи-предложения»): сводка — F1–F3.
        "1_0_1_12"
    };

    /// <summary>Максимум пунктов сводки одного релиза.</summary>
    private const int MaxItems = 10;
}
