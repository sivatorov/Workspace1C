using System;
using System.Collections.Generic;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Раздел списка баз, для которого строится подпись корня дерева (issue #133).
/// Значения соответствуют режимам <see cref="ListViewMode"/>.
/// </summary>
public enum TreeRootSection
{
    /// <summary>«Все» — все базы.</summary>
    All,
    /// <summary>«Избранное» — избранные базы.</summary>
    Favorites,
    /// <summary>«Недавние» — недавно запускавшиеся базы.</summary>
    Recent,
    /// <summary>«Запущенные» — только запущенные базы (issue #339).</summary>
    Running
}

/// <summary>
/// План подписи корня дерева баз (issue #133): какой ключ локализации показать
/// и нужен ли специальный маркер узла «Все базы» (он задаёт иконку базы).
/// </summary>
public readonly record struct TreeRootHeaderPlan(string NameKey, bool UseAllBasesMarker)
{
    /// <summary>Подпись — результат поиска («Найдено»), а не имя раздела.</summary>
    public bool IsSearchResult => string.Equals(NameKey, TreeRootHeaderResolver.FoundKey, StringComparison.Ordinal);
}

/// <summary>
/// Правило подписи корня дерева баз (issue #133). Чистая логика без UI — используется
/// и WPF-версией, и Avalonia, поэтому покрыта юнит-тестами
/// (<c>Issue133TreeRootHeaderTests</c>).
/// <para>
/// При пустом поле поиска корень показывает имя раздела («Все», «Избранное»,
/// «Недавние», «Запущенные»); при непустом поиске — прежнее «Найдено».
/// Фильтр по тегам на подпись не влияет: «Найдено» пишется только при заполненном
/// поле поиска. Счётчик «(N)» добавляется отдельным суффиксом узла
/// (<see cref="ViewModels.GroupNodeViewModel.GroupCountSuffix"/>) и не меняется.
/// </para>
/// </summary>
public static class TreeRootHeaderResolver
{
    /// <summary>Ключ локализации подписи раздела «Все».</summary>
    public const string AllKey = "Main.TreeRoot.All";

    /// <summary>Ключ локализации подписи раздела «Избранное».</summary>
    public const string FavoritesKey = "Main.TreeRoot.Favorites";

    /// <summary>Ключ локализации подписи раздела «Недавние».</summary>
    public const string RecentKey = "Main.TreeRoot.Recent";

    /// <summary>Ключ локализации подписи раздела «Запущенные».</summary>
    public const string RunningKey = "Main.TreeRoot.Running";

    /// <summary>Ключ локализации подписи «Найдено» (непустое поле поиска).</summary>
    public const string FoundKey = "Main.TreeRoot.Found";

    /// <summary>Все ключи имён разделов в порядке отображения вкладок.</summary>
    public static IReadOnlyList<string> SectionKeys { get; } =
        new[] { AllKey, FavoritesKey, RecentKey, RunningKey };

    /// <summary>
    /// Преобразует режим списка в раздел подписи корня. Неизвестные значения
    /// трактуются как «Все» (поведение <see cref="ListViewModes.Normalize"/>).
    /// </summary>
    public static TreeRootSection FromListViewMode(ListViewMode mode) => mode switch
    {
        ListViewMode.Favorites => TreeRootSection.Favorites,
        ListViewMode.Recent => TreeRootSection.Recent,
        ListViewMode.Running => TreeRootSection.Running,
        _ => TreeRootSection.All
    };

    /// <summary>
    /// Ключ локализации подписи корня: имя раздела при пустом поиске либо
    /// <see cref="FoundKey"/> при непустом поле поиска.
    /// </summary>
    /// <param name="section">Раздел списка баз.</param>
    /// <param name="searchActive">Поле поиска непустое.</param>
    public static string ResolveNameKey(TreeRootSection section, bool searchActive)
        => searchActive
            ? FoundKey
            : section switch
            {
                TreeRootSection.Favorites => FavoritesKey,
                TreeRootSection.Recent => RecentKey,
                TreeRootSection.Running => RunningKey,
                _ => AllKey
            };

    /// <summary>
    /// План подписи корня: ключ локализации и признак использования маркера
    /// «Все базы» (узел раздела «Все» сохраняет иконку базы, остальные разделы —
    /// обычный узел без маркера).
    /// </summary>
    public static TreeRootHeaderPlan Resolve(TreeRootSection section, bool searchActive)
        => new(
            ResolveNameKey(section, searchActive),
            UseAllBasesMarker: !searchActive && section == TreeRootSection.All);

    /// <summary>
    /// Собирает полный текст подписи корня: имя и счётчик в текущем формате
    /// <c>«Имя (N)»</c>. Пустое имя даёт только счётчик.
    /// </summary>
    public static string FormatHeader(string? name, int count)
        => string.IsNullOrEmpty(name) ? $"({count})" : $"{name} ({count})";

    /// <summary>
    /// Возвращает полный текст подписи корня по разделу, признаку активного поиска
    /// и количеству баз. <paramref name="translate"/> позволяет подставить
    /// локализатор (в тестах — без инициализации <see cref="LocalizationManager"/>);
    /// по умолчанию используется <see cref="LocalizationManager.T"/>.
    /// </summary>
    public static string ResolveHeader(
        TreeRootSection section,
        bool searchActive,
        int count,
        Func<string, string>? translate = null)
    {
        var name = (translate ?? LocalizationManager.T)(ResolveNameKey(section, searchActive));
        return FormatHeader(name, count);
    }
}
