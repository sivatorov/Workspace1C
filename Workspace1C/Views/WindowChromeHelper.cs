#if WINDOWS
using System;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using Workspace1C.Localization;
using Workspace1C.Themes;

namespace Workspace1C
{
    /// <summary>
    /// Общий «стеклянный» стиль диалоговых окон (Windows/WPF): собственный WindowChrome
    /// без системных кнопок, полупрозрачная подложка цвета темы, системный acrylic/mica
    /// (или blur-behind), скруглённые углы DWM и собственные кнопки «свернуть/закрыть»
    /// (у закрытия — красное выделение). Стиль применяется централизованно ко всем окнам
    /// через <see cref="RegisterGlobalWindowStyling"/>, поэтому отдельные окна править
    /// не нужно: оформление навешивается единым кодом (главное окно, оформленное
    /// собственными средствами, пропускается).
    /// </summary>
    public static class WindowChromeHelper
    {
        /// <summary>Альфа полупрозрачной подложки «стекла» — 0xE8 (~91% непрозрачности).</summary>
        private const byte GlassBackgroundAlpha = 0xE8;

        /// <summary>
        /// Радиус скругления верхних углов полосы заголовка (в DIP), совпадает с радиусом
        /// скругления углов окна, который DWM применяет через DwmWindowCornerPreference
        /// на Windows 11. Если не совпадать с ним, прямоугольная акцентная полоса выходит
        /// за скруглённый клип окна, и в углах шапки просвечивает стеклянная подложка.
        /// </summary>
        private const double DwmCornerRadius = 8;

        // DWMWA_SYSTEMBACKDROP_TYPE (38): 2 = Mica, 3 = Acrylic.
        private const int DwmSystemBackdropType = 38;
        private const int DwmBackdropAcrylic = 3;
        private const int DwmBackdropMica = 2;

        // DWMWA_WINDOW_CORNER_PREFERENCE (33): 1 = не скруглять, 2 = скруглять.
        private const int DwmWindowCornerPreference = 33;
        private const int DwmCornerRound = 2;
        private const int DwmCornerDoNotRound = 1;

        // DWM_BB_ENABLE для DwmEnableBlurBehindWindow.
        private const int DwmBbEnable = 0x00000001;

        // Помечает окно оформленным, чтобы не оборачивать содержимое повторно.
        private static readonly DependencyProperty AppliedProperty =
            DependencyProperty.RegisterAttached(
                "Applied", typeof(bool), typeof(WindowChromeHelper), new PropertyMetadata(false));

        private static bool IsApplied(DependencyObject d) => (bool)d.GetValue(AppliedProperty);
        private static void MarkApplied(DependencyObject d) => d.SetValue(AppliedProperty, true);

        // ---- Вложенные свойства собственных кнопок управления окном (App.xaml) ----
        // Круглая «подложка» кнопок: шаблон WindowControlButton рисует три наложенных
        // круга (hover/press/фокус) и подставляет их кисти из этих свойств. Смена цвета
        // при наведении/нажатии анимируется прозрачностью слоёв, поэтому сами кисти
        // можно спокойно брать DynamicResource'ом из темы — переключение схемы не
        // «замораживает» значения в анимациях. Акцентные варианты стилей переопределяют
        // только эти свойства, не дублируя шаблон.
        public static readonly DependencyProperty HoverBrushProperty =
            DependencyProperty.RegisterAttached(
                "HoverBrush", typeof(Brush), typeof(WindowChromeHelper), new PropertyMetadata(null));
        public static void SetHoverBrush(DependencyObject element, Brush value) => element.SetValue(HoverBrushProperty, value);
        public static Brush GetHoverBrush(DependencyObject element) => (Brush)element.GetValue(HoverBrushProperty);

        public static readonly DependencyProperty PressedBrushProperty =
            DependencyProperty.RegisterAttached(
                "PressedBrush", typeof(Brush), typeof(WindowChromeHelper), new PropertyMetadata(null));
        public static void SetPressedBrush(DependencyObject element, Brush value) => element.SetValue(PressedBrushProperty, value);
        public static Brush GetPressedBrush(DependencyObject element) => (Brush)element.GetValue(PressedBrushProperty);

