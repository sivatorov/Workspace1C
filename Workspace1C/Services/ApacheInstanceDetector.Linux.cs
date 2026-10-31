#if LINUX
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Workspace1C.Models;

namespace Workspace1C.Services
{
    /// <summary>
    /// Linux-часть <see cref="ApacheInstanceDetector"/> (issue #63): параметры инстанса
    /// берутся из вывода <c>apache2ctl -V</c>/<c>httpd -V</c> (ServerRoot/ServerConfigFile)
    /// и <c>apache2ctl -S</c>/<c>httpd -S</c> (DocumentRoot); fallback — стандартные пути
    /// Debian (<c>/etc/apache2/apache2.conf</c>, <c>/var/www</c>) и RHEL
    /// (<c>/etc/httpd/conf/httpd.conf</c>). Конфиг читается, include-директивы разбираются.
    /// </summary>
    public static partial class ApacheInstanceDetector
    {
        private static readonly (string Tool, string Args)[] VersionCommands =
        {
            ("apache2ctl", "-V"),
            ("httpd", "-V"),
            ("apache2", "-V"),
        };

        private static readonly string[] DefaultConfCandidates =
        {
            "/etc/apache2/apache2.conf",
            "/etc/httpd/conf/httpd.conf",
            "/etc/httpd/httpd.conf",
        };

        private static IReadOnlyList<ApacheInstance> DetectLinux()
        {
            var result = new List<ApacheInstance>();
            string? ctlV = null;
            string? ctlS = null;

            foreach (var (tool, args) in VersionCommands)
            {
                ctlV = RunCaptured(tool, args);
                if (!string.IsNullOrWhiteSpace(ctlV))
                    break;
            }

            if (!string.IsNullOrWhiteSpace(ctlV))
                ctlS = RunCaptured("apache2ctl", "-S") ?? RunCaptured("httpd", "-S");

            var defaultConf = PickDefaultConf();
            var instance = ParseApacheCtl(ctlV, ctlS, defaultConf, "/var/www");
            if (instance is null && !string.IsNullOrEmpty(defaultConf) && File.Exists(defaultConf))
            {
                // Утилита -V не запустилась, но конфиг стандартный стоит: создаём запасной
                // инстанс, чтобы окно публикации предложило рабочий вариант по умолчанию.
                instance = new ApacheInstance
                {
                    Id = "Apache (default)",
                    Name = "Apache",
                    Description = "Apache (default paths)",
                    ServerRoot = Path.GetDirectoryName(defaultConf) ?? string.Empty,
                    ConfPath = defaultConf,
                    Htdocs = "/var/www",
                    Kind = WebServerKind.Apache24
                };
            }

            if (instance is not null)
            {
                LoadConfIncludes(instance);
                result.Add(instance);
            }

            return result;
        }

        private static string PickDefaultConf()
        {
            foreach (var candidate in DefaultConfCandidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }
            return DefaultConfCandidates[0];
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

        private static string? RunCaptured(string fileName, string arguments)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                // Синхронное чтение после завершения процесса (аудит sync-over-async):
                // Task.WaitAll + .Result блокировали поток пула без нужды. Чтение ПОСЛЕ
                // WaitForExit/Kill безопасно — вывод процесса завершён и буферизован.
                if (!process.WaitForExit(5000))
                {
                    try { process.Kill(); } catch { /* процесс уже мог завершиться */ }
                }
                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                var output = (stdout + "\n" + stderr).Trim();
                return output.Length == 0 ? null : output;
            }
            catch
            {
                // apache2ctl/httpd не установлены или недоступны — fallback по путям.
                return null;
            }
        }
    }
}
#endif