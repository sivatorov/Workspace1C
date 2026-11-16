using System;
using Workspace1C.Localization;

namespace Workspace1C.Services.Licensing;

/// <summary>
/// Общий слой данных о лицензии для разделов «О программе» и «Лицензия»:
/// редакция, владелец, код компьютера и текст статуса. Без зависимостей от UI —
/// используется как WPF/Avalonia-окнами, так и модульными тестами.
/// </summary>
/// <remarks>
/// Тексты уже локализованы через переданный транслятор (в приложении —
/// <see cref="LocalizationManager.T"/>), поэтому сводка пригодна для привязки
/// к элементам интерфейса напрямую. Логика повторяет поведение
/// <see cref="Workspace1C.ViewModels.LicenseViewModel"/>, но не заменяет его:
/// ViewModel дополнительно хранит введённый ключ и список функций ПРО.
/// </remarks>
public sealed record LicenseSummary
{
    /// <summary>Метка редакции ПРО для заголовка раздела.</summary>
    public const string ProMark = "★";

    /// <summary>Действительный ключ ПРО (редакция ПРО активна).</summary>
    public bool IsPro { get; init; }

    /// <summary>Ключ разобран и присутствует (даже если он не подходит этому компьютеру/версии).</summary>
    public bool HasKey { get; init; }

    /// <summary>Сборка без модулей ПРО (<c>PRO_MODULES</c> не задан).</summary>
    public bool IsCommunityBuild { get; init; }

    /// <summary>Локализованная редакция: <c>Pro.EditionPro</c> или <c>Pro.EditionFree</c>.</summary>
    public string EditionText { get; init; } = string.Empty;

    /// <summary>
    /// Локализованный владелец (<c>Pro.OwnerFormat</c> с владельцем, старшей версией и датой
    /// выпуска); пусто, если действительного ключа нет.
    /// </summary>
    public string OwnerText { get; init; } = string.Empty;

    /// <summary>
    /// Локализованная строка о ключе (<c>Settings.About.LicenseKeyInfo</c>: для версий N.x,
    /// выпущен <c>yyyy-MM-dd</c>); пусто, если действительного ключа нет.
    /// </summary>
    public string KeyInfoText { get; init; } = string.Empty;

    /// <summary>Сырой код компьютера (может быть пустым, если идентификатор ОС не получен).</summary>
    public string MachineCode { get; init; } = string.Empty;

    /// <summary>Код компьютера или <c>Pro.MachineCodeUnavailable</c>, если код получить не удалось.</summary>
    public string MachineCodeText { get; init; } = string.Empty;

    /// <summary>
    /// Локализованный текст ошибки ключа. Пусто для <see cref="LicenseStatus.None"/> и
    /// <see cref="LicenseStatus.Valid"/>; для <see cref="LicenseStatus.WrongMajorVersion"/>
    /// подставляется старшая версия из ключа (<c>Pro.Status.WrongMajorVersionFormat</c>).
    /// </summary>
    public string StatusText { get; init; } = string.Empty;

    /// <summary>
    /// Собирает сводку из результата проверки ключа (чистая функция для тестов и UI).
    /// </summary>
    /// <param name="current">Результат проверки лицензионного ключа.</param>
    /// <param name="machineCode">Код компьютера (как показывает окно «Лицензия»).</param>
    /// <param name="hasProModules">В сборку встроены модули ПРО.</param>
    /// <param name="translate">Функция перевода ключа локализации в текст.</param>
    public static LicenseSummary From(
        LicenseCheckResult current,
        string machineCode,
        bool hasProModules,
        Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(translate);
        machineCode ??= string.Empty;

        var key = current.Key;
        var isValid = current.IsValid && key is not null;

        var ownerText = isValid
            ? string.Format(
                translate("Pro.OwnerFormat"),
                key!.Payload.Owner,
                key.Payload.Major,
                key.Payload.Issued)
            : string.Empty;

        var keyInfoText = isValid
            ? string.Format(
                translate("Settings.About.LicenseKeyInfo"),
                key!.Payload.Major,
                key.Payload.Issued)
            : string.Empty;

        var statusText = current.Status switch
        {
            LicenseStatus.None or LicenseStatus.Valid => string.Empty,
            LicenseStatus.WrongMajorVersion when key is not null =>
                string.Format(translate("Pro.Status.WrongMajorVersionFormat"), key.Payload.Major),
            _ => translate(LicenseVerifier.StatusTextKey(current.Status))
        };

        return new LicenseSummary
        {
            IsPro = isValid,
            HasKey = key is not null,
            IsCommunityBuild = !hasProModules,
            EditionText = translate(isValid ? "Pro.EditionPro" : "Pro.EditionFree"),
            OwnerText = ownerText,
            KeyInfoText = keyInfoText,
            MachineCode = machineCode,
            MachineCodeText = string.IsNullOrEmpty(machineCode)
                ? translate("Pro.MachineCodeUnavailable")
                : machineCode,
            StatusText = statusText
        };
    }

    /// <summary>
    /// Сводка для текущего состояния приложения: сохранённый ключ
    /// (<see cref="EditionService.Current"/>), код компьютера
    /// (<see cref="MachineIdProvider.Current"/>), наличие модулей ПРО и локализация
    /// (<see cref="LocalizationManager.T"/>).
    /// </summary>
    public static LicenseSummary Current() =>
        From(
            EditionService.Current,
            MachineIdProvider.Current,
            EditionService.HasProModules,
            LocalizationManager.T);
}
