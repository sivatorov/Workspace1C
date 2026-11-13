using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Результат пересчёта доступности обновления без сети (issue #129).
/// </summary>
/// <param name="Release">Релиз, который остаётся доступным (null — обновления нет).</param>
/// <param name="NeedsNetworkCheck">
/// True — кэшированного релиза нет; чтобы получить состояние, нужна фоновая проверка.
/// </param>
public readonly record struct UpdateAvailabilityEvaluation(
    ReleaseInfo? Release,
    bool NeedsNetworkCheck);

/// <summary>
/// Чистая логика пересчёта доступности обновления при смене эффективной версии
/// (issue #129 «Считать версию прошлой»). Вынесена из <c>UpdateService</c> WPF/Avalonia,
/// чтобы проверять поведение юнит-тестами без сети и UI.
/// <para>
/// Сценарий дефекта: проверка обновлений уже нашла релиз (или, наоборот, решила, что
/// обновлений нет) по РЕАЛЬНОЙ версии. Пользователь включает галку — эффективная
/// версия понижается. Кэшированный релиз нужно переоценить СРАЗУ: релиз, равный
/// реальной сборке, становится «новее» эффективной версии и должен снова предлагаться,
/// а релиз, который перестал быть новее, — сниматься с предложения.
/// </para>
/// </summary>
public static class UpdateAvailabilityLogic
{
    /// <summary>
    /// Переоценивает кэшированный релиз относительно текущей версии для обновлений
    /// (обычно <see cref="EffectiveVersionService.CurrentForUpdates"/>).
    /// Сеть не используется: при отсутствии кэша возвращается запрос на фоновую проверку.
    /// </summary>
    /// <param name="availableRelease">Ранее найденный (кэшированный) релиз.</param>
    /// <param name="currentVersionForUpdates">Версия, с которой сравнивается релиз.</param>
    /// <param name="skipped">Релиз пропущен пользователем («Пропустить эту версию»).</param>
    public static UpdateAvailabilityEvaluation Reevaluate(
        ReleaseInfo? availableRelease,
        string currentVersionForUpdates,
        bool skipped)
    {
        if (availableRelease is null)
            return new UpdateAvailabilityEvaluation(null, NeedsNetworkCheck: true);

        if (skipped
            || !GitHubReleaseService.IsNewerThan(availableRelease, currentVersionForUpdates))
        {
            return new UpdateAvailabilityEvaluation(null, NeedsNetworkCheck: false);
        }

        return new UpdateAvailabilityEvaluation(availableRelease, NeedsNetworkCheck: false);
    }
}
