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
    /// <summary>
    /// Предельное время записи профиля (issue #143): медленный/недоступный сетевой
    /// профиль не должен держать окно в состоянии «сохранение» бесконечно.
    /// </summary>
    private static readonly TimeSpan SaveTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Предельное время проверки подключения/каталога (issue #143).</summary>
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);

    private readonly IReadOnlyList<Infobase> _infobases;
    private DbmsBackupData _data;
    private readonly bool _needsLoad;
    private bool _initialized;
    private readonly StringBuilder _log = new();
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private readonly string _defaultDbmsKind;
    private readonly ClusterInfobaseCredentialsResolver? _clusterResolver;
    private readonly Dictionary<string, DbmsKind?> _clusterDbmsCache = new(StringComparer.Ordinal);
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _operationCts;
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
    private int _running;
    private int _operationGate;
    /// <summary>
    /// Общий на процесс признак выполняющегося копирования (issue #143): окно стало
    /// немодальным, его можно закрыть и открыть снова, поэтому повторный запуск из
    /// нового экземпляра ViewModel не должен создавать второй дамп.
    /// </summary>
    private static int _activeRuns;
    private string _statusText = string.Empty;
    private string _progressStageText = string.Empty;
    private double _progressPercent;
    private bool _isProgressIndeterminate;
    private string _elapsedText = string.Empty;

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
        // issue #143: НЕ читаем профиль синхронно в конструкторе — он вызывается на
        // UI-потоке (команда меню), а недоступный сетевой профиль «вешал» окно ещё
        // до показа. Если данные не переданы, их загрузит InitializeAsync в фоне.
        _data = data ?? new DbmsBackupData();
        _needsLoad = data is null;
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

    /// <summary>
    /// Данные профилей не переданы в конструктор и требуют фоновой загрузки
    /// (issue #143). Окно вызывает <see cref="InitializeAsync"/> после показа.
    /// </summary>
    public bool NeedsLoad => _needsLoad;

    /// <summary>
    /// Фоновая загрузка профилей (issue #143). Ничего не делает, если данные уже
    /// переданы или загрузка выполнена. После загрузки заново применяет профиль
    /// выбранной базы, чтобы поля показали сохранённые значения.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized)
            return;
        _initialized = true;
        if (!_needsLoad)
            return;

        DbmsBackupTrace.Log("enter", "initialize");
        try
        {
            var loaded = await DbmsBackupService.LoadAsync(ct).ConfigureAwait(true);
            _data = loaded;
            if (SelectedInfobase is not null)
                ApplySelectedProfile();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Битый/недоступный профиль не должен ронять окно — только диагностика.
            DbmsBackupTrace.Error("error", "initialize: " + ex.Message, ex);
            AppendLog(ex.Message);
        }
        finally
        {
            DbmsBackupTrace.Log("exit", "initialize");
        }
    }

    public Infobase? SelectedInfobase =>
        _selectedIndex >= 0 && _selectedIndex < _infobases.Count ? _infobases[_selectedIndex] : null;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (!SetProperty(ref _selectedIndex, value) || SelectedInfobase is null)
                return;
            ApplySelectedProfile();
        }
    }

    /// <summary>
    /// Переносит профиль выбранной базы в поля формы и определяет тип СУБД
    /// (issue #106/#143). Вызывается из <see cref="SelectedIndex"/> и после фоновой
    /// загрузки профилей — так окно не зависит от синхронного чтения файла.
    /// </summary>
    private void ApplySelectedProfile()
    {
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

    /// <summary>Статус последней проверки или операции (issue #143).</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value ?? string.Empty);
    }

    /// <summary>Название текущего этапа операции (issue #143).</summary>
    public string ProgressStageText
    {
        get => _progressStageText;
        private set => SetProperty(ref _progressStageText, value ?? string.Empty);
    }

    /// <summary>Прогресс операции в процентах (оценочный, по этапам).</summary>
    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, value);
    }

    /// <summary>Этап без измеримого прогресса (дамп, выгрузка) — бегущая полоса.</summary>
    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetProperty(ref _isProgressIndeterminate, value);
    }

    /// <summary>Прошедшее время операции «мм:сс».</summary>
    public string ElapsedText
    {
        get => _elapsedText;
        private set => SetProperty(ref _elapsedText, value ?? string.Empty);
    }

    public string LogText => _log.ToString();

    public void AppendLog(string message)
    {
        if (_ui is not null && SynchronizationContext.Current != _ui)
        {
            // issue #143: не пере-постим из самого колбэка — иначе при контексте,
            // который выполняет Post без установки Current (тестовый хост), запись
            // зацикливается и журнал окна остаётся пустым.
            _ui.Post(state => AppendLogCore((string)state!), message);
            return;
        }
        AppendLogCore(message);
    }

    private void AppendLogCore(string message)
    {
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

    /// <summary>
    /// Сохранение профиля кнопкой «Сохранить» (issue #143): запись на диск выполняется
    /// на выделенном потоке, ожидание ограничено сторожевым таймаутом
    /// (<see cref="DbmsTimeoutGuard"/>), который не зависит от пула потоков. Раньше
    /// запись блокировала поток пула синхронным ожиданием, а таймеры пула не
    /// срабатывали при его голодании — окно «наглухо» зависало на медленном или
    /// недоступном сетевом профиле.
    /// <para>
    /// <see cref="IsBusy"/> выставляется до тяжёлых вызовов (<see cref="CurrentProfile"/>,
    /// <see cref="DbmsBackupService.Validate"/>), повторный вход отсекается
    /// <see cref="Interlocked"/>. По таймауту показывается этап и путь профиля,
    /// предлагается прислать <c>dbms-backup-debug.log</c>, кнопки снова активны.
    /// </para>
    /// </summary>
    public async Task<bool> SaveAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (!TryEnterOperation("save"))
            return false;

        // issue #143: IsBusy — ДО CurrentProfile/Validate, чтобы второй клик,
        // пришедший во время медленной проверки, не запустил параллельную запись.
        IsBusy = true;
        var limit = timeout ?? SaveTimeout;
        try
        {
            DbmsBackupTrace.Log("enter", "save");
            var profile = CurrentProfile();
            DbmsBackupTrace.Log("profile", DbmsBackupTrace.DescribeProfile(profile));
            if (profile is null)
            {
                AppendLog(LocalizationManager.T("Dbms.NoBases"));
                return false;
            }
            // Тип СУБД не определён — не сохраняем профиль с молчаливым PostgreSQL.
            if (SelectedInfobase is not null && DbmsIndex < 0)
            {
                AppendLog(LocalizationManager.T("Dbms.DbmsRequired"));
                return false;
            }
            var error = DbmsBackupService.Validate(profile);
            DbmsBackupTrace.Log("validate", error is null ? "ok" : error);
            if (error is not null)
            {
                AppendLog(LocalizationManager.T(error));
                return false;
            }

            AppendLog(LocalizationManager.T("Dbms.Saving"));
            // Мгновенная in-memory операция; запись на диск — на выделенном потоке.
            _data.Upsert(profile);
            DbmsBackupTrace.Log("upsert", $"infobase={profile.InfobaseId}");

            await DbmsTimeoutGuard.RunAsync(
                token => DbmsBackupService.SaveAsync(_data, token), limit, ct, "save")
                .ConfigureAwait(true);

            DbmsBackupTrace.Log("exit", "save");
            AppendLog(LocalizationManager.T("Dbms.Saved"));
            return true;
        }
        catch (TimeoutException ex)
        {
            ReportTimeout("save", "Dbms.SaveTimeout", ex, limit);
            return false;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Внутренний таймаут — это не отмена пользователем.
            ReportTimeout("save", "Dbms.SaveTimeout", null, limit);
            return false;
        }
        catch (OperationCanceledException)
        {
            StatusText = LocalizationManager.T("Designer.Cancelled");
            AppendLog(StatusText);
            return false;
        }
        catch (Exception ex)
        {
            DbmsBackupTrace.Error("error", "save: " + ex.Message, ex);
            StatusText = ex.Message;
            AppendLog(ex.Message);
            return false;
        }
        finally
        {
            IsBusy = false;
            ExitOperation();
            DbmsBackupTrace.Log("exit", "save-finally");
        }
    }

    public void Cancel()
    {
        try { _cts?.Cancel(); }
        catch (ObjectDisposedException) { }
        try { _operationCts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// Запускает копирование (issue #143): защита от повторного запуска, прогресс,
    /// отмена и очистка состояния в <c>finally</c>. Вся тяжёлая работа — в сервисе,
    /// на выделенных потоках; окно остаётся отзывчивым.
    /// </summary>
    public async Task RunAsync()
    {
        if (IsBusy)
        {
            DbmsBackupTrace.Log("reentrancy", "run: busy");
            AppendLog(LocalizationManager.T("Dbms.Reentrancy"));
            return;
        }

        // Двойной клик/автоповтор не должен запускать второй дамп.
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            DbmsBackupTrace.Log("reentrancy", "run");
            AppendLog(LocalizationManager.T("Dbms.Reentrancy"));
            return;
        }

        // issue #143: барьер на процесс — закрытое и снова открытое окно не запустит
        // второй дамп, пока первый ещё выполняется в фоне.
        if (Interlocked.CompareExchange(ref _activeRuns, 1, 0) == 1)
        {
            DbmsBackupTrace.Log("reentrancy", "run: process-busy");
            AppendLog(LocalizationManager.T("Dbms.Reentrancy"));
            Interlocked.Exchange(ref _running, 0);
            return;
        }

        // IsBusy — до CurrentProfile/Validate (issue #143): второй клик не должен
        // проскочить, пока идёт медленная проверка/чтение профиля.
        IsBusy = true;
        CancellationTokenSource? elapsedCts = null;
        try
        {
            DbmsBackupTrace.Log("enter", "run");
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            var profile = CurrentProfile();
            DbmsBackupTrace.Log("profile", DbmsBackupTrace.DescribeProfile(profile));
            if (profile is null)
            {
                AppendLog(LocalizationManager.T("Dbms.NoBases"));
                return;
            }
            if (SelectedInfobase is not null && DbmsIndex < 0)
            {
                AppendLog(LocalizationManager.T("Dbms.DbmsRequired"));
                return;
            }
            var error = DbmsBackupService.Validate(profile);
            DbmsBackupTrace.Log("validate", error is null ? "ok" : error);
            if (error is not null)
            {
                AppendLog(LocalizationManager.T(error));
                return;
            }

            // Запись профиля — на выделенном потоке (issue #143) и с реальной
            // отменой: диск/профиль могут отвечать медленно, а окно должно
            // оставаться отзывчивым. Никакого синхронного Save на UI-пути.
            _data.Upsert(profile);
            DbmsBackupTrace.Log("upsert", $"infobase={profile.InfobaseId}");
            // issue #143: запись профиля — вспомогательный шаг. Её сбой (медленный
            // или недоступный каталог профиля, ограниченный пользователь) не должен
            // отменять саму копию: проверки уже прошли, копия создаётся по текущим
            // параметрам. Ошибка фиксируется в журнале окна.
            try
            {
                await DbmsTimeoutGuard.RunAsync(
                    token => DbmsBackupService.SaveAsync(_data, token), SaveTimeout, _cts.Token, "run-save")
                    .ConfigureAwait(true);
            }
            catch (TimeoutException ex)
            {
                ReportTimeout("run-save", "Dbms.SaveTimeout", ex, SaveTimeout);
                AppendLog(string.Format(LocalizationManager.T("Dbms.SaveFailedContinue"),
                    LocalizationManager.T("Dbms.SaveTimeout")));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DbmsBackupTrace.Error("error", "run-save: " + ex.Message, ex);
                AppendLog(string.Format(LocalizationManager.T("Dbms.SaveFailedContinue"), ex.Message));
            }

            elapsedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            _ = TrackElapsedAsync(elapsedCts.Token);

            ResetProgress();
            var progress = new Progress<DbmsBackupProgress>(OnProgress);
            var result = await DbmsBackupService.RunAsync(profile, AppendLog, progress, _cts.Token)
                .ConfigureAwait(true);
            StatusText = result.Success
                ? LocalizationManager.T("Dbms.Success")
                : result.ErrorMessage ?? LocalizationManager.T("Dbms.Failed");
            AppendLog(StatusText);
            DbmsBackupTrace.Log("exit", "run");
        }
        catch (TimeoutException ex)
        {
            ReportTimeout("run-save", "Dbms.SaveTimeout", ex, SaveTimeout);
        }
        catch (OperationCanceledException)
        {
            StatusText = LocalizationManager.T("Designer.Cancelled");
            AppendLog(StatusText);
        }
        catch (Exception ex)
        {
            DbmsBackupTrace.Error("error", "run: " + ex.Message, ex);
            StatusText = ex.Message;
            AppendLog(ex.Message);
        }
        finally
        {
            elapsedCts?.Cancel();
            elapsedCts?.Dispose();
            ProgressStageText = string.Empty;
            IsProgressIndeterminate = false;
            ElapsedText = string.Empty;
            IsBusy = false;
            Interlocked.Exchange(ref _running, 0);
            Interlocked.Exchange(ref _activeRuns, 0);
            DbmsBackupTrace.Log("exit", "run-finally");
        }
    }

    /// <summary>Проверка подключения к СУБД до запуска копии (issue #143).</summary>
    public Task CheckConnectionAsync(TimeSpan? timeout = null) =>
        RunCheckAsync(DbmsBackupService.CheckConnectionAsync, timeout ?? CheckTimeout);

    /// <summary>Проверка каталога копий на запись (issue #143).</summary>
    public Task CheckDirectoryAsync(TimeSpan? timeout = null) =>
        RunCheckAsync(DbmsBackupService.CheckOutputDirectoryAsync, timeout ?? CheckTimeout);

    private async Task RunCheckAsync(
        Func<DbmsBackupProfile, CancellationToken, Task<DbmsCheckResult>> check,
        TimeSpan timeout)
    {
        if (!TryEnterOperation("check"))
            return;

        // IsBusy — до CurrentProfile (issue #143).
        IsBusy = true;
        // «Отмена» (Cancel) отменяет userCts; сторожевой таймаут — независимый
        // выделенный поток (<see cref="DbmsTimeoutGuard"/>), не таймер пула.
        using var userCts = new CancellationTokenSource();
        _operationCts = userCts;
        try
        {
            DbmsBackupTrace.Log("enter", "check");
            var profile = CurrentProfile();
            DbmsBackupTrace.Log("profile", DbmsBackupTrace.DescribeProfile(profile));
            if (profile is null)
            {
                AppendLog(LocalizationManager.T("Dbms.NoBases"));
                return;
            }

            AppendLog(LocalizationManager.T("Dbms.CheckRunning"));
            // issue #143: вся проверка — на выделенном потоке (DNS, TcpClient,
            // Process.Start/pg_isready/sqlcmd). UI-поток и пул не блокируются;
            // токен реально прерывает проверку по таймауту/«Отмене».
            var result = await DbmsTimeoutGuard.RunAsync(
                token => BlockingIo.RunAsync(() => check(profile, token), token),
                timeout, userCts.Token, "check").ConfigureAwait(true);
            StatusText = FormatCheckResult(result);
            AppendLog(StatusText);
            DbmsBackupTrace.Log("exit", "check");
        }
        catch (TimeoutException ex)
        {
            ReportTimeout("check", "Dbms.CheckTimeout", ex, timeout);
        }
        catch (OperationCanceledException)
        {
            StatusText = userCts.IsCancellationRequested
                ? LocalizationManager.T("Designer.Cancelled")
                : LocalizationManager.T("Dbms.CheckTimeout");
            AppendLog(StatusText);
        }
        catch (Exception ex)
        {
            DbmsBackupTrace.Error("error", "check: " + ex.Message, ex);
            StatusText = ex.Message;
            AppendLog(ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_operationCts, userCts))
                _operationCts = null;
            IsBusy = false;
            ExitOperation();
            DbmsBackupTrace.Log("exit", "check-finally");
        }
    }

    /// <summary>
    /// Захват операции (issue #143): повторный клик не проходит, пока выполняется
    /// сохранение/проверка. <see cref="IsBusy"/> дополнительно отсекает клики во
    /// время запуска копии (у неё свой барьер <c>_running</c>).
    /// </summary>
    private bool TryEnterOperation(string stage)
    {
        if (Interlocked.CompareExchange(ref _operationGate, 1, 0) == 1)
        {
            DbmsBackupTrace.Log("reentrancy", stage);
            return false;
        }
        if (IsBusy)
        {
            Interlocked.Exchange(ref _operationGate, 0);
            DbmsBackupTrace.Log("reentrancy", stage + ": busy");
            return false;
        }
        return true;
    }

    private void ExitOperation() => Interlocked.Exchange(ref _operationGate, 0);

    /// <summary>
    /// Сообщение по таймауту (issue #143): этап, путь профиля без пароля и просьба
    /// прислать <c>dbms-backup-debug.log</c>; кнопки снова активны (IsBusy снимается
    /// в <c>finally</c>).
    /// </summary>
    private void ReportTimeout(string stage, string statusKey, Exception? exception, TimeSpan limit)
    {
        StatusText = LocalizationManager.T(statusKey);
        AppendLog(StatusText);
        var path = DbmsBackupTrace.DescribePath(OutputDirectory);

        // Этап и путь должны попасть в журнал окна даже если строка локализации
        // ещё не загружена (тогда T вернёт ключ без подстановок).
        var template = LocalizationManager.T("Dbms.TimeoutDetails");
        var details = template.Contains("{0}", StringComparison.Ordinal)
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, template, stage, path)
            : template + ": " + stage + "; " + path;
        AppendLog(details);

        // Подсказка обязательно содержит имя файла диагностики — пользователь должен
        // знать, что прислать, даже при отсутствующей локализации.
        var hint = LocalizationManager.T("Dbms.SendDebugLog");
        if (hint.IndexOf(DbmsBackupTrace.FileName, StringComparison.OrdinalIgnoreCase) < 0)
            hint = hint + " (" + DbmsBackupTrace.FileName + ")";
        AppendLog(hint);

        DbmsBackupTrace.Error("timeout",
            $"{stage}: {limit.TotalSeconds:0.###} с; output={path}", exception);
    }

    private static string FormatCheckResult(DbmsCheckResult result)
    {
        var text = LocalizationManager.T(result.MessageKey);
        return string.IsNullOrWhiteSpace(result.Detail) ? text : text + ": " + result.Detail;
    }

    private void OnProgress(DbmsBackupProgress progress)
    {
        ProgressStageText = LocalizationManager.T(StageKey(progress.Stage));
        IsProgressIndeterminate = progress.Stage is DbmsBackupStage.Dump or DbmsBackupStage.Upload;
        ProgressPercent = progress.Stage switch
        {
            DbmsBackupStage.Prepare => 5,
            DbmsBackupStage.Check => 10,
            DbmsBackupStage.Dump => 20,
            DbmsBackupStage.Rotate => 85,
            DbmsBackupStage.Upload => 90,
            DbmsBackupStage.Done => 100,
            _ => ProgressPercent
        };
    }

    private static string StageKey(DbmsBackupStage stage) => stage switch
    {
        DbmsBackupStage.Prepare => "Dbms.Progress.Prepare",
        DbmsBackupStage.Check => "Dbms.Progress.Check",
        DbmsBackupStage.Dump => "Dbms.Progress.Dump",
        DbmsBackupStage.Rotate => "Dbms.Progress.Rotate",
        DbmsBackupStage.Upload => "Dbms.Progress.Upload",
        _ => "Dbms.Progress.Done"
    };

    private void ResetProgress()
    {
        ProgressPercent = 0;
        ProgressStageText = string.Empty;
        IsProgressIndeterminate = false;
        ElapsedText = "00:00";
        StatusText = string.Empty;
    }

    private async Task TrackElapsedAsync(CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                ElapsedText = started.Elapsed.ToString(@"mm\:ss");
                await Task.Delay(1000, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Операция завершилась — таймер больше не нужен.
        }
    }
}
