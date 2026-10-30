#if WINDOWS
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using Workspace1C.Models;

namespace Workspace1C.Services
{
    /// <summary>
    /// Windows-часть <see cref="ApacheInstanceDetector"/> (issue #63): перечисление
    /// служб httpd (имя/DisplayName/Description) через реестр <c>SYSTEM\CurrentControlSet\Services</c>,
    /// из <c>ImagePath</c> извлекаются ключи <c>-d ServerRoot</c> и <c>-f conf</c>
    /// (<see cref="ApacheInstanceDetector.ParseWindowsServiceImagePath"/>), конфиг читается
    /// и разбираются include-директивы. Права администратора не требуются (чтение реестра).
    /// </summary>
    public static partial class ApacheInstanceDetector
    {
        private const string ServicesRegistryPath = @"SYSTEM\CurrentControlSet\Services";

        private static IReadOnlyList<ApacheInstance> DetectWindows()
        {
            var result = new List<ApacheInstance>();
            try
            {
                using var servicesKey = Registry.LocalMachine.OpenSubKey(ServicesRegistryPath);
                if (servicesKey is null)
                    return result;

                foreach (var serviceName in servicesKey.GetSubKeyNames())
                {
                    try
                    {
                        using var key = servicesKey.OpenSubKey(serviceName);
                        if (key is null)
                            continue;

                        var displayName = key.GetValue("DisplayName") as string ?? string.Empty;
                        var description = key.GetValue("Description") as string ?? string.Empty;
                        if (!LooksLikeApache(serviceName) && !LooksLikeApache(displayName) && !LooksLikeApache(description))
                            continue;

                        var imagePath = key.GetValue("ImagePath") as string;

                        // Issue #63 (1.0.1.6): ключи -d/-f у служб, созданных обёрткой
                        // 1С/Apache monitor, лежат не в ImagePath, а в подразделе параметров
                        // службы — Parameters\ConfigArgs (REG_MULTI_SZ). Без них все службы
                        // сводились к дефолтному httpd.conf. Отсутствие подраздела/значения —
                        // прежнее поведение (читается только ImagePath).
                        var configArgs = ReadConfigArgs(key);
                        var instance = ParseWindowsServiceImagePath(
                            serviceName, displayName, description, imagePath, configArgs);
                        if (instance is null)
                            continue;

                        LoadConfIncludes(instance);
                        result.Add(instance);
                    }
                    catch
                    {
                        // Служба недоступна (нет прав/битая запись) — пропускаем.
                    }
                }
            }
            catch
            {
                // Реестр недоступен — пустой список, окно использует fallback.
            }

            // Хотя бы один инстанс с реальным конфигом — рекурсивно не требуется: fallback
            // выполняется в WebPublishViewModel, если список пуст.
            return result;
        }

        /// <summary>
        /// Читает <c>Parameters\ConfigArgs</c> службы (REG_MULTI_SZ или REG_SZ) и склеивает
        /// элементы через пробел: оба формата (массив «-d», «c:\Apache\Apache24», … и одна
        /// строка «-d c:\Apache\Apache24 -f conf\httpd27.conf») покрыты склейкой (issue #63).
        /// Отсутствие подраздела/значения/ошибка чтения — null (прежнее поведение).
        /// </summary>
        private static string? ReadConfigArgs(RegistryKey key)
        {
            try
            {
                using var parameters = key.OpenSubKey("Parameters");
                if (parameters is null)
                    return null;
                var value = parameters.GetValue("ConfigArgs");
                return value switch
                {
                    string[] args when args.Length > 0 => string.Join(" ", args),
                    string single when single.Trim().Length > 0 => single,
                    _ => null
                };
            }
            catch
            {
                // Подраздел/значение недоступны — работаем только по ImagePath.
                return null;
            }
        }

        private static void LoadConfIncludes(ApacheInstance instance)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(instance.ConfPath) || !File.Exists(instance.ConfPath))
                    return;
                var text = File.ReadAllText(instance.ConfPath);
                var confDirectory = Path.GetDirectoryName(instance.ConfPath) ?? string.Empty;
                PopulateIncludes(instance, text, confDirectory);
            }
            catch
            {
                // Некорректный конфиг не важен для списка инстансов.
            }
        }
    }
}
#endif