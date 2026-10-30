using System;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Запоминание размеров окон «Настройки» и «Настройка информационной базы» (issue #71,
/// последний комментарий RizvanShikhammatov «нельзя ли запоминать размеры этого окна?
/// И в следующий раз открывать с последними размерами?»). Чистые функции — обе
/// платформы (WPF и Avalonia) — поверх полей <see cref="AppSettings"/>.
/// <para>
/// Условность «0»: в настройках 0 означает «не задано» — окно открывается со стартовым
/// размером из разметки (1100×760 / 900×800), а при сохранении размер не перезаписывается.
/// Позиция окна не запоминается: окна открываются по центру владельца (CenterOwner/центр
/// экрана). Восстановленный размер зажимается к MinWidth/MinHeight — окна нельзя было
/// сжимать и раньше (issues #71, #55), поэтому сохранённый «слишком малый» размер (со
/// старого монитора) не уводит окно за минимум.
/// </para>
/// </summary>
public static class WindowSizeStore
{
    /// <summary>Виды окон с запоминаемым размером.</summary>
    public enum StoredWindowKind
    {
        /// <summary>Окно «Настройки» (SettingsWindow).</summary>
        Settings,

        /// <summary>Окно «Настройка информационной базы» (ConnectionSettingsWindow).</summary>
        ConnectionSettings
    }

    /// <summary>
    /// Признак валидного сохранённого размера: больше нуля, не NaN и не бесконечность
    /// (0 — «не задано», восстановливать не нужно).
    /// </summary>
    public static bool IsValid(double value)
        => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);

    /// <summary>
    /// Зажим сохранённого размера к минимуму: при «не задано» (значение ≤ 0) возвращает 0
    /// (окно откроется со стартовым размером из разметки); иначе — максимум сохранённого
    /// и минимума, чтобы восстановленное окно не оказалось уже минимально допустимого.
    /// </summary>
    public static double ApplyMin(double saved, double min)
        => saved <= 0 ? 0 : Math.Max(saved, min);

    /// <summary>Сохранённая ширина окна заданного вида (0 — «не задано»).</summary>
    public static double SavedWidth(AppSettings settings, StoredWindowKind kind)
        => kind switch
        {
            StoredWindowKind.Settings => settings.SettingsWindowWidth,
            _ => settings.ConnectionSettingsWindowWidth
        };

    /// <summary>Сохранённая высота окна заданного вида (0 — «не задано»).</summary>
    public static double SavedHeight(AppSettings settings, StoredWindowKind kind)
        => kind switch
        {
            StoredWindowKind.Settings => settings.SettingsWindowHeight,
            _ => settings.ConnectionSettingsWindowHeight
        };

    /// <summary>
    /// Записывает фактические размеры окна заданного вида в <paramref name="settings"/>.
    /// Валидные размеры сохраняются как есть; невалидные (0/NaN/∞) не трогают поле —
    /// «не задано» остаётся «не задано».
    /// </summary>
    public static void SaveSize(AppSettings settings, StoredWindowKind kind, double width, double height)
    {
        if (settings is null)
            return;
        switch (kind)
        {
            case StoredWindowKind.Settings:
                if (IsValid(width)) settings.SettingsWindowWidth = width;
                if (IsValid(height)) settings.SettingsWindowHeight = height;
                break;
            default:
                if (IsValid(width)) settings.ConnectionSettingsWindowWidth = width;
                if (IsValid(height)) settings.ConnectionSettingsWindowHeight = height;
                break;
        }
    }
}
