#if WINDOWS
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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

    public UpdateAvailableWindow(ReleaseInfo release, UpdateService service)
    {
        InitializeComponent();
        _release = release;
        _service = service;

        Title = LocalizationManager.T("Update.NewVersionAvailable");
        HeadingText.Text = LocalizationManager.T("Update.NewVersionAvailable");
        CurrentVersionText.Text = string.Format(
            LocalizationManager.T("Update.CurrentVersion"), VersionInfo.Display());
        NewVersionText.Text = string.Format(
            LocalizationManager.T("Update.NewVersion"), NormalizeTag(release.TagName));
        WhatsNewLabel.Text = LocalizationManager.T("Update.WhatsNew");
        // Текст «Что нового» рендерим как markdown со стилями (заголовки, жирный,
        // списки и т.п.). При отсутствии описания показываем обычный текст-заглушку.
        var body = string.IsNullOrWhiteSpace(release.Body)
            ? LocalizationManager.T("Update.NoDescription")
            : release.Body;
        BodyViewer.Document = MarkdownRenderer.ToFlowDocument(body);

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

        Loaded += (_, _) => DownloadButton.Focus();

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

    /// <summary>
    /// Навешиваем перехватчик Win32-сообщений сразу после инициализации окна,
    /// чтобы жёстко зафиксировать его размер независимо от кастомного Window-Chrome
    /// (MaterialDesignThemes обходит ResizeMode="NoResize").
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
            HwndSource.FromHwnd(handle)?.AddHook(WndProc);
    }

    /// <summary>Обрезает ведущий символ «v» у тега версии для отображения.</summary>
    private static string NormalizeTag(string tag) =>
        !string.IsNullOrEmpty(tag) && (tag[0] == 'v' || tag[0] == 'V') ? tag.Substring(1) : tag;

    /// <summary>
    /// «Скачать»: скрываем предложение, показываем прогресс и запускаем загрузку.
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
    /// Принудительно пересчитывает размер окна по содержимому текущего этапа
    /// (issue #157). WPF не всегда пересобирает окно с <c>SizeToContent="Height"</c>
    /// после изменения <c>Visibility</c> блоков (предложение → скачивание → применение →
    /// ошибка), поэтому сбрасываем авто-подбор в Manual, заново меряем содержимое и
    /// возвращаем обратно. Так окно каждый раз подстраивается именно под текущий этап:
    /// высота не «застревает» от длинного описания изменений или от предыдущего диалога,
    /// а прогресс-бар и кнопки не обрезаются и не остаётся пустоты снизу.
    /// </summary>
    private void RefreshWindowSize()
    {
        try
        {
            if (!IsLoaded)
                return;

            // Сброс в Manual (авто-подбор выключен) и принудительный перерасчёт заставляют
            // окно заново измерить содержимое текущего этапа, после чего возвращаем
            // авто-подбор высоты.
            SizeToContent = SizeToContent.Manual;
            InvalidateMeasure();
            UpdateLayout();

            SizeToContent = SizeToContent.Height;
            InvalidateMeasure();
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
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Окно закрыто без применения обновления («Отмена», крестик, закрытие на этапе
    /// перезапуска): скачанный временный файл удаляется. Раньше он оставался в %TEMP%,
    /// занимал десятки МБ и участвовал в «мгновенных» докачках со смешанными байтами
    /// разных версий (issue #302).
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (!_applyScheduled && _newExe is not null)
        {
            try { File.Delete(_newExe); } catch { /* не критично */ }
            _newExe = null;
        }
    }

    private const int WmGetMinMaxInfo = 0x0024;
    private const int WmSizing = 0x0214;

    // Ориентиры сторон для WM_SIZING.
    private const int WmszLeft = 1;
    private const int WmszRight = 2;
    private const int WmszTop = 3;
    private const int WmszTopLeft = 4;
    private const int WmszBottomLeft = 5;
    private const int WmszBottom = 6;
    private const int WmszTopRight = 7;
    private const int WmszBottomRight = 8;

    [StructLayout(LayoutKind.Sequential)]
    private struct PoINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public PoINT Reserved;
        public PoINT MaxSize;
        public PoINT MaxPosition;
        public PoINT MinTrackSize;
        public PoINT MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// Возвращает фиксированную ширину окна в DIP (device-independent units), которую
    /// нельзя изменить мышью. До первой реальной отрисовки берём значение из свойства
    /// <see cref="Window.Width"/>, иначе — фактическую ширину окна.
    /// </summary>
    private double FixedWidthDips
    {
        get
        {
            var w = ActualWidth;
            return w > 0 ? w : Width;
        }
    }

    /// <summary>
    /// Возвращает фиксированную высоту окна в DIP (device-independent units),
    /// которую нельзя изменить мышью (issue #116 v1.0.1.54: окно подтверждения
    /// перезапуска нельзя растягивать по вертикали — «растягивается странно»).
    /// До первой реальной отрисовки берём значение из <see cref="Window.Height"/>,
    /// иначе — фактическую высоту окна (подстроенную SizeToContent="Height").
    /// </summary>
    private double FixedHeightDips
    {
        get
        {
            var h = ActualHeight;
            return h > 0 ? h : Height;
        }
    }

    /// <summary>
    /// Текущий DPI-масштаб окна по горизонтали (1.0 = 100%). WPF измеряет размеры
    /// (<see cref="Window.Width"/>, <see cref="FrameworkElement.ActualWidth"/>) в DIP,
    /// а Win32-структуры <see cref="MinMaxInfo"/> и <see cref="RECT"/> (WM_GETMINMAXINFO,
    /// WM_SIZING) оперируют физическими пикселями экрана.
    /// </summary>
    private double DpiScaleX
    {
        get
        {
            var source = PresentationSource.FromVisual(this);
            return source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        }
    }

    /// <summary>Текущий DPI-масштаб окна по вертикали (см. <see cref="DpiScaleX"/>).</summary>
    private double DpiScaleY
    {
        get
        {
            var source = PresentationSource.FromVisual(this);
            return source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
        }
    }

    /// <summary>
    /// Фиксированная ширина окна в физических пикселях — то значение, которое надо
    /// подставлять в Win32-структуры MINMAXINFO/RECT. Без перевода DIP→px при масштабе
    /// Windows 125%/150%/200% ширина принудительно сжималась до Width/Dpi (например
    /// 680/1.5 ≈ 453 DIP): окно становилось узким как у диалога сообщения (~440),
    /// и кнопки/прогресс-бар выходили за его правый край (issue #157).
    /// </summary>
    private int FixedWidthPx => (int)Math.Round(FixedWidthDips * DpiScaleX);

    /// <summary>Фиксированная высота окна в физических пикселях (см. <see cref="FixedWidthPx"/>,
    /// issue #116 v1.0.1.54 — запрет вертикального растягивания).</summary>
    private int FixedHeightPx => (int)Math.Round(FixedHeightDips * DpiScaleY);

    /// <summary>
    /// Перехватывает WM_GETMINMAXINFO и WM_SIZING, жёстко фиксируя ширину И высоту
    /// окна. Фиксация ширины блокирует «схлопывание» окна при перетаскивании
    /// правой/левой границы, которое обходит ResizeMode="NoResize" из-за кастомного
    /// Window-Chrome MaterialDesign (issue #162). Фиксация высоты запрещает
    /// вертикальное растягивание окна подтверждения перезапуска (issue #116
    /// v1.0.1.54: «окна обновления растягиваются странно»); высоту контента
    /// по-прежнему подстраивает SizeToContent="Height" — программные изменения
    /// размера под WmGetMinMaxInfo/WmSizing не подпадают. В Win32-структуры
    /// подставляются физические пиксели (с учётом DPI), иначе при масштабе
    /// экрана >100% окно принудительно сжималось и обрезало контент.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WmGetMinMaxInfo:
            {
                var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);

                // Ширина и высота фиксированы: MinTrackSize == MaxTrackSize ==
                // фактическим габаритам, поэтому изменение размера мышью невозможно.
                // Значения в физических пикселях.
                int w = FixedWidthPx;
                int h = FixedHeightPx;
                mmi.MinTrackSize.X = w;
                mmi.MaxTrackSize.X = w;
                mmi.MinTrackSize.Y = h;
                mmi.MaxTrackSize.Y = h;
                Marshal.StructureToPtr(mmi, lParam, true);
                handled = true;
                break;
            }
            case WmSizing:
            {
                // Дополнительная защита на случай, если кастомный Chrome инициирует изменение
                // размера в обход MINMAXINFO: принудительно возвращаем фиксированные ширину
                // и высоту (в физических пикселях, чтобы не сузить окно при масштабе >100%).
                var rc = Marshal.PtrToStructure<RECT>(lParam);
                int w = FixedWidthPx;
                int h = FixedHeightPx;
                switch (wParam.ToInt32())
                {
                    case WmszLeft:
                    case WmszTopLeft:
                    case WmszBottomLeft:
                        rc.Left = rc.Right - w;
                        break;
                    default:
                        rc.Right = rc.Left + w;
                        break;
                }
                switch (wParam.ToInt32())
                {
                    case WmszTop:
                    case WmszTopLeft:
                    case WmszTopRight:
                        rc.Top = rc.Bottom - h;
                        break;
                    default:
                        rc.Bottom = rc.Top + h;
                        break;
                }
                Marshal.StructureToPtr(rc, lParam, true);
                handled = true;
                break;
            }
        }
        return IntPtr.Zero;
    }
}
#endif