        /// <summary>Кисть кольца фокуса вокруг круга (акцентная в обычных темах, светлая на акцентной шапке).</summary>
        public static readonly DependencyProperty FocusBrushProperty =
            DependencyProperty.RegisterAttached(
                "FocusBrush", typeof(Brush), typeof(WindowChromeHelper), new PropertyMetadata(null));
        public static void SetFocusBrush(DependencyObject element, Brush value) => element.SetValue(FocusBrushProperty, value);
        public static Brush GetFocusBrush(DependencyObject element) => (Brush)element.GetValue(FocusBrushProperty);

        /// <summary>
        /// Вложенное свойство: при true у окна не рисуется кнопка «развернуть/восстановить»
        /// в собственной шапке, а системная максимизация (Win+Up, двойной клик по шапке)
        /// подавляется снятием WS_MAXIMIZEBOX (issue #3). Ресайз (CanResize/CanResizeWithGrip)
        /// сохраняется. Устанавливается окном в конструкторе до показа.
        /// </summary>
        public static readonly DependencyProperty SuppressMaximizeButtonProperty =
            DependencyProperty.RegisterAttached(
                "SuppressMaximizeButton", typeof(bool), typeof(WindowChromeHelper), new PropertyMetadata(false));
        public static void SetSuppressMaximizeButton(DependencyObject element, bool value) => element.SetValue(SuppressMaximizeButtonProperty, value);
        public static bool GetSuppressMaximizeButton(DependencyObject element) => (bool)element.GetValue(SuppressMaximizeButtonProperty);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DwmBlurBehind pBlurBehind);

        // GWL_STYLE и флаги стилей окна для удаления системных кнопок заголовка.
        // WS_MAXIMIZEBOX в RemoveSystemCaptionButtons НЕ срезается — на нём держатся
        // Win+Up и штатная максимизация на рабочую область (issue #368).
        private const int GwlStyle = -16;
        private const int WsSysMenu = 0x00080000;
        private const int WsMinimizeBox = 0x00020000;
        private const int WsMaximizeBox = 0x00010000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        // Флаги SetWindowPos для принудительной перерисовки рамки после смены стиля.
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [StructLayout(LayoutKind.Sequential)]
        private struct DwmBlurBehind
        {
            public int dwFlags;
            public int fEnable;
            public IntPtr hRgnBlur;
            public int fTransitionOnMaximized;
        }

        // ---- WM_GETMINMAXINFO: максимизация строго на рабочую область (issue #368) ----

        private const int WmGetMinMaxInfo = 0x0024;

        // ---- Подавление максимизации (issue #3): WM_STYLECHANGING / WM_SYSCOMMAND ----
        // WM_STYLECHANGING: система собирается изменить стиль окна — снимаем WS_MAXIMIZEBOX
        // из предлагаемого стиля, чтобы максимизация не вернулась. WM_SYSCOMMAND/SC_MAXIMIZE:
        // команда разворота (Win+Up, системное меню) — игнорируется.
        private const int WmStyleChanging = 0x007C;
        private const int WmSysCommand = 0x0112;
        private const int ScMaximize = 0xF030;

        [StructLayout(LayoutKind.Sequential)]
        private struct StyleStruct
        {
            public int StyleOld;
            public int StyleNew;
        }

