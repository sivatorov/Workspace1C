using System.Diagnostics;
using System.IO;
using System.Text;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>Результат пакетного запуска конфигуратора.</summary>
public sealed class DesignerCommandResult
{
    public bool Success { get; init; }

    public int ExitCode { get; init; } = -1;

    /// <summary>Содержимое файла /Out (сообщения конфигуратора).</summary>
    public string Log { get; init; } = string.Empty;

    /// <summary>Причина отказа запуска или ошибка выполнения.</summary>
    public string? Error { get; init; }

    public static DesignerCommandResult Fail(string error) => new() { Error = error };
}

/// <summary>
/// Пакетный запуск конфигуратора с произвольными ключами операции (выпуск 1.0.0.69):
/// используется расширениями (.cfe), выгрузкой конфигурации в Git и другими
/// функциями, которым не нужен трекинг «длинных» операций
/// <c>OneCLauncher.RunDesignerBatch</c>. Подключение и авторизация берутся
/// из настроек базы (<c>BuildConnectionArgument</c>/<c>BuildAuthArgument</c>),
/// запуск блокируется, если конфигуратор этой базы уже открыт.
/// </summary>
public static class DesignerCommandRunner
{
    /// <summary>Таймаут по умолчанию: загрузка большого расширения может идти долго.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(60);

    /// <summary>
    /// Значение ключа командной строки безопасно, если не содержит кавычек и переводов
    /// строк: 1С не поддерживает экранирование кавычек в значениях ключей.
    /// </summary>
    public static bool IsSafeValue(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.IndexOfAny(new[] { '"', '\r', '\n' }) < 0;

    /// <summary>Полная строка аргументов DESIGNER (без пути к исполняемому файлу).</summary>
    public static string BuildArguments(string connectionArg, string authArg, string operationArgs, string outLog) =>
        $"DESIGNER {connectionArg}{authArg} {operationArgs} /DisableStartupDialogs /DisableStartupMessages /Out\"{outLog}\"";

    /// <summary>
    /// Запускает конфигуратор с ключами <paramref name="operationArgs"/> и ждёт завершения.
    /// Успех — код возврата 0. Отмена через <paramref name="ct"/> завершает процесс.
    /// </summary>
    public static async Task<DesignerCommandResult> RunAsync(
        Infobase infobase,
        string operationArgs,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        if (infobase is null)
            return DesignerCommandResult.Fail(LocalizationManager.T("Designer.NoInfobase"));

        var exePath = OneCLauncher.ResolveConfiguratorExecutable(infobase);
        if (string.IsNullOrEmpty(exePath))
            return DesignerCommandResult.Fail(LocalizationManager.T("Launcher.ConfiguratorExeNotFound"));

        if (OneCLauncher.IsDesignerBlocked(infobase, out var blockReason))
            return DesignerCommandResult.Fail(
                string.Format(LocalizationManager.T("Launcher.ConfiguratorBlockedFormat"), blockReason));

        var outLog = Path.Combine(Path.GetTempPath(), $"1c_cmd_{Guid.NewGuid():N}.log");
        var connectionArg = OneCLauncher.BuildConnectionArgument(infobase);
        var arguments = BuildArguments(
            connectionArg,
            OneCLauncher.BuildAuthArgument(infobase),
            operationArgs,
            outLog);

        // Issue #87: диагностика «Error in command-line options» на клиент-серверной базе —
        // полная строка запуска пишется под категорией CM_RAC (без учётных данных, чтобы
        // пароли не попали в журнал; гейт CM_ON + CM_RAC выключен по умолчанию).
        Log.Info(LogCategory.Rac,
            $"DESIGNER {connectionArg} {operationArgs} (конфигуратор: {exePath}, out: {outLog})");

        Process? process = null;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty
            });
            if (process is null)
                return DesignerCommandResult.Fail(LocalizationManager.T("Designer.StartFailed"));

            using var timeoutCts = new CancellationTokenSource(timeout ?? DefaultTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* уже завершён */ }
                var log = await ReadLogAsync(outLog).ConfigureAwait(false);
                return new DesignerCommandResult
                {
                    Log = log,
                    Error = ct.IsCancellationRequested
                        ? LocalizationManager.T("Designer.Cancelled")
                        : LocalizationManager.T("Designer.Timeout")
                };
            }

            var text = await ReadLogAsync(outLog).ConfigureAwait(false);
            var exitCode = process.ExitCode;
            return new DesignerCommandResult
            {
                Success = exitCode == 0,
                ExitCode = exitCode,
                Log = text,
                Error = exitCode == 0
                    ? null
                    : string.Format(LocalizationManager.T("Designer.ExitCodeFormat"), exitCode,
                        string.IsNullOrWhiteSpace(text) ? "—" : text.Trim())
            };
        }
        catch (Exception ex)
        {
            return DesignerCommandResult.Fail(ex.Message);
        }
        finally
        {
            process?.Dispose();
            try { if (File.Exists(outLog)) File.Delete(outLog); } catch { /* временный файл */ }
        }
    }

    /// <summary>Читает файл /Out: 1С может дописывать его чуть позже выхода процесса.</summary>
    private static async Task<string> ReadLogAsync(string path)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (!File.Exists(path))
                {
                    await Task.Delay(200).ConfigureAwait(false);
                    continue;
                }
                var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                return DecodeLog(bytes);
            }
            catch (IOException)
            {
                await Task.Delay(200).ConfigureAwait(false);
            }
        }
        return string.Empty;
    }

    /// <summary>
    /// Лог /Out: UTF-8 (с BOM или без); на старых платформах Windows — кодировка ОС.
    /// Признак — валидность UTF-8.
    /// </summary>
    public static string DecodeLog(byte[] bytes)
    {
        if (bytes.Length == 0)
            return string.Empty;
        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            var text = utf8.GetString(bytes);
            return text.TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(1251).GetString(bytes);
            }
            catch
            {
                return Encoding.Latin1.GetString(bytes);
            }
        }
    }
}
