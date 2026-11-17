#if WINDOWS
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Единый модальный диалог обновления (Windows/WPF). Совмещает в себе весь процесс:
/// предложение скачать новую версию, прогресс скачивания и вопрос, как применить
/// обновление («Перезапустить сейчас» или «Обновить после закрытия»). Системный
/// MessageBox здесь не используется — все этапы показаны в этом же окне.
/// </summary>
public partial class UpdateAvailableWindow : Window
{
    private readonly UpdateService _service;
    private readonly ReleaseInfo _release;

    // Путь к текущему exe и к скачанному файлу; заполняются после успешной загрузки.
    private string? _targetExe;
    private string? _newExe;
    // true, когда обновление уже запланировано (сейчас/после закрытия): скачанный
    // файл ещё понадобится помощнику, удалять его нельзя (issue #302).
    private bool _applyScheduled;

    /// <param name="showReleaseNotes">
    /// Issue #116: показывать ли блок «Что нового» (подпись и описание релиза).
    /// В ручных режимах (<see cref="AppUpdateMode.Manual"/>, <see cref="AppUpdateMode.NotifyOnly"/>)
    /// передаётся <c>false</c> — пользователь сам запросил обновление и уже знает, что
    /// ищет; подпись и описание скрываются целиком (без пустого места), окно ужимается
    /// за счёт <c>SizeToContent="WidthAndHeight"</c>. В автоматических режимах
    /// (<see cref="AppUpdateMode.InstallOnStartup"/>, <see cref="AppUpdateMode.DownloadAskInstall"/>)
    /// блок остаётся.
    /// </param>
    public UpdateAvailableWindow(
        ReleaseInfo release, UpdateService service, bool showReleaseNotes = true)
    {
        InitializeComponent();
        _release = release;
        _service = service;

        // Issue #116 (1.0.1.82): когда файл ещё не скачан, заголовок уточняет,
        // что версия будет именно загружена; для готового файла — прежний текст.
        var readyToInstall = _service.IsReadyFor(release);
        var headingKey = readyToInstall
            ? "Update.NewVersionAvailable"
            : "Update.NewVersionAvailableForDownload";
        Title = LocalizationManager.T(headingKey);
        HeadingText.Text = LocalizationManager.T(headingKey);
        // Issue #129: при включённом «Считать версию прошлой» подпись текущей версии
        // показывает реальную сборку и эффективную (понижённую) версию.
        CurrentVersionText.Text = string.Format(
            LocalizationManager.T("Update.CurrentVersion"),
            EffectiveVersionService.FormatVersionForDisplay(
                VersionInfo.Display(),
                EffectiveVersionService.CurrentForUpdates,
                LocalizationManager.T("Common.VersionEffective")));
        NewVersionText.Text = string.Format(
            LocalizationManager.T("Update.NewVersion"), NormalizeTag(release.TagName));

        // Issue #116 (1.0.1.82): ссылка «Что нового» открывает стандартный диалог
        // изменений новой версии. Нужна в ручных режимах, где блок описания скрыт.
        WhatsNewLinkText.Text = LocalizationManager.T("Update.WhatsNewLink");
        WhatsNewLink.Visibility = showReleaseNotes ? Visibility.Collapsed : Visibility.Visible;

        if (showReleaseNotes)
        {
            WhatsNewLabel.Text = LocalizationManager.T("Update.WhatsNew");
            // Текст «Что нового» рендерим как markdown со стилями (заголовки, жирный,
            // списки и т.п.). При отсутствии описания показываем обычный текст-заглушку.
            var body = string.IsNullOrWhiteSpace(release.Body)
                ? LocalizationManager.T("Update.NoDescription")
                : release.Body;
            BodyViewer.Document = MarkdownRenderer.ToFlowDocument(body);
        }
        else
        {
            // Issue #116: в ручных режимах блок «Что нового» не показываем вовсе —
            // подпись и описание скрыты, пустого места не остаётся (SizeToContent
            // пересчитает высоту окна по фактическому содержимому).
            WhatsNewLabel.Visibility = Visibility.Collapsed;
            BodyViewer.Visibility = Visibility.Collapsed;
        }

        // На старте главного окна ещё нет, и первым MainWindow становится само это
        // окно: присваивание Owner самому себе бросает ArgumentException. Без
        // владельца окно просто открывается по центру экрана.
        var owner = Application.Current?.MainWindow;
        if (owner is not null && !ReferenceEquals(owner, this))
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        // Issue #116 (v1.0.1.58): если файл обновления уже скачан в фоне (режим
        // «Загружать с предложением установки»), кнопка сразу применяет готовый
        // файл по логике автообновления. С 17.11.2026 её подпись тоже «Обновить».
        InstallButton.Visibility = readyToInstall ? Visibility.Visible : Visibility.Collapsed;
        DownloadButton.Visibility = readyToInstall ? Visibility.Collapsed : Visibility.Visible;
        InstallButton.IsDefault = readyToInstall;
        DownloadButton.IsDefault = !readyToInstall;

        // Issue #116 (17.11.2026 14:07): размер окна целиком определяет
        // SizeToContent="WidthAndHeight" в XAML — ручного измерения и фиксации
        // габаритов нет, поэтому окно не «прыгает» после первого показа.
        Loaded += (_, _) => (readyToInstall ? InstallButton : DownloadButton).Focus();

        // Закрытие окна по Esc (issue #371): единообразно с остальными окнами приложения
        // (ActualReleasesWindow и др.). Во время скачивания окно не закрывается — как и
        // кнопка «Отмена», которая на этом этапе отключена.
        // handledEventsToo=true: перехватываем Esc, даже если дочерний контрол (например
        // RichTextBox «Что нового») пометил событие обработанным — иначе окно не закрывалось
        // (фикс 0.3.12.49 не сработал именно из-за фокуса в контроле).
        AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnWindow_PreviewKeyDown), true);
    }

    /// <summary>Закрывает окно по Esc без модификаторов (issue #371). Во время скачивания
    /// окно не закрывается: случайный Esc не должен прерывать загрузку и оставлять фоновую
    /// задачу без окна (кнопка «Отмена» на этом этапе тоже отключена).</summary>
    private void OnWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape
            || Keyboard.Modifiers != ModifierKeys.None)
            return;
        if (ProgressPanel.Visibility == Visibility.Visible)
            return;
        e.Handled = true;
        Close();
    }

    /// <summary>Обрезает ведущий символ «v» у тега версии для отображения.</summary>
    private static string NormalizeTag(string tag) =>
        !string.IsNullOrEmpty(tag) && (tag[0] == 'v' || tag[0] == 'V') ? tag.Substring(1) : tag;

    /// <summary>
    /// «Обновить»: скрываем предложение, показываем прогресс и запускаем загрузку.
    /// По завершении загрузки остаёмся в этом же окне и спрашиваем, как применить обновление.
    /// </summary>
    private async void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        DownloadButton.IsEnabled = false;
        SkipVersionButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        OfferPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        ProgressBar.Value = 0;
        ProgressBar.IsIndeterminate = false;
        ProgressText.Text = LocalizationManager.T("Update.Downloading");
        // Размер окна должен перестроиться под компактный этап скачивания (issue #157):
        // не наследуем габариты этапа предложения с длинным описанием изменений.
        RefreshWindowSize();

        // Подписка на прогресс скачивания (событие приходит из фонового потока).
        _service.DownloadProgressChanged += OnDownloadProgress;
        try
        {
            if (string.IsNullOrWhiteSpace(_release.DownloadUrl))
            {
                ShowError(LocalizationManager.T("Update.NoDownloadUrl"));
                return;
            }

            _targetExe = _service.ResolveTargetExe();
            if (_targetExe is null)
            {
                ShowError(LocalizationManager.T("Update.InstallFailed"));
                return;
            }

            bool joinedExisting = false;
            try
            {
                (_newExe, joinedExisting) = await _service.DownloadNewExeCoreAsync(_release);
            }
            catch (Exception ex)
            {
                LogDiagnostic("Ошибка при скачивании обновления: " + ex);
                _newExe = null;
            }

            // issue #22 (1.0.0.28): загрузка того же файла уже идёт (фоновая проверка,
            // задание UpdateApp) — вторую загрузку не начинали; обновление применит
            // первый запуск, повторное применение не требуется.
            if (joinedExisting)
            {
                ShowError(LocalizationManager.T("Update.AlreadyDownloading"));
                return;
            }

            if (_newExe is null)
            {
                ShowError(LocalizationManager.T("Update.DownloadFailed"));
                return;
            }

            // Скачивание завершено — переходим к вопросу о том, как применить обновление.
            ProgressPanel.Visibility = Visibility.Collapsed;
            RestartPanel.Visibility = Visibility.Visible;
            RestartHeadingText.Text = LocalizationManager.T("Update.RestartOrLater");
            RestartNowButton.Focus();
            RefreshWindowSize();
        }
        finally
        {
            _service.DownloadProgressChanged -= OnDownloadProgress;
        }
    }

    /// <summary>Обновляет индикатор прогресса внутри окна (вызывается из фонового потока).</summary>
    private void OnDownloadProgress(double percent)
    {
        Dispatcher.Invoke(() =>
        {
            if (percent < 0)
            {
                // Длина файла заранее неизвестна — индикатор переводим в неопределённый режим.
                ProgressBar.IsIndeterminate = true;
                ProgressText.Text = LocalizationManager.T("Update.Downloading");
                return;
            }

            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = percent;
            ProgressText.Text = string.Format(
                LocalizationManager.T("Update.DownloadProgressFormat"), (int)percent);
        });
    }

    /// <summary>
    /// Клик по ссылке «Что нового» (issue #116, 1.0.1.82): открывает стандартный
    /// диалог изменений новой версии с телом её GitHub-релиза.
    /// </summary>
    private void OnWhatsNewLinkClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = AppServices.GetRequiredService<IInfobaseRepository>().LoadSettings();
            var dialog = new WhatsNewWindow(
                settings,
                NormalizeTag(_release.TagName),
                _release.Body)
            {
                Owner = this
            };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            LogDiagnostic("Не удалось открыть «Что нового»: " + ex.Message);
        }
    }

    /// <summary>«Перезапустить сейчас»: применяем обновление с перезапуском и закрываем приложение.</summary>
    private void OnRestartNowClick(object sender, RoutedEventArgs e)
    {
        if (_targetExe is null || _newExe is null)
        {
            Close();
            return;
        }

        RestartNowButton.IsEnabled = false;
        UpdateAfterCloseButton.IsEnabled = false;

        if (!_service.ApplyRestartNow(_targetExe, _newExe))
        {
            ShowError(LocalizationManager.T("Update.InstallFailed"));
            return;
        }

        // Обновление запланировано: помощник заменит файл сам, temp не удалять.
        _applyScheduled = true;

        // Помощник уже запущен. Показываем подтверждение и через короткую паузу закрываем
        // приложение, чтобы exe освободился и был заменён, а новое приложение запустилось.
        ShowDonePhase(LocalizationManager.T("Update.RestartPrompt"));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _service.ShutdownNow();
        };
        timer.Start();
    }

    /// <summary>
    /// «Установить» (issue #116, v1.0.1.58): применяет уже скачанный в фоне файл
    /// обновления по существующей логике автообновления (помощник замены exe,
    /// маркер pending_update.json), затем закрывает приложение для перезапуска.
    /// </summary>
    private void OnInstallClick(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        SkipVersionButton.IsEnabled = false;
        CancelButton.IsEnabled = false;

        if (!_service.ApplyReadyInstall(_release))
        {
            ShowError(LocalizationManager.T("Update.InstallFailed"));
            return;
        }

        // Обновление запланировано: помощник заменит файл сам, temp не удалять.
        _applyScheduled = true;

        // Помощник уже запущен. Показываем подтверждение и через короткую паузу закрываем
        // приложение, чтобы exe освободился и был заменён, а новое приложение запустилось.
        ShowDonePhase(LocalizationManager.T("Update.RestartPrompt"));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _service.ShutdownNow();
        };
        timer.Start();
    }

    /// <summary>«Обновить после закрытия»: применяем обновление без перезапуска.</summary>
    private void OnUpdateAfterCloseClick(object sender, RoutedEventArgs e)
    {
        if (_targetExe is null || _newExe is null)
        {
            Close();
            return;
        }

        RestartNowButton.IsEnabled = false;
        UpdateAfterCloseButton.IsEnabled = false;

        if (!_service.ApplyAfterClose(_targetExe, _newExe))
        {
            ShowError(LocalizationManager.T("Update.InstallFailed"));
            return;
        }

        // Обновление запланировано: помощник заменит файл после закрытия, temp не удалять.
        _applyScheduled = true;

        // Обновление применится при естественном закрытии программы — показываем
        // подтверждение в этом же окне (без дополнительных диалогов).
        ShowDonePhase(LocalizationManager.T("Update.AppliedLater"));
    }

    /// <summary>Закрывает окно по кнопке финального этапа.</summary>
    private void OnDoneCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>Показывает финальный этап с текстом и кнопкой «Закрыть».</summary>
    private void ShowDonePhase(string text)
    {
        RestartPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Collapsed;
        DonePanel.Visibility = Visibility.Visible;
        DoneText.Text = text;
        DoneCloseButton.Focus();
        RefreshWindowSize();
    }

    /// <summary>
    /// Показывает ошибку обновления прямо в этом окне (вместо закрытия окна и
    /// открытия отдельного диалога ошибки), чтобы во время обновления не появлялось
    /// несколько окон друг за другом.
    /// </summary>
    private void ShowError(string message)
    {
        LogDiagnostic("Ошибка в диалоге обновления: " + message);
        OfferPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Collapsed;
        RestartPanel.Visibility = Visibility.Collapsed;
        DonePanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Visible;
        ErrorText.Text = message;
        ErrorCloseButton.Focus();
        RefreshWindowSize();
    }

    /// <summary>
    /// Пересчитывает компоновку текущего этапа. Размер окна при этом НЕ измеряется
    /// и не фиксируется вручную (issue #116, комментарий 7OH от 17.11.2026 14:07):
    /// его целиком определяет <c>SizeToContent="WidthAndHeight"</c> из XAML. Ручной
    /// сброс в <c>SizeToContent.Manual</c> и обратно давал заметный скачок размера
    /// после первого показа, поэтому оставлен только безопасный пересчёт разметки.
    /// </summary>
    private void RefreshWindowSize()
    {
        try
        {
            if (IsLoaded)
                UpdateLayout();
        }
        catch
        {
            // Сбой пересчёта размера не должен ломать диалог.
        }
    }

    /// <summary>
    /// Записывает диагностическую информацию об ошибках обновления во временный файл
    /// (%TEMP%\Workspace1C\update\update-dialog.log), чтобы причину сбоя можно
    /// было изучить после неудачного скачивания/применения (issue #162).
    /// </summary>
    private static void LogDiagnostic(string message)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "Workspace1C", "update");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "update-dialog.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {message}{Environment.NewLine}",
                System.Text.Encoding.UTF8);
        }
        catch
        {
            // Логирование не должно ломать диалог.
        }
    }

    /// <summary>
    /// «Пропустить эту версию» (issue #116): версия запоминается в настройках и не
    /// будет предлагаться (ни в диалоге, ни ссылкой «Обновить» в подвале), пока не
    /// выйдет более новая версия.
    /// </summary>
    private void OnSkipVersionClick(object sender, RoutedEventArgs e)
    {
        _service.SkipVersion(_release);
        // Issue #129: «Пропустить версию» — единственный сценарий, когда скачанный
        // файл этой версии больше не нужен; убираем его и маркер кэша.
        UpdateDownloadCache.DeleteCachedFile(_service.GetUpdateDownloadTargetPath(_release));
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Окно закрыто без применения обновления («Отмена», крестик, закрытие на этапе
    /// перезапуска). Issue #129: ПОЛНЫЙ скачанный файл НЕ удаляется — при следующем
    /// запуске он переиспользуется (<see cref="UpdateDownloadCache"/>), поэтому
    /// повторной загрузки нет. Удаляется только неполный/битый файл; при «Пропустить
    /// версию» файл убирается явно в <see cref="OnSkipVersionClick"/>.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (!_applyScheduled && _newExe is not null)
        {
            if (!UpdateDownloadCache.IsComplete(_newExe, _release.AssetSize))
                UpdateDownloadCache.DeleteCachedFile(_newExe);
            _newExe = null;
        }
    }

}
#endif