        /// <summary>MONITOR_DEFAULTTONEAREST — ближайший к окну монитор (если окно невидимо).</summary>
        private const uint MonitorDefaultToNearest = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct PointLt { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public PointLt Reserved;
            public PointLt MaxSize;
            public PointLt MaxPosition;
            public PointLt MinTrackSize;
            public PointLt MaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RectLt
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int CbSize;
            public RectLt RcMonitor;
            public RectLt RcWork;
            public int DwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

        /// <summary>
        /// Колбэк применения пользовательского шрифта интерфейса к окну (аудит UI
        /// 1.0.1.10, п. 1.9). Устанавливается из <c>App.OnStartup</c>: литералы
        /// <c>FontFamily="Segoe UI"</c> убраны из разметки окон, диалоги открываются
        /// с <c>FontSettings.FontFamily</c> из настроек, а не с фиксированным значением.
        /// </summary>
        public static Action<Window>? GlobalFontApplier { get; set; }

        /// <summary>
        /// Регистрирует обработчик класса <c>Window.Loaded</c>, который применяет
        /// «стеклянный» стиль ко всем окнам приложения. Вызывается один раз из
        /// <c>App.OnStartup</c> до показа первого окна.
        /// </summary>
        public static void RegisterGlobalWindowStyling()
        {
            EventManager.RegisterClassHandler(
                typeof(Window),
                Window.LoadedEvent,
                new RoutedEventHandler(OnWindowLoaded));
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            var window = (Window)sender;
            // Главное окно оформлено собственными средствами (WindowChrome, свои кнопки),
            // поэтому общий «стеклянный» стиль ему не нужен. Но гарантию минимального
            // размера (issue #55) оно получает тем же общим механизмом WM_GETMINMAXINFO,
            // что и все диалоги: иначе живой ресайз мышью сжимал бы главное окно ниже
            // MinWidth/MinHeight «почти в ноль».
            if (window is MainWindow)
            {
                HookMinTrackAndMaximize(window);
                return;
            }
            // Пользовательский шрифт интерфейса применяется к каждому диалогу
            // (аудит UI, п. 1.9); сбой не должен ломать показ окна.
            try { GlobalFontApplier?.Invoke(window); }
            catch { /* шрифт не критичен для показа диалога */ }
            if (IsApplied(window))
                return;
            try
            {
                Apply(window);
            }
            catch
            {
                // Оформление не должно ломать показ диалога.
            }
        }

        /// <summary>
        /// Применяет «стеклянный» стиль к конкретному окну (идемпотентно): рамка без
        /// системных кнопок, полупрозрачная подложка, acrylic/mica, скруглённые углы
        /// и собственные кнопки окна поверх содержимого.
        /// </summary>
        public static void Apply(Window window)
        {
            if (IsApplied(window))
                return;
            MarkApplied(window);

            // Собственный каркас окна вместо системного (аналог WindowChrome главного окна):
            // UseAeroCaptionButtons=false прячет системные кнопки, CaptionHeight=0 отключает
            // системную зону заголовка, ResizeBorderThickness оставляет невидимую рамку ресайза.
            WindowChrome.SetWindowChrome(window, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(-1),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });

            // Оборачиваем содержимое в полосу заголовка с кнопками «свернуть/закрыть».
            if (window.Content is UIElement inner)
            {
                // Сначала отсоединяем текущее содержимое окна, иначе WPF бросает
                // InvalidOperationException «элемент уже является логическим дочерним для
                // другого элемента» при добавлении inner в новую сетку BuildChrome.
                window.Content = null;
                window.Content = BuildChrome(window, inner);
            }

            // Полупрозрачная подложка пересчитывается при смене темы (ThemeManager заменяет
            // словари в Application.Resources). Подписку снимаем при закрытии окна.
            var themeChangedHandler = new NotifyCollectionChangedEventHandler((_, _) =>
            {
                try { window.Dispatcher.BeginInvoke(new Action(() => ApplyGlassBackground(window))); }
                catch { /* не блокируем смену темы */ }
            });
            if (Application.Current?.Resources is { } resources)
                ((INotifyCollectionChanged)resources.MergedDictionaries).CollectionChanged += themeChangedHandler;
            window.Closed += (_, _) =>
            {
                if (Application.Current?.Resources is { } res)
                    ((INotifyCollectionChanged)res.MergedDictionaries).CollectionChanged -= themeChangedHandler;
            };

            ApplyGlassBackground(window);
            ApplySystemBackdrop(window);
            ApplyCornerPreference(window);
            // Убираем системные кнопки заголовка (свернуть/развернуть/закрыть), которые DWM
            // может рисовать «призрачными» поверх расширенной стеклянной рамки.
            RemoveSystemCaptionButtons(window);

            // Подавление максимизации (issue #3): окно с SuppressMaximizeButton=true не
            // получает кнопку «развернуть» в собственной шапке и не разворачивается
            // системно (Win+Up, двойной клик по шапке). Ресайз сохраняется.
            if (GetSuppressMaximizeButton(window))
                HookSuppressMaximize(window);

            // Максимизация — строго на рабочую область монитора (без панели задач) + Win+Up
            // снова работает: WS_MAXIMIZEBOX сохранён (issue #368). Параллельно тот же
            // WM_GETMINMAXINFO зажимает ptMinTrackSize из MinWidth/MinHeight окна (issue #71):
            // система физически не даёт ужать диалог мышью ниже минимума даже при кастомном
            // WindowChrome/DPI, когда зажим на SizeChanged не успевает.
            HookMinTrackAndMaximize(window);

            // В развёрнутом виде скругление убираем и сбрасываем толщину стеклянной рамки.
            window.StateChanged += (_, _) =>
            {
                try
                {
                    ApplyCornerPreference(window);
                    UpdateGlassFrameForMaximize(window);
                }
                catch { /* некритично */ }
            };
        }

