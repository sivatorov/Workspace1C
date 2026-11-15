#if WINDOWS
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Workspace1C.Controls;
using Workspace1C.Services;

namespace Workspace1C
{
    /// <summary>
    /// Общий механизм закрытия всплывающих подсказок (ToolTip) по ESC и при потере фокуса
    /// для WPF-окон приложения (issues #270, #275): первый ESC закрывает открытые тултипы
    /// (в т.ч. строковые подсказки <c>ToolTip="..."</c>) и помечает событие обработанным,
    /// повторный ESC закрывает само окно (в т.ч. через кнопку IsCancel). При потере фокуса
    /// окном (<see cref="Window.Deactivated"/>) и приложением целиком
    /// (<see cref="Application.Deactivated"/>) все открытые подсказки прячутся.
    /// <para>
    /// Владельцы и сами ToolTip-инстансы записываются классами-обработчиками
    /// <see cref="System.Windows.Controls.ToolTip.OpenedEvent"/>/<see cref="System.Windows.Controls.ToolTip.ClosedEvent"/>
    /// независимо от того, где физически отрисован попап (в визуальном дереве окна или во внешнем
    /// HWND/Popup), поэтому закрытие по ESC детерминированно. Основной путь закрытия — по самим
    /// инстансам ToolTip: для строковых подсказок WPF показывает внутренний общий ToolTip, и
    /// присвоение <c>IsOpen = false</c> именно ему гарантированно гасит попап. Трюки с
    /// <c>ToolTipService.GetIsOpen(владелец)</c> и <c>GetToolTip(владелец) is ToolTip</c> для
    /// строковых подсказок НЕ работают: GetToolTip возвращает строку, а сервис выставляет
    /// IsOpenProperty на самом тултипе, а не на владельце — из-за этого в 0.3.9.25 первый ESC
    /// закрывал окно (#270), а потеря фокуса оставляла подсказку висеть (#275).
    /// </para>
    /// <para>
    /// Использование: вызвать <see cref="Register"/> (идемпотентно, из конструктора окна),
    /// а ESC и потерю фокуса обрабатывать так: <c>PreviewKeyDown</c> — если
    /// <see cref="CloseAll"/> вернул true, пометить <c>e.Handled = true</c>;
    /// <c>Deactivated</c> — просто <see cref="CloseAll"/> (issue #275). Дополнительно
    /// <see cref="Register"/> вешает глобальные обработчики: класс-обработчик
    /// <c>UIElement.PreviewKeyDownEvent</c> (handledEventsToo) перехватывает первый ESC в любом
    /// окне и в любом попапе, а <see cref="Application.Deactivated"/> закрывает подсказки при
    /// уходе фокуса с приложения целиком.
    /// </para>
    /// <para>
    /// С 0.3.9.27 добавлена «страховка» уровня Win32: <see cref="ComponentDispatcher.ThreadPreprocessMessage"/>
    /// перехватывает <c>WM_KEYDOWN/VK_ESCAPE</c> до того, как WPF вообще построит маршрут
    /// routed-событий. Если <see cref="CloseAll"/> закрыл хотя бы одну подсказку — сообщение
    /// проглатывается целиком, и клавиша физически не может дойти до кнопки IsCancel или
    /// хоткея сворачивания в трей, какие бы элементы/попапы/внешние HWND ни держали фокус.
    /// Автоповтор удержанного ESC после проглоченного нажатия тоже гасится, чтобы окно не
    /// закрылось «само» на повторе. Повторное нажатие ESC (новое, не автоповтор) проходит
    /// штатно — окно закрывается как обычно. Ввод в <see cref="HotkeyBox"/> не затрагивается:
    /// там ESC отменяет ввод комбинации (см. <see cref="HotkeyBox.OnPreviewKeyDown"/>).
    /// </para>
    /// <para>
    /// С 0.3.9.28 потеря фокуса закрывается надёжно, а не только через WPF-события (issue #275):
    /// (1) класс-обработчик <c>Window.Loaded</c> подписывает <see cref="Window.Deactivated"/> и
    /// <c>HwndSource.AddHook</c> на ВСЕ окна приложения (раньше Deactivated был только у пяти);
    /// (2) WndProc-хук ловит «sent»-сообщения <c>WM_ACTIVATEAPP(FALSE)</c>/<c>WM_ACTIVATE(WA_INACTIVE)</c>/
    /// <c>WM_KILLFOCUS</c> — они не проходят через <see cref="ComponentDispatcher"/>, и при переходе
    /// фокуса к чужому процессу вызывается <see cref="CloseAll"/> (фокус внутри собственного
    /// приложения — меню/диалоги/попапы — не трогается: проверяется PID нового владельца);
    /// (3) fallback-таймер сверяет foreground-окно с PID процесса и закрывает всплывающие элементы,
    /// даже если ни WPF-событие, ни WM-сообщения не дошли (активным числился сам тултип-попап
    /// или скрытое окно NotifyIcon); (4) <see cref="CloseAll"/> при необходимости планирует
    /// повторный проход — внешний попап ToolTip гасится после обработки очереди ввода/layout.
    /// </para>
    /// </summary>
    internal static class ToolTipCloser
    {
        // ---- Закрытие всплывающих подсказок (issues #270, #275) ----
        // Открытые ToolTip-инстансы. Основной путь закрытия: IsOpen=false на самом тултипе
        // работает и для объектовых подсказок (XAML <ToolTip>), и для строковых (ToolTip="..."),
        // которые WPF показывает через внутренний общий ToolTip сервиса.
        private static readonly HashSet<System.Windows.Controls.ToolTip> _openToolTips = new();

        // Владельцы открытых ToolTip (элементы, к которым привязан тултип). Используются для
        // подавления повторного открытия после ESC и в резервных путях закрытия.
        private static readonly HashSet<DependencyObject> _openToolTipOwners = new();

        /// <summary>
        /// Владельцы подсказок, подавленных после принудительного закрытия по ESC (issues #270,
        /// #131), и момент подавления (Environment.TickCount64). Подавление действует только
        /// на текущий цикл закрытия и ограничено окном
        /// <see cref="ToolTipSuppressionPolicy.WindowMs"/>; обычные закрытия (потеря фокуса,
        /// деактивация, прокрутка, fallback-таймер) запись не создают.
        /// </summary>
        private static readonly Dictionary<DependencyObject, long> _suppressedToolTipOwners = new();

        /// <summary>Чистая политика подавления повторного открытия подсказок (issue #131).</summary>
        private static readonly ToolTipSuppressionPolicy _suppressionPolicy = new();

        // Открытые контекстные меню приложения (глобально, для всех окон). Первый ESC должен
        // закрыть и их тоже, иначе окно закроется, а меню останется «висеть» (issue #261/#270).
        private static readonly HashSet<ContextMenu> _openContextMenus = new();

