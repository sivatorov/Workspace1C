using System.IO;
using System.Text;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Считывает наименование и версию конфигурации 1С (COM-коннектор или эвристика по файлу базы).
/// </summary>
public static class ConfigurationInfoService
{
    /// <summary>
    /// Текст последней ошибки чтения через COM-коннектор (для вывода в UI).
    /// null — если COM-чтение не выполнялось или завершилось успешно.
    /// </summary>
    public static string? LastComError { get; private set; }

    /// <summary>
    /// ProgID COM-коннектора, который фактически использовался при последнем чтении
    /// (первый предпочтительный кандидат с учётом шаблона имени, issue #175). Выводится
    /// в UI при ошибке определения свойств конфигурации (issue #174), чтобы было видно,
    /// какой именно COM-коннектор пробовался. Пустая строка — COM не использовался.
    /// </summary>
    public static string? LastUsedProgId { get; private set; }

    /// <summary>
    /// Версия платформы базы, по которой разворачивался шаблон имени COM-коннектора
    /// при последнем чтении (issue #174). Пустая строка, если версия не задана.
    /// </summary>
    public static string? LastUsedPlatformVersion { get; private set; }

    /// <summary>
    /// Хост, через который выполнено последнее чтение сведений о конфигурации на Windows:
    /// «PowerShell 5.1 x64»/«x86» (issue #154). Null — чтение шло через агент, Linux-эвристику
    /// или ещё не выполнялось. Показывается в диагностике кнопки «Определить».
    /// </summary>
    public static string? LastUsedHost { get; private set; }

    /// <summary>
    /// Пытается прочитать имя и версию конфигурации для информационной базы.
    /// Сначала используется COM-коннектор (только на Windows; на Linux его заменяет
    /// реализация <c>OneCComConnector.Linux</c>, которая COM не использует — эвристика
    /// по файловой базе и пакетный режим конфигуратора), затем эвристика по файлу 1Cv8.1CD.
    /// <paramref name="mode"/> — режим, по которому выбираются учётные данные (issue #236);
    /// по умолчанию «Конфигуратор», т.к. сведения о конфигурации обычно читает конфигуратор.
    /// <paramref name="onStage"/> — обратный вызов смены этапа для диалога прогресса (issue #174).
    /// </summary>
    public static OneCConfigInfo? TryRead(Infobase ib, int? timeoutMs = null, Action<string>? onStage = null,
        OneCLaunchMode mode = OneCLaunchMode.Configurator)
    {
        if (ib is null) return null;
        var effectiveTimeout = ResolveTimeoutMs(timeoutMs);

        LastComError = null;
        LastUsedProgId = null;
        LastUsedPlatformVersion = null;
        LastUsedHost = null;
        try
        {
            var connector = AppServices.GetRequiredService<IOneCComConnector>();
            var viaCom = connector.ReadConfigurationInfo(ib, mode, effectiveTimeout, onStage);

            // Фиксируем, какой COM-коннектор/версия платформы фактически использовались
            // при попытке чтения (issue #174): это помогает понять, почему «Определить»
            // не сработало (например, шаблон имени дал неправильный ProgID).
            LastUsedProgId = connector.LastUsedProgId;
            LastUsedPlatformVersion = connector.LastUsedPlatformVersion;
            LastUsedHost = connector.LastUsedHost;

            if (viaCom is not null)
                return viaCom;
            LastComError = connector.LastError;
        }
        catch (Exception ex)
        {
            // COM недоступен / таймаут
            LastComError = ex.Message;
        }

        try
        {
            if (ib.Connection?.Type == ConnectionType.File)
                return TryReadFromFileBase(ib.Connection.FilePath);
        }
        catch
        {
            // ignore
        }

        return null;
    }