        /// <summary>
        /// Перехватывает WM_GETMINMAXINFO: при максимизации подставляет границы рабочей
        /// области ближайшего монитора (см. <see cref="WindowSizeMath.ComputeMaximizedBounds"/>),
        /// поэтому окно никогда не перекрывает панель задач — даже если системный стиль
        /// урезан или DWM ведёт себя нестандартно (issue #368). Параллельно зажимает
        /// ptMinTrackSize из MinWidth/MinHeight окна (issue #71): ОС физически не даёт
        /// ужать окно мышью ниже минимума, что не гарантирует зажим на SizeChanged
        /// (при кастомном WindowChrome или нестандартном DPI WPF может не перевести
        /// минимумы в трек-размер, и окно «ужимается до размера заголовка»).
        /// </summary>
        private static IntPtr MinTrackAndMaximizeWndProc(Window window, IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WmGetMinMaxInfo)
                return IntPtr.Zero;

            try
            {
                var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);

                // Максимум — рабочая область ближайшего монитора (issue #368).
                var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
                if (monitor != IntPtr.Zero)
                {
                    var mi = new MonitorInfo { CbSize = Marshal.SizeOf<MonitorInfo>() };
                    if (GetMonitorInfo(monitor, ref mi))
                    {
                        WindowSizeMath.ComputeMaximizedBounds(
                            (mi.RcMonitor.Left, mi.RcMonitor.Top, mi.RcMonitor.Right, mi.RcMonitor.Bottom),
                            (mi.RcWork.Left, mi.RcWork.Top, mi.RcWork.Right, mi.RcWork.Bottom),
                            out mmi.MaxPosition.X, out mmi.MaxPosition.Y,
                            out mmi.MaxSize.X, out mmi.MaxSize.Y);
                    }
                }

                // Минимум — из MinWidth/MinHeight окна (issue #71): DIP → физические пиксели.
                var scale = GetWindowDpiScale(window);
                var minTrackX = window.MinWidth > 0 ? (int)Math.Ceiling(window.MinWidth * scale.X) : 0;
                var minTrackY = window.MinHeight > 0 ? (int)Math.Ceiling(window.MinHeight * scale.Y) : 0;
                if (minTrackX > 0 && mmi.MinTrackSize.X < minTrackX)
                    mmi.MinTrackSize.X = minTrackX;
                if (minTrackY > 0 && mmi.MinTrackSize.Y < minTrackY)
                    mmi.MinTrackSize.Y = minTrackY;

