using System;
using Workspace1C.Models;
using Workspace1C.Services.Mcp;

namespace Workspace1C.Services;

/// <summary>
/// Headless-вход команд CLI (функция 10). Вызывается первым из <c>Program.Main</c> обеих
/// платформ — **до** создания App/WPF/Avalonia: команды работают без GUI, в т.ч. из
/// планировщика ОС (cron/schtasks) в окружении без DISPLAY. Возвращает true, если аргументы
/// содержали команду CLI — вызывающий код завершает процесс кодом возврата, не показывая окно.
/// </summary>
public static class CliEntryPoint
{
    /// <summary>
    /// Пытается обработать аргументы как команду CLI. Возвращает <c>true</c>, если аргументы
    /// содержали команду (независимо от успеха действия) — вызывающий код должен завершить
    /// приложение с <paramref name="exitCode"/>, не показывая окно.
    /// </summary>
    public static bool TryHandle(string[]? args, out int exitCode)
    {
        exitCode = 0;
        var parsed = CliArgs.Parse(args);
        if (parsed is null)
            return false;

        try
        {
            CliOutput.EnsureUtf8();

            // Минимальная headless-инициализация без UI: DI → портативный режим → профили.
            // Порядок повторяет App.OnStartup (App.xaml.cs, строки 65–80), но без локализации
            // и окна входа: команды читают данные активного профиля напрямую.
            AppServices.Configure();
            // Журнал аудита: действия этого процесса — из командной строки (или MCP).
            Audit.AuditHub.DefaultSource = parsed.Kind == CliCommandKind.Mcp ? Audit.AuditSources.Mcp : Audit.AuditSources.Cli;
            try { PortablePaths.EnsurePortableData(); }
            catch { /* портативный режим — вспомогательная возможность */ }

            var profileService = AppServices.GetRequiredService<IProfileService>();
            profileService.EnsureInitialized();

            // Профиль сессии (--profile <id>). До появления ActivateProfileForSession из
            // функции 2 (цикл 0.3.9.167–171) используем SetCurrentProfile — он сохраняет
            // «последний использованный профиль»; на этапе интеграции 0.3.9.223 заменится
            // на сессионную активацию без побочного эффекта.
            if (!string.IsNullOrWhiteSpace(parsed.Options.Profile))
            {
                try
                {
                    profileService.SetCurrentProfile(parsed.Options.Profile);
                }
                catch (InvalidOperationException)
                {
                    var message = $"Профиль не найден: '{parsed.Options.Profile}'";
                    CliOutput.WriteResult(
                        CliResult.Failure(CommandName(parsed.Kind), "profile_not_found", message),
                        parsed.Options.Json);
                    exitCode = CliExitCodes.Error;
                    return true;
                }
            }

            // MCP-сервер по stdio (п.6 «MCP-сервер и расширение CLI», выпуск 1.0.0.88):
            // работает ДО инициализации UI (обе платформы) в том же headless-процессе.
            // В stdout пишутся ТОЛЬКО JSON-RPC сообщения; диагностика — в stderr
            // (внутри McpServer); сервисы инструментов берутся из AppServices.
            if (parsed.Kind == CliCommandKind.Mcp)
            {
                // MCP-сервер — функция редакции ПРО (docs/pro/PRO-EDITION.md).
                if (!Licensing.EditionService.Has(Licensing.ProFeature.Mcp))
                {
                    exitCode = CliLicenseCommands.ProRequired(CommandName(parsed.Kind), Licensing.ProFeature.Mcp, parsed.Options.Json);
                    return true;
                }
                var confirmationService = AppServices.TryGetService<TelegramConfirmationService>();
                var server = new McpServer(
                    output: Console.Out,
                    diagnostics: Console.Error,
                    safetyGate: new McpSafetyGate(confirmationService));
                exitCode = server.Run(Console.In);
                return true;
            }

            exitCode = CliCommands.Execute(parsed);
            if (parsed.Kind is not (CliCommandKind.List or CliCommandKind.Help or CliCommandKind.Status
                or CliCommandKind.LicenseInfo or CliCommandKind.Check))
                Audit.AuditHub.Write(Audit.AuditActions.CliCommand, parsed.Target ?? string.Empty,
                    exitCode == CliExitCodes.Success, CommandName(parsed.Kind) + "; exit=" + exitCode);
            return true;
        }
        catch (Exception ex)
        {
            try
            {
                CliOutput.WriteResult(
                    CliResult.Failure(CommandName(parsed.Kind), "internal", "Ошибка обработки команды: " + ex.Message),
                    parsed.Options.Json);
                AppServices.GetRequiredService<IAppLogger>().Error("[cli] Ошибка обработки команды: " + ex);
            }
            catch { /* логирование не должно маскировать ошибку */ }
            exitCode = CliExitCodes.Error;
            return true;
        }
    }

    private static string CommandName(CliCommandKind kind) => CliCommands.CommandName(kind);
}