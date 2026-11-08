using System;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services.Licensing;

namespace Workspace1C.Services;

/// <summary>
/// Команды CLI лицензии редакции ПРО (2.0, docs/pro/PRO-EDITION.md):
/// <c>--license-info</c> и <c>--activate "W1C2.…"</c>.
/// </summary>
internal static class CliLicenseCommands
{
    /// <summary>Редакция, код компьютера, владелец ключа и доступность функций.</summary>
    public static int Info(string command, CliOptions options)
    {
        var current = EditionService.Current;
        var data = new
        {
            edition = current.IsValid ? "pro" : "free",
            status = current.Status.ToString(),
            machineCode = MachineIdProvider.Current,
            programMajor = VersionInfo.Major(),
            proModules = EditionService.HasProModules,
            owner = current.IsValid ? current.Key?.Payload.Owner : null,
            licenseMajor = current.Key?.Payload.Major,
            issued = current.IsValid ? current.Key?.Payload.Issued : null,
            features = ProFeatures.All.ToDictionaryCompat()
        };
        CliOutput.WriteResult(CliResult.Success(command, data), options.Json);

        if (!options.Json)
        {
            Console.WriteLine(LocalizationManager.T(current.IsValid ? "Pro.EditionPro" : "Pro.EditionFree"));
            Console.WriteLine(LocalizationManager.T("Pro.MachineCode") + " " + MachineIdProvider.Current);
            if (current.IsValid && current.Key is not null)
                Console.WriteLine(string.Format(LocalizationManager.T("Pro.OwnerFormat"),
                    current.Key.Payload.Owner, current.Key.Payload.Major, current.Key.Payload.Issued));
            else if (current.Status != LicenseStatus.None)
                Console.WriteLine(LocalizationManager.T(LicenseVerifier.StatusTextKey(current.Status)));
        }
        return CliExitCodes.Success;
    }

    /// <summary>Проверяет и сохраняет ключ из <c>--activate</c>.</summary>
    public static int Activate(string command, CliOptions options)
    {
        var result = EditionService.Activate(options.Text);
        var message = LocalizationManager.T(LicenseVerifier.StatusTextKey(result.Status));
        if (!result.IsValid)
        {
            CliOutput.WriteResult(CliResult.Failure(command, "license_" + result.Status.ToString().ToLowerInvariant(), message), options.Json);
            return CliExitCodes.Error;
        }

        CliOutput.WriteResult(CliResult.Success(command, new
        {
            edition = "pro",
            owner = result.Key?.Payload.Owner,
            major = result.Key?.Payload.Major
        }), options.Json);
        if (!options.Json)
            Console.WriteLine(message);
        return CliExitCodes.Success;
    }

    /// <summary>Отказ команды ПРО без лицензии: код возврата <see cref="CliExitCodes.ProRequired"/>.</summary>
    public static int ProRequired(string command, ProFeature feature, bool json)
    {
        var message = string.Format(LocalizationManager.T("Pro.RequiredFormat"),
            LocalizationManager.T(ProFeatures.TitleKey(feature)));
        CliOutput.WriteResult(CliResult.Failure(command, "pro_required", message), json);
        return CliExitCodes.ProRequired;
    }

    private static System.Collections.Generic.Dictionary<string, bool> ToDictionaryCompat(
        this System.Collections.Generic.IReadOnlyList<ProFeature> features)
    {
        var map = new System.Collections.Generic.Dictionary<string, bool>();
        foreach (var f in features)
            map[f.ToString()] = EditionService.Has(f);
        return map;
    }
}
