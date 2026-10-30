using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// ViewModel окна «Публикация на веб-сервере» (выпуск 1.0.0.69): параметры webinst
/// для выбранной базы, публикация, удаление публикации и журнал вывода утилиты.
/// С 1.0.0.71 (#63) список веб-серверов дополнен обнаруженными инстансами Apache
/// (служба/конфиг), при выборе инстанса автозаполняются конфигурация и каталог
/// публикации, а пользователь может выбрать конфиг-файл для webinst: основной conf
/// или include-файл.
/// </summary>
public sealed class WebPublishViewModel : ViewModelBase
{
    private readonly record struct ServerOption(WebServerKind Kind, ApacheInstance? Instance);

    private readonly IReadOnlyList<Infobase> _infobases;
    private readonly IReadOnlyList<ServerOption> _servers;
    private readonly StringBuilder _log = new();
    private readonly List<string> _confTargetPaths = new();

    private int _selectedIndex = -1;
    private int _serverIndex;
    private int _confTargetIndex = -1;
    private ApacheInstance? _selectedInstance;
    private ApacheInstance? _fallbackInstance;
    private bool _suppressConfPathRebuild;
    private string _virtualDirectory = string.Empty;
    private string _physicalDirectory = string.Empty;
    private string _apacheConfPath = string.Empty;
    private string _connectionString = string.Empty;
    private readonly ObservableCollection<string> _confTargets = new();
    private bool _isBusy;

