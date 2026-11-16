using System.Globalization;
using System.Text;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// ViewModel окна «Резервные копии СУБД» (выпуск 1.0.0.69): профиль копирования
/// клиент-серверной базы (PostgreSQL / MS SQL), ротация, выгрузка в WebDAV, запуск
/// «сейчас». Тот же профиль использует задание по расписанию «Копия средствами СУБД».
/// </summary>
public sealed class DbmsBackupViewModel : ViewModelBase
{
    private readonly IReadOnlyList<Infobase> _infobases;
    private readonly DbmsBackupData _data;
    private readonly StringBuilder _log = new();
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private readonly string _defaultDbmsKind;
    private readonly ClusterInfobaseCredentialsResolver? _clusterResolver;
    private readonly Dictionary<string, DbmsKind?> _clusterDbmsCache = new(StringComparer.Ordinal);
    private CancellationTokenSource? _cts;
    private int _selectedIndex = -1;
    private int _dbmsIndex = -1;
    private bool _applyingDbmsSelection;
    private bool _dbmsUserSelected;
    private bool _savedProfileExists;
    private string _dbmsHint = string.Empty;
    private string _host = string.Empty;
    private string _port = "0";
    private string _database = string.Empty;
    private string _user = string.Empty;
    private string _password = string.Empty;
    private string _outputDirectory = string.Empty;
    private string _toolsDirectory = string.Empty;
    private string _keepCount = "7";
    private string _webDavUrl = string.Empty;
    private string _webDavUser = string.Empty;
    private string _webDavPassword = string.Empty;
    private bool _isBusy;