        // Открытые пользовательские Popup-подсказки (HelpLink и т.п., issue #275). Класс-обработчики
        // Popup.Loaded/Unloaded записывают только «пользовательские» попапы (ShouldClosePopup),
        // поэтому штатные дропдауны селекторов/календарей в этот набор не попадают. Используется
        // как основной источник для CloseOpenPopups и для быстрой проверки в fallback-таймере.
        private static readonly HashSet<Popup> _openPopups = new();

        /// <summary>Признак того, что класс-обработчики уже зарегистрированы (Register идемпотентен).</summary>
        private static bool _registered;

        // ---- Потеря фокуса / активации (issue #275) ----
        // WPF Application.Deactivated опирается на WM_ACTIVATEAPP — «sent»-сообщение, которое НЕ
        // проходит через ComponentDispatcher.ThreadPreprocessMessage (тот видит только сообщения из
        // очереди потока) и доходит не во всех сценариях (активным числится тултип-попап, скрытое
        // окно NotifyIcon и т.п.). Поэтому дополнительно вешаем HwndSource-хук (WndProc) на каждое
        // окно: он видит и sent-, и queued-сообщения, а страховкой от пропущенных сообщений служит
        // fallback-таймер, сверяющий foreground-окно с PID процесса.

        private const int WM_ACTIVATE = 0x0006;
        private const int WM_KILLFOCUS = 0x0008;
        private const int WM_ACTIVATEAPP = 0x001C;
        private const int WA_INACTIVE = 0;

        // Окна, для которых уже подписаны Deactivated и HwndSource-хук (Window.Loaded может
        // срабатывать повторно при повторном показе окна — защита от дублирующих подписок).
        private static readonly HashSet<Window> _focusSubscribedWindows = new();
        private static readonly HashSet<HwndSource> _hookedSources = new();

        // Fallback-таймер: если foreground-окно принадлежит другому процессу, а у нас открыты
        // всплывающие элементы — закрываем их (страховка от пропущенных WM-сообщений, issue #275).
        private static DispatcherTimer? _focusCheckTimer;

        // Повторный проход закрытия тултипов (issue #275): внешний попап ToolTip живёт в отдельном
        // окне и может «не успеть» погаснуть в том же кадре, в котором было снято IsOpen.
        private static bool _toolTipRetryScheduled;

        // ---- Win32-перехват ESC (гарантия первого ESC) ----
        private const int WM_KEYDOWN = 0x0100;
        private const int VK_ESCAPE = 0x1B;

        /// <summary>
        /// Интервал (мс), в течение которого автоповтор удержанного ESC после проглоченного
        /// нажатия продолжает глотаться: иначе «залипший» ESC повтором закроет окно.
        /// Новые нажатия (бит предыдущего состояния lParam = 0) всегда проходят штатно.
        /// </summary>
        private const long EscAutoRepeatGuardMs = 1500;

        /// <summary>Момент последнего проглоченного в Win32-перехвате нажатия ESC (Environment.TickCount64).</summary>
        private static long _lastEscSwallowedTick = long.MinValue;

        // ---- Диагностический трейс (CM_TOOLTIP_TRACE=1) ----
        private static readonly bool _traceEnabled =
            string.Equals(Environment.GetEnvironmentVariable("CM_TOOLTIP_TRACE"), "1", StringComparison.OrdinalIgnoreCase);
        private static readonly string _tracePath = Path.Combine(Path.GetTempPath(), "cm_tooltip_trace.log");
        private static readonly object _traceLock = new();

        /// <summary>
        /// Регистрирует класс-обработчики отслеживания открытых подсказок, глобальный перехват
        /// ESC (WPF-маршрут + страховка уровня Win32) и подписку на потерю фокуса приложением
        /// (issues #270, #275). Вызывается из конструктора каждого WPF-окна, участвующего в
        /// механизме; повторные вызовы безопасны (регистрация выполняется один раз).
        /// Обработчики классовые — срабатывают для любой открытой подсказки независимо от того,
        /// лежит ли её владелец в визуальном дереве окна или во внешнем попапе/HWND.
        /// </summary>
        public static void Register()
        {
            if (_registered)
                return;
            _registered = true;

            // Класс-обработчик открытия ToolTip: запоминает сам тултип и его владельца.
            EventManager.RegisterClassHandler(
                typeof(System.Windows.Controls.ToolTip),
                System.Windows.Controls.ToolTip.OpenedEvent,
                new RoutedEventHandler(OnToolTipOpened));

            // Класс-обработчик закрытия ToolTip: убирает тултип и владельца.
            EventManager.RegisterClassHandler(
                typeof(System.Windows.Controls.ToolTip),
                System.Windows.Controls.ToolTip.ClosedEvent,
                new RoutedEventHandler(OnToolTipClosed));

            // Класс-обработчик Preview (туннелирование) на тип ToolTip: ESC, приходящийся на
            // открытую подсказку (фокус во внешнем попапе/HWND), закрывает именно подсказку,
            // а не всё окно.
            EventManager.RegisterClassHandler(
                typeof(System.Windows.Controls.ToolTip),
                UIElement.PreviewKeyDownEvent,
                new KeyEventHandler(OnToolTipPreviewKeyDown));

            // Глобальный перехват ESC на любом UIElement (issue #270): первый ESC гарантированно
            // закрывает открытые подсказки в ЛЮБОМ окне приложения (включая окна без собственного
            // PreviewKeyDown) и помечает событие обработанным ДО срабатывания кнопки IsCancel или
            // хоткея сворачивания в трей. handledEventsToo=true — перехватываем клавишу даже там,
            // где ниже по маршруту её уже начали обрабатывать.
            EventManager.RegisterClassHandler(
                typeof(UIElement),
                UIElement.PreviewKeyDownEvent,
                new KeyEventHandler(OnGlobalPreviewKeyDown),
                handledEventsToo: true);

            // Класс-обработчик вето на открытие: после принудительного закрытия по ESC
            // подавленный владелец не должен автоматически показывать тултип снова, но только
            // на время текущего цикла закрытия (issue #131).
            EventManager.RegisterClassHandler(
                typeof(FrameworkElement),
                System.Windows.Controls.ToolTipService.ToolTipOpeningEvent,
                new ToolTipEventHandler(OnToolTipOpening));

            // Снятие подавления при уходе курсора с владельца (issue #131): подавление
            // действует только на текущий цикл закрытия. Как только указатель покинул
            // владельца (или истекло окно политики), запись удаляется, и повторное наведение
            // снова показывает подсказку — без перезапуска приложения.
            EventManager.RegisterClassHandler(
                typeof(UIElement),
                Mouse.MouseMoveEvent,
                new MouseEventHandler(OnGlobalMouseMove));

            // Удалённый из дерева элемент не должен удерживаться в реестрах подавления.
            EventManager.RegisterClassHandler(
                typeof(FrameworkElement),
                FrameworkElement.UnloadedEvent,
                new RoutedEventHandler(OnElementUnloaded));

            // Глобальное отслеживание открытых контекстных меню (issue #261/#270): первый ESC
            // должен закрыть открытое меню в любом окне приложения, а не само окно.
            EventManager.RegisterClassHandler(
                typeof(ContextMenu),
                ContextMenu.OpenedEvent,
                new RoutedEventHandler(OnContextMenuOpened));

            EventManager.RegisterClassHandler(
                typeof(ContextMenu),
                ContextMenu.ClosedEvent,
                new RoutedEventHandler(OnContextMenuClosed));

            // Пользовательские Popup-подсказки (HelpLink и т.п., issue #275): запоминаем их
            // класс-обработчиками Loaded/Unloaded (у Popup нет routed-событий Opened/Closed —
            // только CLR-события, а Loaded/Unloaded срабатывают при показе/скрытии попапа),
            // чтобы закрывать их по потере фокуса детерминированно и быстро проверять в
            // fallback-таймере. Штатные попапы селекторов/календарей фильтруются в
            // OnPopupLoaded через ShouldClosePopup (см. CloseOpenPopups).
            EventManager.RegisterClassHandler(
                typeof(Popup),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnPopupLoaded));

