using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>Вариант команды запуска внешней программы (первый найденный — запускается).</summary>
/// <param name="FileName">Исполняемый файл (имя в PATH или полный путь).</param>
/// <param name="Arguments">Аргументы по одному (передаются через ArgumentList, без shell).</param>
/// <param name="UseShellExecute">Windows: запуск через shell — нужен для консольного окна ssh.</param>
public sealed record RemoteCommand(string FileName, IReadOnlyList<string> Arguments, bool UseShellExecute = false);

/// <summary>
/// Быстрый доступ к серверу 1С по RDP и SSH из окна «Серверы 1С» (2.0, бесплатная
/// редакция, docs/plans/plan-v2-free-features.md, п.3). Построение команд — чистые
/// функции (тестируются); пароли не хранятся и в командную строку не попадают:
/// клиент RDP/SSH спрашивает их сам (или берёт из своих хранилищ/ключей).
/// </summary>
public static class RemoteAccessLauncher
{
    /// <summary>Хост сервера: <see cref="ManagementServer.Address"/> без порта 1С.</summary>
    public static string HostOf(ManagementServer server)
    {
        var address = (server?.Address ?? string.Empty).Trim();
        var colon = address.LastIndexOf(':');
        if (colon > 0 && colon < address.Length - 1 && int.TryParse(address[(colon + 1)..], out _)
            && address.IndexOf(':') == colon) // IPv6 без скобок не трогаем
            return address[..colon];
        return address;
    }

    /// <summary>Адрес RDP: явный <see cref="ManagementServer.RdpHost"/> или хост сервера.</summary>
    public static string RdpAddress(ManagementServer server) =>
        string.IsNullOrWhiteSpace(server.RdpHost) ? HostOf(server) : server.RdpHost.Trim();

    /// <summary>Цель SSH: явная <see cref="ManagementServer.SshTarget"/> или хост сервера.</summary>
    public static string SshTarget(ManagementServer server) =>
        string.IsNullOrWhiteSpace(server.SshTarget) ? HostOf(server) : server.SshTarget.Trim();

    /// <summary>
    /// Текст поля «SSH» карточки сервера: «цель» или «цель:порт» (порт — если не 22).
    /// </summary>
    public static string FormatSshTarget(ManagementServer? server)
    {
        if (server is null || string.IsNullOrWhiteSpace(server.SshTarget))
            return string.Empty;
        return server.SshPort is >= 1 and <= 65535 && server.SshPort != 22
            ? server.SshTarget.Trim() + ":" + server.SshPort.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : server.SshTarget.Trim();
    }

    /// <summary>
    /// Разбирает поле «SSH» карточки сервера («user@host», «host:2222», «user@host:2222»)
    /// в <see cref="ManagementServer.SshTarget"/> и <see cref="ManagementServer.SshPort"/>.
    /// Адрес IPv6 без скобок портом не считается.
    /// </summary>
    public static void ApplySshTarget(ManagementServer server, string? text)
    {
        var value = (text ?? string.Empty).Trim();
        server.SshTarget = value;
        server.SshPort = 0;
        var colon = value.LastIndexOf(':');
        if (colon > 0 && colon < value.Length - 1 && value.IndexOf(':') == colon
            && int.TryParse(value[(colon + 1)..], out var port) && port is >= 1 and <= 65535)
        {
            server.SshTarget = value[..colon];
            server.SshPort = port;
        }
    }

