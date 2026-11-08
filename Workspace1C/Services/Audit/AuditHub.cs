using System;

namespace Workspace1C.Services.Audit;

/// <summary>Источник действия в журнале аудита.</summary>
public static class AuditSources
{
    public const string Ui = "UI";
    public const string Scheduler = "Scheduler";
    public const string Cli = "CLI";
    public const string Telegram = "Telegram";
    public const string Mcp = "MCP";
    public const string Pipeline = "Pipeline";
}

/// <summary>Действия журнала аудита (стабильные коды — сохраняются в файлах журнала).</summary>
public static class AuditActions
{
    public const string ExtensionInstall = "extension.install";
    public const string ExtensionDelete = "extension.delete";
    public const string DtRestore = "dt.restore";
    public const string ConfigUpdate = "config.update";
    public const string CfMassLoad = "cf.massload";
    public const string TestDeploy = "testdeploy.run";
    public const string ScheduledTaskRun = "task.run";
    public const string SessionTerminate = "session.terminate";
    public const string ServerInfobaseDrop = "server.infobase.drop";
    public const string IbUserChange = "ibuser.change";
    public const string TelegramCommand = "telegram.command";
    public const string McpToolCall = "mcp.tool";
    public const string CliCommand = "cli.command";
    public const string LicenseActivate = "license.activate";
    public const string LicenseDeactivate = "license.deactivate";
    public const string ExternalProcessingsLoad = "extproc.load";
}

/// <summary>Событие журнала аудита.</summary>
/// <param name="Time">Локальное время события.</param>
/// <param name="Actor">Пользователь ОС (DOMAIN\user или user).</param>
/// <param name="Source">Источник: <see cref="AuditSources"/>.</param>
/// <param name="Action">Код действия: <see cref="AuditActions"/>.</param>
/// <param name="Target">Объект действия (база, сервер, задание).</param>
/// <param name="Success">Действие выполнено успешно.</param>
/// <param name="Details">Подробности (без паролей).</param>
public sealed record AuditEvent(DateTime Time, string Actor, string Source, string Action, string Target,
    bool Success, string Details);

/// <summary>
/// Точка записи журнала аудита (редакция ПРО, функция <c>AuditLog</c>). Публичный код
/// сообщает о значимых действиях; сам журнал (хранение, окно, оповещения) реализует
/// модуль ПРО, подписываясь на <see cref="Sink"/>. Без модуля или без лицензии вызовы
/// ничего не делают. Ошибка приёмника никогда не ломает основное действие.
/// </summary>
public static class AuditHub
{
    /// <summary>Приёмник событий (модуль журнала аудита). null — журнал не подключён.</summary>
    public static Action<AuditEvent>? Sink { get; set; }

    /// <summary>Источник по умолчанию для текущего процесса (CLI/MCP переопределяют при старте).</summary>
    public static string DefaultSource { get; set; } = AuditSources.Ui;

    /// <summary>Записывает событие (если журнал подключён).</summary>
    public static void Write(string action, string target, bool success, string? details = null, string? source = null)
    {
        var sink = Sink;
        if (sink is null)
            return;
        try
        {
            var actor = string.IsNullOrEmpty(Environment.UserDomainName) || Environment.UserDomainName == Environment.MachineName
                ? Environment.UserName
                : Environment.UserDomainName + "\\" + Environment.UserName;
            sink(new AuditEvent(DateTime.Now, actor, source ?? DefaultSource, action ?? string.Empty,
                target ?? string.Empty, success, MaskSecrets(details ?? string.Empty)));
        }
        catch
        {
            // Журнал аудита не должен ломать действие, о котором сообщает.
        }
    }

    /// <summary>Маскирует пароли СУБД, rac и хранилища в подробностях события.</summary>
    internal static string MaskSecrets(string text)
    {
        var masked = SensitiveDataMasker.MaskDbPassword(text);
        masked = SensitiveDataMasker.MaskRacPassword(masked);
        return SensitiveDataMasker.MaskRepositoryPassword(masked) ?? string.Empty;
    }
}