    /// <summary>
    /// Обновляет поля ConfigurationName / ConfigurationVersion, если удалось прочитать.
    /// Политика ручного ввода (issue #164): при <paramref name="overwriteExisting"/>=false
    /// (фоновое автополучение) непустые поля считаются введёнными пользователем и никогда
    /// не перезаписываются — заполняются только пустые. При true (явная команда пользователя
    /// «Обновить информацию») допускается перезапись. На Linux COM-чтение недоступно, поэтому
    /// значения остаются ручными, если эвристика/конфигуратор их не вернули.
    /// <paramref name="mode"/> — режим выбора учётных данных при чтении (issue #236).
    /// </summary>
    public static bool TryApply(Infobase ib, bool overwriteExisting = false,
        OneCLaunchMode mode = OneCLaunchMode.Configurator)
    {
        if (ib is null) return false;
        if (!overwriteExisting
            && !string.IsNullOrWhiteSpace(ib.ConfigurationName)
            && !string.IsNullOrWhiteSpace(ib.ConfigurationVersion))
            return false;

        var info = TryRead(ib, mode: mode);
        if (info is null) return false;

        return ApplyReadInfo(ib, info.Value, overwriteExisting);
    }

    /// <summary>
    /// Применяет уже прочитанные сведения к базе. Вынесено из <see cref="TryApply"/>,
    /// чтобы <see cref="ReadAndApply"/> не выполнял повторное COM-чтение: при падении
    /// агента и fallback в PowerShell второй проход означал бы ещё одно падение и запуск
    /// (issue #154). Политика <paramref name="overwriteExisting"/> та же, что и раньше.
    /// </summary>
    private static bool ApplyReadInfo(Infobase ib, OneCConfigInfo info, bool overwriteExisting)
    {
        var changed = false;
        if (!string.IsNullOrWhiteSpace(info.Name)
            && (overwriteExisting || string.IsNullOrWhiteSpace(ib.ConfigurationName)))
        {
            ib.ConfigurationName = info.Name.Trim();
            changed = true;
        }
        if (!string.IsNullOrWhiteSpace(info.Version)
            && (overwriteExisting || string.IsNullOrWhiteSpace(ib.ConfigurationVersion)))
        {
            ib.ConfigurationVersion = info.Version.Trim();
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Проверяет, соответствует ли имя ProgID COM-коннектора версии платформы базы
    /// (например, «V83.COMConnector» ↔ версия «8.3.x», «V85.COMConnector» ↔ «8.5.x»).
    /// Используется в диагностике «Определить свойства конфигурации» (issue #174), чтобы
    /// предупредить о том, что имя коннектора не соответствует реальной платформе базы.
    /// Возвращает true, если данные для сравнения отсутствуют (настаивать на несовпадении
    /// не на чем).
    /// </summary>
    public static bool ProgIdMatchesPlatform(string? progId, string? platformVersion)
    {
        if (string.IsNullOrWhiteSpace(progId) || string.IsNullOrWhiteSpace(platformVersion))
            return true;

        var ver = ExtractDigits(platformVersion);
        if (ver.Length < 2)
            return true;

        // Первые две цифры версии платформы образуют имя ProgID: «8.3…» → V83, «8.5…» → V85.
        var expected = "V" + ver[0] + ver[1] + ".";
        return progId.StartsWith(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractDigits(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            if (ch >= '0' && ch <= '9')
                sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Читает наименование и версию конфигурации и сразу применяет их к базе
    /// (по умолчанию перезаписывая уже заполненные значения). Возвращает прочитанные
    /// данные, либо null, если чтение не удалось. <paramref name="onStage"/> — обратный
    /// вызов смены этапа для диалога прогресса (issue #174). <paramref name="mode"/> —
    /// режим выбора учётных данных при чтении (issue #236); по умолчанию «Конфигуратор».
    /// </summary>
    public static OneCConfigInfo? ReadAndApply(Infobase ib, bool overwriteExisting = true, int? timeoutMs = null,
        Action<string>? onStage = null, OneCLaunchMode mode = OneCLaunchMode.Configurator)
    {
        if (ib is null) return null;
        var info = TryRead(ib, timeoutMs, onStage, mode);
        if (info is null) return null;
        // Применяем уже прочитанное значение, не вызывая TryApply: тот делает собственное
        // чтение, и одно нажатие «Определить» давало бы два COM-прохода (issue #154).
        ApplyReadInfo(ib, info.Value, overwriteExisting);
        return info;
    }

    /// <summary>
    /// Возвращает фактический таймаут чтения свойств конфигурации через COM (issue #174).
    /// Если вызывающий не задал значение явно — берётся настройка <see cref="AppSettings.ComDetectTimeoutMs"/>
    /// (по умолчанию 30000 мс), минимум 1000. Раньше 8000 мс было зашито в каждый уровень чтения,
    /// и первое COM-подключение к клиент-серверной базе на localhost (холодный старт сервера,
    /// лицензии HASP, создание сеанса пользователя) регулярно не укладывалось в этот срок, хотя
    /// в конфигураторе 1С то же подключение работало.
    /// </summary>
    private static int ResolveTimeoutMs(int? requested)
    {
        if (requested is { } v && v >= 1000)
            return v;

        try
        {
            var settings = AppServices.GetRequiredService<IInfobaseRepository>().LoadSettings();
            if (settings?.ComDetectTimeoutMs >= 1000)
                return settings.ComDetectTimeoutMs;
        }
        catch
        {
            // Настройки недоступны — остаёмся на значении по умолчанию.
        }

        return 30000;
    }

    /// <summary>
    /// Эвристика: в 1Cv8.1CD иногда встречаются читаемые UTF-8/UTF-16 строки с версией вида 1.2.3.4.
    /// Имя конфигурации надёжно так не извлечь — возвращаем только версию, если найдена.
    /// </summary>
    private static OneCConfigInfo? TryReadFromFileBase(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return null;

        string? cdPath = null;
        if (File.Exists(filePath) && filePath.EndsWith(".1CD", StringComparison.OrdinalIgnoreCase))
            cdPath = filePath;
        else if (Directory.Exists(filePath))
        {
            cdPath = Path.Combine(filePath, "1Cv8.1CD");
            if (!File.Exists(cdPath))
                cdPath = Directory.EnumerateFiles(filePath, "1Cv8.1CD", SearchOption.TopDirectoryOnly).FirstOrDefault();
        }

        if (string.IsNullOrEmpty(cdPath) || !File.Exists(cdPath))
            return null;

        // Читаем ограниченный объём (начало файла), ищем version-like строки
        const int maxBytes = 4 * 1024 * 1024;
        byte[] data;
        try
        {
            using var fs = new FileStream(cdPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var len = (int)Math.Min(fs.Length, maxBytes);
            data = new byte[len];
            _ = fs.Read(data, 0, len);
        }
        catch
        {
            return null;
        }

        var version = FindVersionString(data);
        if (string.IsNullOrEmpty(version))
            return null;

        return new OneCConfigInfo(string.Empty, version);
    }

    private static string? FindVersionString(byte[] data)
    {
        // Ищем ASCII-последовательности вида N.N.N.N
        var candidates = new List<string>();
        var sb = new StringBuilder();
        for (var i = 0; i < data.Length; i++)
        {
            var b = data[i];
            if ((b >= '0' && b <= '9') || b == '.')
            {
                sb.Append((char)b);
                if (sb.Length > 24)
                    sb.Clear();
            }
            else
            {
                if (sb.Length >= 5)
                {
                    var s = sb.ToString();
                    if (IsLikelyConfigVersion(s))
                        candidates.Add(s);
                }
                sb.Clear();
            }
        }

        // Берём «самую похожую» на версию конфигурации (4+ компонента)
        return candidates
            .OrderByDescending(c => c.Count(ch => ch == '.'))
            .ThenByDescending(c => c.Length)
            .FirstOrDefault();
    }

    private static bool IsLikelyConfigVersion(string s)
    {
        var parts = s.Split('.');
        if (parts.Length < 3 || parts.Length > 5) return false;
        foreach (var p in parts)
        {
            if (p.Length == 0 || p.Length > 6) return false;
            if (!int.TryParse(p, out _)) return false;
        }
        // отсекаем слишком мелкие вроде 1.0
        return parts.Length >= 3;
    }
}