    /// <summary>
    /// Содержимое .rdp-файла для mstsc (нужен, чтобы передать имя пользователя: у mstsc
    /// нет ключа /u). Пароль в файл не пишется.
    /// </summary>
    public static string BuildRdpFile(ManagementServer server)
    {
        var lines = new List<string>
        {
            "full address:s:" + RdpAddress(server),
            "prompt for credentials:i:1",
            "screen mode id:i:2"
        };
        if (!string.IsNullOrWhiteSpace(server.RdpUser))
            lines.Add("username:s:" + server.RdpUser.Trim());
        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>Варианты команды RDP для платформы (по порядку предпочтения).</summary>
    /// <param name="rdpFilePath">Windows: путь к подготовленному .rdp-файлу (если задан пользователь).</param>
    public static IReadOnlyList<RemoteCommand> BuildRdpCommands(ManagementServer server, bool windows, string? rdpFilePath = null)
    {
        var address = RdpAddress(server);
        if (address.Length == 0)
            return Array.Empty<RemoteCommand>();
        var user = (server.RdpUser ?? string.Empty).Trim();

        if (windows)
        {
            return new[]
            {
                string.IsNullOrEmpty(rdpFilePath)
                    ? new RemoteCommand("mstsc.exe", new[] { "/v:" + address })
                    : new RemoteCommand("mstsc.exe", new[] { rdpFilePath! })
            };
        }

        var freeRdpArgs = new List<string> { "/v:" + address, "/dynamic-resolution", "/cert:tofu" };
        if (user.Length > 0)
            freeRdpArgs.Add("/u:" + user);
        var remminaUri = "rdp://" + (user.Length > 0 ? Uri.EscapeDataString(user) + "@" : string.Empty) + address;
        return new[]
        {
            new RemoteCommand("xfreerdp3", freeRdpArgs),
            new RemoteCommand("xfreerdp", freeRdpArgs),
            new RemoteCommand("remmina", new[] { "-c", remminaUri })
        };
    }

    /// <summary>Аргументы самой команды ssh: <c>[-p порт] цель</c>.</summary>
    public static IReadOnlyList<string> BuildSshArguments(ManagementServer server)
    {
        var args = new List<string>();
        if (server.SshPort is >= 1 and <= 65535 && server.SshPort != 22)
        {
            args.Add("-p");
            args.Add(server.SshPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        args.Add(SshTarget(server));
        return args;
    }

    /// <summary>Варианты запуска ssh в окне терминала для платформы (по порядку предпочтения).</summary>
    public static IReadOnlyList<RemoteCommand> BuildSshCommands(ManagementServer server, bool windows)
    {
        if (SshTarget(server).Length == 0)
            return Array.Empty<RemoteCommand>();
        var ssh = BuildSshArguments(server);

        if (windows)
        {
            return new[]
            {
                // Windows Terminal — новая вкладка/окно с ssh.
                new RemoteCommand("wt.exe", new[] { "ssh" }.Concat(ssh).ToList()),
                // Встроенный OpenSSH в отдельном консольном окне.
                new RemoteCommand("ssh.exe", ssh, UseShellExecute: true)
            };
        }

        return new[]
        {
            new RemoteCommand("x-terminal-emulator", new[] { "-e", "ssh" }.Concat(ssh).ToList()),
            new RemoteCommand("gnome-terminal", new[] { "--", "ssh" }.Concat(ssh).ToList()),
            new RemoteCommand("konsole", new[] { "-e", "ssh" }.Concat(ssh).ToList()),
            new RemoteCommand("xfce4-terminal", new[] { "-x", "ssh" }.Concat(ssh).ToList()),
            new RemoteCommand("xterm", new[] { "-e", "ssh" }.Concat(ssh).ToList())
        };
    }

    /// <summary>Открывает RDP-подключение к серверу. Возвращает текст ошибки или null.</summary>
    public static string? LaunchRdp(ManagementServer server)
    {
        var windows = OperatingSystem.IsWindows();
        string? rdpFile = null;
        if (windows && !string.IsNullOrWhiteSpace(server.RdpUser))
        {
            try
            {
                var dir = Path.Combine(Path.GetTempPath(), "Workspace1C", "rdp");
                Directory.CreateDirectory(dir);
                rdpFile = Path.Combine(dir, MakeSafeFileName(server.ToString()) + ".rdp");
                File.WriteAllText(rdpFile, BuildRdpFile(server));
            }
            catch
            {
                rdpFile = null; // без файла mstsc спросит пользователя сам
            }
        }
        return LaunchFirst(BuildRdpCommands(server, windows, rdpFile), windows,
            "RemoteAccess.RdpClientNotFound");
    }

    /// <summary>Открывает SSH-сессию с сервером в терминале. Возвращает текст ошибки или null.</summary>
    public static string? LaunchSsh(ManagementServer server)
    {
        var windows = OperatingSystem.IsWindows();
        return LaunchFirst(BuildSshCommands(server, windows), windows, "RemoteAccess.SshClientNotFound");
    }

    private static string? LaunchFirst(IReadOnlyList<RemoteCommand> commands, bool windows, string notFoundKey)
    {
        if (commands.Count == 0)
            return Localization.LocalizationManager.T("RemoteAccess.NoAddress");

        foreach (var command in commands)
        {
            if (!windows && !ExistsInPath(command.FileName))
                continue;
            try
            {
                var info = new ProcessStartInfo(command.FileName) { UseShellExecute = command.UseShellExecute };
                foreach (var arg in command.Arguments)
                    info.ArgumentList.Add(arg);
                using var process = Process.Start(info);
                if (process is not null)
                    return null;
            }
            catch
            {
                // Не найден (wt.exe нет на старых Windows) — пробуем следующий вариант.
            }
        }
        return Localization.LocalizationManager.T(notFoundKey);
    }

    private static bool ExistsInPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir =>
            {
                try { return File.Exists(Path.Combine(dir, fileName)); }
                catch { return false; }
            });
    }

    private static string MakeSafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string((name ?? "server").Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return safe.Length == 0 ? "server" : safe;
    }
}