            EventManager.RegisterClassHandler(
                typeof(Popup),
                FrameworkElement.UnloadedEvent,
                new RoutedEventHandler(OnPopupUnloaded));

            // Потеря фокуса приложением целиком (issue #275): открытый тултип в любом окне
            // должен исчезнуть при Alt+Tab / клике в другое приложение. Дублирует
            // Window.Deactivated отдельных окон — второй вызов CloseAll просто вернёт false.
            if (Application.Current is { } app)
                app.Deactivated += OnApplicationDeactivated;

            // Единая точка подписки на потерю фокуса ВСЕМИ WPF-окнами (issue #275): класс-обработчик
            // Loaded срабатывает для любого окна приложения (включая те ~20 окон, где Deactivated
            // раньше не был подписан), вешает Window.Deactivated → CloseAll и HwndSource-хук (WndProc)
            // на WM_ACTIVATEAPP/WM_ACTIVATE/WM_KILLFOCUS. Так как речь о «sent»-сообщениях, они не
            // проходят через ComponentDispatcher, поэтому хук на уровне окна — единственный надёжный
            // путь увидеть фактическую потерю фокуса/активации.
            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnWindowLoaded));

            // Fallback-таймер (issue #275): страховка от сценариев, в которых ни Application.Deactivated,
            // ни WM-сообщения не дошли до окон (активным числится тултип-попап или скрытое окно
            // NotifyIcon). Раз в 250 мс сверяем foreground-окно с PID процесса; если foreground —
            // чужой процесс, а всплывающие элементы открыты — закрываем их.
            _focusCheckTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _focusCheckTimer.Tick += OnFocusCheckTick;
            _focusCheckTimer.Start();
            TraceLog("Register: fallback-таймер контроля фокуса запущен");

            // Страховка уровня Win32 (issue #270): перехватываем WM_KEYDOWN/VK_ESCAPE на уровне
            // очереди сообщений потока ДО обработки WPF. Если CloseAll() закрыл хотя бы одну
            // подсказку/меню/попап — сообщение проглатывается целиком: routed-события WPF вообще
            // не создаются, поэтому клавиша не может дойти до кнопки IsCancel или хоткея
            // сворачивания в трей, какие бы элементы ни держали фокус (TextBox/ComboBox, внешний
            // HWND попапа подсказки и т.п.). Повторное нажатие ESC проходит штатно и закрывает окно.
            ComponentDispatcher.ThreadPreprocessMessage += OnThreadPreprocessMessage;

            TraceLog("Register: класс-обработчики и Win32-перехват ESC установлены");
        }

        /// <summary>
        /// Потеря фокуса приложением (issue #275): прячем все открытые подсказки, чтобы они
        /// не «висели» поверх другого окна/браузера. Диагностический трейс помогает понять,
        /// доходит ли WPF-событие (на практике оно опирается на WM_ACTIVATEAPP и может не
        /// срабатывать, когда активным числится тултип-попап или скрытое окно NotifyIcon —
        /// для таких случаев есть WndProc-хук и fallback-таймер).
        /// </summary>
        private static void OnApplicationDeactivated(object? sender, EventArgs e)
        {
            TraceLog("OnApplicationDeactivated: приложение потеряло активацию");

            // Закрытие откладываем на Background и выполняем, только если ни одно окно
            // приложения не стало активным за это время (issue #131): кратковременная
            // деактивация (активация попапа подсказки/меню, мигание foreground) не должна
            // гасить только что показанную подсказку.
            var app = Application.Current;
            if (app is null)
                return;

            app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                try
                {
                    if (IsAnyWindowActive())
                    {
                        TraceLog("OnApplicationDeactivated: окно снова активно — закрытие отменено");
                        return;
                    }
                    CloseAll();
                }
                catch
                {
                    // потеря фокуса не должна ронять приложение
                }
            }));
        }

        /// <summary>
        /// Класс-обработчик Loaded любого WPF-окна (issue #275): единая точка подписки
        /// Deactivated и HwndSource-хука для ВСЕХ окон приложения, а не только тех пяти,
        /// где раньше подписка стояла вручную. Защита от повторных подписок — через
        /// <see cref="_focusSubscribedWindows"/> (Loaded у окна может сработать повторно).
        /// </summary>
        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Window window)
                return;
            if (!_focusSubscribedWindows.Add(window))
                return;

            window.Deactivated += OnWindowDeactivated;
            TraceLog($"OnWindowLoaded: окно {FormatObject(window)} подписано на Deactivated");

            AttachFocusHook(window);
        }

        /// <summary>
        /// Потеря фокуса окном (issue #275): прячем открытые подсказки. Дублирует
        /// Application.Deactivated — второй вызов CloseAll просто вернёт false.
        /// </summary>
        private static void OnWindowDeactivated(object? sender, EventArgs e)
        {
            if (sender is not Window window)
            {
                try { CloseAll(); }
                catch { /* потеря фокуса не должна ронять приложение */ }
                return;
            }

            TraceLog($"OnWindowDeactivated: окно {FormatObject(window)} потеряло фокус");

            // Закрытие откладываем на Background и проверяем фактическую активность окна
            // на момент выполнения (issue #131): кратковременная деактивация из-за попапа
            // подсказки/меню не должна закрывать только что показанную подсказку.
            window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                try
                {
                    if (window.IsActive)
                    {
                        TraceLog($"OnWindowDeactivated: окно {FormatObject(window)} снова активно — закрытие отменено");
                        return;
                    }
                    CloseAll();
                }
                catch
                {
                    // потеря фокуса не должна ронять приложение
                }
            }));
        }

        /// <summary>
        /// Вешает HwndSource.AddHook на окно (issue #275). Хук уровня окна видит и «sent»-,
        /// и queued-сообщения Win32 — в отличие от ComponentDispatcher.ThreadPreprocessMessage,
        /// который получает только сообщения из очереди потока. Благодаря этому перехватываются
        /// WM_ACTIVATEAPP/WM_ACTIVATE/WM_KILLFOCUS, которые присылаются окну напрямую (SendMessage).
        /// </summary>
        private static void AttachFocusHook(Window window)
        {
            try
            {
                var source = PresentationSource.FromVisual(window) as HwndSource;
                if (source is null && new WindowInteropHelper(window).Handle != IntPtr.Zero)
                    source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);

                if (source is null || !_hookedSources.Add(source))
                    return;

                source.AddHook(WindowProcHook);
                TraceLog($"AttachFocusHook: WndProc-хук установлен на окно {FormatObject(window)}");
            }
            catch
            {
                // Окно могло быть закрыто до установки хука — не роняем приложение.
            }
        }

        /// <summary>
        /// WndProc-хук окна (issue #275): при фактической потере фокуса/активации закрываем
        /// все всплывающие элементы. Не трогаем WM_KEYDOWN/VK_ESCAPE — им занимается
        /// ComponentDispatcher (см. OnThreadPreprocessMessage, issue #270).
        /// <para>
        /// Чтобы не ломать штатные попапы/меню собственного приложения (контекстные меню,
        /// дропдауны селекторов), закрываем только тогда, когда новый владелец фокуса/активации —
        /// чужой процесс или HWND отсутствует вовсе (сворачивание, клик вне приложения).
        /// </para>
        /// </summary>
        private static IntPtr WindowProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            try
            {
                switch (msg)
                {
                    case WM_ACTIVATEAPP:
                        if (wParam == IntPtr.Zero)
                        {
                            TraceLog("WndProc: WM_ACTIVATEAPP(FALSE) — активация ушла от приложения");
                            CloseAll();
                        }
                        break;

                    case WM_ACTIVATE:
                        // wParam: WA_INACTIVE=0 / WA_ACTIVE=1 / WA_CLICKACTIVE=2; lParam — HWND
                        // активируемого окна (0, если активируется окно другого приложения).
                        if (wParam.ToInt32() == WA_INACTIVE && (lParam == IntPtr.Zero || IsForeignWindow(lParam)))
                        {
                            TraceLog($"WndProc: WM_ACTIVATE(WA_INACTIVE), lParam={lParam}");
                            CloseAll();
                        }
                        break;

                    case WM_KILLFOCUS:
                        // wParam — HWND, получающий фокус (0 = фокус ушёл в никуда/другое приложение).
                        if (wParam == IntPtr.Zero || IsForeignWindow(wParam))
                        {
                            TraceLog($"WndProc: WM_KILLFOCUS, wParam={wParam}");
                            CloseAll();
                        }
                        break;
                }
            }
            catch
            {
                // Перехват никогда не должен ронять приложение.
            }
            return IntPtr.Zero;
        }

        /// <summary>Принадлежит ли HWND другому процессу (а не нашему приложению).</summary>
        private static bool IsForeignWindow(IntPtr hwnd)
        {
            try
            {
                GetWindowThreadProcessId(hwnd, out var pid);
                return pid != 0 && pid != Environment.ProcessId;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Класс-обработчик Loaded пользовательского <see cref="Popup"/> (issue #275): попап попал
        /// в визуальное дерево — если он открыт и «пользовательский» (не дропдаун селектора/
        /// календаря), запоминаем его для закрытия по потере фокуса.
        /// </summary>
        private static void OnPopupLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Popup { IsOpen: true } popup && ShouldClosePopup(popup))
            {
                _openPopups.Add(popup);
                TraceLog($"OnPopupLoaded: popup={FormatObject(popup)}, всего={_openPopups.Count}");
            }
        }

        /// <summary>
        /// Класс-обработчик Unloaded пользовательского <see cref="Popup"/> (issue #275): попап
        /// ушёл из визуального дерева — забываем его. Не критично, если Unloaded не сработает:
        /// CloseOpenPopups дополнительно обходит визуальное дерево и гасит попапы по IsOpen.
        /// </summary>
        private static void OnPopupUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is Popup popup)
            {
                _openPopups.Remove(popup);
                TraceLog($"OnPopupUnloaded: popup={FormatObject(popup)}, осталось={_openPopups.Count}");
            }
        }

        /// <summary>
        /// Fallback-таймер контроля фокуса (issue #275). Срабатывает каждые 250 мс; если открытых
        /// всплывающих элементов нет — выходит сразу. Если foreground-окно принадлежит другому
        /// процессу (приложение неактивно), закрывает все всплывающие элементы. Это страховка от
        /// сценариев, в которых ни Application.Deactivated, ни WndProc-хук не сработали (например,
        /// активным окном процесса числился сам тултип-попап, на который хук не повешен, а главное
        /// окно к моменту потери фокуса уже было неактивным).
        /// </summary>
        private static void OnFocusCheckTick(object? sender, EventArgs e)
        {
            try
            {
                if (_openToolTips.Count == 0 && _openContextMenus.Count == 0 && _openPopups.Count == 0)
                    return;

                var ourProcessForeground = IsOurProcessForeground();
                var pointerOverOurWindow = IsPointerOverAnyWindow();
                if (ToolTipSuppressionPolicy.IsFallbackCloseAllowed(ourProcessForeground, pointerOverOurWindow))
                {
                    TraceLog($"OnFocusCheckTick: foreground — чужой процесс, тултипов={_openToolTips.Count}, " +
                             $"меню={_openContextMenus.Count}, попапов={_openPopups.Count}");
                    // Fallback-таймер не подавляет повторное открытие (issue #131): закрытие
                    // по ложной деактивации не должно «замораживать» владельца подсказки.
                    CloseAll();
                }
                else if (!ourProcessForeground)
                {
                    TraceLog("OnFocusCheckTick: закрытие отменено — курсор над нашим окном");
                }
            }
            catch
            {
                // Таймер никогда не должен ронять приложение.
            }
        }

        /// <summary>
        /// Активно ли сейчас окно нашего процесса (GetForegroundWindow). Приложение считается
        /// активным, если foreground-окно принадлежит тому же PID, что и мы.
        /// </summary>
        private static bool IsOurProcessForeground()
        {
            try
            {
                var fg = GetForegroundWindow();
                if (fg == IntPtr.Zero)
                    return false;
                GetWindowThreadProcessId(fg, out var pid);
                return pid == Environment.ProcessId;
            }
            catch
            {
                return true; // при сбое не принимаем агрессивных решений
            }
        }

        /// <summary>
        /// Планирует повторный проход закрытия тултипов (issue #275): внешний попап ToolTip
        /// живёт в отдельном окне верхнего уровня и может «не успеть» погаснуть в том же кадре,
        /// в котором было снято IsOpen. Повторный вызов через Dispatcher (после обработки очереди
        /// ввода/layout) закрывает то, что не погасло с первого раза. Не более одного запланированного
        /// повтора одновременно — зацикливания нет.
        /// </summary>
        private static void ScheduleToolTipRetry()
        {
            if (_toolTipRetryScheduled)
                return;
            _toolTipRetryScheduled = true;

            Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                _toolTipRetryScheduled = false;
                // Повторный проход — не ESC-путь, поэтому подавление повторного открытия
                // не включает (issue #131); уже подавленные ESC владельцы сохраняют запись.
                if (_openToolTips.Count > 0 && CloseOpenToolTips(suppressReopen: false))
                    TraceLog("ScheduleToolTipRetry: повторный проход закрыл оставшиеся тултипы");
            }));
        }

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        /// <summary>
        /// Закрывает все открытые всплывающие элементы приложения (issues #270, #275): тултипы,
        /// контекстные меню и пользовательские Popup-подсказки (HelpLink и т.п.).
        /// Возвращает true, если был закрыт хотя бы один элемент. Основной путь для тултипов —
        /// закрытие через сами инстансы ToolTip, записанные класс-обработчиками
        /// <see cref="System.Windows.Controls.ToolTip.OpenedEvent"/>/<see cref="System.Windows.Controls.ToolTip.ClosedEvent"/>:
        /// это работает и для объектовых, и для строковых подсказок (внутренний общий ToolTip
        /// сервиса) и не зависит от того, где физически отрисован попап. Резервные пути — закрытие
        /// по владельцам (улучшенный <see cref="TryCloseToolTip"/>), обход визуального дерева
        /// открытых окон и цепочка визуальных родителей элемента под курсором/в фокусе. Затем
        /// закрываются открытые контекстные меню и пользовательские Popup (кроме штатных
        /// дропдаунов селекторов/календарей, которыми управляют сами контролы).
        /// <para>
        /// <paramref name="suppressReopen"/> (issue #131): запрещать ли автоматическое повторное
        /// открытие закрытых подсказок на время текущего цикла. <c>true</c> передают только
        /// ESC-пути; потеря фокуса, деактивация, прокрутка и fallback-таймер вызывают метод
        /// без подавления, чтобы подсказка снова открывалась при повторном наведении.
        /// </para>
        /// </summary>
        public static bool CloseAll(bool suppressReopen = false)
        {
            var closed = false;

            closed |= CloseOpenToolTips(suppressReopen);
            closed |= CloseOpenContextMenus();
            closed |= CloseOpenPopups();

            // Повторный проход (issue #275): внешний попап ToolTip живёт в отдельном окне верхнего
            // уровня и может «не успеть» погаснуть в том же кадре, в котором было снято IsOpen.
            // Отложенный вызов через Dispatcher после обработки очереди ввода/layout закрывает то,
            // что осталось. Планирование — не более одного повтора одновременно.
            if (_openToolTips.Count > 0)
                ScheduleToolTipRetry();

            TraceLog($"CloseAll: итог closed={closed}, тултипов={_openToolTips.Count}, " +
                     $"меню={_openContextMenus.Count}, попапов={_openPopups.Count}");
            return closed;
        }

        /// <summary>
        /// Закрывает все открытые ToolTip приложения (основная часть <see cref="CloseAll"/>).
        /// Основной путь — по самим ToolTip-инстансам; резервные — по владельцам, обход
        /// визуального дерева окон и цепочка родителей элемента под курсором/в фокусе.
        /// <paramref name="suppressReopen"/> включает подавление повторного открытия
        /// только в ESC-путях (issue #131).
        /// </summary>
        private static bool CloseOpenToolTips(bool suppressReopen)
        {
            var closed = false;

            // Основной путь: закрываем ВСЕ открытые подсказки через сами ToolTip-инстансы.
            // Для строковых подсказок это внутренний ToolTip, созданный сервисом, — IsOpen=false
            // на нём гарантированно гасит попап (GetToolTip у строковых возвращает строку, а
            // GetIsOpen на владельце — false, потому что сервис выставляет IsOpen на тултипе).
            foreach (var tip in _openToolTips.ToArray())
            {
                if (tip.IsOpen)
                {
                    try { tip.IsOpen = false; } catch { /* не роняем по одной подсказке */ }
                    closed = true;
                    // Подавляем повторное автоматическое открытие только для ESC (issue #270,
                    // #131): при обычной потере фокуса подсказка должна открываться снова.
                    SuppressToolTipOwner(tip.PlacementTarget ?? tip, suppressReopen);
                }
            }
            if (closed)
            {
                _openToolTips.Clear();
                _openToolTipOwners.Clear();
                TraceLog("CloseOpenToolTips: закрыты по ToolTip-инстансам");
                return true;
            }

            // Путь по владельцам: подсказки, у которых Opened/Closed не отразились в
            // _openToolTips (например, показанные до регистрации класс-обработчика).
            foreach (var owner in _openToolTipOwners.ToArray())
            {
                if (TryCloseToolTip(owner))
                {
                    closed = true;
                    SuppressToolTipOwner(owner, suppressReopen);
                }
            }
            if (closed)
            {
                _openToolTips.Clear();
                _openToolTipOwners.Clear();
                TraceLog("CloseOpenToolTips: закрыты по владельцам");
                return true;
            }

            // Резервный путь: обход визуального дерева открытых окон (для подсказок, не
            // попавших в отслеживание).
            if (Application.Current is { } app)
            {
                foreach (Window window in app.Windows)
                {
                    if (CloseToolTipsIn(window, suppressReopen))
                        closed = true;
                }
            }

            // Попап открытой подсказки размещается вне визуального дерева окна (отдельный
            // HWND/Popup), поэтому до «хозяина» добираемся и по курсору/фокусу.
            closed |= CloseToolTipByMouseOrFocus(suppressReopen);

            if (closed)
                TraceLog("CloseOpenToolTips: закрыты резервными путями");

            return closed;
        }

        /// <summary>Закрывает все открытые <see cref="System.Windows.Controls.ToolTip"/> в поддереве.</summary>
        private static bool CloseToolTipsIn(DependencyObject root, bool suppressReopen)
        {
            var closed = false;
            var queue = new Queue<DependencyObject>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();

                if (node is UIElement ui && TryCloseToolTip(ui))
                {
                    closed = true;
                    SuppressToolTipOwner(ui, suppressReopen);
                }

                for (var i = VisualTreeHelper.GetChildrenCount(node) - 1; i >= 0; i--)
                    queue.Enqueue(VisualTreeHelper.GetChild(node, i));
            }
            return closed;
        }

        /// <summary>
        /// Закрывает открытую подсказку на элементе под курсором или в фокусе (issue #270).
        /// Всплывающий попап ToolTip живёт вне визуального дерева окна и мог не попасть в обход
        /// <see cref="CloseToolTipsIn"/>, поэтому дотягиваемся до «хозяина» подсказки по цепочке
        /// визуальных родителей от элемента под мышью/в фокусе.
        /// </summary>
        private static bool CloseToolTipByMouseOrFocus(bool suppressReopen)
        {
            var closed = false;
            var candidates = new DependencyObject?[]
            {
                Mouse.DirectlyOver as DependencyObject,
                Keyboard.FocusedElement as DependencyObject
            };

            foreach (var candidate in candidates)
            {
                for (var node = candidate; node is not null; node = VisualTreeHelper.GetParent(node))
                {
                    if (TryCloseToolTip(node))
                    {
                        closed = true;
                        SuppressToolTipOwner(node, suppressReopen);
                    }
                }
            }

            return closed;
        }

        /// <summary>
        /// Закрывает открытую подсказку элемента, если таковая есть. Возвращает true,
        /// если элемент держал открытый ToolTip и тот был закрыт (issues #261/#270).
        /// Три пути: (1) у элемента присоединён объектовый ToolTip — гасим его IsOpen;
        /// (2) среди отслеживаемых открытых тултипов есть такой, чей PlacementTarget — этот
        /// элемент (строковые подсказки: GetToolTip возвращает строку, а попап держит
        /// внутренний ToolTip сервиса); (3) сервис числит подсказку открытой через
        /// GetIsOpen — снимаем через SetIsEnabled(false) с отложенным восстановлением true
        /// через Dispatcher (синхронное включение мгновенно переоткрыло бы подсказку, пока
        /// курсор над владельцем).
        /// </summary>
        private static bool TryCloseToolTip(DependencyObject element)
        {
            // Путь 1: объектовый ToolTip (XAML <ToolTip>), присоединённый к элементу.
            if (System.Windows.Controls.ToolTipService.GetToolTip(element) is System.Windows.Controls.ToolTip tip && tip.IsOpen)
            {
                try { tip.IsOpen = false; } catch { }
                return true;
            }

            // Путь 2: строковая подсказка (ToolTip="..."). GetToolTip возвращает строку, поэтому
            // ищем открытый ToolTip, чей владелец (PlacementTarget) — этот элемент.
            foreach (var open in _openToolTips)
            {
                if (ReferenceEquals(open.PlacementTarget, element) && open.IsOpen)
                {
                    try { open.IsOpen = false; } catch { }
                    return true;
                }
            }

            // Путь 3: страховка через состояние сервиса. Снимаем подсказку отключением тултипа,
            // а восстановление true откладываем: синхронное включение тут же переоткрыло бы её
            // (вето ToolTipOpeningEvent при этом подавляет повторный показ — см. OnToolTipOpening).
            if (System.Windows.Controls.ToolTipService.GetIsOpen(element))
            {
                System.Windows.Controls.ToolTipService.SetIsEnabled(element, false);
                element.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { System.Windows.Controls.ToolTipService.SetIsEnabled(element, true); }
                    catch { /* элемент мог быть уничтожен до восстановления */ }
                }), DispatcherPriority.Input);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Глобальный класс-обработчик ESC на типе UIElement (issue #270). Первый ESC в любом
        /// окне/попапе закрывает открытые подсказки и помечает событие обработанным — до того,
        /// как его обработает кнопка IsCancel окна или хоткей сворачивания в трей. Если открытых
        /// подсказок нет — событие не трогаем, и ESC работает как обычно (закрытие окна, меню,
        /// ввод в TextBox/ComboBox/HotkeyBox и т.п.). Этот обработчик — «второй рубеж»: первым
        /// ESC перехватывается уже в <see cref="OnThreadPreprocessMessage"/> (уровень Win32).
        /// </summary>
        private static void OnGlobalPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || Keyboard.Modifiers != ModifierKeys.None)
                return;

            try
            {
                // ESC — принудительное закрытие: повторное открытие подавляется на текущий
                // цикл, чтобы подсказка не «мигнула» под курсором (issue #131).
                if (CloseAll(suppressReopen: true))
                {
                    e.Handled = true;
                    TraceLog($"OnGlobalPreviewKeyDown: ESC обработан, sender={sender?.GetType().Name}");
                }
            }
            catch
            {
                // Перехват ESC никогда не должен ронять приложение.
            }
        }

        /// <summary>Класс-обработчик открытия <see cref="System.Windows.Controls.ToolTip"/> (issue #270): запоминает тултип и владельца.</summary>
        private static void OnToolTipOpened(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.ToolTip tip)
            {
                _openToolTips.Add(tip);
                var owner = tip.PlacementTarget ?? tip;
                _openToolTipOwners.Add(owner);
                TraceLog($"OnToolTipOpened: tooltip={FormatObject(tip)}, owner={FormatObject(owner)}, всего={_openToolTips.Count}");
            }
        }

        /// <summary>Класс-обработчик закрытия <see cref="System.Windows.Controls.ToolTip"/> (issue #270): убирает тултип и владельца.</summary>
        private static void OnToolTipClosed(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.ToolTip tip)
            {
                _openToolTips.Remove(tip);
                var owner = tip.PlacementTarget ?? tip;
                _openToolTipOwners.Remove(owner);
                TraceLog($"OnToolTipClosed: tooltip={FormatObject(tip)}, осталось={_openToolTips.Count}");
            }
        }

        /// <summary>
        /// Класс-обработчик ESC на типе <see cref="System.Windows.Controls.ToolTip"/> (issue #270):
        /// когда клавиша приходится на открытую подсказку (фокус во внешнем попапе/HWND),
        /// закрывает подсказку и помечает событие обработанным, чтобы первый ESC не закрыл окно.
        /// </summary>
        private static void OnToolTipPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || Keyboard.Modifiers != ModifierKeys.None)
                return;

            if (sender is System.Windows.Controls.ToolTip tip)
            {
                try { tip.IsOpen = false; } catch { }
                var owner = tip.PlacementTarget ?? tip;
                _openToolTips.Remove(tip);
                _openToolTipOwners.Remove(owner);
                SuppressToolTipOwner(owner, suppressReopen: true);
                e.Handled = true;
                TraceLog("OnToolTipPreviewKeyDown: ESC по тултипу");
            }
        }

        /// <summary>
        /// Класс-обработчик вето на повторное открытие подсказки (issues #270, #131).
        /// Вызывается до показа тултипа владельца. Вето действует только внутри окна
        /// <see cref="ToolTipSuppressionPolicy.WindowMs"/> от принудительного закрытия по ESC
        /// и НЕ продлевается наведённым курсором: при показе по наведению указатель всегда над
        /// владельцем, и прежняя проверка <c>IsPointerOver</c> навсегда «замораживала» подсказку
        /// (issue #131). Когда окно истекло или курсор ушёл — запись снимается, и подсказка
        /// снова открывается при повторном наведении.
        /// </summary>
        private static void OnToolTipOpening(object sender, ToolTipEventArgs e)
        {
            if (_suppressedToolTipOwners.Count == 0 || sender is not DependencyObject owner)
                return;
            if (!_suppressedToolTipOwners.TryGetValue(owner, out var suppressedTick))
                return;

            var pointerOver = IsPointerOver(owner);
            if (_suppressionPolicy.ShouldRelease(Environment.TickCount64, suppressedTick, pointerOver))
            {
                _suppressedToolTipOwners.Remove(owner);
                TraceLog($"OnToolTipOpening: подавление снято, owner={FormatObject(owner)}, pointerOver={pointerOver}");
                return;
            }

            // Подавление действует только на текущий цикл закрытия после ESC.
            e.Handled = true;
            TraceLog($"OnToolTipOpening: показ подавлен после ESC, owner={FormatObject(owner)}");
        }

        /// <summary>
        /// Подавляет повторное открытие подсказки владельца (issue #270). Запись создаётся
        /// только для ESC-путей (<paramref name="suppressReopen"/> = true); обычное закрытие
        /// снимает прежнюю запись, чтобы владелец не «замораживался» (issue #131).
        /// </summary>
        private static void SuppressToolTipOwner(DependencyObject owner, bool suppressReopen)
        {
            if (!suppressReopen)
            {
                if (_suppressedToolTipOwners.Remove(owner))
                    TraceLog($"SuppressToolTipOwner: подавление снято (не ESC), owner={FormatObject(owner)}");
                return;
            }

            _suppressedToolTipOwners[owner] = Environment.TickCount64;
            TraceLog($"SuppressToolTipOwner: owner={FormatObject(owner)}, окно={_suppressionPolicy.WindowMs} мс");
        }

        /// <summary>
        /// Класс-обработчик движения мыши (issue #131): снимает подавление с владельцев,
        /// над которыми курсора больше нет (или истекло окно политики). Проверка дешёвая —
        /// при пустом реестре сразу выходит.
        /// </summary>
        private static void OnGlobalMouseMove(object sender, MouseEventArgs e)
        {
            ReleaseSuppressedOwners();
        }

        /// <summary>Снимает устаревшие записи о подавлении подсказок (issue #131).</summary>
        private static void ReleaseSuppressedOwners()
        {
            if (_suppressedToolTipOwners.Count == 0)
                return;

            List<DependencyObject>? release = null;
            var now = Environment.TickCount64;
            foreach (var pair in _suppressedToolTipOwners)
            {
                var pointerOver = IsPointerOver(pair.Key);
                if (_suppressionPolicy.ShouldRelease(now, pair.Value, pointerOver))
                    (release ??= new List<DependencyObject>()).Add(pair.Key);
            }

            if (release is null)
                return;

            foreach (var owner in release)
            {
                _suppressedToolTipOwners.Remove(owner);
                TraceLog($"ReleaseSuppressedOwners: подавление снято, owner={FormatObject(owner)}");
            }
        }

        /// <summary>
        /// Класс-обработчик Unloaded (issue #131): удалённый из дерева элемент не должен
        /// удерживаться в реестрах открытых подсказок и подавления.
        /// </summary>
        private static void OnElementUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is not DependencyObject element)
                return;

            _suppressedToolTipOwners.Remove(element);
            _openToolTipOwners.Remove(element);
        }

        /// <summary>Есть ли хотя бы одно активное окно приложения (issue #131).</summary>
        private static bool IsAnyWindowActive()
        {
            if (Application.Current is not { } app)
                return false;

            foreach (Window window in app.Windows)
            {
                try
                {
                    if (window.IsActive)
                        return true;
                }
                catch
                {
                    // окно могло закрыться во время обхода
                }
            }
            return false;
        }

        /// <summary>
        /// Находится ли курсор над каким-либо окном приложения (issue #131). Нужно
        /// fallback-таймеру, чтобы не закрывать подсказку, пока пользователь работает
        /// с приложением (кратковременный «чужой» foreground — артефакт).
        /// </summary>
        private static bool IsPointerOverAnyWindow()
        {
            if (Application.Current is not { } app)
                return false;

            foreach (Window window in app.Windows)
            {
                try
                {
                    if (window.IsMouseOver)
                        return true;
                }
                catch
                {
                    // окно могло закрыться во время обхода
                }
            }
            return false;
        }

        /// <summary>Находится ли указатель мыши над элементом или его потомком (issue #270).</summary>
        private static bool IsPointerOver(DependencyObject owner)
        {
            for (var node = Mouse.DirectlyOver as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (ReferenceEquals(node, owner))
                    return true;
            }
            return false;
        }

        // ---- Контекстные меню (issue #261/#270) ----

        private static void OnContextMenuOpened(object sender, RoutedEventArgs e)
        {
            if (sender is ContextMenu menu)
            {
                _openContextMenus.Add(menu);
                TraceLog($"OnContextMenuOpened: menu={FormatObject(menu)}, всего={_openContextMenus.Count}");
            }
        }

        private static void OnContextMenuClosed(object sender, RoutedEventArgs e)
        {
            if (sender is ContextMenu menu)
            {
                _openContextMenus.Remove(menu);
                TraceLog($"OnContextMenuClosed: menu={FormatObject(menu)}, осталось={_openContextMenus.Count}");
            }
        }

        /// <summary>
        /// Закрывает все открытые контекстные меню приложения (issue #261/#270). Используется
        /// из <see cref="CloseAll"/>: первый ESC закрывает открытое меню в любом окне (не только
        /// главном), а не само окно. Закрытие через владельца детерминированно — меню записываются
        /// класс-обработчиками Opened/Closed независимо от того, как были показаны.
        /// </summary>
        private static bool CloseOpenContextMenus()
        {
            var closed = false;
            foreach (var menu in _openContextMenus.ToArray())
            {
                if (menu.IsOpen)
                {
                    try { menu.IsOpen = false; } catch { }
                    closed = true;
                }
            }
            if (closed)
            {
                _openContextMenus.Clear();
                TraceLog("CloseOpenContextMenus: закрыты меню");
            }
            return closed;
        }

        // ---- Пользовательские Popup-подсказки (HelpLink и т.п., issue #261/#270) ----

        /// <summary>
        /// Закрывает открытые пользовательские Popup (всплывающие «пузыри» HelpLink, кастомные
        /// оверлеи), которые не являются ни стандартным ToolTip, ни ContextMenu (issue #261/#275).
        /// Основной путь — по отслеживаемым инстансам <see cref="_openPopups"/> (класс-обработчики
        /// Loaded/Unloaded); резервный — обход визуального дерева всех окон приложения для попапов,
        /// открытых до регистрации механизма. Штатные дропдауны селекторов (ComboBox) и календари
        /// DatePicker НЕ трогаем — ими управляют сами контролы, и грубое гашение IsOpen разорвало
        /// бы их внутреннее состояние (см. <see cref="ShouldClosePopup"/>).
        /// </summary>
        private static bool CloseOpenPopups()
        {
            var closed = false;

            // Основной путь: по инстансам, записанным класс-обработчиками Loaded/Unloaded.
            foreach (var popup in _openPopups.ToArray())
            {
                if (popup.IsOpen)
                {
                    try { popup.IsOpen = false; } catch { }
                    closed = true;
                }
            }
            if (closed)
            {
                _openPopups.Clear();
                TraceLog("CloseOpenPopups: закрыты попапы по инстансам");
            }

            // Резервный путь: обход визуального дерева окон (попапы, открытые до регистрации).
            if (Application.Current is { } app)
            {
                foreach (Window window in app.Windows)
                {
                    if (ClosePopupsIn(window))
                        closed = true;
                }
            }

            if (closed)
                TraceLog("CloseOpenPopups: закрыты пользовательские попапы");
            return closed;
        }

        /// <summary>Закрывает все «пользовательские» открытые <see cref="Popup"/> в поддереве.</summary>
        private static bool ClosePopupsIn(DependencyObject root)
        {
            var any = false;
            var queue = new Queue<DependencyObject>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if (node is Popup { IsOpen: true } popup && ShouldClosePopup(popup))
                {
                    try { popup.IsOpen = false; } catch { }
                    any = true;
                }
                for (var i = VisualTreeHelper.GetChildrenCount(node) - 1; i >= 0; i--)
                    queue.Enqueue(VisualTreeHelper.GetChild(node, i));
            }
            return any;
        }

        /// <summary>
        /// Является ли попап «пользовательским» (закрывается по ESC/потере фокуса), а не штатным
        /// дропдауном контрола: попап, привязанный к селектору (ComboBox и т.п.), или попап
        /// календаря DatePicker закрываются самими контролами по ESC, поэтому их не гасим.
        /// </summary>
        private static bool ShouldClosePopup(Popup popup)
        {
            if (popup.PlacementTarget is Selector)
                return false;
            if (popup.PlacementTarget is DatePicker)
                return false;
            if (ContainsCalendar(popup.Child))
                return false;
            return true;
        }

        /// <summary>Содержит ли поддерево элемент календаря DatePicker (в таком случае попап — штатный).</summary>
        private static bool ContainsCalendar(DependencyObject? root)
        {
            if (root is null)
                return false;

            var queue = new Queue<DependencyObject>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if (node is System.Windows.Controls.Calendar or System.Windows.Controls.DatePicker)
                    return true;
                for (var i = VisualTreeHelper.GetChildrenCount(node) - 1; i >= 0; i--)
                    queue.Enqueue(VisualTreeHelper.GetChild(node, i));
            }
            return false;
        }

        // ---- Страховка уровня Win32 (гарантия первого ESC, issue #270) ----

        /// <summary>
        /// Перехват WM_KEYDOWN/VK_ESCAPE на уровне очереди сообщений потока (до обработки WPF).
        /// Если <see cref="CloseAll"/> закрыл хотя бы одну подсказку/меню/попап — сообщение
        /// проглатывается целиком: WPF не строит routed-события, и клавиша физически не доходит
        /// до кнопки IsCancel окна или хоткея сворачивания в трей — какие бы элементы ни держали
        /// фокус (TextBox/ComboBox, внешний HWND попапа подсказки). Автоповтор удержанного ESC
        /// после проглоченного нажатия тоже гасится (иначе «залипшая» клавиша закроет окно на
        /// повторе). Новое нажатие ESC (не автоповтор) проходит штатно — повторный ESC закрывает
        /// окно как обычно. Ввод в <see cref="HotkeyBox"/> не перехватывается: там ESC отменяет
        /// ввод комбинации собственным обработчиком.
        /// </summary>
        private static void OnThreadPreprocessMessage(ref MSG msg, ref bool handled)
        {
            try
            {
                if (msg.message != WM_KEYDOWN || msg.wParam.ToInt32() != VK_ESCAPE)
                    return;

                if (Keyboard.Modifiers != ModifierKeys.None)
                    return;

                // HotkeyBox сам обрабатывает ESC (отмена текущего ввода комбинации) — его не трогаем.
                if (Keyboard.FocusedElement is HotkeyBox)
                    return;

                // ESC — принудительное закрытие: подавляем повторное открытие на текущий
                // цикл, чтобы подсказка не «мигнула» под курсором (issue #131).
                if (CloseAll(suppressReopen: true))
                {
                    handled = true;
                    _lastEscSwallowedTick = Environment.TickCount64;
                    TraceLog("OnThreadPreprocessMessage: ESC проглочен (закрыты всплывающие элементы)");
                    return;
                }

                // Автоповтор удержанного ESC после проглоченного нажатия: глотаем, чтобы окно
                // не закрылось «само» на повторе, пока пользователь держит клавишу.
                if (Environment.TickCount64 - _lastEscSwallowedTick < EscAutoRepeatGuardMs && IsEscAutoRepeat(msg.lParam))
                {
                    handled = true;
                    return;
                }
            }
            catch
            {
                // Перехват никогда не должен ронять приложение.
            }
        }

        /// <summary>
        /// Является ли WM_KEYDOWN автоповтором: бит 30 lParam — предыдущее состояние клавиши
        /// (1 = клавиша уже была нажата → автоповтор, а не новое нажатие).
        /// </summary>
        private static bool IsEscAutoRepeat(IntPtr lParam)
        {
            return (((ulong)lParam.ToInt64() >> 30) & 1) == 1;
        }

        // ---- Диагностический трейс (CM_TOOLTIP_TRACE=1) ----

        /// <summary>Короткое представление объекта для трейс-лога.</summary>
        private static string FormatObject(object? o)
        {
            if (o is null)
                return "<null>";
            var type = o.GetType().Name;
            return o is FrameworkElement fe ? $"{type}[{fe.Name}]" : type;
        }

        /// <summary>
        /// Пишет строку в файл %TEMP%\cm_tooltip_trace.log, только если задана переменная
        /// окружения CM_TOOLTIP_TRACE=1. В проде вызов — практически бесплатный no-op.
        /// </summary>
        private static void TraceLog(string message)
        {
            if (!_traceEnabled)
                return;
            try
            {
                lock (_traceLock)
                {
                    File.AppendAllText(_tracePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}\r\n");
                }
            }
            catch
            {
                // Логирование не должно ронять приложение.
            }
        }
    }
}
#endif