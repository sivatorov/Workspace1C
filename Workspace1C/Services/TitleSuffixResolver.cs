using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Workspace1C.Services;

/// <summary>
/// Подстановка переменных в дополнение заголовка окна (issue #28).
/// Чистый сервис без UI-зависимостей, тестируемый.
/// Известные токены: <see cref="ComputerNameToken"/>, <see cref="UserNameToken"/>,
/// <see cref="UserDomainToken"/>, <see cref="AppUserToken"/>, <see cref="ComputerIpToken"/>,
/// <see cref="DisplayServerToken"/>.
/// Неизвестные токены вида «%xxx%» в шаблоне сохраняются как есть — они не являются
/// опечаткой пользователя и могут быть добавлены в будущих версиях.
/// </summary>
public static class TitleSuffixResolver
{
    /// <summary>Имя компьютера (локальный узел).</summary>
    public const string ComputerNameToken = "%computer_name%";

    /// <summary>Имя пользователя Windows/Linux.</summary>
    public const string UserNameToken = "%user_name%";

    /// <summary>Домен (компьютерная группа) пользователя.</summary>
    public const string UserDomainToken = "%user_domain%";

    /// <summary>Имя активной учётной записи приложения (issue #164).</summary>
    public const string AppUserToken = "%app_user%";

    /// <summary>Первый IPv4-адрес локального интерфейса (не loopback).</summary>
    public const string ComputerIpToken = "%computer_ip%";

    /// <summary>Тип дисплейного сервера: x11/wayland на Linux, «windows» на Windows.</summary>
    public const string DisplayServerToken = "%display_server%";

    /// <summary>Все известные токены в порядке отображения в подсказке настроек.</summary>
    public static IReadOnlyList<string> Tokens { get; } = new[]
    {
        ComputerNameToken,
        UserNameToken,
        UserDomainToken,
        AppUserToken,
        ComputerIpToken,
        DisplayServerToken
    };

    /// <summary>
    /// Подставляет значения известных токенов в шаблон. Возвращает пустую строку,
    /// если шаблон пуст или после подстановки остались только пробелы.
    /// Неизвестные токены «%xxx%» сохраняются как есть.
    /// </summary>
    public static string Resolve(string template) => Resolve(template, null);

    /// <summary>
    /// Подставляет значения известных токенов в шаблон, включая <see cref="AppUserToken"/>
    /// (issue #164). Если имя активной учётной записи неизвестно (<paramref name="appUser"/>
    /// равен null или пуст), токен заменяется пустой строкой. Неизвестные токены «%xxx%»
    /// сохраняются как есть.
    /// </summary>
    public static string Resolve(string template, string? appUser)
    {
        if (string.IsNullOrWhiteSpace(template))
            return string.Empty;

        var result = template
            .Replace(ComputerNameToken, Safe(() => Environment.MachineName))
            .Replace(UserNameToken, Safe(() => Environment.UserName))
            .Replace(UserDomainToken, Safe(() => Environment.UserDomainName))
            .Replace(AppUserToken, appUser ?? string.Empty)
            .Replace(ComputerIpToken, ComputerIp())
            .Replace(DisplayServerToken, DisplayServer());

        return string.IsNullOrWhiteSpace(result) ? string.Empty : result.Trim();
    }

    /// <summary>
    /// Собирает полный заголовок окна: (необязательная метка редакции ПРО) + базовое
    /// имя + суффикс версии «v…» + (если дополнение включено и результат непустой)
    /// разделитель и дополнение.
    /// Устойчив к повторному вызову: если базовое имя уже заканчивается на суффикс
    /// версии (локаль может его содержать), суффикс не добавляется повторно; если
    /// базовое имя уже начинается с метки редакции, метка не задваивается.
    /// </summary>
    /// <param name="baseTitle">Локализованное имя приложения («Workspace 1C»).</param>
    /// <param name="version">Версия без префикса «v» (например «1.0.0.31»).</param>
    /// <param name="enabled">Включено ли дополнение заголовка (галочка в настройках).</param>
    /// <param name="template">Шаблон дополнения (токены подставляются).</param>
    /// <param name="separator">Разделитель между суффиксом версии и дополнением.</param>
    /// <param name="proMark">
    /// Метка редакции ПРО (обычно <see cref="Licensing.LicenseSummary.ProMark"/> — «★»).
    /// Непустая метка ставится префиксом перед названием программы как признак
    /// действующего лицензионного ключа ПРО; пустая — заголовок без изменений.
    /// </param>
    /// <param name="appUser">
    /// Имя активной учётной записи приложения для токена <see cref="AppUserToken"/>
    /// (issue #164); null или пусто — токен заменяется пустой строкой.
    /// </param>
    public static string Compose(
        string baseTitle,
        string version,
        bool enabled,
        string template,
        string separator = " — ",
        string proMark = "",
        string? appUser = null)
    {
        var versionSuffix = string.IsNullOrWhiteSpace(version)
            ? string.Empty
            : $" v{version.Trim()}";

        // Метка редакции ПРО — признак действующего ключа: ставится перед названием
        // программы. Повторный вызов (или локализованное имя) может уже содержать
        // метку с пробелом — тогда второй раз её не добавляем.
        var head = baseTitle;
        if (!string.IsNullOrEmpty(proMark)
            && !head.StartsWith(proMark + " ", StringComparison.Ordinal))
        {
            head = proMark + " " + head;
        }

        if (versionSuffix.Length > 0 && !head.EndsWith(versionSuffix, StringComparison.Ordinal))
            head += versionSuffix;

        if (!enabled)
            return head;

        var extra = Resolve(template, appUser);
        return extra.Length == 0 ? head : head + separator + extra;
    }

    /// <summary>Первый IPv4-адрес локального интерфейса (не loopback); пусто при недоступности.</summary>
    private static string ComputerIp()
    {
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;
                var props = adapter.GetIPProperties();
                foreach (var address in props.UnicastAddresses)
                {
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork
                        && !IPAddress.IsLoopback(address.Address))
                    {
                        return address.Address.ToString();
                    }
                }
            }
        }
        catch
        {
            return string.Empty;
        }

        return string.Empty;
    }

    /// <summary>
    /// Тип дисплейного сервера. Linux: значение переменной XDG_SESSION_TYPE (x11/wayland),
    /// иначе определяется по WAYLAND_DISPLAY/DISPLAY. Windows: «windows».
    /// </summary>
    private static string DisplayServer()
    {
#if LINUX
        var session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE")?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(session) && session is "x11" or "wayland")
            return session;
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            return "wayland";
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
            return "x11";
        return string.Empty;
#else
        return "windows";
#endif
    }

    private static string Safe(Func<string> getter)
    {
        try
        {
            return getter() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
