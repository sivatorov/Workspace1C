using System;
using System.Collections.Generic;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services.EventLog;

/// <summary>
/// Человекочитаемые представления полей журнала регистрации (выпуск 1.0.0.69):
/// служебные имена событий (<c>_$Session$_.Start</c>) и приложений (<c>1CV8C</c>)
/// превращаются в привычные подписи («Сеанс. Начало», «Тонкий клиент»).
/// Неизвестные имена показываются в нормализованном виде без служебных знаков.
/// </summary>
public static class EventLogPresentation
{
    /// <summary>Известные события → ключ локализации.</summary>
    private static readonly HashSet<string> KnownEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "Session.Start", "Session.Finish", "Session.Authentication", "Session.AuthenticationError",
        "Data.New", "Data.Update", "Data.Delete", "Data.Post", "Data.Unpost",
        "InfoBase.ConfigUpdate", "InfoBase.DBConfigUpdate", "InfoBase.DBConfigBackgroundUpdateFinished",
        "InfoBase.ConfigExtensionUpdate", "InfoBase.EventLogSettingsUpdate",
        "Job.Start", "Job.Succeed", "Job.Fail", "Job.Cancel",
        "User.New", "User.Update", "User.Delete", "User.AuthenticationLock",
        "Transaction.Begin", "Transaction.Commit", "Transaction.Rollback",
        "PerformError",
    };

    /// <summary>Известные приложения → ключ локализации.</summary>
    private static readonly HashSet<string> KnownApplications = new(StringComparer.OrdinalIgnoreCase)
    {
        "1CV8", "1CV8C", "WebClient", "Designer", "COMConnection", "BackgroundJob",
        "SrvrConsole", "WSConnection", "HTTPServiceConnection", "JobScheduler", "COMConsole"
    };

    /// <summary>Нормализует служебное имя: «_$Session$_.Start» → «Session.Start».</summary>
    public static string NormalizeEventName(string? name)
    {
        var text = (name ?? string.Empty).Trim();
        if (text.Length == 0)
            return string.Empty;
        text = text.Replace("_$", string.Empty).Replace("$_", string.Empty);
        if (text.StartsWith("$", StringComparison.Ordinal))
            text = text.Substring(1);
        return text;
    }

    /// <summary>Подпись события для таблицы.</summary>
    public static string EventTitle(string? name)
    {
        var normalized = NormalizeEventName(name);
        if (normalized.Length == 0)
            return string.Empty;
        if (KnownEvents.Contains(normalized))
            return TranslateOr("EventLog.Ev." + normalized, normalized);
        return normalized;
    }

    /// <summary>Подпись приложения для таблицы.</summary>
    public static string ApplicationTitle(string? name)
    {
        var text = (name ?? string.Empty).Trim();
        if (text.Length == 0)
            return string.Empty;
        return KnownApplications.Contains(text)
            ? TranslateOr("EventLog.App." + text.ToUpperInvariant(), text)
            : text;
    }

    public static string SeverityTitle(EventLogSeverity severity) => severity switch
    {
        EventLogSeverity.Warning => LocalizationManager.T("EventLog.Severity.Warning"),
        EventLogSeverity.Error => LocalizationManager.T("EventLog.Severity.Error"),
        EventLogSeverity.Note => LocalizationManager.T("EventLog.Severity.Note"),
        _ => LocalizationManager.T("EventLog.Severity.Info"),
    };

    /// <summary>
    /// Ключ векторной иконки уровня для компактной колонки (P1a аудита UI): вместо
    /// текстовых значков возвращается ключ набора иконок, который отрисовывается
    /// через Path/IconHelper. Так индикатор следует активной теме и не зависит от шрифта.
    /// </summary>
    public static string SeverityGlyph(EventLogSeverity severity) => severity switch
    {
        EventLogSeverity.Warning => "IconWarning",
        EventLogSeverity.Error => "IconError",
        EventLogSeverity.Note => "IconNote",
        _ => "IconInfo",
    };

    /// <summary>Цвет уровня (#RRGGBB) — одинаковый в обеих темах.</summary>
    public static string SeverityColor(EventLogSeverity severity) => severity switch
    {
        EventLogSeverity.Warning => "#F59E0B",
        EventLogSeverity.Error => "#EF4444",
        EventLogSeverity.Note => "#8B5CF6",
        _ => "#0EA5E9",
    };

    public static string TransactionTitle(EventLogTransactionStatus status) => status switch
    {
        EventLogTransactionStatus.Committed => LocalizationManager.T("EventLog.Tran.Committed"),
        EventLogTransactionStatus.RolledBack => LocalizationManager.T("EventLog.Tran.RolledBack"),
        EventLogTransactionStatus.Started => LocalizationManager.T("EventLog.Tran.Started"),
        _ => string.Empty,
    };

    /// <summary>Перевод ключа; если перевода нет (возвращён сам ключ) — запасная строка.</summary>
    private static string TranslateOr(string key, string fallback)
    {
        var text = LocalizationManager.T(key);
        return string.IsNullOrEmpty(text) || string.Equals(text, key, StringComparison.Ordinal) ? fallback : text;
    }
}