    public WebPublishViewModel(IEnumerable<Infobase> infobases, Infobase? selected,
        IReadOnlyList<ApacheInstance>? apacheInstances = null)
    {
        _infobases = (infobases ?? Array.Empty<Infobase>())
            .Where(ib => ib is not null && ib.Connection.Type != ConnectionType.WebServer)
            .OrderBy(ib => ib.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        InfobaseNames = _infobases.Select(ib => ib.Name).ToList();

        var instances = apacheInstances ?? ApacheInstanceDetector.Detect();
        var servers = new List<ServerOption>();
        if (OperatingSystem.IsWindows())
            servers.Add(new ServerOption(WebServerKind.Iis, null));
        foreach (var instance in instances ?? Array.Empty<ApacheInstance>())
            servers.Add(new ServerOption(instance.Kind, instance));
        // Fallback: инстансы не найдены — остаётся вариант Apache по умолчанию
        // (конфиг/каталог — прежние SuggestApacheConf/SuggestPhysicalDirectory).
        if (servers.All(s => s.Kind == WebServerKind.Iis))
            servers.Add(new ServerOption(WebServerKind.Apache24, null));
        _servers = servers;
        ServerOptions = _servers.Select(DisplayServerOption).ToList();

        TraceServerOptions(instances);

        _serverIndex = 0;
        ApplyCurrentServerDefaults(initial: true);

        var index = selected is null ? -1 : _infobases.ToList().FindIndex(ib => ib.Id == selected.Id);
        SelectedIndex = index >= 0 ? index : (_infobases.Count > 0 ? 0 : -1);
    }

    public IReadOnlyList<string> InfobaseNames { get; }

    /// <summary>Отображаемые варианты веб-серверов: «IIS» (Windows) + инстансы Apache.</summary>
    public IReadOnlyList<string> ServerOptions { get; }

    /// <summary>Варианты конфиг-файла webinst: основной conf и include-файлы (пусто — не применимо).</summary>
    public ObservableCollection<string> ConfTargets => _confTargets;

    /// <summary>Абсолютные пути конфиг-файлов (для webinst): основной и include-файлы графа.</summary>
    internal IReadOnlyList<string> ConfTargetPaths => _confTargetPaths;

    /// <summary>Выбран ли инстанс Apache (а не IIS или fallback Apache).</summary>
    public bool IsInstanceSelected => _selectedInstance is not null;

    /// <summary>Есть ли выбор конфиг-файла для webinst.</summary>
    public bool HasConfTargets => _confTargets.Count > 0;

    public Infobase? SelectedInfobase =>
        _selectedIndex >= 0 && _selectedIndex < _infobases.Count ? _infobases[_selectedIndex] : null;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (!SetProperty(ref _selectedIndex, value))
                return;
            var ib = SelectedInfobase;
            if (ib is null)
                return;
            VirtualDirectory = WebPublishService.SuggestVirtualDirectory(ib.Name);
            ConnectionString = WebPublishService.ConnectionStringFor(ib);
        }
    }

    public int ServerIndex
    {
        get => _serverIndex;
        set
        {
            if (!SetProperty(ref _serverIndex, Math.Clamp(value, 0, _servers.Count - 1)))
                return;
            ApplyCurrentServerDefaults(initial: false);
        }
    }

    public int ConfTargetIndex
    {
        get => _confTargetIndex;
        set
        {
            if (_servers.Count == 0 || _confTargetPaths.Count == 0)
                value = -1;
            else
                value = Math.Clamp(value, 0, _confTargetPaths.Count - 1);
            if (!SetProperty(ref _confTargetIndex, value))
                return;
            if (_confTargetIndex >= 0 && _confTargetIndex < _confTargetPaths.Count)
            {
                // Выбор готового пункта списка не должен перечитывать конфиг заново
                // (см. ApacheConfPath) — значение уже получено из построенного графа.
                _suppressConfPathRebuild = true;
                try { ApacheConfPath = _confTargetPaths[_confTargetIndex]; }
                finally { _suppressConfPathRebuild = false; }
            }
            // Issue #63: при смене файла webinst пересчитываем каталог публикации и
            // порт/URL — значения по умолчанию берутся только из конфига службы и его
            // include-графа (DocumentRoot/Listen инстанса), выбор файла их не «сбрасывает».
            RebuildPhysicalForCurrent();
            OnPropertyChanged(nameof(PhysicalDirectory));
            OnPropertyChanged(nameof(Url));
        }
    }

    public bool IsApache => _servers.Count > 0 && _servers[_serverIndex].Kind != WebServerKind.Iis;

    public string VirtualDirectory
    {
        get => _virtualDirectory;
        set
        {
            if (SetProperty(ref _virtualDirectory, value ?? string.Empty))
            {
                ApplyPhysicalForCurrent();
                OnPropertyChanged(nameof(Url));
            }
        }
    }

    public string PhysicalDirectory { get => _physicalDirectory; set => SetProperty(ref _physicalDirectory, value ?? string.Empty); }

    /// <summary>
    /// Файл конфигурации Apache для webinst. При РУЧНОЙ правке поля (не из выпадающего
    /// списка ConfTargets) конфиг перечитывается, а список файлов и значения по умолчанию
    /// перестраиваются — выделенная строка:
    /// issue #63, комментарий 7OH 2026-10-28 «файла в списке нет»: даже когда детектор
    /// инстансов не нашёл службу (fallback-ветка), введённый/выбранный вручную конфиг
    /// обязан дать список «основной conf + include-файлы (1c8Aliases*.conf)».
    /// </summary>
    public string ApacheConfPath
    {
        get => _apacheConfPath;
        set
        {
            var newValue = value ?? string.Empty;
            if (!SetProperty(ref _apacheConfPath, newValue))
                return;
            if (_suppressConfPathRebuild)
                return;
            RebuildFromEditedConfPath();
        }
    }

    public string ConnectionString { get => _connectionString; set => SetProperty(ref _connectionString, value ?? string.Empty); }

    /// <summary>
    /// Инстанс, из которого берутся значения по умолчанию (DocumentRoot/Listen): для
    /// обнаруженной службы — <see cref="_selectedInstance"/>, для fallback-ветки/ручного
    /// конфига — <see cref="_fallbackInstance"/> (построен из введённого conf-файла).
    /// </summary>
    private ApacheInstance? EffectiveInstance => _fallbackInstance ?? _selectedInstance;

    /// <summary>Порт слушателя инстанса Apache (LISTEN); null — 80.</summary>
    private int? ListenPort => EffectiveInstance?.ListenPorts.FirstOrDefault();

    /// <summary>Адрес опубликованной базы (для Apache учитывается порт LISTEN — issue #63).</summary>
    public string Url => WebPublishService.LocalUrl(VirtualDirectory, ListenPort);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                OnPropertyChanged(nameof(IsIdle));
        }
    }

    public bool IsIdle => !_isBusy;

    public string LogText => _log.ToString();

    public void AppendLog(string message)
    {
        _log.Append(DateTime.Now.ToString("HH:mm:ss")).Append("  ").AppendLine(message);
        OnPropertyChanged(nameof(LogText));
    }

    public WebPublishRequest BuildRequest() => new()
    {
        Server = _servers[_serverIndex].Kind,
        VirtualDirectory = VirtualDirectory,
        PhysicalDirectory = PhysicalDirectory,
        ApacheConfPath = ApacheConfPath,
        ConnectionString = ConnectionString
    };

    public Task<bool> PublishAsync() => RunAsync(publish: true);

    public Task<bool> DeleteAsync() => RunAsync(publish: false);

    /// <summary>Показывает, выбран ли именно обнаруженный инстанс Apache (для внутренних тестов).</summary>
    internal ApacheInstance? SelectedApacheInstance => _selectedInstance;

    /// <summary>Индекс пункта в ServerOptions для конкретного инстанса (-1 — не найден).</summary>
    internal int ServerIndexFor(ApacheInstance instance)
    {
        for (var i = 0; i < _servers.Count; i++)
        {
            if (ReferenceEquals(_servers[i].Instance, instance))
                return i;
        }
        return -1;
    }

    private string DisplayServerOption(ServerOption option)
    {
        if (option.Kind == WebServerKind.Iis)
            return LocalizationManager.T("WebPublish.Server.Iis");

        var version = option.Kind == WebServerKind.Apache22 ? "2.2" : "2.4";
        if (option.Instance is { } instance && !string.IsNullOrWhiteSpace(instance.Name))
        {
            // Issue #63: имя конфиг-файла в подписи инстанса — при нескольких службах
            // Apache (httpd.conf / conf\httpd27.conf) видно, что значения по умолчанию
            // берутся из конфига конкретной службы, а не из «обычного» httpd.conf.
            if (!string.IsNullOrWhiteSpace(instance.ConfPath))
            {
                return string.Format(LocalizationManager.T("WebPublish.ApacheInstanceOptionWithConf"),
                    version, instance.Name, Path.GetFileName(instance.ConfPath));
            }
            return string.Format(LocalizationManager.T("WebPublish.ApacheInstanceOption"), version, instance.Name);
        }
        return string.Format(LocalizationManager.T("WebPublish.ApacheDefaultOption"), version);
    }

    private void ApplyCurrentServerDefaults(bool initial)
    {
        if (_servers.Count == 0)
            return;

        var option = _servers[_serverIndex];
        _selectedInstance = option.Instance;
        _fallbackInstance = null;

        if (option.Instance is { } instance)
        {
            BuildConfTargets(instance);
            // Каталог публикации — htdocs инстанса; имя виртуального каталога добавляется позже.
            RebuildPhysicalForCurrent();
        }
        else
        {
            // Fallback-ветка (issue #63, «файла в списке нет»): детектор инстансов не
            // нашёл службу/конфиг — но список файлов для webinst обязан строиться и
            // здесь, иначе пользователь снова не увидит 1c8Aliases*.conf. Конфиг по
            // умолчанию перечитывается через тот же детектор-парсер (PopulateIncludes),
            // а при ручной правке ApacheConfPath список перестраивается заново.
            var fallbackConf = WebPublishService.SuggestApacheConf();
            _apacheConfPath = fallbackConf;
            ApplyManualConfDefaults(fallbackConf, option.Kind);
            TracePublish("fallback-ветка: инстансы не найдены (или выбран «Apache по умолчанию»), " +
                         "конфиг для webinst — «" + fallbackConf + "»");
            RebuildPhysicalForCurrent();
        }

        OnPropertyChanged(nameof(ConfTargets));
        OnPropertyChanged(nameof(ConfTargetIndex));
        OnPropertyChanged(nameof(HasConfTargets));
        OnPropertyChanged(nameof(IsInstanceSelected));
        OnPropertyChanged(nameof(IsApache));
        OnPropertyChanged(nameof(PhysicalDirectory));
        OnPropertyChanged(nameof(ApacheConfPath));
        OnPropertyChanged(nameof(Url));

        _ = initial;
    }

    /// <summary>
    /// Перестраивает список файлов webinst (ConfTargets) и значения по умолчанию из
    /// конфига, который пользователь ввёл/выбрал ВРУЧНУЮ в поле ApacheConfPath
    /// (issue #63: fallback-ветка и ручной ввод). Работает через
    /// <see cref="WebPublishService.BuildInstanceFromConf"/> — тот же парсер include-графа,
    /// что и для обнаруженных инстансов.
    /// </summary>
    private void RebuildFromEditedConfPath()
    {
        if (_servers.Count == 0)
            return;
        var option = _servers[_serverIndex];
        if (option.Kind == WebServerKind.Iis)
            return;
        if (string.IsNullOrWhiteSpace(_apacheConfPath))
            return;

        _suppressConfPathRebuild = true;
        try
        {
            TracePublish("ручная правка конфига webinst: «" + _apacheConfPath + "»");
            ApplyManualConfDefaults(_apacheConfPath, option.Kind);

            OnPropertyChanged(nameof(ConfTargets));
            OnPropertyChanged(nameof(ConfTargetIndex));
            OnPropertyChanged(nameof(HasConfTargets));
            OnPropertyChanged(nameof(IsInstanceSelected));
            OnPropertyChanged(nameof(PhysicalDirectory));
            OnPropertyChanged(nameof(ApacheConfPath));
            OnPropertyChanged(nameof(Url));
            RebuildPhysicalForCurrent();
        }
        finally
        {
            _suppressConfPathRebuild = false;
        }
    }

    /// <summary>
    /// Fallback-ветка: строит ConfTargets «основной конфиг + include-файлы (в т.ч.
    /// 1c8Aliases*.conf)» по файлу на диске. Если файла нет — список остаётся пустым,
    /// окно не падает (issue #63).
    /// </summary>
    private void ApplyManualConfDefaults(string confPath, WebServerKind kind)
    {
        var instance = WebPublishService.BuildInstanceFromConf(confPath, kind);
        _fallbackInstance = instance;
        if (instance is null)
        {
            _confTargetPaths.Clear();
            _confTargets.Clear();
            _confTargetIndex = -1;
            TracePublish(string.IsNullOrWhiteSpace(confPath)
                ? "конфиг не задан — список файлов webinst пуст"
                : "конфиг не прочитан (файл отсутствует/недоступен): «" + confPath + "» — список файлов webinst пуст");
            return;
        }

        BuildConfTargets(instance);
        TracePublish($"конфиг построен: {instance.ConfPath}; " +
                     $"DocumentRoot={instance.DocumentRoot}; Listen=[{string.Join(",", instance.ListenPorts ?? Array.Empty<int>())}]");
    }

    private void BuildConfTargets(ApacheInstance instance)
    {
        _confTargetPaths.Clear();
        _confTargets.Clear();

        // Файлы для webinst: основной конфиг службы (из -f) и включаемые файлы графа
        // Include (в т.ч. используемый файл алиасов 1c8Aliases*.conf) — issue #63.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(instance.ConfPath))
        {
            seen.Add(NormalizeTargetKey(instance.ConfPath));
            _confTargetPaths.Add(instance.ConfPath);
            _confTargets.Add(string.Format(
                LocalizationManager.T("WebPublish.ConfTargetMain"),
                Path.GetFileName(instance.ConfPath)));
            TracePublish("основной конфиг в списке webinst: " + instance.ConfPath);
        }
        var aliasFileInGraph = false;
        foreach (var include in instance.IncludeFiles ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(include))
                continue;
            if (!seen.Add(NormalizeTargetKey(include)))
                continue;
            _confTargetPaths.Add(include);
            _confTargets.Add(string.Format(
                LocalizationManager.T("WebPublish.ConfTargetInclude"),
                Path.GetFileName(include)));
            aliasFileInGraph |= IsAliasFileName(include);
            TracePublish("include-файл графа в списке webinst: " + include);
        }

        // Раскрытый Define-граф (issue #63, «а переменные из файла кто подставлять будет
        // сейчас?») — при отладке видно, по каким значениям резолвятся Include/DocumentRoot
        // и номер инстанса (OneCPort → «27» при fallback-поиске файла алиасов).
        if (instance.Defines is { Count: > 0 })
        {
            foreach (var pair in instance.Defines)
                TracePublish($"Define {pair.Key} = {pair.Value}");
        }

        // Fallback (issue #63, «файл алиаса по-прежнему не вернулся»): файл алиасов
        // 1c8Aliases*.conf может подключаться через IncludeOptional вне графа Include или
        // маску каталога (conf\aliases\*.conf), которую не удалось раскрыть. Если его нет
        // в Include-графе — добавляем РОВНО ОДИН файл, совпадающий с include-строкой
        // конфига службы («Include ${SRVROOT}/conf/1c8Aliases${OneCPort}.conf» — по имени
        // из DocumentRoot/Listen/имени конфига), а НЕ все 1c8Aliases*.conf каталога
        // (комментарий 7OH 2026-10-26: «Зачем мне все файлы алиасов? … вот его и ожидаем
        // увидеть — только тот, который используется»).
        if (!aliasFileInGraph)
        {
            var alias = FindUsedAliasFileNear(instance);
            if (alias is not null && seen.Add(NormalizeTargetKey(alias)))
            {
                _confTargetPaths.Add(alias);
                _confTargets.Add(string.Format(
                    LocalizationManager.T("WebPublish.ConfTargetInclude"),
                    Path.GetFileName(alias)));
                TracePublish("fallback: файл алиасов добавлен в список webinst: " + alias);
            }
            else
            {
                TracePublish("fallback: файл алиасов по include-строке/имени инстанса не найден");
            }
        }

        // issue #63 (комментарий 7OH 2026-10-30): при наличии в списке файла алиасов
        // 1c8Aliases*.conf — выбирать ЕГО по умолчанию (webinst пишет блок публикации
        // именно туда, как уже сделано в остальных записях файла).
        _confTargetIndex = DefaultConfTargetIndex();
        _apacheConfPath = _confTargetIndex >= 0 && _confTargetIndex < _confTargetPaths.Count
            ? _confTargetPaths[_confTargetIndex]
            : string.Empty;
    }

    /// <summary>
    /// Индекс файла webinst по умолчанию (issue #63): существующий конфиг-файл алиасов
    /// «1c8Aliases*» (в т.ч. добавленный fallback-поиском), иначе — основной conf.
    /// </summary>
    private int DefaultConfTargetIndex()
    {
        for (var i = 0; i < _confTargetPaths.Count; i++)
        {
            if (IsAliasFileName(_confTargetPaths[i]))
            {
                TracePublish("файл webinst по умолчанию — конфиг алиасов 1С: " + _confTargetPaths[i]);
                return i;
            }
        }
        return _confTargetPaths.Count > 0 ? 0 : -1;
    }

    /// <summary>INFO-запись направления «Публикация на веб-сервере» (issue #63, комментарий
    /// 7OH 2026-10-28 «давайте отладку и тут включать подробную»): пишется в журнал
    /// приложения с префиксом «[CM_PUBLISH]» только при включённых категории CM_PUBLISH
    /// и общем выключателе CM_ON (записи выполненных операций — в собственном логе окна).</summary>
    private static void TracePublish(string message) => Log.Info(LogCategory.Publish, message);

    /// <summary>
    /// Трассирует результат обнаружения инстансов (issue #63): для каждого найденного —
    /// имя службы, конфиг и ServerRoot; при пустом списке — причину перехода на
    /// fallback-ветку (по логу сразу видно, что окно работает «по умолчанию»).
    /// </summary>
    private void TraceServerOptions(IReadOnlyList<ApacheInstance>? instances)
    {
        var found = instances ?? Array.Empty<ApacheInstance>();
        if (found.Count == 0)
        {
            TracePublish("инстансы Apache не обнаружены (службы httpd не найдены/реестр недоступен) — " +
                         "доступен только fallback «Apache по умолчанию»");
            return;
        }
        foreach (var instance in found)
        {
            // Issue #63, комментарий 7OH от 2026-10-29: «Добавляем в отладку исходные
            // данные службы» — пишем и исходную строку ImagePath: по ней видно, что
            // реально записано в службе (в т.ч. -d/-f), и почему конфиг тот или иной.
            var imagePath = string.IsNullOrWhiteSpace(instance.RawCommandLine)
                ? "(пусто)"
                : instance.RawCommandLine.Replace("\r", "\\r").Replace("\n", "\\n");
            TracePublish($"инстанс Apache найден: служба={instance.Name}, " +
                         $"конфиг={instance.ConfPath}, ServerRoot={instance.ServerRoot}, ImagePath={imagePath}");
        }
    }

    /// <summary>Имя файла алиасов 1С: «1c8Aliases27.conf» и т.п.</summary>
    private static bool IsAliasFileName(string? path)
        => !string.IsNullOrWhiteSpace(path)
           && Path.GetFileName(path).StartsWith("1c8Aliases", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Fallback (issue #63): единственный используемый файл алиасов 1С «1c8Aliases<n>.conf»,
    /// если он не подключён через Include-граф (IncludeOptional вне графа / маска каталога не
    /// раскрылась / переменная ${…} не определилась). Имена кандидатов берутся из
    /// Include-строк графа («Include "${SRVROOT}/conf/1c8Aliases${OneCPort}.conf"») —
    /// «Его можно же найти в строках с include?» (7OH) — и из номера <n> (OneCPort):
    /// DocumentRoot («…\htdocs\27»), имени конфига службы («httpd27.conf» → «27») и портов
    /// Listen («80${OneCPort}» → 8027 → «27»). Остальные 1c8Aliases*.conf каталога в список
    /// НЕ попадают. null — файл не найден.
    /// </summary>
    private static string? FindUsedAliasFileNear(ApacheInstance instance)
    {
        // Include-цели с полным путём (например «c:\Apache\Apache24\conf\1c8Aliases27.conf»,
        // раскрытые по финальному Define-графу — см. ApacheInstanceDetector.PopulateIncludes):
        // файл алиасов может лежать ВНЕ стандартных каталогов поиска (другой диск,
        // conf\vhosts\...), поэтому сначала проверяем точный путь Include-строки напрямую.
        foreach (var pattern in instance.AliasTargetPatterns ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(pattern) || pattern.IndexOf("${", StringComparison.Ordinal) >= 0)
                continue;
            if (!IsRootedPath(pattern))
                continue;
            try
            {
                if (File.Exists(pattern))
                    return Path.GetFullPath(pattern);
            }
            catch
            {
                // Файл недоступен — пробуем следующие кандидаты.
            }
        }

        foreach (var fileName in CandidateAliasFileNames(instance))
        {
            var file = FindExistingAliasFile(instance.ConfPath, fileName);
            if (file is not null)
                return file;
        }
        return null;
    }

    /// <summary>
    /// Имена файлов-кандидатов алиасов для fallback (по возрастанию приоритета):
    /// полностью раскрытые Include-цели графа («…\1c8Aliases27.conf»), шаблоны с
    /// нераскрытыми ${…} (подставляются известные номера инстанса), затем типовое
    /// «1c8Aliases<n>.conf» по номеру из DocumentRoot/конфига/Listen.
    /// </summary>
    private static IEnumerable<string> CandidateAliasFileNames(ApacheInstance instance)
    {
        var suffixes = CandidateAliasSuffixes(instance).ToList();
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pattern in instance.AliasTargetPatterns ?? Array.Empty<string>())
        {
            string? fileName;
            try
            {
                fileName = Path.GetFileName(pattern.Trim());
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrEmpty(fileName) || !fileName.StartsWith("1c8Aliases", StringComparison.OrdinalIgnoreCase))
                continue;

            if (fileName.IndexOf("${", StringComparison.Ordinal) < 0)
            {
                if (emitted.Add(fileName))
                    yield return fileName;
                continue;
            }

            // Шаблон с переменными («1c8Aliases${OneCPort}.conf»): подставляем каждый
            // известный номер инстанса и ищем файл рядом с конфигом.
            foreach (var suffix in suffixes)
            {
                var candidate = SubstituteAllVariables(fileName, suffix);
                if (candidate.IndexOf("${", StringComparison.Ordinal) < 0 && emitted.Add(candidate))
                    yield return candidate;
            }
        }

        foreach (var suffix in suffixes)
        {
            var fileName = "1c8Aliases" + suffix + ".conf";
            if (emitted.Add(fileName))
                yield return fileName;
        }
    }

    /// <summary>Заменяет все вхождения «${Name}» в тексте на одно значение (без Regex).</summary>
    private static string SubstituteAllVariables(string text, string value)
    {
        var result = new StringBuilder(text.Length + 16);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '$' && i + 1 < text.Length && text[i + 1] == '{')
            {
                var closing = text.IndexOf('}', i + 2);
                if (closing > i + 2)
                {
                    result.Append(value);
                    i = closing;
                    continue;
                }
            }
            result.Append(text[i]);
        }
        return result.ToString();
    }

    /// <summary>Кандидаты номера инстанса «n» для имени файла «1c8Aliases<n>.conf».</summary>
    private static IEnumerable<string> CandidateAliasSuffixes(ApacheInstance instance)
    {
        var suffixes = new List<string>();
        void Add(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            text = text.Trim();
            if (text.Length == 0 || !text.All(c => c is >= '0' and <= '9'))
                return;
            if (!suffixes.Contains(text, StringComparer.Ordinal))
                suffixes.Add(text);
        }

        // DocumentRoot «…\htdocs\27» → «27».
        if (!string.IsNullOrWhiteSpace(instance.DocumentRoot))
        {
            try
            {
                Add(Path.GetFileName(instance.DocumentRoot.TrimEnd('\\', '/')));
            }
            catch
            {
                // Невалидный путь — пропускаем кандидат.
            }
        }

        // Имя конфига службы «httpd27.conf» → цифровой хвост «27».
        if (!string.IsNullOrWhiteSpace(instance.ConfPath))
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(instance.ConfPath);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var digitStart = name.IndexOfAny(new[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' });
                    if (digitStart >= 0)
                        Add(name.Substring(digitStart));
                }
            }
            catch
            {
                // Невалидное имя — пропускаем кандидат.
            }
        }

        // Имя конфига из строки запуска службы («-f conf\httpd27.conf» → «httpd27.conf» →
        // «27») — issue #63, последний комментарий 7OH: у пользователя все службы Apache
        // (2.4_51/_54/_55 и др.) определялись с дефолтным httpd.conf, т.к. -f из ImagePath
        // не извлекался. Даже в этом случае номер инстанса из строки запуска обязан
        // дать суффикс для fallback-поиска файла алиасов 1c8Aliases27.conf.
        if (!string.IsNullOrWhiteSpace(instance.RawCommandLine))
        {
            var rawConfName = ApacheInstanceDetector.ExtractConfFileNameFromCommandLine(instance.RawCommandLine);
            if (!string.IsNullOrWhiteSpace(rawConfName))
            {
                var rawName = Path.GetFileNameWithoutExtension(rawConfName);
                if (!string.IsNullOrWhiteSpace(rawName))
                {
                    var digitStart = rawName.IndexOfAny(new[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' });
                    if (digitStart >= 0)
                        Add(rawName.Substring(digitStart));
                }
            }
        }

        // Listen «80${OneCPort}» → 8027 → «27»; «${OneCPort}» → 8027 или 27 → «27».
        foreach (var port in instance.ListenPorts ?? Array.Empty<int>())
        {
            if (port is < 1 or > 65535)
                continue;
            var text = port.ToString();
            if (text.Length >= 4 && text.StartsWith("80", StringComparison.Ordinal))
                Add(text.Substring(2));
            else if (text.Length >= 3 && text.StartsWith("8", StringComparison.Ordinal))
                Add(text.Substring(1));
            else
                Add(text);
        }

        // Значения Define «Define OneCPort 27» (issue #63): прямой признак номера
        // инстанса, даже когда DocumentRoot/Listen/Include-граф из конфига не дали
        // суффикс (например, файл с переменными не прочитался). Нечисловые значения
        // (SRVROOT, пути) отбрасываются внутри Add.
        if (instance.Defines is not null)
        {
            foreach (var value in instance.Defines.Values)
                Add(value);
        }

        return suffixes;
    }

    /// <summary>
    /// Существующий файл с указанным именем рядом с конфигом службы: каталог конфига,
    /// conf\aliases, ServerRoot\conf\aliases и ServerRoot\aliases. Пути с неразрешёнными
    /// ${...} пропускаются. null — файл не найден.
    /// </summary>
    private static string? FindExistingAliasFile(string? confPath, string fileName)
    {
        foreach (var dir in AliasSearchDirs(confPath))
        {
            string path;
            try
            {
                path = Path.Combine(dir, fileName);
            }
            catch
            {
                continue;
            }
            try
            {
                if (File.Exists(path))
                    return path;
            }
            catch
            {
                // Файл недоступен — пробуем следующий каталог.
            }
        }
        return null;
    }

    /// <summary>Каталоги поиска файлов алиасов рядом с конфигом службы.</summary>
    private static IEnumerable<string> AliasSearchDirs(string? confPath)
    {
        if (string.IsNullOrWhiteSpace(confPath))
            yield break;
        string? confDir;
        try
        {
            confDir = Path.GetDirectoryName(confPath);
        }
        catch
        {
            yield break;
        }
        if (string.IsNullOrWhiteSpace(confDir))
            yield break;

        var dirs = new List<string> { confDir };
        try
        {
            dirs.Add(Path.Combine(confDir, "aliases"));
            var serverRoot = Path.GetDirectoryName(confDir);
            if (!string.IsNullOrWhiteSpace(serverRoot))
            {
                dirs.Add(Path.Combine(serverRoot, "conf", "aliases"));
                dirs.Add(Path.Combine(serverRoot, "aliases"));
            }
        }
        catch
        {
            // Невалидный путь — используем только каталог конфига.
        }

        foreach (var dir in dirs.Distinct(StringComparer.OrdinalIgnoreCase))
            yield return dir;
    }

    /// <summary>
    /// Абсолютный ли путь: «C:\...» / «C:/...», UNC «\\...» или POSIX «/...». Нужен и при
    /// кросс-сборке под Linux, где <see cref="Path.IsPathRooted"/> не распознаёт диск Windows.
    /// </summary>
    private static bool IsRootedPath(string path)
    {
        if (Path.IsPathRooted(path))
            return true;
        return path.Length >= 3
               && char.IsLetter(path[0])
               && path[1] == ':'
               && (path[2] == '\\' || path[2] == '/');
    }

    /// <summary>Ключ сравнения путей конфиг-файлов: без учёта регистра и разделителей пути.</summary>
    private static string NormalizeTargetKey(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        return path.Trim()
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .ToUpperInvariant();
    }

    private void ApplyPhysicalForCurrent() => RebuildPhysicalForCurrent();

    private void RebuildPhysicalForCurrent()
    {
        if (_servers.Count == 0)
            return;
        var option = _servers[_serverIndex];
        // EffectiveInstance: для обнаруженной службы — это она; для fallback-ветки/
        // ручного конфига — построенный из введённого файла (issue #63: каталог и
        // порт обязаны взяться из конфига, а не из «обычного» C:\Apache24\htdocs).
        var instance = EffectiveInstance;
        if (instance is not null)
        {
            // DocumentRoot из конфига (issue #63): точный каталог публикации инстанса
            // (включает подкаталог сайта — «ServerRoot\htdocs\27»); fallback — Htdocs.
            var root = !string.IsNullOrWhiteSpace(instance.DocumentRoot) ? instance.DocumentRoot : instance.Htdocs;
            if (!string.IsNullOrWhiteSpace(root))
                PhysicalDirectory = Path.Combine(root, VirtualDirectory);
            else
                PhysicalDirectory = WebPublishService.SuggestPhysicalDirectory(VirtualDirectory, option.Kind);
        }
        else
            PhysicalDirectory = WebPublishService.SuggestPhysicalDirectory(VirtualDirectory, option.Kind);
    }

    private async Task<bool> RunAsync(bool publish)
    {
        var ib = SelectedInfobase;
        if (IsBusy || ib is null)
            return false;
        var request = BuildRequest();
        var error = WebPublishService.Validate(request);
        if (error is not null)
        {
            AppendLog(LocalizationManager.T(error));
            return false;
        }

        var args = publish
            ? WebPublishService.BuildPublishArguments(request)
            : WebPublishService.BuildDeleteArguments(request);
        IsBusy = true;
        try
        {
            AppendLog("webinst " + string.Join(" ", args.Select(a => a.Contains(' ') || a.Contains('"') ? "\"" + a.Replace("\"", "\\\"") + "\"" : a)));
            var result = await WebPublishService.RunAsync(ib, args);
            if (!string.IsNullOrWhiteSpace(result.CombinedOutput))
                AppendLog(result.CombinedOutput);
            AppendLog(LocalizationManager.T(result.Success
                ? (publish ? "WebPublish.Published" : "WebPublish.Deleted")
                : "WebPublish.Failed"));
            if (result.Success && publish)
            {
                AppendLog(Url);
                // issue #63 (комментарий 7OH 2026-10-30): пост-обработка конфига Apache
                // после успешной публикации — .bak-копия, дедупликация
                // LoadModule _1cws_module, подстановка ${…}-переменных в новом блоке.
                PostProcessApacheConfAfterPublish(request);
            }
            return result.Success;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Пост-обработка конфига Apache после успешной публикации (issue #63, комментарий
    /// 7OH от 2026-10-30): .bak-копия исходного файла, дедупликация строк
    /// <c>LoadModule _1cws_module</c> (webinst дописывает её, хотя в основном конфиге
    /// она уже есть) и подстановка путей нового блока алиаса через ${…}-переменные —
    /// как в существующих записях файла. Только для Apache (не IIS); ошибки пост-
    /// обработки не отменяют успешную публикацию — пишутся в журнал окна и CM_PUBLISH.
    /// </summary>
    internal void PostProcessApacheConfAfterPublish(WebPublishRequest request)
    {
        if (request.Server == WebServerKind.Iis)
            return;
        var confPath = (request.ApacheConfPath ?? string.Empty).Trim();
        if (confPath.Length == 0)
            return;

        try
        {
            if (!File.Exists(confPath))
            {
                TracePublish("пост-обработка: конфиг webinst не найден на диске: «" + confPath + "»");
                return;
            }

            var original = File.ReadAllText(confPath);

            // 1) Не дублировать LoadModule _1cws_module: схлопываем повторы (оставляем первую).
            var (deduped, removedDuplicates) = WebPublishConfPostProcessor.DeduplicateLoadModule(original);
            if (removedDuplicates > 0)
                TracePublish($"пост-обработка: удалено дубликатов LoadModule _1cws_module: {removedDuplicates}");

            // 2) Подстановка путей через переменные (${myDocumentRoot} и т.п.) — только
            //    при однозначном совпадении с префиксом и наличии существующих записей
            //    с этой переменной; иначе обычные пути остаются как есть.
            var (rewritten, pathsRewritten) = WebPublishConfPostProcessor.RewriteAliasBlockWithVariables(
                deduped, request.VirtualDirectory, MergeDefinesWith(deduped));
            if (pathsRewritten)
                TracePublish("пост-обработка: пути нового блока алиаса переписаны через ${…}-переменную (как в существующих записях файла)");

            if (removedDuplicates == 0 && !pathsRewritten)
            {
                TracePublish("пост-обработка: правки конфига не требуются (дубликатов нет, переменная не однозначна или не используется)");
                return;
            }

            // .bak-копия исходного конфига перед первой записью (issue #63).
            var backupPath = confPath + ".bak";
            File.Copy(confPath, backupPath, overwrite: true);
            TracePublish("пост-обработка: создана резервная копия конфига: " + backupPath);

            File.WriteAllText(confPath, rewritten);

            var changes = new List<string>();
            if (removedDuplicates > 0)
                changes.Add($"удалено дубликатов LoadModule _1cws_module: {removedDuplicates}");
            if (pathsRewritten)
                changes.Add("пути нового блока переписаны через ${…}-переменные");
            AppendLog("Конфиг Apache обновлён после публикации (" + string.Join("; ", changes) + ").");
        }
        catch (Exception ex)
        {
            // Публикация уже успешна — пост-обработка не должна её «отменить».
            TracePublish("пост-обработка конфига не выполнена: " + ex.Message);
        }
    }

    /// <summary>
    /// Словарь Define для пост-обработки: значения из include-графа выбранного инстанса
    /// + директивы Define самого конфига webinst (поздние переопределяют ранние).
    /// </summary>
    private IReadOnlyDictionary<string, string> MergeDefinesWith(string confText)
    {
        var defines = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (EffectiveInstance?.Defines is { Count: > 0 } instanceDefines)
        {
            foreach (var pair in instanceDefines)
                defines[pair.Key] = pair.Value;
        }
        foreach (var pair in ApacheConfIncludeParser.ParseDefines(confText))
            defines[pair.Key] = pair.Value;
        return defines;
    }
}