                Marshal.StructureToPtr(mmi, lParam, true);
                handled = true;
            }
            catch
            {
                // Не критично: останется системное поведение ресайза/максимизации.
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Масштаб DPI окна (WPF DIP → физические пиксели) для пересчёта MinWidth/MinHeight
        /// в ptMinTrackSize WM_GETMINMAXINFO (issue #71). До показа окна возвращает 1.0.
        /// </summary>
        private static (double X, double Y) GetWindowDpiScale(Window window)
        {
            try
            {
                var dpi = VisualTreeHelper.GetDpi(window);
                return (dpi.DpiScaleX, dpi.DpiScaleY);
            }
            catch
            {
                return (1.0, 1.0);
            }
        }

        /// <summary>
        /// Ставит хук <see cref="MinTrackAndMaximizeWndProc"/> на окно (идемпотентно,
        /// безопасно до создания HWND — в этом случае просто не ставится).
        /// </summary>
        private static void HookMinTrackAndMaximize(Window window)
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                    return;
                System.Windows.Interop.HwndSource.FromHwnd(hwnd)
                    ?.AddHook((h, m, w, l, ref handled) => MinTrackAndMaximizeWndProc(window, h, m, w, l, ref handled));
            }
            catch
            {
                // Не критично: останется системное поведение максимизации.
            }
        }

        /// <summary>
        /// Подавляет максимизацию окна (issue #3): снимает WS_MAXIMIZEBOX со стиля окна
        /// и ставит хук <see cref="SuppressMaximizeWndProc"/>, чтобы Win+Up и системная
        /// максимизация не разворачивали окно. Ресайз (CanResize/CanResizeWithGrip)
        /// не затрагивается. Идемпотентно, безопасно до создания HWND.
        /// </summary>
        private static void HookSuppressMaximize(Window window)
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                    return;
                System.Windows.Interop.HwndSource.FromHwnd(hwnd)
                    ?.AddHook(SuppressMaximizeWndProc);

                // Снимаем WS_MAXIMIZEBOX сразу: без флага системная максимизация
                // (Win+Up, двойной клик по системному заголовку) не работает вовсе.
                int style = GetWindowLong(hwnd, GwlStyle);
                if ((style & WsMaximizeBox) != 0)
                {
                    SetWindowLong(hwnd, GwlStyle, style & ~WsMaximizeBox);
                    SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                        SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
                }
            }
            catch
            {
                // Не критично: кнопка максимизации уже не рисуется в собственной шапке.
            }
        }

        /// <summary>
        /// WndProc-хук подавления максимизации: не даёт системе вернуть WS_MAXIMIZEBOX
        /// (WM_STYLECHANGING) и игнорирует команду разворота (WM_SYSCOMMAND/SC_MAXIMIZE).
        /// Не пересекается с <see cref="MaximizeToWorkAreaWndProc"/> (issue #368): тот
        /// обрабатывает только WM_GETMINMAXINFO.
        /// </summary>
        private static IntPtr SuppressMaximizeWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmStyleChanging)
            {
                try
                {
                    var ss = Marshal.PtrToStructure<StyleStruct>(lParam);
                    ss.StyleNew &= ~WsMaximizeBox;
                    Marshal.StructureToPtr(ss, lParam, true);
                }
                catch
                {
                    // Не критично: максимизация подавлена и снятием флага в HookSuppressMaximize.
                }
                return IntPtr.Zero;
            }

            if (msg == WmSysCommand && (wParam.ToInt32() & 0xFFF0) == ScMaximize)
            {
                handled = true;
                return IntPtr.Zero;
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Снимает флаги стиля окна WS_SYSMENU / WS_MINIMIZEBOX, чтобы
        /// системные кнопки заголовка (свернуть/развернуть/закрыть) не рисовались DWM
        /// «призрачными» поверх расширенной стеклянной рамки (GlassFrameThickness=-1).
        /// <para>
        /// WS_MAXIMIZEBOX <b>оставляем</b>: именно на него опираются системный хоткей
        /// Win+Up и штатная максимизация на рабочую область. Собственные кнопки окна
        /// (свернуть/развернуть/закрыть) рисует сам WindowChromeHelper
        /// (UseAeroCaptionButtons=false), поэтому системные кнопки не нужны, а вот
        /// Win+Up без WS_MAXIMIZEBOX молча перестаёт работать (issue #368).
        /// </para>
        /// </summary>
        private static void RemoveSystemCaptionButtons(Window window)
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                    return;

                int style = GetWindowLong(hwnd, GwlStyle);
                style &= ~(WsSysMenu | WsMinimizeBox);
                SetWindowLong(hwnd, GwlStyle, style);

                // Принудительно перерисовываем рамку, чтобы изменения стиля применились сразу.
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
            }
            catch
            {
                // Не критично: останется штатное поведение WindowChrome.
            }
        }

        /// <summary>
        /// Оборачивает содержимое окна в сетку: сверху полоса заголовка (перетаскивание +
        /// кнопки окна), ниже — исходное содержимое диалога.
        /// </summary>
        private static UIElement BuildChrome(Window window, UIElement inner)
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var titleBar = BuildTitleBar(window);
            Grid.SetRow(titleBar, 0);
            Grid.SetRow(inner, 1);

            grid.Children.Add(titleBar);
            grid.Children.Add(inner);
            return grid;
        }

        /// <summary>
        /// Полоса заголовка: слева заголовок окна, справа собственные кнопки управления
        /// (свернуть, закрыть с красным выделением). Пустое место полосы таскает окно.
        /// </summary>
        private static UIElement BuildTitleBar(Window window)
        {
            var bar = new Border
            {
                Height = 34,
                // Полоса заголовка диалога заливается акцентным цветом темы на всю ширину.
                // HorizontalAlignment=Stretch и нулевые Margin гарантируют, что полоса занимает
                // всю ширину окна и не оставляет незалитых участков по краям (issue #135).
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0),
                // Скругляем два верхних угла с тем же радиусом, что и окно (DWM, Windows 11).
                // Иначе прямоугольная полоса не доходит до скруглённых углов окна и в углах
                // шапки просвечивает стеклянная подложка/рабочий стол — «недозалитые» углы.
                CornerRadius = new CornerRadius(DwmCornerRadius, DwmCornerRadius, 0, 0)
            };
            bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            bar.MouseLeftButtonDown += (_, e) =>
            {
                // Двойной клик по шапке — развернуть/восстановить (как в MainWindow,
                // issue #368: поведение шапки второстепенных окон должно совпадать
                // с главным). У окон с SuppressMaximizeButton=true (issue #3) двойной
                // клик окно не разворачивает — как и системная максимизация.
                if (e.ClickCount == 2)
                {
                    if (!GetSuppressMaximizeButton(window))
                        ToggleMaximize(window);
                    return;
                }

                if (window.WindowState != WindowState.Maximized)
                {
                    try { window.DragMove(); }
                    catch { /* иногда DWM не даёт начать перетаскивание */ }
                }
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = window.Title,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 0, 0),
                FontWeight = FontWeights.SemiBold
            };
            // Текст заголовка читаемым цветом на акцентной полосе (AccentTextBrush,
            // выбирается по WCAG-контрасту с фактическим акцентом — issue #26):
            // при тёмном пользовательском акценте заголовок белый, а не чёрный.
            title.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            // В диалоговых окнах показываем только кнопку «закрыть». У больших
            // изменяемых окон (например «Серверы 1С», issue #324) добавляем кнопку
            // «развернуть/восстановить» — ResizeMode по умолчанию CanResize. Окна
            // с SuppressMaximizeButton=true (issue #3) кнопку не получают, ресайз
            // при этом сохраняется.
            if (window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip &&
                !GetSuppressMaximizeButton(window))
                buttons.Children.Add(BuildMaximizeButton(window));
            buttons.Children.Add(BuildButton(window));

            Grid.SetColumn(title, 0);
            Grid.SetColumn(buttons, 1);
            grid.Children.Add(title);
            grid.Children.Add(buttons);

            bar.Child = grid;
            return bar;
        }

        /// <summary>
        /// Переключает окно между развёрнутым и нормальным состоянием (кнопка
        /// «развернуть/восстановить», двойной клик по шапке — issue #368).
        /// </summary>
        private static void ToggleMaximize(Window window)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        /// <summary>
        /// Кнопка «развернуть/восстановить» окна (issue #324, 0.3.12.7). Глиф
        /// переключается при смене WindowState (окно разворачивается/восстанавливается).
        /// </summary>
        private static Button BuildMaximizeButton(Window window)
        {
            // Стиль «на акценте»: полоса заголовка диалога всегда акцентная. Для
            // macOS-«светофора» (Задача 3) выбирается отдельный шаблон с зелёным кругом.
            var style = (Style?)window.TryFindResource(ThemeManager.WindowButtonStyleKey(close: false, onAccent: true))
                        ?? (Style?)window.TryFindResource(ThemeManager.WindowButtonStyleKey(close: false, onAccent: false));

            var path = new Path
            {
                Width = 14,
                Height = 14,
                Stretch = Stretch.Uniform,
                StrokeThickness = 1.6,
                StrokeLineJoin = PenLineJoin.Round,
                Stroke = new SolidColorBrush(Colors.Transparent)
            };
            // Глиф — ссылка на ресурс набора иконок темы (0.3.12.61): при смене темы
            // кнопка перерисовывается сама (GlyphWindowMaximize/Restore.<Набор>).
            path.SetResourceReference(Path.DataProperty,
                window.WindowState == WindowState.Maximized ? "GlyphWindowRestore" : "GlyphWindowMaximize");
            var button = new Button
            {
                Style = style,
                Content = path,
                ToolTip = LocalizationManager.T("Window.Maximize")
            };
            // «Светофор» macOS: зелёный круг «развернуть» задаётся кистью HoverBrush
            // кнопки (шаблон рисует круг всегда видимым, цвет — из этой кисти).
            // Ссылка на ресурс, а не снимок: кисть обновляется при смене темы.
            if (ThemeManager.CurrentWindowButtonStyle == WindowButtonStyle.MacOS)
                button.SetResourceReference(WindowChromeHelper.HoverBrushProperty, "WindowButtonMaximizeHoverBrush");
            path.SetBinding(Shape.StrokeProperty, new Binding(nameof(Button.Foreground)) { Source = button });
            button.Click += (_, _) => ToggleMaximize(window);
            window.StateChanged += (_, _) =>
            {
                bool maximized = window.WindowState == WindowState.Maximized;
                button.ToolTip = LocalizationManager.T(maximized ? "Window.Restore" : "Window.Maximize");
                path.SetResourceReference(Path.DataProperty,
                    maximized ? "GlyphWindowRestore" : "GlyphWindowMaximize");
            };
            return button;
        }

        /// <summary>
        /// Кнопка «закрыть» окна. Стиль WindowControlCloseButton (красное выделение)
        /// лежит в общих ресурсах приложения (App.xaml). В диалоговых окнах показывается
        /// только эта кнопка — без «свернуть» и «развернуть».
        /// </summary>
        private static Button BuildButton(Window window)
        {
            // Стиль «на акценте»: базовый значок ButtonTextBrush (читается на акцентной
            // полосе заголовка), красное hover-выделение и белый значок наследуются из
            // базового шаблона. Задаём цвет сеттером стиля, а не локальным значением —
            // иначе локальное значение (приоритет выше шаблонного триггера) «ломало» бы
            // белое выделение значка при наведении. Для macOS-«светофора» (Задача 3)
            // выбирается отдельный шаблон с красным кругом.
            var style = (Style?)window.TryFindResource(ThemeManager.WindowButtonStyleKey(close: true, onAccent: true))
                        ?? (Style?)window.TryFindResource(ThemeManager.WindowButtonStyleKey(close: true, onAccent: false));

            // Значок креста в координатном поле 14 на 14 (как в MainWindow.xaml):
            // округлённые концы и толщина 1.7 — выразительнее прежней тонкой обводки.
            var path = new Path
            {
                Width = 14,
                Height = 14,
                Stretch = Stretch.Uniform,
                StrokeThickness = 1.7,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            // Глиф «закрыть» из набора иконок темы (0.3.12.61), обновляется при смене темы.
            path.SetResourceReference(Path.DataProperty, "GlyphWindowClose");
            var button = new Button
            {
                Style = style,
                Content = path,
                ToolTip = LocalizationManager.T("Common.Close")
            };
            // Значок следует за цветом Foreground кнопки (тема + состояние hover/pressed).
            path.SetBinding(Shape.StrokeProperty, new Binding(nameof(Button.Foreground)) { Source = button });
            button.Click += (_, _) => window.Close();
            return button;
        }

        /// <summary>
        /// Полупрозрачная подложка окна: берём текущий цвет темы (ContentBackgroundBrush,
        /// обновляется для светлой/тёмной темы и любой цветовой схемы) и пересчитываем его
        /// с альфой ~0xE8. Именно эта подложка остаётся основным фоном, а размытие DWM
        /// добавляет эффект «стекла» сквозь прозрачные области.
        /// </summary>
        private static void ApplyGlassBackground(Window window)
        {
            if (window.TryFindResource("ContentBackgroundBrush") is SolidColorBrush brush)
            {
                var c = brush.Color;
                window.Background = new SolidColorBrush(Color.FromArgb(GlassBackgroundAlpha, c.R, c.G, c.B));
            }
        }

        /// <summary>
        /// Включает системный размытый фон: на Windows 11 — acrylic, при недоступности —
        /// mica; на старых Windows — blur-behind. Если ничего не удалось — полупрозрачный
        /// фон без размытия.
        /// </summary>
        private static void ApplySystemBackdrop(Window window)
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                    return;

                if (Environment.OSVersion.Version.Build >= 22000)
                {
                    int backdrop = DwmBackdropAcrylic;
                    if (DwmSetWindowAttribute(hwnd, DwmSystemBackdropType, ref backdrop, sizeof(int)) == 0)
                        return;

                    backdrop = DwmBackdropMica;
                    if (DwmSetWindowAttribute(hwnd, DwmSystemBackdropType, ref backdrop, sizeof(int)) == 0)
                        return;
                }

                var bb = new DwmBlurBehind { dwFlags = DwmBbEnable, fEnable = 1 };
                DwmEnableBlurBehindWindow(hwnd, ref bb);
            }
            catch
            {
                // Не блокируем: останется полупрозрачный фон без размытия.
            }
        }

        /// <summary>
        /// Скруглённые углы окна на уровне DWM (Windows 11). В развёрнутом состоянии углы
        /// обнуляются, чтобы в углах окна не просвечивал рабочий стол.
        /// </summary>
        private static void ApplyCornerPreference(Window window)
        {
            try
            {
                if (Environment.OSVersion.Version.Build < 22000)
                    return;

                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                    return;

                int pref = window.WindowState == WindowState.Maximized ? DwmCornerDoNotRound : DwmCornerRound;
                DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref pref, sizeof(int));
            }
            catch
            {
                // Игнорируем: скругление — некритичное улучшение.
            }
        }

        /// <summary>
        /// При максимизации возвращаем толщину стеклянной рамки к 0 (известное обходное
        /// решение для WindowChrome с GlassFrameThickness=-1 и панели задач Windows 11).
        /// </summary>
        private static void UpdateGlassFrameForMaximize(Window window)
        {
            try
            {
                var chrome = WindowChrome.GetWindowChrome(window);
                if (chrome is null)
                    return;
                chrome.GlassFrameThickness = window.WindowState == WindowState.Maximized
                    ? new Thickness(0)
                    : new Thickness(-1);
            }
            catch
            {
                // Игнорируем: если рамку не удалось поправить, окно всё равно работает.
            }
        }
    }
}
#endif