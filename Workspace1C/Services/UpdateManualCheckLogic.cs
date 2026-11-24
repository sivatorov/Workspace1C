using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// План ручной проверки обновлений (issue #155). Отделён от UI, чтобы одинаково
/// применяться в WPF и Avalonia и проверяться юнит-тестами.
/// </summary>
/// <param name="HasNewerRelease">Найден релиз новее текущей (эффективной) версии.</param>
/// <param name="WasSkipped">Релиз ранее пропущен пользователем («Пропустить эту версию»).</param>
/// <param name="ShowDialog">Нужно показать диалог обновления.</param>
/// <param name="InstallAutomatically">
/// Нужно применить обновление без диалога (только режим «Обновлять при запуске»
/// и только если версия НЕ была пропущена).
/// </param>
public readonly record struct ManualUpdateCheckPlan(
    bool HasNewerRelease,
    bool WasSkipped,
    bool ShowDialog,
    bool InstallAutomatically);

/// <summary>
/// Чистая логика ручной проверки обновлений и семантики пропуска версии (issue #155).
/// <para>
/// Ключевое правило: «Пропустить эту версию» влияет только на фоновые проверки.
/// Ручная проверка (кнопка/значок «Обновить» в панели статуса, меню «Проверить
/// обновления») обязана снова показать диалог, даже если версия была пропущена:
/// пользователь мог передумать. В режиме «Обновлять при запуске» пропущенная версия
/// тоже не ставится молча — показывается диалог с выбором «Обновить»/«Пропустить».
/// </para>
/// </summary>
public static class UpdateManualCheckLogic
{
    /// <summary>
    /// Считается ли релиз пропущенным относительно сохранённой версии.
    /// Пропуск действует, пока релиз НЕ новее пропущенной версии (равен или старше).
    /// Нераспознанная сохранённая версия ничего не блокирует (issue #155): раньше
    /// битое значение в настройках скрывало ЛЮБОЕ обновление, потому что
    /// <c>!IsNewerThan</c> для нераспознанной версии давал <c>true</c>.
    /// </summary>
    public static bool IsSkipped(ReleaseInfo? release, string? skippedVersion)
    {
        if (release is null || string.IsNullOrWhiteSpace(skippedVersion))
            return false;

        return GitHubReleaseService.TryCompareVersions(release, skippedVersion, out var comparison)
            && comparison <= 0;
    }

    /// <summary>
    /// Строит план ручной проверки. Если релиз новее:
    /// <list type="bullet">
    /// <item>пропущенный релиз ВСЕГДА показывается в диалоге, даже в режиме
    /// <see cref="AppUpdateMode.InstallOnStartup"/>;</item>
    /// <item>непропущенный релиз в <see cref="AppUpdateMode.InstallOnStartup"/>
    /// по-прежнему ставится автоматически (прежнее поведение);</item>
    /// <item>в остальных режимах показывается диалог.</item>
    /// </list>
    /// Если релиз не новее — ничего не показываем и не устанавливаем.
    /// </summary>
    public static ManualUpdateCheckPlan Plan(
        ReleaseInfo? release,
        string currentVersion,
        string? skippedVersion,
        AppUpdateMode mode)
    {
        if (release is null
            || !GitHubReleaseService.IsNewerThan(release, currentVersion))
        {
            return new ManualUpdateCheckPlan(
                HasNewerRelease: false,
                WasSkipped: false,
                ShowDialog: false,
                InstallAutomatically: false);
        }

        var wasSkipped = IsSkipped(release, skippedVersion);
        var installAutomatically = mode == AppUpdateMode.InstallOnStartup && !wasSkipped;
        return new ManualUpdateCheckPlan(
            HasNewerRelease: true,
            WasSkipped: wasSkipped,
            ShowDialog: !installAutomatically,
            InstallAutomatically: installAutomatically);
    }
}
