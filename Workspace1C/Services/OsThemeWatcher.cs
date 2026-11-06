using System;

namespace Workspace1C.Services;

#if WINDOWS
using Microsoft.Win32;

/// <summary>
/// Наблюдатель режима оформления приложений ОС Windows (issue #108): реестр
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize</c>,
/// значение <c>AppsUseLightTheme</c> («Режим приложений по умолчанию» — покрывает
/// в том числе гибридный режим персонализации «Настраиваемый»: при выборе режима
/// «Тёмный» для приложений в ОС значение равно 0). Смена режима отслеживается
/// через <see cref="Microsoft.Win32.SystemEvents.UserPreferenceChanged"/>.
/// </summary>
public static class OsThemeWatcher
{
    private const string PersonalizeKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeName = "AppsUseLightTheme";

    private static bool? _lastKnown;
    private static bool _subscribed;

    /// <summary>Платформа поддерживает слежение за режимом ОС.</summary>
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>
    /// Текущий тёмный режим приложений ОС: true — тёмный, false — светлый,
    /// null — определить не удалось.
    /// </summary>
    public static bool? GetAppsDarkMode()
    {
        if (!IsSupported)
            return null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            if (key?.GetValue(AppsUseLightThemeName) is int value)
                return value == 0;
        }
        catch
        {
            // Реестр недоступен (нестандартные окружения) — режима не знаем.
        }
        return null;
    }

    /// <summary>
    /// Смена режима приложений ОС. Параметр — тёмный режим (true) / светлый (false).
    /// Событие поднимается на потоке, установившем подписку (UI-потоке приложения).
    /// </summary>
    public static event Action<bool>? AppsThemeChanged;

    /// <summary>Начинает слежение. Повторный вызов безопасен.</summary>
    public static void Start()
    {
        if (!IsSupported)
            return;
        _lastKnown = GetAppsDarkMode();
        if (!_subscribed)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _subscribed = true;
        }
    }

    /// <summary>Останавливает слежение. Повторный вызов безопасен.</summary>
    public static void Stop()
    {
        if (_subscribed)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _subscribed = false;
        }
        _lastKnown = null;
    }

    private static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        // Событие может приходить на любые категории персонализации (обои, цвета и т. д.) —
        // проверяем фактическое значение реестра и уведомляем только при реальной смене.
        var dark = GetAppsDarkMode();
        if (dark is null || dark == _lastKnown)
            return;
        _lastKnown = dark;
        AppsThemeChanged?.Invoke(dark.Value);
    }
}
#else
/// <summary>
/// Заглушка наблюдателя режима ОС для Linux/Avalonia (issue #108): режим приложений
/// рабочего стола Linux не определяется (GNOME/KDE дают его разными способами), поэтому
/// опция «Тема приложения следует за режимом ОС» пока действует только на Windows.
/// API совместим с Windows-версией, <see cref="GetAppsDarkMode"/> всегда null.
/// </summary>
public static class OsThemeWatcher
{
    public static bool IsSupported => false;

    public static bool? GetAppsDarkMode() => null;

#pragma warning disable CS0067 // Событие не поднимается в заглушке — API совместим с Windows-версией.
    public static event Action<bool>? AppsThemeChanged;
#pragma warning restore CS0067

    public static void Start() { }

    public static void Stop() { }
}
#endif
