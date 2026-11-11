using System.IO;
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
    /// И для текущей версии есть сводка — захардкоженная (<see cref="HasEntry"/>) или
    /// локальная секция CHANGELOG.md рядом с exe (<see cref="HasChangelogSection"/>,
    /// issue #116 v1.0.1.54). Если сводку получить не удалось, диалог при старте
    /// не показывается вовсе (окно никогда не открывается с пустым содержимым).
    /// </summary>
    public static bool ShouldShow(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.ShowWhatsNewOnStart)
            return false;
        if (string.Equals(settings.LastWhatsNewVersion, CurrentVersion, StringComparison.Ordinal))
            return false;
        return HasEntry(CurrentVersion) || HasChangelogSection(CurrentVersion);
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

    /// <summary>Кэш body GitHub-релиза текущей сессии (ключ — нормализованная версия).</summary>
    private static readonly Dictionary<string, string> GithubBodyCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Кэш локальных CHANGELOG-секций текущей сессии (ключ — нормализованная версия;
    /// null означает «секции нет», тоже кэшируется — файл не перечитывается).
    /// </summary>
    private static readonly Dictionary<string, string?> ChangelogSectionCache =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Есть ли локальная секция CHANGELOG.md для версии <paramref name="version"/>
    /// (файл ищется рядом с exe; issue #116 v1.0.1.54 — fallback «пустого списка
    /// изменений после обновления»).
    /// </summary>
    public static bool HasChangelogSection(string version) =>
        TryGetChangelogSection(version) is not null;

    /// <summary>
    /// Локальная секция CHANGELOG.md для версии <paramref name="version"/>: текст
    /// между заголовком «## [<версия>]» и следующим заголовком «## »
    /// (issue #116 v1.0.1.54). Файл ищется рядом с exe
    /// (<see cref="AppContext.BaseDirectory"/>/CHANGELOG.md); результат кэшируется
    /// на сессию (в т.ч. «секции нет»). null — файл не найден или секции нет.
    /// </summary>
    public static string? TryGetChangelogSection(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;
        var normalized = Normalize(version);
        if (ChangelogSectionCache.TryGetValue(normalized, out var cached))
            return cached;
        string? section;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md");
            section = File.Exists(path)
                ? ParseChangelogSection(File.ReadAllText(path), version)
                : null;
        }
        catch
        {
            // Чтение CHANGELOG — вспомогательный fallback: сбой не роняет вызывающего.
            section = null;
        }
        ChangelogSectionCache[normalized] = section;
        return section;
    }

    /// <summary>
    /// Извлекает секцию версии из текста CHANGELOG.md (чистая функция, для тестов):
    /// заголовок «## [1.0.1.54] — дата», тело — до следующего «## ». Возвращает
    /// trimmed-текст или null, если заголовок не найден или секция пустая.
    /// </summary>
    internal static string? ParseChangelogSection(string? markdown, string version)
    {
        if (string.IsNullOrWhiteSpace(markdown) || string.IsNullOrWhiteSpace(version))
            return null;
        var lines = markdown.Split('\n');
        var headerPrefix = "## [" + version.Trim() + "]";
        var start = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith(headerPrefix, StringComparison.Ordinal))
            {
                start = i;
                break;
            }
        }
        if (start < 0)
            return null;
        var body = new List<string>();
        for (var i = start + 1; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("## ", StringComparison.Ordinal))
                break;
            body.Add(lines[i].TrimEnd('\r'));
        }
        var text = string.Join("\n", body).Trim();
        return text.Length > 0 ? text : null;
    }

    /// <summary>
    /// Issue #116 п.1: fallback «Что нового» — body GitHub-релиза текущей версии,
    /// когда в <see cref="KnownVersions"/> нет готовой сводки. Тег ищется в двух
    /// распространённых форматах («v1.0.1.49» и «1.0.1.49»). Успешный результат
    /// кэшируется на время сессии; при любой ошибке возвращает null.
    /// </summary>
    public static async Task<string?> GetGithubReleaseNotesAsync(string version, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;
        var normalized = Normalize(version);
        if (GithubBodyCache.TryGetValue(normalized, out var cached))
            return cached;

        var service = new GitHubReleaseService();
        foreach (var tag in new[] { "v" + version.Trim(), version.Trim() })
        {
            var notes = await service.GetReleaseNotesByTagAsync(tag, ct).ConfigureAwait(false);
            var body = notes?.Body?.Trim();
            if (string.IsNullOrWhiteSpace(body))
                continue;
            GithubBodyCache[normalized] = body;
            return body;
        }
        return null;
    }
}
