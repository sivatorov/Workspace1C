using System;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.ViewModels;

/// <summary>
/// Строка выпадающего списка «Конфигурация» окна «Связать с конфигурацией» (issue #322):
/// имя конфигурации + пометка происхождения («типовая» / «пользовательская» /
/// «пользовательская копия — перекрывает типовую»), чтобы было видно, какая именно запись
/// выбрана, и ToolTip с полной справкой (код, сегмент адреса, происхождение).
/// Обёртка над <see cref="OneCConfigType"/>; <see cref="ToString"/> не расширяется суффиксами
/// (его используют и другие окна) — метка живёт в отдельном свойстве <see cref="OriginBadge"/>.
/// </summary>
public sealed class ConfigLinkItemViewModel
{
    /// <param name="model">Типовая конфигурация. Не может быть null.</param>
    public ConfigLinkItemViewModel(OneCConfigType model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
    }

    /// <summary>Оборачиваемая типовая конфигурация.</summary>
    public OneCConfigType Model { get; }

    /// <summary>Отображаемое имя конфигурации.</summary>
    public string Name => Model.Name;

    /// <summary>Стабильный код конфигурации (связь ИБ ↔ конфигурация).</summary>
    public string Code => Model.Code;

    /// <summary>Признак предопределённой конфигурации из встроенного набора.</summary>
    public bool IsBuiltIn => Model.IsBuiltIn;

    /// <summary>Признак пользовательской копии предопределённой (правка встроенной, issue #321).</summary>
    public bool IsOverride => Model.OverridesBuiltIn;

    /// <summary>
    /// Короткая пометка происхождения для строки выпадающего списка: ★ — типовая,
    /// «заменена пользовательской» — пользовательская копия (перекрывает типовую),
    /// без звёздочки — пользовательская. Текстовый глиф «✎» не используется (P1b):
    /// пометка остаётся читаемой строкой, а иконки рисуются в таблицах.
    /// </summary>
    public string OriginBadge
    {
        get
        {
            if (Model.OverridesBuiltIn)
                return LocalizationManager.T("Updates.OriginOverride");
            if (Model.IsBuiltIn)
                return "★ " + LocalizationManager.T("Updates.OriginBuiltIn");
            return LocalizationManager.T("Updates.OriginCustom");
        }
    }

    /// <summary>Полная справка по записи (ToolTip строки списка): имя, код, сегмент адреса, происхождение.</summary>
    public string ToolTipText
    {
        get
        {
            var url = string.IsNullOrWhiteSpace(Model.UrlCode) ? Model.Name : Model.UrlCode;
            var origin = Model.OverridesBuiltIn
                ? LocalizationManager.T("Updates.OriginOverride")
                : (Model.IsBuiltIn
                    ? LocalizationManager.T("Updates.OriginBuiltIn")
                    : LocalizationManager.T("Updates.OriginCustom"));
            return $"{Model.Name} · {LocalizationManager.T("Updates.ConfigCode")}: {Model.Code} · " +
                   $"{LocalizationManager.T("Updates.UrlCode")}: {url} · {origin}";
        }
    }

    /// <summary>Безопасное строковое представление (ComboBox вне окна связи использует только имя).</summary>
    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Code : Name;
}