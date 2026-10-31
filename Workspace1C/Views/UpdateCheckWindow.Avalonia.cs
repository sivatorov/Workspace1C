#if LINUX
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно проверки обновлений конфигурации для выбранной информационной базы (горячая клавиша F9).
    /// Показывает имя базы, текущую версию конфигурации, последнюю версию из каталога релизов 1С,
    /// адрес каталога и статус проверки; подсвечивает наличие нового релиза и позволяет скачать
    /// дистрибутив с индикатором прогресса. Сетевые операции выполняются в фоновом потоке.
    /// Avalonia/Linux-версия WPF-окна <see cref="UpdateCheckWindow"/>.
    /// </summary>
    public sealed class UpdateCheckWindow : ModalWindowBase
    {
        private readonly Services.IOneCUpdatesService _updates = AppServices.GetRequiredService<Services.IOneCUpdatesService>();
        private readonly Services.IInfobaseRepository _repository = AppServices.GetRequiredService<Services.IInfobaseRepository>();
        private readonly Services.ICustomConfigTypesStore _store = AppServices.GetRequiredService<Services.ICustomConfigTypesStore>();
        private readonly Services.IItsAccountsStore _itsAccounts = AppServices.GetRequiredService<Services.IItsAccountsStore>();
        private readonly Services.IAppLogger _logger = AppServices.GetRequiredService<Services.IAppLogger>();
        private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

        private readonly Infobase _infobase;
        private readonly UpdateCheckRowViewModel _row;
        private CancellationTokenSource? _cts;

        /// <summary>Оценщик оставшегося времени одиночного скачивания (issue #360).</summary>
        private Services.DownloadEtaTracker? _singleDownloadEta;

        /// <summary>Активна ли ссылка каталога релизов (валидный http/https-адрес, issue #323).</summary>
        private bool _urlLinkEnabled;

        /// <summary>Кэш последнего каталога версий (issue #352.4): переключение галочки
        /// «Не повышать» пересобирает цепочки из него, не дёргая сеть.</summary>
        private IReadOnlyList<PlatformRelease>? _catalogReleases;

        /// <summary>Глобальная последняя версия из проверки (без ограничения «Не повышать»);
        /// используется при снятии галочки (issue #352.4).</summary>
        private string _latestVersionFromCheck = string.Empty;

        /// <summary>Галочка «Не повышать» под «Последней версией» (issue #352.4).</summary>
        private readonly CheckBox _noBumpCheckBox = new() { IsChecked = false };

        /// <summary>Кнопка «Выбрать…» рядом с папкой цепочки (issue #352.2).</summary>
        private readonly Button _chainChooseFolderButton = new();

        private readonly TextBlock _baseNameText = new();
        private readonly TextBlock _currentVersionText = new();

        /// <summary>Красное предупреждение «Версии нет на сайте» под текущей версией (issue #352).</summary>
        private readonly TextBlock _currentVersionMissingText = new() { IsVisible = false };

        private readonly TextBlock _latestVersionText = new();
        private readonly TextBlock _urlText = new();
        private readonly TextBlock _statusText = new();
        private readonly TextBlock _errorText = new();
        private readonly StackPanel _authActionsPanel = new() { IsVisible = false };
        private readonly TextBlock _authAccountText = new();
        private readonly StackPanel _progressPanel = new() { IsVisible = false };
        private readonly ProgressBar _progressBar = new() { Minimum = 0, Maximum = 1 };
        private readonly TextBlock _progressText = new();

        // Блок цепочки обновлений (issue #352): секция с таблицей вариантов и прогрессом.
        private readonly StackPanel _chainSection = new() { IsVisible = false };
        private readonly TextBlock _chainHintText = new();
        private readonly TextBlock _chainStatusValue = new();

        // Папка сохранения цепочки (issue #352.2): «Папка: <путь>» + кнопка «Открыть».
        private readonly TextBlock _chainFolderValue = new();
        private readonly Button _chainOpenFolderButton = new();
        private readonly StackPanel _chainFolderPanel = new() { IsVisible = false };

        private readonly StackPanel _chainRowsPanel = new();
        private readonly StackPanel _chainProgressPanel = new() { IsVisible = false };
        private readonly ProgressBar _chainProgressBar = new() { Minimum = 0, Maximum = 1 };
        private readonly TextBlock _chainProgressText = new();
        private Button _checkButton = new();
        private Button _downloadButton = new();
        private Button _downloadChainButton = new();

        /// <param name="infobase">Информационная база, для которой выполняется проверка обновлений.</param>
        public UpdateCheckWindow(Infobase infobase)
        {
            _infobase = infobase ?? throw new ArgumentNullException(nameof(infobase));
            _row = new UpdateCheckRowViewModel(infobase, OnDownloadRow);

            Title = LocalizationManager.T("Updates.CheckTitle");
            Width = 720;
            Height = 660;
            MinWidth = 640;
            MinHeight = 500;
            FontSize = 13;
            CanResize = true;

            _baseNameText.Text = _row.Name;
            _currentVersionText.Text = string.IsNullOrWhiteSpace(_row.CurrentVersion) ? "—" : _row.CurrentVersion;

            _row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(_row.Progress))
                    UpdateProgressDisplay();
                else if (e.PropertyName == nameof(_row.ChainProgress)
                         || e.PropertyName == nameof(_row.ChainProgressText)
                         || e.PropertyName == nameof(_row.IsChainDownloading))
                    UpdateChainProgressDisplay();
                else if (e.PropertyName == nameof(_row.LatestVersion)
                         || e.PropertyName == nameof(_row.Url)
                         || e.PropertyName == nameof(_row.Status)
                         || e.PropertyName == nameof(_row.Error)
                         || e.PropertyName == nameof(_row.CanDownload))
                    RefreshDetailDisplay();
            };

            // Клик по адресу каталога релизов открывает браузер (issue #323).
            _urlText.PointerReleased += OnUrlTextPointerReleased;

            Content = BuildRoot();
            // Папка цепочки (issue #352.2): строка с путём и кнопкой «Открыть».
            RefreshChainFolderDisplay();

            // issue #352 (0.3.12.6): при переоткрытии окна для ИБ с уже идущей фоновой
            // закачкой подхватываем активную запись менеджера — прогресс/кнопки отражают
            // реальное состояние, а не «пустое» окно.
            TryAttachActiveDownload();

            Opened += async (_, _) => await RunCheckAsync();
        }

        /// <summary>
        /// Подхватывает активную фоновую закачку этой ИБ (issue #352, 0.3.12.6): при повторном
        /// открытии окна (клик по индикатору) прогресс и состояние кнопок отражают уже идущую
        /// загрузку из <see cref="BackgroundDownloadManager.Default"/>.
        /// </summary>
        private void TryAttachActiveDownload()
        {
            var name = _row.Name;
            var snapshot = BackgroundDownloadManager.Default.Snapshot();
            var entry = snapshot.FirstOrDefault(d => d.IsActive && IsDownloadForInfobase(d.Id, name));
            if (entry is null)
                return;

            if (entry.Id.StartsWith("update:", StringComparison.Ordinal))
            {
                _row.IsDownloading = true;
                _row.Progress = entry.Progress;
                UpdateProgressDisplay();
            }
            else if (entry.Id.StartsWith("chain:", StringComparison.Ordinal))
            {
                _row.IsChainDownloading = true;
                _row.ChainProgress = entry.Progress;
                UpdateChainProgressDisplay();
            }
        }

        /// <summary>Относится ли запись менеджера (id вида «update:ИМЯ:файл»/«chain:ИМЯ») к базе.</summary>
        private static bool IsDownloadForInfobase(string id, string infobaseName)
        {
            if (id.StartsWith("update:", StringComparison.Ordinal))
            {
                var rest = id["update:".Length..];
                var cut = rest.LastIndexOf(':');
                var baseName = cut >= 0 ? rest[..cut] : rest;
                return string.Equals(baseName, infobaseName, StringComparison.OrdinalIgnoreCase);
            }
            if (id.StartsWith("chain:", StringComparison.Ordinal))
                return string.Equals(id["chain:".Length..], infobaseName, StringComparison.OrdinalIgnoreCase);
            return false;
        }

        /// <summary>
        /// Показывает окно модально (синхронно). Открытая публичная обёртка над
        /// <see cref="ModalWindowBase.ShowDialogSync(Window?)"/>, чтобы диалог можно было
        /// вызывать из ViewModel (не наследника окна).
        /// </summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private static string T(string key) => LocalizationManager.T(key);

        /// <summary>Запускает сетевую проверку наличия обновлений для связанной конфигурации.</summary>
        private async Task RunCheckAsync()
        {
            if (_row.IsChecking)
                return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _row.IsChecking = true;
            _checkButton.IsEnabled = false;
            _errorText.Text = string.Empty;

            try
            {
                // Явное связывание (UpdateConfigCode) — приоритет. Если связи нет, но свойства
                // конфигурации базы определены (вкладка «Платформа»), строим каталог релизов из них:
                // типовая конфигурация подбирается автоматически по имени (issue #323).
                var config = FindEffectiveConfig();

                // Редакция по версии базы («3.1.38.92» → «3.1»), иначе первая редакция по умолчанию
                // (issue #346: в F9 раньше всегда бралась DefaultEdition, версия не учитывалась).
                var edition = ConfigTypeMatcher.FindEditionByVersion(config, _infobase.ConfigurationVersion)
                    ?? config?.DefaultEdition;
                var url = _updates.BuildUpdateUrl(config, edition, _infobase.UpdateUrlOverride,
                    _infobase.UpdateUrlSegment);
                _row.Url = url;

                if (string.IsNullOrWhiteSpace(url))
                {
                    // Понятное пояснение вместо сухого «не задан адрес»: база не связана с типовой
                    // конфигурацией либо у конфигурации нет сегмента/ника каталога релизов (issue #323).
                    _row.Status = ConfigUpdateStatus.Failed;
                    _row.Error = EmptyUrlMessage();
                }
                else
                {
                    var configName = config?.Name ?? _row.Name;
                    var currentVersion = _row.CurrentVersion;
                    var result = await Task.Run(
                        () => _updates.CheckForUpdatesAsync(configName, currentVersion, url, token,
                            accountId: config?.AccountId), token);
                    _row.ApplyResult(result);
                    SaveUpdateCache(result);
                    // Цепочка обновлений (issue #352): при наличии нового релиза строим
                    // варианты от текущей версии до последней и показываем их в таблице.
                    await BuildChainsAsync(url, token, config?.AccountId);
                }
            }
            catch (OperationCanceledException)
            {
                _row.Status = ConfigUpdateStatus.Failed;
                _row.Error = T("Updates.Cancelled");
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка проверки обновлений конфигурации базы «{_row.Name}»", ex);
                _row.Status = ConfigUpdateStatus.Failed;
                _row.Error = T("Updates.NetworkError");
            }
            finally
            {
                _row.IsChecking = false;
                _checkButton.IsEnabled = true;
                RefreshDetailDisplay();
            }
        }

        /// <summary>Находит типовую конфигурацию, связанную с базой (по коду связи).
        /// Общий список типовых = встроенные + пользовательские из файла custom_config_types.json
        /// (единый загрузчик <see cref="Services.ICustomConfigTypesStore"/> — issue #321).</summary>
        private OneCConfigType? FindLinkedConfig()
        {
            var code = _infobase.UpdateConfigCode;
            if (string.IsNullOrWhiteSpace(code))
                return null;

            return _store.LoadAll().FirstOrDefault(c =>
                string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Учётная запись ИТС, назначенная связанной конфигурации (issue #371): передаётся
        /// сервису для запросов к сайту 1С (проверка/каталог/файлы релиза). Пусто — сервис использует
        /// «Основную» запись справочника либо выбранную в настройках (issue #333).</summary>
        private string? ResolveAccountId() => ConfigTypeMatcher.AccountIdFor(FindEffectiveConfig());

        /// <summary>
        /// Типовая конфигурация базы для каталога релизов И учётной записи ИТС (issue #13): явная
        /// связь, иначе автоподбор по имени конфигурации базы. Раньше учётная запись бралась только
        /// по явной связи — для базы без связи использовалась «Основная», хотя адрес каталога
        /// строился по подобранной конфигурации с другой назначенной учётной записью.
        /// </summary>
        private OneCConfigType? FindEffectiveConfig() =>
            ConfigTypeMatcher.ResolveForInfobase(_store.LoadAll(), _infobase.UpdateConfigCode,
                _infobase.ConfigurationName);

        /// <summary>Обновляет текстовые ячейки деталей и состояние кнопок из модели строки.</summary>
        private void RefreshDetailDisplay()
        {
            _latestVersionText.Text = string.IsNullOrWhiteSpace(_row.LatestVersion) ? "—" : _row.LatestVersion;
            UpdateUrlLinkDisplay();
            _statusText.Text = StatusTextLocalized(_row.Status);

            IBrush? brush = TryGetBrush("TextSecondaryBrush") ?? new SolidColorBrush(Colors.Gray);
            var primary = TryGetBrush("TextPrimaryBrush");
            if (primary is not null) brush = primary;
            if (_row.HasNewer)
            {
                brush = TryGetBrush("SuccessBrush") ?? new SolidColorBrush(Color.Parse("#16A34A"));
            }
            else if (_row.Status == ConfigUpdateStatus.Failed)
            {
                brush = TryGetBrush("ErrorBrush") ?? new SolidColorBrush(Color.Parse("#DC2626"));
            }
            _statusText.Foreground = brush;

            // Error может быть ключом локализации (Updates.*) либо свободным текстом («HTTP 500»):
            // ключ переводим, свободный текст LocalizationManager.T() вернёт как есть.
            _errorText.Text = _row.Status == ConfigUpdateStatus.Failed ? LocalizeError(_row.Error) : string.Empty;
            _downloadButton.IsEnabled = _row.CanDownload;

            // При ошибке авторизации показываем панель действий: имя учётной записи ИТС,
            // «Открыть login.1c.ru в браузере», «Учётные данные ИТС…» (issue #323/#330/#334).
            ShowAuthActions(_row.Status == ConfigUpdateStatus.Failed && IsAuthErrorKey(_row.Error));

            // Блок цепочки обновлений (issue #352): таблица вариантов и кнопка «Скачать цепочку».
            RefreshChainDisplay();
        }

        /// <summary>Показывает/скрывает панель действий при ошибке авторизации и заполняет имя
        /// используемой учётной записи ИТС (анонимизированно — без логина, issue #323/#330/#334).</summary>
        private void ShowAuthActions(bool visible)
        {
            _authActionsPanel.IsVisible = visible;
            if (!visible)
                return;

            var settings = _repository.LoadSettings();
            // issue #371: имя показываемой записи — из привязки конфигурации (используется
            // для запросов), иначе глобальная из настроек.
            var accountId = ResolveAccountId();
            var account = _itsAccounts.Resolve(
                string.IsNullOrWhiteSpace(accountId) ? settings.ItsAccountId : accountId);
            var accountName = account is not null && !string.IsNullOrWhiteSpace(account.Name)
                ? account.Name!
                : T("Updates.AccountPrimary");
            _authAccountText.Text = string.Format(T("Updates.AccountUsed"), accountName);
        }

        /// <summary>True — ключ ошибки относится к авторизации на portal.1c.ru (для панели действий).</summary>
        private static bool IsAuthErrorKey(string error)
            => error is "Updates.AuthRequired" or "Updates.AuthFailed"
                or "Updates.FormUnavailable" or "Updates.LoginLimitReached"
                or "Updates.NoAccess";

        /// <summary>Открывает login.1c.ru в браузере (issue #323/#330/#334): пользователь выполняет
        /// вход вручную, после чего возвращается в окно и повторяет проверку.</summary>
        private void OnOpenLoginClick()
        {
            if (!OneCLauncher.OpenUrl("https://login.1c.ru/login"))
                _errorText.Text = T("Settings.About.LinkOpenFailed");
        }

        /// <summary>Открывает справочник учётных записей ИТС (issue #323/#330/#334): после правки
        /// данных повторный вход использует обновлённую запись.</summary>
        private void OnItsAccountsClick()
        {
            var win = new ItsAccountsWindow();
            win.ShowSync(this);
        }

        /// <summary>Открывает список типовых конфигураций (issue #371): пользователь может
        /// проверить ссылки каталогов релизов и редакции сразу, не закрывая окно проверки.</summary>
        private void OnOpenConfigListClick()
        {
            var win = new ConfigTypesEditWindow();
            win.ShowSync(this);
        }

        /// <summary>Обновляет ссылку каталога релизов: текст, активность и вид (issue #323).
        /// Валидный http/https-адрес — кликабельная ссылка (AccentBrush, подчёркивание, курсор Hand,
        /// ToolTip); пустой/невалидный адрес («—») — обычный вторичный текст без перехода.</summary>
        private void UpdateUrlLinkDisplay()
        {
            var url = string.IsNullOrWhiteSpace(_row.Url) ? null : _row.Url;
            _urlText.Text = url ?? "—";

            var valid = url is not null
                        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
                        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
            _urlLinkEnabled = valid;

            if (valid)
            {
                _urlText.TextDecorations = TextDecorations.Underline;
                _urlText.Cursor = new Cursor(StandardCursorType.Hand);
                Themes.ThemeBrushes.Bind(_urlText, TextBlock.ForegroundProperty, "AccentBrush");
                ToolTip.SetTip(_urlText, T("Updates.OpenCatalog"));
            }
            else
            {
                _urlText.TextDecorations = null;
                _urlText.Cursor = Cursor.Default;
                Themes.ThemeBrushes.Bind(_urlText, TextBlock.ForegroundProperty, "TextSecondaryBrush");
                ToolTip.SetTip(_urlText, null);
            }
        }

        /// <summary>Клик по адресу каталога релизов открывает браузер через
        /// <see cref="OneCLauncher.OpenUrl"/> (не собственным Process.Start, issue #323).
        /// Проверка попадания по Bounds — как в ссылке окна «Ручное обновление».</summary>
        private void OnUrlTextPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_urlLinkEnabled || e.InitialPressMouseButton != MouseButton.Left)
                return;

            var point = e.GetPosition(_urlText);
            if (point.X < 0 || point.Y < 0
                || point.X > _urlText.Bounds.Width || point.Y > _urlText.Bounds.Height)
                return;

            if (!OneCLauncher.OpenUrl(_row.Url))
            {
                _errorText.Text = T("Settings.About.LinkOpenFailed");
            }
        }

        /// <summary>Локализует текст ошибки: ключи «Updates.*» переводит, остальное возвращает без изменений.</summary>
        private static string LocalizeError(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
                return string.Empty;
            return LocalizationManager.T(error);
        }

        /// <summary>
        /// Пояснение при пустом адресе каталога релизов: различает «база не связана», «у конфигурации
        /// нет ника на releases.1c.ru» и «нет адреса» (issue #323).
        /// </summary>
        private string EmptyUrlMessage()
        {
            if (!string.IsNullOrWhiteSpace(_infobase.UpdateConfigCode))
            {
                var config = FindLinkedConfig();
                if (config is not null && string.IsNullOrWhiteSpace(config.Nick))
                    return string.Format(T("Updates.NoNick"), config.Name);
                return T("Updates.NoUrl");
            }

            if (!string.IsNullOrWhiteSpace(_infobase.ConfigurationName))
            {
                var config = ConfigTypeMatcher.FindByInfobaseName(_store.LoadAll(), _infobase.ConfigurationName);
                if (config is not null && string.IsNullOrWhiteSpace(config.Nick))
                    return string.Format(T("Updates.NoNick"), config.Name);
                // Имя конфигурации базы не сопоставилось ни с одной типовой (issue #346):
                // сообщаем конкретную причину вместо общего «База не связана с типовой».
                if (config is null)
                    return string.Format(T("Updates.NoMatchFound"), _infobase.ConfigurationName);
            }
            return T("Updates.NoLink");
        }

        /// <summary>
        /// Сохраняет результат успешной проверки в <see cref="Models.AppSettings.UpdateCheckCache"/>
        /// (по коду связи базы), чтобы колонка «Обновление» в Центре обслуживания показывала
        /// последний результат. Ошибки/отмена в кэш не пишутся.
        /// </summary>
        private void SaveUpdateCache(Models.ConfigUpdateCheckResult result)
        {
            if (!result.Succeeded || string.IsNullOrWhiteSpace(_infobase.UpdateConfigCode))
                return;
            try
            {
                var settings = _repository.LoadSettings();
                settings.UpdateCheckCache ??= new System.Collections.Generic.Dictionary<string, Models.ConfigUpdateCheckResult>();
                settings.UpdateCheckCache[_infobase.UpdateConfigCode] = result;
                _repository.SaveSettings(settings);
            }
            catch (Exception ex)
            {
                _logger.Warn("Не удалось сохранить результат проверки обновлений в кэш: " + ex.Message);
            }
        }

        private IBrush? TryGetBrush(string key)
        {
            if (Application.Current is not { } app || !app.TryFindResource(key, out var found))
                return null;
            return found as IBrush;
        }

        private void UpdateProgressDisplay()
        {
            // Progress<double> приходит из фонового потока — маршализуем обновление в UI-поток.
            Dispatcher.UIThread.Post(() =>
            {
                if (_row.IsDownloading)
                {
                    _progressPanel.IsVisible = true;
                    _progressBar.Value = Math.Clamp(_row.Progress, 0, 1);
                    var percent = $"{Math.Round(_row.Progress * 100, 0):0} %";
                    // issue #360: «45 % · 16:13» — оценка оставшегося времени рядом с процентами.
                    _progressText.Text = Services.DownloadEta.AppendSuffix(
                        percent, _singleDownloadEta?.EstimatedRemaining);
                }
                else
                {
                    _progressPanel.IsVisible = false;
                }
            });
        }

        private async void OnCheckClick()
        {
            await RunCheckAsync();
        }

        /// <summary>Загружает дистрибутив обновления по ссылке каталога релизов (кнопка «Скачать»).
        /// С 0.3.11 (issue #352.1): варианты файлов релиза («Дистрибутив обновления» /
        /// «Полный дистрибутив»), 1 вариант — сразу, 2+ — диалог выбора, 0 — прежний путь.</summary>
        private async void OnDownloadRow(UpdateCheckRowViewModel row)
        {
            if (!row.CanDownload || string.IsNullOrWhiteSpace(row.Url))
                return;

            row.IsDownloading = true;
            row.Progress = 0;
            UpdateProgressDisplay();
            _cts?.Cancel();

            try
            {
                IReadOnlyList<UpdateFileChoice> choices = Array.Empty<UpdateFileChoice>();
                try
                {
                    // issue #352.4: при «Не повышать» скачивается ограниченная последняя версия.
                    // issue #352.1 (0.3.12.2): известная «Последняя версия» передаётся сервису,
                    // чтобы адрес без ver резолвился в неё, а не в глобальную последнюю.
                    var downloadUrl = ResolveDownloadUrl(row);
                    choices = await Task.Run(() =>
                        _updates.GetReleaseFileChoicesAsync(downloadUrl, CancellationToken.None, row.LatestVersion,
                            ResolveAccountId()));
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Не удалось получить варианты файлов релиза: {ex.Message}");
                }

                var choice = choices.Count switch
                {
                    1 => choices[0],
                    > 1 => ShowFileChoiceDialog(choices),
                    _ => null,
                };

                if (choice is not null)
                {
                    await DownloadUpdateChoiceAsync(row, choice);
                }
                else if (choices.Count == 0)
                {
                    // Fallback — прежний путь: страница скачивания по цепочке, имя «.zip».
                    var targetPath = ResolveSavePath(BuildDownloadFileName(row, null));
                    if (string.IsNullOrWhiteSpace(targetPath))
                        return;
                    await DownloadUpdateFileAsync(row, row.Url, targetPath);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка загрузки обновления конфигурации «{row.Name}»", ex);
                _dialogs.ShowError(T("Updates.NetworkError"), T("Updates.CheckTitle"));
            }
            finally
            {
                row.IsDownloading = false;
                UpdateProgressDisplay();
            }
        }

        /// <summary>Диалог выбора файла релиза (2+ варианта); null — пользователь отменил.</summary>
        private UpdateFileChoice? ShowFileChoiceDialog(IReadOnlyList<UpdateFileChoice> choices)
        {
            try
            {
                return UpdateFileChoiceWindow.Pick(choices,
                    string.Format(T("Updates.FileChoice.Heading"), _row.Name), this);
            }
            catch (Exception ex)
            {
                // Диалог не должен ронять скачивание — берём первый (приоритетный) вариант.
                _logger.Warn("Не удалось показать выбор файла релиза: " + ex.Message);
                return choices[0];
            }
        }

        /// <summary>Скачивает выбранный вариант файла релиза (issue #352.1).</summary>
        private async Task DownloadUpdateChoiceAsync(UpdateCheckRowViewModel row, UpdateFileChoice choice)
        {
            var targetPath = ResolveSavePath(BuildDownloadFileName(row, choice));
            if (string.IsNullOrWhiteSpace(targetPath))
                return;
            await DownloadUpdateFileAsync(row, choice.Url, targetPath);
        }

        /// <summary>Путь сохранения одиночного скачивания (issue #352.2, 0.3.12.2): при
        /// существующей папке одиночного дистрибутива в настройках файл сохраняется в неё БЕЗ
        /// диалога; иначе — папка цепочки (совместимость), затем диалог с начальным каталогом.
        /// Раздельна с цепочкой (issue #352, 0.3.12.6): «что для полной версии, что для цепочки».</summary>
        private string? ResolveSavePath(string fileName)
        {
            var settings = _repository.LoadSettings();
            var direct = UpdateChainDownloadPlanner.ResolveSaveTarget(
                PickDistributionFolder(settings), fileName);
            if (!string.IsNullOrWhiteSpace(direct))
                return direct;

            return AskSavePath(fileName);
        }

        /// <summary>Папка одиночного дистрибутива: отдельная настройка, при пустоте — папка цепочки.</summary>
        private static string PickDistributionFolder(AppSettings settings)
            => !string.IsNullOrWhiteSpace(settings.UpdateDistributionFolder)
                ? settings.UpdateDistributionFolder
                : settings.UpdateChainFolder;

        /// <summary>Диалог сохранения с фильтром известных расширений дистрибутивов;
        /// начальный каталог — сохранённая папка одиночного дистрибутива (issue #352), иначе профиль.</summary>
        private string? AskSavePath(string fileName)
        {
            var settings = _repository.LoadSettings();
            var initial = UpdateChainDownloadPlanner.GetChainInitialFolder(PickDistributionFolder(settings));
            var path = _dialogs.SaveFileDialog(
                T("Updates.Download"),
                fileName,
                "Архивы (*.zip;*.rar;*.7z)|*.zip;*.rar;*.7z|Файлы 1С (*.cf;*.cfu)|*.cf;*.cfu|Все файлы (*.*)|*.*",
                initial);
            // issue #352 (0.3.12.6): запоминаем папку одиночного дистрибутива (раздельно с цепочкой).
            if (!string.IsNullOrWhiteSpace(path))
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                    SaveDistributionFolder(dir);
            }
            return path;
        }

        /// <summary>Сохраняет папку одиночного дистрибутива в настройки (пустая папка не пишется).</summary>
        private void SaveDistributionFolder(string folder)
        {
            try
            {
                var settings = _repository.LoadSettings();
                settings.UpdateDistributionFolder = folder;
                _repository.SaveSettings(settings);
            }
            catch (Exception ex)
            {
                _logger.Warn("Не удалось сохранить папку одиночного дистрибутива: " + ex.Message);
            }
        }

        /// <summary>
        /// Общая загрузка файла обновления с прогрессом и отчётом о результате.
        /// Загрузка регистрируется в менеджере фоновых загрузок (issue #352, 0.3.12.3):
        /// продолжается после закрытия окна, видна в индикаторе главного окна и
        /// отменяется из него — как и загрузка цепочки (<see cref="DownloadChainAsync"/>).
        /// </summary>
        private async Task DownloadUpdateFileAsync(UpdateCheckRowViewModel row, string url, string targetPath)
        {
            var fileName = Path.GetFileName(targetPath);

            // issue #22 (1.0.0.26, Этап C): перед ручным запуском проверяем, не качается ли
            // уже этот файл в фоне; при конфликте — диалог с вариантами. «Отменить фоновую
            // загрузку и начать заново» отменяет фоновую запись и продолжает ручную загрузку;
            // «Подождать»/«Отмена» — вторую загрузку не начинаем.
            var guard = new Services.DownloadConflictGuard(Services.BackgroundDownloadManager.Default);
            var decision = guard.Resolve(targetPath, AskDownloadConflict);
            if (decision == Services.DownloadConflictDecision.CancelBackgroundAndRestart)
            {
                var active = guard.FindActiveDownload(targetPath);
                if (active is not null)
                    Services.BackgroundDownloadManager.Default.Cancel(active.Id);
            }
            else if (decision != Services.DownloadConflictDecision.Proceed)
            {
                _logger.Info($"Скачивание «{fileName}» не начато: файл уже скачивается в фоне.");
                return;
            }

            // issue #352 (0.3.12.3): запись в менеджере заводится ДО скачивания,
            // реальный токен отмены вместо CancellationToken.None.
            var downloadId = $"update:{row.Name}:{fileName}";
            var entry = Services.BackgroundDownloadManager.Default.Start(downloadId, fileName);

            // issue #360: оценка оставшегося времени — обновляется ДО присваивания
            // row.Progress: UpdateProgressDisplay читает её после маршала в UI-поток.
            _singleDownloadEta = new Services.DownloadEtaTracker();
            var progress = new Progress<double>(v => Dispatcher.UIThread.Post(() =>
            {
                _singleDownloadEta.Update(v);
                row.Progress = Math.Clamp(v, 0, 1);
                Services.BackgroundDownloadManager.Default.ReportProgress(downloadId, row.Progress);
            }));

            try
            {
                var savedPath = await Task.Run(() =>
                    _updates.DownloadUpdateAsync(url, targetPath, progress, entry.Cancellation.Token,
                        ResolveAccountId()));

                if (string.IsNullOrWhiteSpace(savedPath))
                {
                    if (entry.Cancellation.Token.IsCancellationRequested)
                    {
                        // Отмена из индикатора главного окна: состояние Cancelled
                        // уже выставлено менеджером (Cancel); здесь только журнал.
                        _logger.Info($"Скачивание «{fileName}» отменено пользователем.");
                    }
                    else
                    {
                        Services.BackgroundDownloadManager.Default.Fail(downloadId, "Updates.NetworkError");
                        _dialogs.ShowWarning(T("Updates.NetworkError"), T("Updates.CheckTitle"));
                    }
                }
                else
                {
                    if (entry.IsActive)
                        Services.BackgroundDownloadManager.Default.Complete(downloadId);
                    row.Progress = 1;
                    UpdateProgressDisplay();
                    _dialogs.ShowInfo(string.Format(T("Updates.Loaded"), savedPath), T("Updates.CheckTitle"));
                }
            }
            catch (OperationCanceledException)
            {
                // Отмена из индикатора главного окна: состояние Cancelled уже
                // выставлено менеджером; здесь только журнал, без диалога.
                _logger.Info($"Скачивание «{fileName}» отменено пользователем.");
            }
            catch (Exception ex)
            {
                Services.BackgroundDownloadManager.Default.Fail(downloadId, "Updates.NetworkError");
                _logger.Error($"Ошибка скачивания «{fileName}» ({url})", ex);
                _dialogs.ShowWarning(T("Updates.NetworkError"), T("Updates.CheckTitle"));
            }
        }

        /// <summary>Имя сохранения (issue #352, 0.3.12.12): оригинальное имя файла дистрибутива,
        /// как на сайте (<c>Trade_11_5_27_98_setup1c.zip</c>), без префикса «&lt;Имя&gt;_&lt;версия&gt;_»
        /// (пожелание 7OH от 2026-10-11). «Полный дистрибутив» и «Дистрибутив обновления»
        /// различаются самими оригинальными именами файлов. Без варианта (fallback) — прежнее
        /// «&lt;Имя&gt;_&lt;версия&gt;.zip».</summary>
        private static string BuildDownloadFileName(UpdateCheckRowViewModel row, UpdateFileChoice? choice)
            => UpdateDownloadNaming.BuildDownloadFileName(
                row.Name, row.LatestVersion, choice?.FileName, SanitizeFileName);

        /// <summary>
        /// Уточняет заголовок фоновой загрузки цепочки именем файла (issue #352): в индикаторе
        /// главного окна вместо названия конфигурации («УТ 11») показывается имя скачиваемого
        /// файла; пустое имя (резолв не удался) — заголовок не меняется.
        /// </summary>
        private static void UpdateChainTitle(string chainId, string targetPath)
        {
            var fileName = Path.GetFileName(targetPath);
            if (!string.IsNullOrWhiteSpace(fileName))
                Services.BackgroundDownloadManager.Default.UpdateTitle(chainId, fileName);
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Concat(name.Select(ch => invalid.Contains(ch) ? '_' : ch));
        }

        private static string StatusTextLocalized(ConfigUpdateStatus status)
        {
            return status switch
            {
                ConfigUpdateStatus.UpToDate => LocalizationManager.T("Updates.UpToDate"),
                ConfigUpdateStatus.NewerAvailable => LocalizationManager.T("Updates.NewerAvailable"),
                ConfigUpdateStatus.Unavailable => LocalizationManager.T("Updates.Unavailable"),
                ConfigUpdateStatus.Failed => LocalizationManager.T("Updates.CheckFailed"),
                _ => LocalizationManager.T("Updates.Status"),
            };
        }

        /// <summary>
        /// Строит цепочку обновлений (issue #352): получает полный каталог версий конфигурации
        /// через <see cref="Services.IOneCUpdatesService.GetUpdateCatalogAsync"/> и вычисляет
        /// варианты от текущей версии до последней (<see cref="UpdateChainBuilder"/>). Ошибки
        /// каталога не роняют результат основной проверки: состояние цепочки сбрасывается,
        /// остаётся прежнее поведение — «Скачать» только последнюю версию.
        /// </summary>
        private async Task BuildChainsAsync(string url, CancellationToken token, string? accountId = null)
        {
            _row.ResetChains();
            if (!_row.HasNewer || string.IsNullOrWhiteSpace(_row.CurrentVersion)
                || string.IsNullOrWhiteSpace(_row.LatestVersion))
            {
                RefreshChainDisplay();
                return;
            }

            // Глобальная последняя версия запоминается до наложения ограничения
            // «Не повышать» — она возвращается при снятии галочки (issue #352.4).
            _latestVersionFromCheck = _row.LatestVersion;

            try
            {
                // issue #352: каталог запрашивается с allUpdates=true — без параметра портал
                // отдаёт только последние релизы, и цепочка не строится.
                var catalogUrl = OneCUpdatesService.BuildAllUpdatesCatalogUrl(url);
                var catalog = await Task.Run(() => _updates.GetUpdateCatalogAsync(catalogUrl, token, accountId), token);
                if (catalog.Status == PortalFetchStatus.Ok)
                {
                    // Кэш каталога (issue #352.4): переключение галочки «Не повышать»
                    // пересобирает цепочки из него без повторного запроса.
                    _catalogReleases = catalog.Releases;

                    var target = _row.NoVersionBump
                        ? UpdateChainBuilder.SelectCappedTarget(_row.CurrentVersion, catalog.Releases)
                            ?? _row.LatestVersion
                        : _row.LatestVersion;
                    if (!string.Equals(target, _row.LatestVersion, StringComparison.Ordinal))
                        _row.LatestVersion = target;

                    var set = UpdateChainBuilder.Build(_row.CurrentVersion, target, catalog.Releases);
                    _row.SetChains(set);
                }
                else
                {
                    _logger.Warn($"Не удалось получить каталог версий для цепочки обновлений ({catalog.ErrorKey}).");
                }
            }
            catch (OperationCanceledException)
            {
                // Проверка отменена — цепочка не строится (прежнее поведение).
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка построения цепочки обновлений для «{_row.Name}»", ex);
            }

            RefreshChainDisplay();
        }

        /// <summary>Обновляет блок цепочки обновлений: видимость секции и кнопки «Скачать цепочку»,
        /// статус и состав таблицы вариантов (issue #352).</summary>
        private void RefreshChainDisplay()
        {
            Dispatcher.UIThread.Post(() =>
            {
                var hasChain = _row.HasChain;
                _chainSection.IsVisible = hasChain;
                _downloadChainButton.IsVisible = hasChain;
                _downloadChainButton.IsEnabled = _row.CanDownloadChain;

                if (!hasChain)
                {
                    _chainRowsPanel.Children.Clear();
                    return;
                }

                // issue #352: при появлении таблицы вариантов окно поднимается так, чтобы
                // 2–3 строки таблицы были видны без прокрутки.
                EnsureWindowHeightForChain();

                _chainStatusValue.Text = _row.ChainStatusText;
                // issue #352: красное предупреждение под текущей версией.
                _currentVersionMissingText.IsVisible = _row.CurrentVersionMissing;
                RebuildChainRows();
            });
        }

        /// <summary>Поднимает высоту окна при появлении таблицы вариантов цепочки (issue #352):
        /// 2–3 строки таблицы должны быть видны без прокрутки. Ограничение — рабочая область
        /// экрана (окно не должно становиться выше неё).</summary>
        /// <summary>Поднимает высоту окна при появлении таблицы вариантов цепочки (issue #352):
        /// 2–3 строки таблицы должны быть видны без прокрутки. Цель 800 (было 760): в
        /// 0.3.12.1 под таблицей добавлена панель папки цепочки с кнопками «Выбрать…»/
        /// «Открыть» (issue #352.2) — без запаса кнопка «Открыть» была обрезана снизу.
        /// Ограничение — рабочая область экрана.</summary>
        private void EnsureWindowHeightForChain()
        {
            try
            {
                var target = 800d;
                var screen = Screens.Primary;
                if (screen is not null && screen.Scaling > 0)
                {
                    var workHeight = screen.WorkingArea.Height / screen.Scaling;
                    target = Math.Min(target, Math.Max(workHeight - 40d, MinHeight));
                }

                if (Height < target)
                    Height = target;

                // issue #352 (7OH от 2026-10-11): при появлении таблицы цепочек низ
                // окна уходил за панель задач — поднимаем окно в пределы рабочей
                // области (WindowSizeMath.FitTop, как в окне «Серверы 1С», #368).
                // Подъём выполняется ВСЕГДА (не только при увеличении высоты): окно,
                // уже растянутое пользователем выше рабочей области, тоже должно
                // вернуться в её пределы.
                if (screen is not null && screen.Scaling > 0)
                {
                    // Рабочая область — PixelRect в физических пикселях; позиция окна
                    // задаётся через Position (PixelPoint, точки DIP от верхнего левого угла).
                    var wa = screen.WorkingArea;
                    var workTop = wa.Y / screen.Scaling;
                    var workBottom = (wa.Y + wa.Height) / screen.Scaling;
                    var desiredTop = WindowSizeMath.FitTop(Position.Y / screen.Scaling, Height, workTop, workBottom);
                    var newTopPx = (int)Math.Round(desiredTop * screen.Scaling);
                    if (newTopPx != Position.Y)
                        Position = new PixelPoint(Position.X, newTopPx);
                }
            }
            catch
            {
                // Определение экрана не должно мешать показу таблицы цепочки.
            }
        }

        /// <summary>Кнопка «Выбрать…» рядом с папкой цепочки (issue #352.2): выбор каталога
        /// сохраняется в настройках, без диалога при нажатии «Скачать цепочку».</summary>
        private void OnChooseChainFolderClick()
        {
            ChooseChainFolder();
        }

        /// <summary>Общий выбор папки цепочки (issue #352.2): диалог выбора каталога →
        /// сохранение в настройки → обновление строки пути. Null — пользователь отменил.</summary>
        private string? ChooseChainFolder()
        {
            var settingsFolder = _repository.LoadSettings().UpdateChainFolder;
            var folder = _dialogs.OpenFolderDialog(
                T("Updates.Chain.ChooseFolder"),
                UpdateChainDownloadPlanner.GetChainInitialFolder(settingsFolder));
            if (string.IsNullOrWhiteSpace(folder))
                return null;

            SaveChainFolder(folder);
            RefreshChainFolderDisplay();
            return folder;
        }

        /// <summary>Переключение галочки «Не повышать» (issue #352.4): цепочки и «Последняя
        /// версия» пересчитываются из кэшированного каталога (без повторного запроса сети).</summary>
        private void OnNoVersionBumpChanged()
        {
            ApplyNoVersionBump(_noBumpCheckBox.IsChecked == true);
        }

        /// <summary>Применяет ограничение «Не повышать» (issue #352.4): при включении целевой
        /// версией становится максимум той же линии (3.1.x), при выключении — глобальная
        /// последняя из проверки; цепочки пересобираются из кэшированного каталога.</summary>
        private void ApplyNoVersionBump(bool enabled)
        {
            if (string.IsNullOrWhiteSpace(_row.CurrentVersion))
                return;

            var releases = _catalogReleases;
            if (releases is null || releases.Count == 0)
            {
                // Каталог ещё не получен (проверка не выполнялась/не удалась) — повторный
                // запрос как при обычной проверке (issue #352.4).
                _ = RunCheckAsync();
                return;
            }

            var target = enabled
                ? UpdateChainBuilder.SelectCappedTarget(_row.CurrentVersion, releases)
                : _latestVersionFromCheck;
            if (string.IsNullOrWhiteSpace(target))
                return;

            _row.LatestVersion = target;
            var set = UpdateChainBuilder.Build(_row.CurrentVersion, target, releases);
            _row.SetChains(set);
            RefreshChainDisplay();
        }

        /// <summary>Адрес для кнопки «Скачать» (issue #352.4): при включённой галочке «Не
        /// повышать» строится прямая ссылка version_files?nick&ver кап-версии — даже когда
        /// кап равен отображаемой «Последней версии» (0.3.12.2, issue #352.1: раньше при
        /// равенстве возвращался сырой URL каталога и сервис резолвил глобальную последнюю);
        /// при выключенной галочке и известной версии — version_files этой версии.
        /// Логика — в чистом хелпере <see cref="UpdateChainBuilder.ResolveSingleDownloadUrl"/>
        /// (юнит-тесты).</summary>
        private string ResolveDownloadUrl(UpdateCheckRowViewModel row)
        {
            return UpdateChainBuilder.ResolveSingleDownloadUrl(
                row.CurrentVersion, row.LatestVersion, row.NoVersionBump, _catalogReleases, row.Url);
        }

        /// <summary>Перестраивает строки таблицы вариантов цепочки («№» и «Список версий»).</summary>
        private void RebuildChainRows()
        {
            _chainRowsPanel.Children.Clear();
            foreach (var variant in _row.ChainVariants)
                _chainRowsPanel.Children.Add(MakeChainRow(variant));
        }

        /// <summary>Строка таблицы вариантов: номер и список версий (issue #352);
        /// клик по строке выбирает вариант для «Скачать цепочку», подсказка — тип варианта.</summary>
        private Control MakeChainRow(UpdateChainVariantViewModel variant)
        {
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(36)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            var number = new TextBlock
            {
                Text = variant.Number.ToString(),
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Themes.ThemeBrushes.Bind(number, TextBlock.ForegroundProperty, "TextPrimaryBrush");
            Grid.SetColumn(number, 0);
            grid.Children.Add(number);

            var versions = new TextBlock
            {
                Text = variant.VersionsText,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Themes.ThemeBrushes.Bind(versions, TextBlock.ForegroundProperty, "TextPrimaryBrush");
            ToolTip.SetTip(versions, variant.KindText);
            Grid.SetColumn(versions, 1);
            grid.Children.Add(versions);

            grid.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left)
                    _row.SelectedVariant = variant;
            };

            return grid;
        }

        // ===================== Папка цепочки обновлений (issue #352.2) =====================

        /// <summary>Сохраняет папку цепочки в настройки (пустая папка не пишется).</summary>
        private void SaveChainFolder(string folder)
        {
            try
            {
                var settings = _repository.LoadSettings();
                settings.UpdateChainFolder = folder;
                _repository.SaveSettings(settings);
            }
            catch (Exception ex)
            {
                _logger.Warn("Не удалось сохранить папку цепочки обновлений: " + ex.Message);
            }
        }

        /// <summary>Обновляет строку «Папка: …» над таблицей цепочки: путь либо подсказка
        /// «выберите папку при скачивании» (issue #352.2).</summary>
        private void RefreshChainFolderDisplay()
        {
            var folder = _repository.LoadSettings().UpdateChainFolder;
            var hasFolder = !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder);
            _chainFolderValue.Text = hasFolder ? folder : T("Updates.Chain.FolderHint");
            _chainOpenFolderButton.IsEnabled = hasFolder;
        }

        /// <summary>Кнопка «Открыть»: показывает сохранённую папку цепочки в файловом менеджере.</summary>
        private void OnOpenChainFolderClick()
        {
            try
            {
                var folder = _repository.LoadSettings().UpdateChainFolder;
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                    return;

                using var process = new System.Diagnostics.Process();
                process.StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "xdg-open",
                    UseShellExecute = false,
                    ArgumentList = { folder },
                };
                process.Start();
            }
            catch (Exception ex)
            {
                _logger.Warn("Не удалось открыть папку цепочки обновлений: " + ex.Message);
            }
        }

        /// <summary>Обновляет панель прогресса цепочки: какой файл скачивается и сколько осталось.</summary>
        private void UpdateChainProgressDisplay()
        {
            Dispatcher.UIThread.Post(() =>
            {
                _chainProgressPanel.IsVisible = _row.IsChainDownloading;
                _chainProgressBar.Value = Math.Clamp(_row.ChainProgress, 0, 1);
                _chainProgressText.Text = _row.ChainProgressText;
                _downloadChainButton.IsEnabled = _row.CanDownloadChain;
            });
        }

        /// <summary>Кнопка «Скачать цепочку» (issue #352).</summary>
        private async void OnDownloadChainClick()
        {
            await DownloadChainAsync();
        }

        /// <summary>
        /// Скачивает выбранную цепочку обновлений в указанный каталог (issue #352):
        /// последовательно все версии варианта (от текущей к последней) с прогрессом —
        /// какой файл скачивается и сколько ещё осталось.
        /// </summary>
        private async Task DownloadChainAsync()
        {
            if (!_row.CanDownloadChain || string.IsNullOrWhiteSpace(_row.Url))
                return;

            var variant = _row.SelectedVariant ?? _row.ChainVariants.FirstOrDefault();
            if (variant is null || variant.Steps.Count == 0)
                return;

            // issue #352.2: сохранённая папка используется без повторного вопроса;
            // диалог показывается только если папка не выбрана либо не существует.
            var folder = _repository.LoadSettings().UpdateChainFolder;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                folder = ChooseChainFolder();
                if (string.IsNullOrWhiteSpace(folder))
                    return;
            }

            _row.IsChainDownloading = true;
            _row.ChainProgress = 0;
            UpdateChainProgressDisplay();

            // issue #334 п.1: загрузка цепочки регистрируется в менеджере фоновых
            // загрузок — продолжается после закрытия окна и видна в индикаторе.
            var chainId = $"chain:{_row.Name}";
            var backgroundEntry = Services.BackgroundDownloadManager.Default.Start(chainId, _row.Name);

            try
            {
                // issue #352.3 (0.3.12.2): повтор «Попробовать ещё раз?» — ЦИКЛ, а не
                // рекурсия: рекурсивный вызов выходил по guard-у CanDownloadChain, т.к.
                // IsChainDownloading сбрасывался только в finally внешнего вызова (после
                // возврата рекурсии) — диалог закрывался «в никуда». Гварды и статус
                // «идёт загрузка» — вне цикла; планировщик каждой попытки сам пропускает
                // скачанные файлы и показывает корректный «Скачивается X из Y».
                var retry = true;
                while (retry)
                {
                    retry = false;
                    var (failed, total) = await DownloadChainAttemptAsync(
                        folder, variant, chainId, backgroundEntry.Cancellation.Token);
                    if (failed > 0)
                    {
                        var message = string.Format(T("Updates.Chain.LoadedFailedDetailed"), failed, total);
                        if (ChainRetryWindow.Ask(this, message))
                            retry = true; // повтор: недостающие файлы докачиваются.
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Отмена из индикатора главного окна (issue #334 п.1): состояние Cancelled
                // уже выставлено менеджером; здесь только журнал.
                _logger.Info($"Загрузка цепочки обновлений «{_row.Name}» отменена пользователем.");
            }
            catch (Exception ex)
            {
                Services.BackgroundDownloadManager.Default.Fail(chainId);
                _logger.Error($"Ошибка загрузки цепочки обновлений «{_row.Name}»", ex);
                _dialogs.ShowError(T("Updates.NetworkError"), T("Updates.CheckTitle"));
            }
            finally
            {
                if (backgroundEntry.IsActive)
                    Services.BackgroundDownloadManager.Default.Complete(chainId);
                _row.IsChainDownloading = false;
                UpdateChainProgressDisplay();
            }
        }

        /// <summary>
        /// Одна попытка скачивания цепочки (0.3.12.2, issue #352.3): расчёт целевых путей,
        /// пропуск уже скачанных файлов (<see cref="UpdateChainDownloadPlanner"/>), цикл
        /// скачивания с прогрессом и итоговые сообщения. Возвращает (число нескачанных,
        /// всего требовалось) — повтор решает <see cref="DownloadChainAsync"/>.
        /// </summary>
        private async Task<(int Failed, int Total)> DownloadChainAttemptAsync(
            string folder,
            UpdateChainVariantViewModel variant,
            string chainId,
            CancellationToken downloadToken)
        {
            // issue #352 (комментарий 7OH от 2026-10-09): докачка цепочки — файлы,
            // уже скачанные ранее (существуют и непусты), пропускаются ещё на старте;
            // счётчик «Скачивается X из Y» считается только по требующим скачивания.
            // issue #352 (комментарий 7OH от 2026-10-11): файлы цепочки сохраняются под
            // ОРИГИНАЛЬНЫМИ именами дистрибутивов (Trade_11_5_27_93_updsetup.zip), как при
            // одиночном скачивании; имя шага уточняется со страницы файлов версии
            // («Дистрибутив обновления»), сбой — fallback «Имя_версия.zip».
            var originalFileNames = new List<string?>();
            foreach (var step in variant.Steps)
                originalFileNames.Add(await TryResolveChainFileNameAsync(step, downloadToken, ResolveAccountId()));
            var targetPaths = UpdateChainDownloadPlanner.BuildChainTargetPaths(
                folder, _row.Name,
                variant.Steps.Select(s => s.Version).ToList(),
                originalFileNames,
                SanitizeFileName);
            var pending = UpdateChainDownloadPlanner.SelectPendingSteps(targetPaths);
            var skipped = variant.Steps.Count - pending.Count;
            if (skipped > 0)
                _logger.Info($"Пропущено уже скачанных файлов цепочки «{_row.Name}»: {skipped} из {variant.Steps.Count}.");

            // issue #352: заголовок фоновой загрузки — имя первого файла цепочки (не название
            // конфигурации «УТ 11»); далее в цикле обновляется на имя текущего файла.
            if (pending.Count > 0)
                UpdateChainTitle(chainId, targetPaths[pending[0]]);

            if (pending.Count == 0)
            {
                // Цепочка уже полностью скачана — фоновая запись сразу завершается.
                Services.BackgroundDownloadManager.Default.Complete(chainId);
                _dialogs.ShowInfo(string.Format(
                        T("Updates.Chain.AllDownloaded"), variant.Steps.Count, folder),
                    T("Updates.CheckTitle"));
                return (0, 0);
            }

            var total = pending.Count;
            var ok = 0;
            var failed = 0;

            // issue #360: единый оценщик на всю попытку — ETA агрегированного прогресса цепочки.
            var chainEta = new Services.DownloadEtaTracker();

            // issue #22 (1.0.0.26, Этап C): перед скачиванием каждого файла цепочки проверяем,
            // не качается ли он уже в фоне (одиночная загрузка того же файла, платформа и т.п.).
            var chainGuard = new Services.DownloadConflictGuard(Services.BackgroundDownloadManager.Default);

            for (var n = 0; n < total; n++)
            {
                var i = pending[n];
                var step = variant.Steps[i];
                var url = OneCUpdatesService.ToAbsoluteVersionFilesUrl(step.VersionFilesUrl, _row.Url);
                var targetPath = targetPaths[i];

                // issue #22: при конфликте «Отменить фоновую загрузку и начать заново» отменяем
                // фоновую запись и продолжаем ручную загрузку; «Подождать»/«Отмена» — ручная
                // загрузка цепочки прерывается (файл качается в фоне, пересечение недопустимо).
                var chainDecision = chainGuard.Resolve(targetPath, AskDownloadConflict);
                if (chainDecision == Services.DownloadConflictDecision.CancelBackgroundAndRestart)
                {
                    var active = chainGuard.FindActiveDownload(targetPath);
                    if (active is not null)
                        Services.BackgroundDownloadManager.Default.Cancel(active.Id);
                }
                else if (chainDecision != Services.DownloadConflictDecision.Proceed)
                {
                    _logger.Info($"Загрузка цепочки «{_row.Name}» прервана: файл «{Path.GetFileName(targetPath)}» уже скачивается в фоне.");
                    return (total - ok, total);
                }

                // issue #352: в индикаторе главного окна показываем имя скачиваемого файла.
                UpdateChainTitle(chainId, targetPath);

                var baseText = string.Format(
                    T("Updates.Chain.DownloadProgress"), n + 1, total, step.Version);
                _row.ChainProgressText = baseText;
                _row.ChainProgress = (double)n / total;

                var progress = new Progress<double>(p => Dispatcher.UIThread.Post(() =>
                {
                    var aggregate = Math.Clamp((n + p) / total, 0, 1);
                    chainEta.Update(aggregate);
                    _row.ChainProgress = aggregate;
                    Services.BackgroundDownloadManager.Default.ReportProgress(chainId, aggregate);
                    _row.ChainProgressText = Services.DownloadEta.AppendSuffix(
                        baseText, chainEta.EstimatedRemaining);
                }));
                var saved = await Task.Run(() =>
                    _updates.DownloadUpdateAsync(url, targetPath, progress, downloadToken, ResolveAccountId()));

                if (!string.IsNullOrWhiteSpace(saved))
                {
                    ok++;
                    _logger.Info($"Скачана версия {step.Version} цепочки: {saved}");
                }
                else
                {
                    failed++;
                    _logger.Warn($"Не удалось скачать версию {step.Version} цепочки ({url}).");
                }

                _row.ChainProgress = (double)(n + 1) / total;
                _row.ChainProgressText = string.Format(
                        T("Updates.Chain.DownloadProgress"), n + 1, total, step.Version)
                    + " " + string.Format(T("Updates.Chain.Remaining"), total - n - 1);
            }

            if (failed == 0 && skipped > 0)
            {
                _dialogs.ShowInfo(string.Format(
                    T("Updates.Chain.LoadedOkSkipped"), ok, total, skipped, folder),
                    T("Updates.CheckTitle"));
            }
            else if (failed == 0)
            {
                _dialogs.ShowInfo(string.Format(T("Updates.Chain.LoadedOk"), ok, total, folder),
                    T("Updates.CheckTitle"));
            }

            return (failed, total);
        }

        /// <summary>
        /// Оригинальное имя дистрибутива шага цепочки (issue #352): имя файла «Дистрибутива
        /// обновления» со страницы файлов версии (Trade_11_5_27_93_updsetup.zip), чтобы файлы
        /// цепочки сохранялись под оригинальными именами, как одиночное скачивание. Сбой
        /// сети/пустой список — null (fallback «Имя_версия.zip»).
        /// </summary>
        private async Task<string?> TryResolveChainFileNameAsync(Models.PlatformRelease step, CancellationToken ct, string? accountId = null)
        {
            try
            {
                var url = OneCUpdatesService.ToAbsoluteVersionFilesUrl(step.VersionFilesUrl, _row.Url);
                var choices = await _updates.GetReleaseFileChoicesAsync(url, ct, accountId: accountId);
                var choice = choices.FirstOrDefault(c =>
                                 c.CaptionKey == UpdateFileChoice.UpdateDistributionCaptionKey)
                             ?? choices.FirstOrDefault();
                return choice?.FileName;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Колонка сводного ряда «Текущая / Последняя / Статус» (issue #352).
        /// <paramref name="extra"/> — необязательная третья строка (предупреждение).</summary>
        private static Grid MakeSummaryColumn(string labelKey, TextBlock value, int column, TextBlock? extra = null)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, column < 2 ? 14 : 0, 0) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            if (extra is not null)
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var label = new TextBlock
            {
                Text = labelKey,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 4),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Themes.ThemeBrushes.Bind(label, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetRow(label, 0);
            grid.Children.Add(label);

            value.VerticalAlignment = VerticalAlignment.Center;
            value.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetRow(value, 1);
            grid.Children.Add(value);

            if (extra is not null)
            {
                extra.FontSize = 12;
                extra.FontWeight = FontWeight.SemiBold;
                extra.TextWrapping = TextWrapping.Wrap;
                // Ошибка — на семантической кисти темы (аудит UI, п. 2.9).
                ThemeBrushes.Bind(extra, Avalonia.Controls.TextBlock.ForegroundProperty, "ErrorBrush");
                Grid.SetRow(extra, 2);
                grid.Children.Add(extra);
            }

            return grid;
        }

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var title = new TextBlock
            {
                Text = T("Updates.CheckTitle"),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            // Карточка сведений о базе и результате проверки.
            var card = new Border
            {
                Margin = new Thickness(0, 12, 0, 0),
                Padding = new Thickness(14, 12),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };
            Themes.ThemeBrushes.Bind(card, Border.BackgroundProperty, "CardBackgroundColorBrush");
            Themes.ThemeBrushes.Bind(card, Border.BorderBrushProperty, "BorderColorBrush");

            var fields = new StackPanel { Spacing = 8 };

            // Сводка в один ряд: Текущая версия | Последняя версия | Статус (issue #352).
            var summary = new Grid();
            summary.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(0.8, GridUnitType.Star)));
            summary.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(0.8, GridUnitType.Star)));
            summary.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1.6, GridUnitType.Star)));
            _currentVersionText.FontSize = 14;
            _currentVersionText.FontWeight = FontWeight.SemiBold;
            _latestVersionText.FontSize = 14;
            _latestVersionText.FontWeight = FontWeight.SemiBold;
            _statusText.FontSize = 14;
            _statusText.FontWeight = FontWeight.SemiBold;
            // issue #371: статус переносится по словам — длинные сообщения («каталог доступен,
            // но версия не распарсена») не обрезаются и не раздвигают колонки версий.
            _statusText.TextWrapping = TextWrapping.Wrap;
            // issue #352: предупреждение «Версии нет на сайте» — третьей строкой в колонке
            // «Текущая версия»; видимость переключается в RefreshChainDisplay.
            _currentVersionMissingText.Text = T("Updates.Chain.VersionMissing");
            summary.Children.Add(MakeSummaryColumn(T("Updates.CurrentVersion"), _currentVersionText, 0,
                _currentVersionMissingText));
            summary.Children.Add(MakeSummaryColumn(T("Updates.LatestVersion"), _latestVersionText, 1));
            summary.Children.Add(MakeSummaryColumn(T("Updates.Status"), _statusText, 2));
            fields.Children.Add(summary);

            // issue #352.4: галочка «Не повышать» под «Последней версией» — ограничение
            // обновлений той же линией версии (3.1.2.345 → можно 3.1.3.456, нельзя 3.2.3.456).
            _noBumpCheckBox.Content = T("Updates.NoBump");
            ToolTip.SetTip(_noBumpCheckBox, T("Updates.NoBump.Hint"));
            _noBumpCheckBox.Click += (_, _) => OnNoVersionBumpChanged();
            var noBumpRow = new StackPanel { Orientation = Orientation.Horizontal };
            noBumpRow.Children.Add(_noBumpCheckBox);
            fields.Children.Add(noBumpRow);

            _baseNameText.FontWeight = FontWeight.SemiBold;
            _baseNameText.FontSize = 14;
            fields.Children.Add(MakeFieldRow(T("Updates.Name"), _baseNameText));

            fields.Children.Add(MakeFieldRow(T("Updates.Url"), _urlText));

            _errorText.TextWrapping = TextWrapping.Wrap;
            _errorText.Foreground = TryGetBrush("ErrorBrush") ?? new SolidColorBrush(Color.Parse("#DC2626"));
            fields.Children.Add(MakeFieldRow(string.Empty, _errorText));

            // Панель действий при ошибке авторизации (issue #323/#330/#334): имя учётной
            // записи ИТС, открыть login.1c.ru в браузере, справочник учётных данных ИТС.
            _authAccountText.FontSize = 12;
            _authAccountText.TextWrapping = TextWrapping.Wrap;
            Themes.ThemeBrushes.Bind(_authAccountText, TextBlock.ForegroundProperty, "TextSecondaryBrush");

            var openLoginButton = new Button { Content = T("Updates.OpenLoginPage"), MinWidth = 150, Height = 32 };
            openLoginButton.Styled(ControlThemes.SecondaryButton);
            openLoginButton.Click += (_, _) => OnOpenLoginClick();

            var itsAccountsButton = new Button { Content = T("Updates.OpenItsAccounts"), MinWidth = 150, Height = 32 };
            itsAccountsButton.Styled(ControlThemes.SecondaryButton);
            itsAccountsButton.Click += (_, _) => OnItsAccountsClick();

            var authButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 6, 0, 0)
            };
            authButtons.Children.Add(openLoginButton);
            authButtons.Children.Add(itsAccountsButton);
            _authActionsPanel.Children.Add(_authAccountText);
            _authActionsPanel.Children.Add(authButtons);
            fields.Children.Add(_authActionsPanel);

            // Пояснение пользователю: откуда берётся адрес каталога и где задать логин/пароль (issue #323).
            var helpText = new TextBlock
            {
                Text = T("Updates.HelpText"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            Themes.ThemeBrushes.Bind(helpText, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            fields.Children.Add(helpText);

            // Индикатор прогресса загрузки.
            _progressBar.Height = 6;
            _progressText.FontSize = 12;
            Themes.ThemeBrushes.Bind(_progressText, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            var progressStack = new StackPanel { Spacing = 4 };
            progressStack.Children.Add(_progressBar);
            progressStack.Children.Add(_progressText);
            _progressPanel.Children.Add(progressStack);
            fields.Children.Add(_progressPanel);

            // Блок цепочки обновлений (issue #352): видим при наличии вариантов.
            var chainTitle = new TextBlock
            {
                Text = T("Updates.Chain.Title"),
                FontSize = 13,
                FontWeight = FontWeight.SemiBold
            };
            _chainSection.Children.Add(chainTitle);

            _chainHintText.Text = T("Updates.Chain.Hint");
            _chainHintText.FontSize = 12;
            _chainHintText.TextWrapping = TextWrapping.Wrap;
            Themes.ThemeBrushes.Bind(_chainHintText, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            _chainSection.Children.Add(_chainHintText);

            _chainStatusValue.FontSize = 12;
            _chainStatusValue.TextWrapping = TextWrapping.Wrap;
            Themes.ThemeBrushes.Bind(_chainStatusValue, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            _chainSection.Children.Add(_chainStatusValue);

            // Строка «Папка: <путь>» + кнопки «Выбрать…» и «Открыть» (issue #352.2).
            var folderLabel = new TextBlock
            {
                Text = T("Updates.Chain.Folder"),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            Themes.ThemeBrushes.Bind(folderLabel, TextBlock.ForegroundProperty, "TextSecondaryBrush");

            _chainFolderValue.FontSize = 12;
            _chainFolderValue.VerticalAlignment = VerticalAlignment.Center;
            _chainFolderValue.TextTrimming = TextTrimming.CharacterEllipsis;
            Themes.ThemeBrushes.Bind(_chainFolderValue, TextBlock.ForegroundProperty, "TextSecondaryBrush");

            // issue #352 (7OH от 2026-10-11): фиксированная высота 28 обрезала текст
            // кнопок снизу — даём кнопкам расти под контент (MinHeight), контент по центру.
            _chainChooseFolderButton.Content = T("Updates.Chain.ChooseFolderButton");
            _chainChooseFolderButton.MinWidth = 96;
            _chainChooseFolderButton.MinHeight = 30;
            _chainChooseFolderButton.VerticalContentAlignment = VerticalAlignment.Center;
            _chainChooseFolderButton.Margin = new Thickness(8, 0, 0, 0);
            _chainChooseFolderButton.Styled(ControlThemes.SecondaryButton);
            _chainChooseFolderButton.Click += (_, _) => OnChooseChainFolderClick();

            _chainOpenFolderButton.Content = T("Updates.Chain.OpenFolder");
            _chainOpenFolderButton.MinWidth = 96;
            _chainOpenFolderButton.MinHeight = 30;
            _chainOpenFolderButton.VerticalContentAlignment = VerticalAlignment.Center;
            _chainOpenFolderButton.Margin = new Thickness(8, 0, 0, 0);
            _chainOpenFolderButton.Styled(ControlThemes.SecondaryButton);
            _chainOpenFolderButton.Click += (_, _) => OnOpenChainFolderClick();

            // Кнопки «Выбрать…» и «Открыть» в одной панели (issue #352.2): выбор папки
            // сохраняет её в настройках без диалога при скачивании цепочки.
            var folderButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };
            folderButtons.Children.Add(_chainChooseFolderButton);
            folderButtons.Children.Add(_chainOpenFolderButton);

            var folderGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            folderGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            folderGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            folderGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Grid.SetColumn(folderLabel, 0);
            Grid.SetColumn(_chainFolderValue, 1);
            Grid.SetColumn(folderButtons, 2);
            folderGrid.Children.Add(folderLabel);
            folderGrid.Children.Add(_chainFolderValue);
            folderGrid.Children.Add(folderButtons);
            _chainFolderPanel.Children.Add(folderGrid);
            _chainSection.Children.Add(_chainFolderPanel);

            _chainSection.Children.Add(_chainRowsPanel);

            _chainProgressBar.Height = 6;
            _chainProgressText.FontSize = 12;
            _chainProgressText.TextWrapping = TextWrapping.Wrap;
            Themes.ThemeBrushes.Bind(_chainProgressText, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            var chainProgressStack = new StackPanel { Spacing = 4 };
            chainProgressStack.Children.Add(_chainProgressBar);
            chainProgressStack.Children.Add(_chainProgressText);
            _chainProgressPanel.Children.Add(chainProgressStack);
            _chainSection.Children.Add(_chainProgressPanel);

            fields.Children.Add(_chainSection);

            card.Child = new ScrollViewer
            {
                Content = fields,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            Grid.SetRow(card, 1);
            grid.Children.Add(card);

            // Нижняя панель: «Список конфигураций», «Проверить», «Скачать», закрыть.
            var bottom = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            bottom.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            // issue #371: кнопка «Список конфигураций» — открыть список типовых
            // конфигураций, чтобы проверить ссылки каталогов релизов сразу.
            var configListButton = new Button
            {
                Content = T("Updates.OpenConfigList"),
                MinWidth = 160,
                Height = 40,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            configListButton.Styled(ControlThemes.SecondaryButton);
            configListButton.Click += (_, _) => OnOpenConfigListClick();
            Grid.SetColumn(configListButton, 0);
            bottom.Children.Add(configListButton);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10
            };
            // Ширины кнопок не фиксированы (только минимальные): при любом шрифте и масштабе
            // кнопка растягивается под текст и не обрезается (issue #323).
            _checkButton = new Button { Content = T("Updates.Check"), MinWidth = 160, Height = 40 };
            _checkButton.Styled(ControlThemes.SecondaryButton);
            _checkButton.Click += (_, _) => OnCheckClick();

            _downloadChainButton = new Button
            {
                Content = T("Updates.Chain.Download"),
                MinWidth = 170,
                Height = 40,
                IsVisible = false,
                IsEnabled = false,
            };
            _downloadChainButton.Styled(ControlThemes.DialogConfirmButton);
            _downloadChainButton.Click += (_, _) => OnDownloadChainClick();

            _downloadButton = new Button { Content = T("Updates.Download"), MinWidth = 150, Height = 40, IsEnabled = false };
            _downloadButton.Styled(ControlThemes.DialogConfirmButton);
            _downloadButton.Click += (_, _) => OnDownloadRow(_row);

            var close = BuildCancelActionButton(150, 40);
            close.Click += (_, _) => Close();

            buttons.Children.Add(_checkButton);
            buttons.Children.Add(_downloadChainButton);
            buttons.Children.Add(_downloadButton);
            buttons.Children.Add(close);
            Grid.SetColumn(buttons, 1);
            bottom.Children.Add(buttons);

            Grid.SetRow(bottom, 2);
            grid.Children.Add(bottom);

            return grid;
        }

        /// <summary>Строка «подпись / значение» карточки.</summary>
        private static Grid MakeFieldRow(string labelKey, TextBlock value)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            var label = new TextBlock
            {
                Text = labelKey,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Themes.ThemeBrushes.Bind(label, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            value.VerticalAlignment = VerticalAlignment.Center;
            value.TextTrimming = TextTrimming.CharacterEllipsis;
            value.TextWrapping = TextWrapping.Wrap;
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);
            return grid;
        }

        /// <summary>Диалог «файл уже скачивается» (issue #22, Этап C): три варианта —
        /// «Отменить фоновую загрузку и начать заново» / «Подождать» / «Отмена».</summary>
        private Services.DownloadConflictDecision AskDownloadConflict(string title, string message)
            => DownloadConflictWindow.Ask(this, message);
    }
}
#endif