using System;

namespace Workspace1C.Models;

/// <summary>
/// Режим обновления приложения (issue #116): замена пары флагов
/// <see cref="AppSettings.CheckForUpdatesOnStartup"/>/<see cref="AppSettings.AutoUpdateEnabled"/>,
/// покрывающая все комбинации «проверять/не проверять» × «качать и ставить/спрашивать/уведомлять».
/// </summary>
public enum AppUpdateMode
{
    /// <summary>Обновлять при запуске: фоновая проверка, новая версия скачивается и устанавливается молча.</summary>
    InstallOnStartup,

    /// <summary>Загружать автоматически, предлагать установку: фоновая проверка показывает диалог обновления.</summary>
    DownloadAskInstall,

    /// <summary>Только уведомлять о новых версиях: файл не скачивается, ни диалог, ни установка.</summary>
    NotifyOnly,

    /// <summary>Проверять вручную: фоновая проверка при старте не выполняется вовсе.</summary>
    Manual
}

/// <summary>
/// Хелпер режима обновления (issue #116): канонические строковые значения для JSON настроек,
/// устойчивый разбор и миграция из устаревших флагов
/// <see cref="AppSettings.CheckForUpdatesOnStartup"/>/<see cref="AppSettings.AutoUpdateEnabled"/>.
/// </summary>
public static class AppUpdateModeHelper
{
    /// <summary>Значение по умолчанию (текущее поведение до issue #116).</summary>
    public const AppUpdateMode Default = AppUpdateMode.InstallOnStartup;

    /// <summary>
    /// Каноническая строка режима для хранения в настройках (читаемо в JSON).
    /// Пустая/неизвестная строка на входе не даёт пустого значения на выходе —
    /// хранится всегда каноническое имя варианта.
    /// </summary>
    public static string ToStringValue(AppUpdateMode mode) => mode switch
    {
        AppUpdateMode.DownloadAskInstall => "DownloadAskInstall",
        AppUpdateMode.NotifyOnly => "NotifyOnly",
        AppUpdateMode.Manual => "Manual",
        _ => "InstallOnStartup"
    };

    /// <summary>
    /// Разбор сохранённой строки режима. Пустая или неизвестная строка даёт
    /// <see cref="Default"/> (InstallOnStartup). Регистр не важен.
    /// </summary>
    public static AppUpdateMode Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Default;

        return value.Trim() switch
        {
            var v when v.Equals("InstallOnStartup", StringComparison.OrdinalIgnoreCase) => AppUpdateMode.InstallOnStartup,
            var v when v.Equals("DownloadAskInstall", StringComparison.OrdinalIgnoreCase) => AppUpdateMode.DownloadAskInstall,
            var v when v.Equals("NotifyOnly", StringComparison.OrdinalIgnoreCase) => AppUpdateMode.NotifyOnly,
            var v when v.Equals("Manual", StringComparison.OrdinalIgnoreCase) => AppUpdateMode.Manual,
            _ => Default
        };
    }

    /// <summary>
    /// Миграция из устаревшей пары флагов (issue #116): у выключивших автообновление
    /// режим НЕ должен стать «обновлять при запуске». Комбинации:
    /// <c>CheckForUpdatesOnStartup=false</c> → <see cref="AppUpdateMode.Manual"/>;
    /// <c>AutoUpdateEnabled=false</c> → <see cref="AppUpdateMode.DownloadAskInstall"/>;
    /// оба включены → <see cref="AppUpdateMode.InstallOnStartup"/>.
    /// </summary>
    public static AppUpdateMode MigrateFromLegacyFlags(bool checkForUpdatesOnStartup, bool autoUpdateEnabled)
    {
        if (!checkForUpdatesOnStartup)
            return AppUpdateMode.Manual;
        return autoUpdateEnabled
            ? AppUpdateMode.InstallOnStartup
            : AppUpdateMode.DownloadAskInstall;
    }

    /// <summary>
    /// Обратная синхронизация устаревших флагов из режима (issue #116): старые флаги
    /// остаются в модели настроек, чтобы старая версия приложения (откат) и старый код
    /// чтения настроек продолжали работать. <c>AutoUpdateEnabled = mode == InstallOnStartup</c>,
    /// <c>CheckForUpdatesOnStartup = mode != Manual</c>.
    /// </summary>
    public static (bool CheckForUpdatesOnStartup, bool AutoUpdateEnabled) ToLegacyFlags(AppUpdateMode mode)
        => (mode != AppUpdateMode.Manual, mode == AppUpdateMode.InstallOnStartup);
}
