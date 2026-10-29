using System.IO;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Операции с расширениями конфигурации (.cfe) через пакетный режим конфигуратора
/// (выпуск 1.0.0.69): список расширений базы, загрузка/обновление расширения
/// (<c>/LoadCfg -Extension</c> + <c>/UpdateDBCfg -Extension</c>), выгрузка в файл
/// (<c>/DumpCfg -Extension</c>) и удаление (<c>/DeleteCfg -Extension</c>).
/// Построение ключей — чистые функции (покрыты тестами), запуск —
/// <see cref="DesignerCommandRunner"/>.
/// </summary>
public static class ConfigExtensionService
{
    /// <summary>
    /// Ключи загрузки расширения из файла и применения его к базе данных.
    /// Issue #87 («Загрузка расширения конфигурации в клиент-серверную базу»):
    /// у <c>/UpdateDBCfg</c> ключа <c>-Extension</c> НЕТ (он определен для
    /// <c>/LoadCfg</c>/<c>/DumpCfg</c>) — повторный <c>-Extension</c> у операции
    /// применения даёт «Error in command-line options» при запуске конфигуратора
    /// на клиент-серверной базе. Расширение, загруженное через
    /// <c>/LoadCfg "файл.cfe" -Extension "имя"</c>, применяется к базе данных
    /// последующим <c>/UpdateDBCfg</c> (без повторения ключа).
    /// </summary>
    public static string? BuildInstallArguments(string cfePath, string extensionName)
    {
        if (!DesignerCommandRunner.IsSafeValue(cfePath) || !DesignerCommandRunner.IsSafeValue(extensionName))
            return null;
        var name = extensionName.Trim();
        return $"/LoadCfg\"{cfePath}\" -Extension \"{name}\" /UpdateDBCfg";
    }

    /// <summary>Ключи выгрузки расширения в файл .cfe.</summary>
    public static string? BuildDumpArguments(string cfePath, string extensionName)
    {
        if (!DesignerCommandRunner.IsSafeValue(cfePath) || !DesignerCommandRunner.IsSafeValue(extensionName))
            return null;
        return $"/DumpCfg\"{cfePath}\" -Extension \"{extensionName.Trim()}\"";
    }

    /// <summary>Ключи удаления расширения из базы.</summary>
    public static string? BuildDeleteArguments(string extensionName)
    {
        if (!DesignerCommandRunner.IsSafeValue(extensionName))
            return null;
        return $"/DeleteCfg -Extension \"{extensionName.Trim()}\"";
    }

    /// <summary>Ключи вывода списка расширений базы данных в файл /Out.</summary>
    public const string ListArguments = "/DumpDBCfgList -AllExtensions";

    /// <summary>
    /// Имя расширения по умолчанию из имени файла: «Доработки_1.2.cfe» → «Доработки_1.2».
    /// </summary>
    public static string SuggestName(string? cfePath)
    {
        if (string.IsNullOrWhiteSpace(cfePath))
            return string.Empty;
        return Path.GetFileNameWithoutExtension(cfePath.Trim());
    }

    /// <summary>
    /// Разбор вывода <c>/DumpDBCfgList</c>: имена расширений по одному в строке.
    /// Служебные строки конфигуратора (пустые, с двоеточием в начале, сообщения
    /// о завершении) отбрасываются; повторы убираются.
    /// </summary>
    public static IReadOnlyList<string> ParseList(string? log)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(log))
            return result;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in log.Split('\n'))
        {
            var line = raw.Trim().Trim('﻿');
            if (line.Length == 0)
                continue;
            if (line.StartsWith(":", StringComparison.Ordinal) || line.EndsWith(".", StringComparison.Ordinal))
                continue;
            if (line.Contains(' ') && !line.Contains('_') && line.Split(' ').Length > 3)
                continue; // похоже на фразу-сообщение, а не на имя (имена — идентификаторы)
            if (seen.Add(line))
                result.Add(line);
        }
        return result;
    }

    public static async Task<DesignerCommandResult> ListAsync(Infobase infobase, CancellationToken ct = default) =>
        await DesignerCommandRunner.RunAsync(infobase, ListArguments, TimeSpan.FromMinutes(10), ct).ConfigureAwait(false);

    public static async Task<DesignerCommandResult> InstallAsync(Infobase infobase, string cfePath, string extensionName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cfePath) || !File.Exists(cfePath))
            return DesignerCommandResult.Fail(LocalizationManager.T("Extensions.FileNotFound"));
        var args = BuildInstallArguments(cfePath, extensionName);
        if (args is null)
            return DesignerCommandResult.Fail(LocalizationManager.T("Extensions.BadName"));
        return await DesignerCommandRunner.RunAsync(infobase, args, null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Признак отказа конфигуратора по причине «Error in command-line options» (issue #87):
    /// конфигуратор не принял строку параметров (например, на клиент-серверной базе).
    /// Возвращает <c>true</c>, если результат неуспешен и в тексте ошибки/вывода есть
    /// признак ошибки параметров командной строки. Чистая функция — для юнит-тестов
    /// и автоматического fallback'а на ibcmd-ветку.
    /// </summary>
    public static bool IsCommandLineOptionsError(DesignerCommandResult result)
    {
        if (result is null || result.Success || result.ExitCode == 0)
            return false;
        var text = (result.Error ?? string.Empty) + "\n" + (result.Log ?? string.Empty);
        return text.IndexOf("command-line options", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("command line options", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static async Task<DesignerCommandResult> DumpAsync(Infobase infobase, string cfePath, string extensionName,
        CancellationToken ct = default)
    {
        var args = BuildDumpArguments(cfePath, extensionName);
        if (args is null)
            return DesignerCommandResult.Fail(LocalizationManager.T("Extensions.BadName"));
        var dir = Path.GetDirectoryName(cfePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var result = await DesignerCommandRunner.RunAsync(infobase, args, null, ct).ConfigureAwait(false);
        if (result.Success && (!File.Exists(cfePath) || new FileInfo(cfePath).Length == 0))
        {
            return new DesignerCommandResult
            {
                ExitCode = result.ExitCode,
                Log = result.Log,
                Error = LocalizationManager.T("Extensions.DumpEmpty")
            };
        }
        return result;
    }

    public static async Task<DesignerCommandResult> DeleteAsync(Infobase infobase, string extensionName,
        CancellationToken ct = default)
    {
        var args = BuildDeleteArguments(extensionName);
        if (args is null)
            return DesignerCommandResult.Fail(LocalizationManager.T("Extensions.BadName"));
        return await DesignerCommandRunner.RunAsync(infobase, args, null, ct).ConfigureAwait(false);
    }
}