    /// <param name="data">Профили копирования; null — читать <c>dbms_backups.json</c>.</param>
    /// <param name="defaultDbms">Общая настройка «Тип СУБД по умолчанию» (issue #106,
    /// v1.0.1.73): «PostgreSQL», «MsSql» или пусто — не задан.</param>
    /// <param name="clusterResolver">Резолвер данных кластера rac: тип СУБД базы
    /// определяется асинхронно (<see cref="ResolveDbmsFromClusterAsync"/>), не блокируя окно.</param>
    public DbmsBackupViewModel(IEnumerable<Infobase> infobases, Infobase? selected,
        DbmsBackupData? data = null, string? defaultDbms = null,
        ClusterInfobaseCredentialsResolver? clusterResolver = null)
    {
        _infobases = (infobases ?? Array.Empty<Infobase>())
            .Where(ib => ib is not null && ib.Connection.Type == ConnectionType.ClientServer)
            .OrderBy(ib => ib.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        _data = data ?? DbmsBackupService.Load();
        _defaultDbmsKind = DbmsDefaults.NormalizeSetting(defaultDbms);
        _clusterResolver = clusterResolver;
        InfobaseNames = _infobases.Select(ib => ib.Name).ToList();
        DbmsOptions = new[] { DbmsDefaults.PostgreSqlName, DbmsDefaults.MsSqlName };
        var index = selected is null ? -1 : _infobases.ToList().FindIndex(ib => ib.Id == selected.Id);
        SelectedIndex = index >= 0 ? index : (_infobases.Count > 0 ? 0 : -1);
    }

    public IReadOnlyList<string> InfobaseNames { get; }

    public IReadOnlyList<string> DbmsOptions { get; }

    public bool HasBases => _infobases.Count > 0;

    public Infobase? SelectedInfobase =>
        _selectedIndex >= 0 && _selectedIndex < _infobases.Count ? _infobases[_selectedIndex] : null;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (!SetProperty(ref _selectedIndex, value) || SelectedInfobase is null)
                return;
            var profile = LoadSelectedProfile();
            Host = profile.Host;
            Port = profile.Port.ToString(CultureInfo.InvariantCulture);
            Database = profile.Database;
            User = profile.User;
            Password = profile.Password;
            OutputDirectory = profile.OutputDirectory;
            ToolsDirectory = profile.ToolsDirectory;
            KeepCount = profile.KeepCount.ToString(CultureInfo.InvariantCulture);
            WebDavUrl = profile.WebDavUrl;
            WebDavUser = profile.WebDavUser;
            WebDavPassword = profile.WebDavPassword;
        }
    }

    /// <summary>
    /// Индекс выпадающего списка «СУБД»: 0 — PostgreSQL, 1 — Microsoft SQL Server,
    /// −1 — тип не определён (issue #106, v1.0.1.73: больше не «PostgreSQL всегда»).
    /// </summary>
    public int DbmsIndex
    {
        get => _dbmsIndex;
        set
        {
            if (!SetProperty(ref _dbmsIndex, Math.Clamp(value, -1, 1)))
                return;
            if (!_applyingDbmsSelection)
            {
                _dbmsUserSelected = true;
                UpdateDbmsHint();
            }
        }
    }

    /// <summary>
    /// Подсказка под списком СУБД: при неизвестном типе объясняет, что профиль,
    /// кластер и общая настройка не дали типа, и просит выбрать его вручную.
    /// </summary>
    public string DbmsHint
    {
        get => _dbmsHint;
        private set => SetProperty(ref _dbmsHint, value ?? string.Empty);
    }

    /// <summary>
    /// Загружает профиль выбранной базы и определяет тип СУБД по приоритету
    /// (issue #106, v1.0.1.73): сохранённый профиль → данные кластера rac (если уже
    /// получены) → общая настройка → неизвестно (пустой выбор + подсказка).
    /// </summary>
    private DbmsBackupProfile LoadSelectedProfile()
    {
        var infobase = SelectedInfobase!;
        var saved = _data.Find(infobase.Id);
        _savedProfileExists = saved is not null;
        var profile = saved ?? DbmsBackupService.SuggestProfile(infobase);
        _dbmsUserSelected = false;
        _clusterDbmsCache.TryGetValue(infobase.Id, out var clusterKind);
        ApplyResolvedDbms(DbmsDefaults.Resolve(saved?.Dbms, clusterKind, _defaultDbmsKind));
        return profile;
    }

    /// <summary>Применяет результат выбора типа СУБД к списку и подсказке.</summary>
    private void ApplyResolvedDbms(DbmsKindResolution resolution)
    {
        _applyingDbmsSelection = true;
        try
        {
            DbmsIndex = DbmsDefaults.ToIndex(resolution.Kind);
        }
        finally
        {
            _applyingDbmsSelection = false;
        }
        UpdateDbmsHint();
    }

    /// <summary>Показывает подсказку, когда тип СУБД определить не удалось.</summary>
    private void UpdateDbmsHint() =>
        DbmsHint = _dbmsIndex < 0 ? LocalizationManager.T("Dbms.NotDetectedHint") : string.Empty;

    /// <summary>
    /// Асинхронно уточняет тип СУБД по данным кластера 1С (rac) для выбранной базы
    /// (issue #106, v1.0.1.73). Применяется, только если у базы нет сохранённого
    /// профиля и пользователь ещё не выбрал тип вручную; сохранённый профиль всегда
    /// приоритетнее кластера.
    /// </summary>
    public async Task ResolveDbmsFromClusterAsync(CancellationToken ct = default)
    {
        var resolver = _clusterResolver;
        var infobase = SelectedInfobase;
        if (resolver is null || infobase is null || _savedProfileExists || _dbmsUserSelected)
            return;

        DbmsKind? kind;
        try
        {
            var resolution = await resolver.ResolveDetailedAsync(infobase, ct).ConfigureAwait(false);
            kind = resolution.Profile?.Dbms;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return; // rac недоступен — остаётся общая настройка/подсказка.
        }

        _clusterDbmsCache[infobase.Id] = kind;
        if (kind is null || !ReferenceEquals(SelectedInfobase, infobase) ||
            _savedProfileExists || _dbmsUserSelected)
            return;

        var applied = DbmsDefaults.Resolve(null, kind, _defaultDbmsKind);
        if (_ui is not null && !ReferenceEquals(SynchronizationContext.Current, _ui))
            _ui.Post(_ => ApplyResolvedDbms(applied), null);
        else
            ApplyResolvedDbms(applied);
    }
    public string Host { get => _host; set => SetProperty(ref _host, value ?? string.Empty); }
    public string Port { get => _port; set => SetProperty(ref _port, value ?? string.Empty); }
    public string Database { get => _database; set => SetProperty(ref _database, value ?? string.Empty); }
    public string User { get => _user; set => SetProperty(ref _user, value ?? string.Empty); }
    public string Password { get => _password; set => SetProperty(ref _password, value ?? string.Empty); }
    public string OutputDirectory { get => _outputDirectory; set => SetProperty(ref _outputDirectory, value ?? string.Empty); }
    public string ToolsDirectory { get => _toolsDirectory; set => SetProperty(ref _toolsDirectory, value ?? string.Empty); }
    public string KeepCount { get => _keepCount; set => SetProperty(ref _keepCount, value ?? string.Empty); }
    public string WebDavUrl { get => _webDavUrl; set => SetProperty(ref _webDavUrl, value ?? string.Empty); }
    public string WebDavUser { get => _webDavUser; set => SetProperty(ref _webDavUser, value ?? string.Empty); }
    public string WebDavPassword { get => _webDavPassword; set => SetProperty(ref _webDavPassword, value ?? string.Empty); }

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
        if (_ui is not null && SynchronizationContext.Current != _ui)
        {
            _ui.Post(_ => AppendLog(message), null);
            return;
        }
        _log.Append(DateTime.Now.ToString("HH:mm:ss")).Append("  ").AppendLine(message);
        OnPropertyChanged(nameof(LogText));
    }

    private static int ParseInt(string text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : fallback;

    public DbmsBackupProfile? CurrentProfile() => SelectedInfobase is null
        ? null
        : new DbmsBackupProfile
        {
            InfobaseId = SelectedInfobase.Id,
            Dbms = DbmsIndex == 1 ? DbmsKind.MsSql : DbmsKind.PostgreSql,
            Host = Host.Trim(),
            Port = ParseInt(Port, 0),
            Database = Database.Trim(),
            User = User.Trim(),
            Password = Password,
            OutputDirectory = OutputDirectory.Trim(),
            ToolsDirectory = ToolsDirectory.Trim(),
            KeepCount = ParseInt(KeepCount, 7),
            WebDavUrl = WebDavUrl.Trim(),
            WebDavUser = WebDavUser.Trim(),
            WebDavPassword = WebDavPassword
        };

    public bool Save()
    {
        var profile = CurrentProfile();
        if (profile is null)
        {
            AppendLog(LocalizationManager.T("Dbms.NoBases"));
            return false;
        }
        // Тип СУБД не определён — не сохраняем профиль с молчаливым PostgreSQL
        // (issue #106, v1.0.1.73): пользователь должен выбрать тип явно.
        if (SelectedInfobase is not null && DbmsIndex < 0)
        {
            AppendLog(LocalizationManager.T("Dbms.DbmsRequired"));
            return false;
        }
        var error = DbmsBackupService.Validate(profile);
        if (error is not null)
        {
            AppendLog(LocalizationManager.T(error));
            return false;
        }
        _data.Upsert(profile);
        DbmsBackupService.Save(_data);
        AppendLog(LocalizationManager.T("Dbms.Saved"));
        return true;
    }

    public void Cancel()
    {
        try { _cts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public async Task RunAsync()
    {
        if (IsBusy || !Save())
            return;
        var profile = CurrentProfile()!;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        IsBusy = true;
        try
        {
            var result = await DbmsBackupService.RunAsync(profile, AppendLog, _cts.Token);
            AppendLog(result.Success
                ? LocalizationManager.T("Dbms.Success")
                : result.ErrorMessage ?? LocalizationManager.T("Dbms.Failed"));
        }
        catch (OperationCanceledException)
        {
            AppendLog(LocalizationManager.T("Designer.Cancelled"));
        }
        catch (Exception ex)
        {
            AppendLog(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
