using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Платформенно-нейтральное представление пункта-группы меню «Утилиты» (issue #146):
/// режим отображения («Иконка» / «С надписью»), заголовок/подсказка и эффективные ключи
/// иконки для WPF и Avalonia. Один расчёт используют оба построителя меню, дерево
/// редактора и диалог правки группы — настройка иконки и режима совпадает везде.
/// </summary>
/// <param name="ShowLabel">True — группа показывается с подписью; false — только иконка.</param>
/// <param name="Header">Заголовок пункта меню (null в режиме «только иконка»).</param>
/// <param name="ToolTip">Подсказка пункта (нужна и в режиме «только иконка»).</param>
/// <param name="WpfIconKey">Эффективный ключ иконки для WPF (PackIcon или <c>Icon*</c>).</param>
/// <param name="AvaloniaIconKey">Эффективный ключ геометрии иконки для Avalonia.</param>
/// <param name="ExternalIconPath">Путь к внешнему файлу иконки (или null).</param>
public sealed record UtilitiesMenuGroupLayout(
    bool ShowLabel,
    string? Header,
    string ToolTip,
    string WpfIconKey,
    string AvaloniaIconKey,
    string? ExternalIconPath);

/// <summary>
/// Построение представления группы меню «Утилиты» (issue #146). Вынесено из WPF/Avalonia,
/// чтобы режим отображения и иконка были одинаковыми на обеих платформах и проверялись
/// юнит-тестами без запуска UI.
/// </summary>
public static class UtilitiesMenuGroupPresentation
{
    /// <summary>Строит представление группы по сохранённому узлу компоновки.</summary>
    /// <param name="node">Узел-группа компоновки.</param>
    /// <param name="title">Локализованный или пользовательский заголовок группы.</param>
    public static UtilitiesMenuGroupLayout Build(UtilitiesMenuNode node, string title)
    {
        var showLabel = node is null || node.DisplayMode != UtilitiesMenuGroupDisplay.Icon;
        var external = string.IsNullOrWhiteSpace(node?.ExternalIconPath) ? null : node!.ExternalIconPath;
        return new UtilitiesMenuGroupLayout(
            ShowLabel: showLabel,
            Header: showLabel ? title : null,
            ToolTip: title ?? string.Empty,
            WpfIconKey: UtilitiesMenuCatalog.ResolveGroupIconKeyWpf(node?.GroupId, node?.IconKey),
            AvaloniaIconKey: UtilitiesMenuCatalog.ResolveGroupIconKeyAvalonia(node?.GroupId, node?.IconKey),
            ExternalIconPath: external);
    }
}
