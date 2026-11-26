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
using System.Windows.Threading;
using Workspace1C.Localization;
using Workspace1C.Services;
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

        /// <summary>
        /// Радиус собственного скругления содержимого окон (issue #126), общий с
        /// Avalonia-слоем. Применяется, когда состояние DWM неизвестно (RDP/Windows 10),
        /// но композиция включена: рамка повторяет скругление содержимого, а не
        /// остаётся квадратной вокруг скруглённых углов.
        /// </summary>
        private const double FrameContentCornerRadius = WindowSizeMath.DefaultContentCornerRadius;

        // DWMWA_SYSTEMBACKDROP_TYPE (38): 2 = Mica, 3 = Acrylic.
        private const int DwmSystemBackdropType = 38;
        private const int DwmBackdropAcrylic = 3;
        private const int DwmBackdropMica = 2;

        // DWMWA_WINDOW_CORNER_PREFERENCE (33): 1 = не скруглять, 2 = скруглять.
        private const int DwmWindowCornerPreference = 33;
        private const int DwmCornerRound = 2;
        private const int DwmCornerDoNotRound = 1;

        // DWMWA_WINDOW_CORNER_STATE (34): фактическое состояние скругления углов окна,
        // которое DWM применил к окну. 1 = DWMWCS_DO_NOT_ROUND (в том числе у
        // максимизированных окон), 2 = DWMWCS_ROUNDED. Читается для синхронизации
        // скругления видимой рамки с системным скруглением (issue #126).
        private const int DwmWindowCornerState = 34;
        private const int DwmStateDoNotRound = 1;
        private const int DwmStateRounded = 2;

        // Сообщения, после которых DWM может пересоздать скругление углов окна
        // (issue #126): смена системной темы/схемы и смена режима композиции
        // (включение/выключение Aero, вход/выход из RDP). Рамка не должна
        // «застревать» в прежнем радиусе — пересчитываем её без перезапуска.
        private const int WmThemeChanged = 0x031A;
        private const int WmDwmCompositionChanged = 0x031E;

        // WM_SIZE: регион окна (issue #126) задаётся в координатах HWND, поэтому
        // после изменения размера его нужно пересоздать — иначе углы обрежутся
        // по прежнему размеру окна.
        private const int WmSize = 0x0005;

        // Задержка отложенного повтора пересчёта радиуса после показа окна:
        // один кадр (Dispatcher) закрывает обычный случай, ~200 мс — медленное
        // применение темы/композиции (холодный старт, RDP, виртуализация).
        private const int FrameCornerDeferredDelayMs = 200;

        // DWM_BB_ENABLE для DwmEnableBlurBehindWindow.
        private const int DwmBbEnable = 0x00000001;

        // Помечает окно оформленным, чтобы не оборачивать содержимое повторно.
        private static readonly DependencyProperty AppliedProperty =
            DependencyProperty.RegisterAttached(
                "Applied", typeof(bool), typeof(WindowChromeHelper), new PropertyMetadata(false));

        private static bool IsApplied(DependencyObject d) => (bool)d.GetValue(AppliedProperty);
        private static void MarkApplied(DependencyObject d) => d.SetValue(AppliedProperty, true);

        // Ссылки на элементы, скругление которых синхронизируется с системным
        // скруглением DWM (issue #126): видимый оверлей-рамка и полоса заголовка.
        // Хранятся как вложенные свойства, чтобы StateChanged-хук мог обновить
        // радиусы при максимизации/восстановлении окна без внешних словарей.
        private static readonly DependencyProperty FrameBorderProperty =
            DependencyProperty.RegisterAttached(
                "FrameBorder", typeof(Border), typeof(WindowChromeHelper), new PropertyMetadata(null));
        private static Border? GetFrameBorder(DependencyObject d) => (Border?)d.GetValue(FrameBorderProperty);
        private static void SetFrameBorder(DependencyObject d, Border? value) => d.SetValue(FrameBorderProperty, value);

        // Контейнер содержимого диалога (шапка + тело) со скруглением, совпадающим с
        // радиусом видимой рамки (issue #126): иначе при неизвестном DWM (RDP/Windows 10)
        // рамка скругляется по радиусу содержимого, а само содержимое остаётся
        // прямоугольным — «скруглённая рамка вокруг квадратных углов». Радиус и клип
        // синхронизируются в ApplyFrameCornerRadius вместе с рамкой.
        private static readonly DependencyProperty ContentBorderProperty =
            DependencyProperty.RegisterAttached(
                "ContentBorder", typeof(Border), typeof(WindowChromeHelper), new PropertyMetadata(null));
        private static Border? GetContentBorder(DependencyObject d) => (Border?)d.GetValue(ContentBorderProperty);
        private static void SetContentBorder(DependencyObject d, Border? value) => d.SetValue(ContentBorderProperty, value);

        private static readonly DependencyProperty TitleBarBorderProperty =
            DependencyProperty.RegisterAttached(
                "TitleBarBorder", typeof(Border), typeof(WindowChromeHelper), new PropertyMetadata(null));
        private static Border? GetTitleBarBorder(DependencyObject d) => (Border?)d.GetValue(TitleBarBorderProperty);
        private static void SetTitleBarBorder(DependencyObject d, Border? value) => d.SetValue(TitleBarBorderProperty, value);

        // Дополнительная полупрозрачная линия тени рядом с видимой рамкой (issue #126):
        // нужна только там, где DWM не рисует собственное ребро/тень (Windows 10/RDP,
        // выключенная композиция). Создаётся один раз на окно и хранится вложенным
        // свойством, чтобы не плодить оверлеи при каждом пересчёте.
        private static readonly DependencyProperty OuterShadowLineProperty =
            DependencyProperty.RegisterAttached(
                "OuterShadowLine", typeof(Border), typeof(WindowChromeHelper), new PropertyMetadata(null));
        private static Border? GetOuterShadowLine(DependencyObject d) => (Border?)d.GetValue(OuterShadowLineProperty);
        private static void SetOuterShadowLine(DependencyObject d, Border? value) => d.SetValue(OuterShadowLineProperty, value);

        // Состояние слежения за скруглением рамки конкретного окна (issue #126):
        // последний применённый радиус (чтобы не переустанавливать CornerRadius без
        // изменения — риск мерцания) и одноразовый таймер отложенного повтора после
        // показа окна. Хранится вложенным свойством: подписки/таймер умирают вместе
        // с окном, глобальных словарей нет.
        private static readonly DependencyProperty FrameTrackingProperty =
            DependencyProperty.RegisterAttached(
                "FrameTracking", typeof(FrameCornerTracking), typeof(WindowChromeHelper),
                new PropertyMetadata(null));

        private static FrameCornerTracking? GetFrameTracking(DependencyObject d)
            => (FrameCornerTracking?)d.GetValue(FrameTrackingProperty);
        private static void SetFrameTracking(DependencyObject d, FrameCornerTracking? value)
            => d.SetValue(FrameTrackingProperty, value);

        /// <summary>
        /// Состояние пересчёта радиуса рамки окна (issue #126): последний применённый
        /// радиус и таймер отложенного повтора. Не UI-элемент — только данные.
        /// </summary>
        private sealed class FrameCornerTracking
        {
            public double? LastRadius { get; set; }
            public bool Closed { get; set; }
            public DispatcherTimer? PendingTimer { get; set; }

            /// <summary>
            /// Последнее применённое визуальное решение рамки (issue #126): толщина,
            /// усиление контраста, линия тени. Нужно, чтобы не переустанавливать
            /// кисть/свойства на каждом Activated/DPI без реального изменения.
            /// </summary>
            public WindowFrameVisual? LastFrameVisual { get; set; }

            /// <summary>
            /// Фактические цвета рамки и фона, для которых посчитано последнее решение:
            /// при смене темы/схемы цвета меняются, и решение нужно пересчитать даже
            /// при том же самом наборе флагов.
            /// </summary>
            public (uint Frame, uint Background)? LastFrameColors { get; set; }

            /// <summary>Фактический контраст рамки к фону (для строки журнала frame-corner).</summary>
            public double? LastFrameContrast { get; set; }

            /// <summary>
            /// Скругление рисуем мы сами (policy=ContentRadius): фон окна должен быть
            /// прозрачным, а подложка темы — на контейнере содержимого (issue #126).
            /// Не зависит от успеха <c>SetWindowRgn</c>: если регион не сработал,
            /// прозрачный фон всё равно убирает «белые углы» (клип содержимого).
            /// </summary>
            public bool OwnRounding { get; set; }

            /// <summary>
            /// Регион окна (issue #126) применён: вне скругления фон окна прозрачный,
            /// а подложка темы лежит на контейнере содержимого. Нужно, чтобы не
            /// переключать фон зря и снимать регион только когда он реально был.
            /// </summary>
            public bool RegionApplied { get; set; }

            /// <summary>Последний применённый диаметр региона (физические пиксели).</summary>
            public int RegionDiameter { get; set; }

            /// <summary>Ширина окна, для которой построен регион (физические пиксели).</summary>
            public int RegionWidth { get; set; }

            /// <summary>Высота окна, для которой построен регион (физические пиксели).</summary>
            public int RegionHeight { get; set; }

            /// <summary>
            /// Предупреждение «показанное окно, но DWM не сообщил состояние скругления»
            /// уже записано — повторно не спамим на каждом Activated/DPI.
            /// </summary>
            public bool WarnedUnknownState { get; set; }

            public void StopTimer()
            {
                try
                {
                    PendingTimer?.Stop();
                }
                catch
                {
                    // Таймер уже мог быть остановлен/освобождён — не критично.
                }
                PendingTimer = null;
            }
        }

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

        /// <summary>
        /// Вложенное свойство: рисовать ли видимую рамку окна (issue #126). По умолчанию
        /// true — все диалоговые окна, как и главное окно (issue #273, MainWindow.xaml),
        /// получают тонкую рамку цвета темы (DynamicResource BorderBrushColor), чтобы
        /// окно оставалось различимым на системах без системных теней/эффектов DWM
        /// (терминал, виртуализация, некоторые темы ОС). Специальные окна (полноэкранные,
        /// просмотрщики изображений и т.п.) могут отключить рамку, установив false
        /// в конструкторе до показа.
        /// </summary>
        public static readonly DependencyProperty ShowWindowFrameProperty =
            DependencyProperty.RegisterAttached(
                "ShowWindowFrame", typeof(bool), typeof(WindowChromeHelper), new PropertyMetadata(true));
        public static void SetShowWindowFrame(DependencyObject element, bool value) => element.SetValue(ShowWindowFrameProperty, value);
        public static bool GetShowWindowFrame(DependencyObject element) => (bool)element.GetValue(ShowWindowFrameProperty);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        // Состояние композиции DWM (issue #126): при выключенных эффектах системного
        // скругления нет — рамка не должна скругляться «наугад».
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmIsCompositionEnabled(out bool pfEnabled);

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

        // Реальная видимость окна (WS_VISIBLE): до первого показа окно имеет HWND,
        // но ещё не видимо — состояние DWM в этот момент недостоверно (issue #126).
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        // SM_REMOTESESSION: сеанс удалённого рабочего стола (RDP). В удалённом сеансе
        // DWM не рисует ребро/тень окна, и рамка должна усиливаться по контрасту
        // (issue #126). Значение попадает и в строку журнала frame-corner.
        private const int SmRemoteSession = 0x1000;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private static bool IsRemoteSession()
        {
            try
            {
                return GetSystemMetrics(SmRemoteSession) != 0;
            }
            catch
            {
                // API недоступен — считаем сеанс локальным.
                return false;
            }
        }

        // Флаги SetWindowPos для принудительной перерисовки рамки после смены стиля.
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        // ---- Регион окна со скруглёнными углами (issue #126) ----
        // На Windows 10/RDP DWM не обрезает окно по скруглению: клип содержимого
        // обрезает только содержимое, а Window.Background заливал весь прямоугольник
        // HWND, и вне рамки оставались четыре треугольника фона темы («белые углы»).
        // Регион обрезает сам HWND по тому же радиусу, что и рамка/клип.
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(
            int nLeftRect, int nTopRect, int nRightRect, int nBottomRect,
            int nWidthEllipse, int nHeightEllipse);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RectLt lpRect);

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
                ResizeBorderThickness = window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip
                    ? new Thickness(6)
                    : new Thickness(0),
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
                try
                {
                    window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ApplyGlassBackground(window);
                        // Смена темы/схемы меняет BorderColor и ContentBackgroundColor:
                        // решение о контрасте рамки нужно пересчитать (issue #126).
                        if (GetFrameBorder(window) is { } themeFrame)
                            ApplyWindowFrameVisual(window, themeFrame);
                    }));
                }
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
            // Слежение за скруглением рамки (issue #126): состояние создаётся ДО первого
            // применения радиуса, чтобы пересчёт после показа/активации не переустанавливал
            // CornerRadius без реального изменения (иначе рамка мерцала бы).
            HookFrameCornerTracking(window);
            // Рамка и шапка повторяют системное скругление углов (issue #126):
            // читаем фактическое состояние DWM после применения CornerPreference.
            ApplyFrameCornerRadius(window);
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

            // Дефект «пустое поле внизу программы» (1.0.1.122): толщина стеклянной рамки
            // зависит не только от состояния окна, но и от его фактического положения
            // относительно рабочей области, поэтому пересчитываем её при изменении
            // размеров/позиции и после загрузки — как в главном окне. Issue #172: при
            // смене DPI (перенос между мониторами 100% ↔ 125% ↔ 150%) геометрия рамки
            // тоже устаревает — пересчитываем по фактическому DPI окна.
            window.SizeChanged += (_, _) => UpdateGlassFrameForMaximize(window);
            window.LocationChanged += (_, _) => UpdateGlassFrameForMaximize(window);
            window.DpiChanged += (_, _) => UpdateGlassFrameForMaximize(window);
            window.Loaded += (_, _) => UpdateGlassFrameForMaximize(window);
            UpdateGlassFrameForMaximize(window);

            // В развёрнутом виде скругление убираем и сбрасываем толщину стеклянной рамки.
            window.StateChanged += (_, _) =>
            {
                try
                {
                    ApplyCornerPreference(window);
                    // Рамка/шапка следуют за состоянием DWM (issue #126): при
                    // максимизации DWM рисует прямые углы — радиусы обнуляются,
                    // при восстановлении скругление возвращается.
                    ApplyFrameCornerRadius(window);
                    // В maximized-окне линия тени рамке не нужна (issue #126).
                    if (GetFrameBorder(window) is { } stateFrame)
                        ApplyWindowFrameVisual(window, stateFrame);
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

            // Видимая рамка окна (issue #126): у главного окна она есть (issue #273,
            // MainWindow.xaml — Border поверх всей клиентской области), а диалоговые
            // окна на системах без теней/эффектов DWM сливались с фоном. Рамка — тонкий
            // слой поверх всего окна (шапка + содержимое) цвета темы: DynamicResource
            // BorderBrushColor перекрашивается при смене темы/схемы так же, как у
            // главного окна. Слой не перехватывает мышь и не получает фокус. Окно может
            // отключить рамку вложенным свойством ShowWindowFrame=false.
            if (!GetShowWindowFrame(window))
                return grid;

            var root = new Grid();

            // Содержимое (шапка + тело) живёт в контейнере с тем же радиусом, что и
            // видимая рамка (issue #126): на Windows 10/RDP, где DWM не клипует окно,
            // скруглённая рамка вокруг прямоугольного содержимого выглядела бы как
            // «срезанные» углы. Радиус и клип синхронизируются в ApplyFrameCornerRadius.
            var contentHost = new Border
            {
                CornerRadius = new CornerRadius(FrameContentCornerRadius)
            };
            contentHost.Child = grid;
            contentHost.SizeChanged += (_, _) =>
                ApplyRoundedContentClip(contentHost, GetFrameTracking(window)?.LastRadius ?? 0);
            SetContentBorder(window, contentHost);
            ApplyRoundedContentClip(contentHost, FrameContentCornerRadius);
            root.Children.Add(contentHost);

            var frame = new Border
            {
                IsHitTestVisible = false,
                Focusable = false,
                BorderThickness = new Thickness(1),
                // Скругление рамки синхронизируется с системным скруглением углов окна
                // (issue #126): стартовое значение 0 — актуальный радиус выставит
                // ApplyFrameCornerRadius по фактическому состоянию DWM.
                CornerRadius = new CornerRadius(0)
            };
            frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrushColor");
            SetFrameBorder(window, frame);
            Panel.SetZIndex(frame, 2000);
            root.Children.Add(frame);
            // Видимость рамки (issue #126): если DWM не рисует ребро/тень, цвет
            // усиливается по WCAG-контрасту и добавляется линия тени. На Windows 11
            // остаётся прежняя привязка к BorderBrushColor — поведение не меняется.
            ApplyWindowFrameVisual(window, frame);
            return root;
        }

        /// <summary>
        /// Обрезает содержимое окна по скруглённому контуру (issue #126). WPF
        /// <see cref="Border"/> с CornerRadius сам содержимое не клипует, поэтому
        /// клип задаётся геометрией по фактическому размеру элемента. Радиус
        /// синхронизирован с радиусом видимой рамки; при прямых углах
        /// (<paramref name="radius"/> <= 0) клип снимается.
        /// </summary>
        private static void ApplyRoundedContentClip(FrameworkElement element, double radius)
        {
            try
            {
                if (radius <= 0 || element.ActualWidth <= 0 || element.ActualHeight <= 0)
                {
                    element.Clip = null;
                    return;
                }

                element.Clip = new RectangleGeometry(
                    new Rect(0, 0, element.ActualWidth, element.ActualHeight), radius, radius);
            }
            catch
            {
                // Клип — косметика: при сбое содержимое просто останется прямоугольным.
                element.Clip = null;
            }
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
            // Запоминаем полосу, чтобы ApplyFrameCornerRadius (issue #126) мог
            // обновить её скругление при максимизации/восстановлении окна: у
            // maximized-окна верхние углы прямые — «недозалитые» углы шапки недопустимы.
            SetTitleBarBorder(window, bar);
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
            UiAutomation.SetNameFromTip(button);
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
                UiAutomation.SetNameFromTip(button);
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
            UiAutomation.SetNameFromTip(button);
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
        /// <para>
        /// issue #126: подложка переносится на контейнер содержимого (<c>contentHost</c>),
        /// который обрезан по скруглению. Фон самого окна заливал весь прямоугольник HWND,
        /// и на Windows 10/RDP вне скругления оставались «белые углы». Когда скругление
        /// рисуем мы сами (policy=ContentRadius), фон окна становится прозрачным —
        /// страховка на случай, если регион не сработал; на Windows 11 поведение прежнее.
        /// </para>
        /// </summary>
        private static void ApplyGlassBackground(Window window)
        {
            if (window.TryFindResource("ContentBackgroundBrush") is not SolidColorBrush brush)
                return;

            var c = brush.Color;
            var glassColor = Color.FromArgb(GlassBackgroundAlpha, c.R, c.G, c.B);

            // Подложка содержимого: внутри скругления вид не меняется, но вне него
            // больше ничего не заливается (клип контейнера + прозрачный фон окна).
            if (GetContentBorder(window) is { } content)
            {
                if (content.Background is not SolidColorBrush contentBrush || contentBrush.Color != glassColor)
                    content.Background = new SolidColorBrush(glassColor);
            }

            bool ownRounding = GetFrameTracking(window)?.OwnRounding == true;
            var desired = ownRounding ? Colors.Transparent : glassColor;
            if (window.Background is not SolidColorBrush windowBrush || windowBrush.Color != desired)
                window.Background = new SolidColorBrush(desired);
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
        /// Синхронизирует скругление видимой рамки окна и полосы заголовка с фактическим
        /// системным скруглением углов DWM (issue #126). На Windows 11 DWM скругляет
        /// углы окна, и прямоугольная рамка «срезается» скруглённым клипом окна — линия
        /// по углам обрывается и выглядит неаккуратно. Рамке задаётся CornerRadius,
        /// совпадающий с системным радиусом (<see cref="DwmCornerRadius"/>), поэтому
        /// рамка рисуется ПО скруглению. Состояние читается из
        /// DWMWA_WINDOW_CORNER_STATE (фактическое значение DWM), при недоступности —
        /// оценка по версии ОС. Максимизированное окно DWM рисует с прямыми углами —
        /// радиусы рамки и шапки обнуляются. Если состояние DWM неизвестно (RDP/Windows
        /// 10), но композиция включена, рамка повторяет радиус собственного скруглённого
        /// содержимого (<see cref="WindowSizeMath.DefaultContentCornerRadius"/>), чтобы
        /// не оставаться квадратной вокруг скруглённых углов. Содержимое диалога
        /// (шапка + тело) скругляется и клипуется тем же радиусом, что и рамка, — рамка
        /// и содержимое совпадают. Окна с собственным каркасом (главное окно) и окна с
        /// <c>ShowWindowFrame=false</c> своего скругления не имеют: на Windows 10/RDP их
        /// рамка остаётся прямой (явный opt-out от «фантомного» скругления).
        /// </summary>
        private static void ApplyFrameCornerRadius(Window window)
            => ApplyFrameCornerRadius(window, "apply");

        /// <summary>
        /// Пересчитывает радиус рамки и верхних углов шапки по фактическому состоянию
        /// окна (issue #126). <paramref name="reason"/> попадает в диагностику, чтобы по
        /// журналу было видно, на каком событии радиус изменился (показ окна, смена
        /// темы/композиции, DPI, активация).
        /// <para>
        /// Решение принимает единая чистая политика
        /// <see cref="WindowSizeMath.ResolveFrameCornerRadius"/>: до показа окна состояние
        /// DWM недостоверно (часто DWMWCS_DO_NOT_ROUND) — используется оценка по версии ОС,
        /// а после показа радиус уточняется повторно. В RDP/Windows 10 (DWM не сообщает
        /// состояние) рамка повторяет радиус содержимого. Повторное присваивание
        /// CornerRadius выполняется только при реальном изменении радиуса — иначе рамка
        /// мерцала бы на каждом Activated.
        /// </para>
        /// </summary>
        private static void ApplyFrameCornerRadius(Window window, string reason)
        {
            try
            {
                bool maximized = window.WindowState == WindowState.Maximized;
                bool shown = IsWindowShown(window, out var hwnd);
                bool stateKnown = TryReadDwmCornerState(hwnd, out bool dwmRounded, out int cornerHr);
                bool osRounds = Environment.OSVersion.Version.Build >= 22000;
                bool compositionEnabled = TryGetDwmCompositionEnabled(out int compositionHr);
                // Радиус собственного скруглённого содержимого (issue #126): у диалогов
                // WPF содержимое обёрнуто в скруглённый контейнер (BuildChrome), поэтому
                // рамка может повторять его радиус. У окон с собственным каркасом
                // (главное окно) своего скругления нет — на Windows 10/RDP рамка
                // остаётся прямой, как и содержимое (нет «круглых углов с квадратной
                // рамкой» и наоборот).
                double contentRadius = GetContentBorder(window) is not null ? FrameContentCornerRadius : 0;
                // Единая политика скругления (issue #126): чистая функция, покрытая
                // тестами. В RDP/Windows 10 DWM не сообщает состояние — рамка
                // повторяет радиус собственного содержимого, а не остаётся квадратной.
                var policy = WindowSizeMath.ResolveFrameCornerRadius(new WindowSizeMath.FrameRoundingInputs(
                    DwmStateKnown: stateKnown,
                    DwmRounded: dwmRounded,
                    WindowShown: shown,
                    Maximized: maximized,
                    CompositionEnabled: compositionEnabled,
                    OsRoundsWindows: osRounds,
                    ContentCornerRadius: contentRadius,
                    SystemRadius: DwmCornerRadius));
                var radius = policy.Radius;

                var tracking = GetFrameTracking(window);

                // Регион окна (issue #126): клип содержимого обрезает только
                // содержимое, а фон окна заливал весь прямоугольник HWND — на
                // Windows 10/RDP DWM не обрезает окно по скруглению, и вне рамки
                // оставались четыре треугольника фона темы («белые углы»).
                // Когда скругление рисуем мы сами (policy=ContentRadius), HWND
                // обрезается тем же радиусом; на Windows 11 (DWM скругляет сам),
                // в maximized и при opt-out регион снимается, чтобы не отключать
                // системную тень/анимацию.
                var region = WindowSizeMath.ResolveWindowRegion(new WindowSizeMath.WindowRegionInputs(
                    Policy: policy.Policy,
                    RadiusDip: radius,
                    FrameOptOut: !GetShowWindowFrame(window),
                    DpiScale: GetWindowDpiScale(window).X));
                if (tracking is not null)
                    tracking.OwnRounding = region.Apply;
                bool regionApplied = ApplyWindowRegion(window, hwnd, region);
                // Страховка от «белых углов» (issue #126): подложка темы перенесена
                // на контейнер содержимого, а фон окна в режиме собственного
                // скругления прозрачный — даже если регион не сработал.
                if (GetContentBorder(window) is not null)
                    ApplyGlassBackground(window);
                double? previous = tracking?.LastRadius;
                bool changed = previous is null || Math.Abs(previous.Value - radius) > 0.01;
                if (changed)
                {
                    if (GetFrameBorder(window) is { } frame)
                        frame.CornerRadius = new CornerRadius(radius);
                    // Содержимое диалога скругляется тем же радиусом, что и рамка:
                    // на Windows 10/RDP DWM окно не клипует, и без этого скруглённая
                    // рамка обрамляла бы прямоугольное содержимое (issue #126).
                    if (GetContentBorder(window) is { } content)
                    {
                        content.CornerRadius = new CornerRadius(radius);
                        ApplyRoundedContentClip(content, radius);
                    }
                    // Полоса заголовка повторяет геометрию верхних углов окна (issue #135):
                    // при скруглённом окне — скруглённая (включая фолбэк RDP/Win10 по радиусу
                    // содержимого), при maximized/прямых углах — прямая.
                    if (GetTitleBarBorder(window) is { } bar)
                        bar.CornerRadius = new CornerRadius(radius, radius, 0, 0);
                    if (tracking != null)
                        tracking.LastRadius = radius;
                }

                // Видимость рамки (issue #126): при отсутствии системного ребра/тени
                // (Windows 10/RDP, выключенная композиция) усиливаем контраст рамки,
                // привязываем её к пикселям и добавляем линию тени. На Windows 11
                // политика возвращает прежнее поведение — привязку к BorderBrushColor.
                var frameVisual = GetFrameBorder(window) is { } visualFrame
                    ? ApplyWindowFrameVisual(window, visualFrame)
                    : default;
                double frameContrast = tracking?.LastFrameContrast ?? 0;

                LogFrameCornerRadius(window, reason, hwnd, shown, stateKnown, dwmRounded,
                    osRounds, compositionEnabled, contentRadius, maximized, radius, policy.Policy,
                    previous, changed, IsRemoteSession(), frameVisual, frameContrast,
                    regionApplied, region, cornerHr, compositionHr);
            }
            catch
            {
                // Скругление — некритичное улучшение: не ломаем показ окна.
            }
        }

        /// <summary>
        /// Применяет (или снимает) регион окна со скруглёнными углами (issue #126).
        /// Решение принимает чистая политика <see cref="WindowSizeMath.ResolveWindowRegion"/>:
        /// регион нужен только там, где скругление рисуем мы сами (Windows 10/RDP,
        /// выключенная композиция), и снимается на Windows 11 с системным скруглением,
        /// в maximized и при opt-out — иначе пропала бы системная тень DWM.
        /// <para>
        /// Регион задаётся в координатах HWND, поэтому пересоздаётся при изменении
        /// размера/DPI и не пересоздаётся, если диаметр и размер окна не изменились
        /// (иначе окно мигало бы на каждом Activated). Диаметр — в физических
        /// пикселях, <c>CreateRoundRectRgn</c> не включает правую/нижнюю границу,
        /// поэтому ширина/высота увеличены на 1 пиксель.
        /// </para>
        /// </summary>
        /// <returns><c>true</c>, если регион фактически применён.</returns>
        private static bool ApplyWindowRegion(
            Window window, IntPtr hwnd, WindowSizeMath.WindowRegionResult region)
        {
            try
            {
                var tracking = GetFrameTracking(window);
                if (hwnd == IntPtr.Zero)
                    return false;

                if (!region.Apply)
                {
                    // Windows 11 с системным скруглением, maximized, opt-out или
                    // прямые углы: регион снимаем (в т.ч. оставшийся от прежнего
                    // состояния окна), чтобы вернуть системную тень/анимацию.
                    if (tracking is { RegionApplied: true })
                        SetWindowRgn(hwnd, IntPtr.Zero, true);
                    if (tracking is not null)
                    {
                        tracking.RegionApplied = false;
                        tracking.RegionDiameter = 0;
                        tracking.RegionWidth = 0;
                        tracking.RegionHeight = 0;
                    }
                    return false;
                }

                if (!GetWindowRect(hwnd, out var rect))
                    return false;

                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;
                if (width <= 0 || height <= 0)
                    return false;

                // Регион уже построен для этого размера и диаметра — не пересоздаём.
                if (tracking is { RegionApplied: true } &&
                    tracking.RegionDiameter == region.DiameterPixels &&
                    tracking.RegionWidth == width &&
                    tracking.RegionHeight == height)
                {
                    return true;
                }

                // +1 по правой/нижней границе: CreateRoundRectRgn не включает её,
                // иначе срезался бы край окна (1 физический пиксель).
                IntPtr hrgn = CreateRoundRectRgn(
                    0, 0, width + 1, height + 1, region.DiameterPixels, region.DiameterPixels);
                if (hrgn == IntPtr.Zero)
                    return false;

                if (SetWindowRgn(hwnd, hrgn, true) == 0)
                {
                    // Система не забрала владение регионом — освобождаем сами,
                    // иначе утечка GDI-объекта на каждом пересчёте.
                    DeleteObject(hrgn);
                    return false;
                }

                // После успешного SetWindowRgn регион принадлежит системе —
                // DeleteObject для него не вызывается.
                if (tracking is not null)
                {
                    tracking.RegionApplied = true;
                    tracking.RegionDiameter = region.DiameterPixels;
                    tracking.RegionWidth = width;
                    tracking.RegionHeight = height;
                }
                return true;
            }
            catch
            {
                // Регион — страховка от «белых углов»: при сбое остаётся
                // прозрачный фон окна и клип содержимого.
                return false;
            }
        }

        /// <summary>
        /// Показано ли окно фактически (есть HWND, WPF-видимость и WS_VISIBLE).
        /// До первого показа состояние DWM недостоверно (issue #126).
        /// </summary>
        private static bool IsWindowShown(Window window, out IntPtr hwnd)
        {
            hwnd = IntPtr.Zero;
            try
            {
                hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                    return false;
                return window.IsVisible && IsWindowVisible(hwnd);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Читает фактическое состояние скругления углов окна у DWM
        /// (DWMWA_WINDOW_CORNER_STATE). Возвращает <c>true</c>, если DWM ответил
        /// известным состоянием (<c>DWMWCS_ROUNDED</c>/<c>DWMWCS_DO_NOT_ROUND</c>);
        /// иначе состояние неизвестно и решение принимает
        /// <see cref="WindowSizeMath.ResolveFrameCornerRadius"/> (радиус содержимого
        /// на RDP/Windows 10). На Windows 10 (build < 22000) атрибут не
        /// поддерживается — состояние всегда неизвестно, без ложного «do-not-round».
        /// </summary>
        private static bool TryReadDwmCornerState(IntPtr hwnd, out bool rounded, out int hresult)
        {
            rounded = false;
            hresult = 0;
            if (Environment.OSVersion.Version.Build < 22000 || hwnd == IntPtr.Zero)
                return false;
            try
            {
                // Код возврата DwmGetWindowAttribute не глушим: он попадает в строку
                // журнала frame-corner (dwmCornerHr=…), но сбой не ломает отрисовку.
                hresult = DwmGetWindowAttribute(hwnd, DwmWindowCornerState, out int state, sizeof(int));
                if (hresult != 0)
                    return false;
                if (state == DwmStateRounded)
                {
                    rounded = true;
                    return true;
                }
                if (state == DwmStateDoNotRound)
                {
                    rounded = false;
                    return true;
                }
            }
            catch
            {
                // Не удалось прочитать состояние — решение примет оценка по версии ОС.
            }
            return false;
        }

        /// <summary>
        /// Включена ли композиция DWM (issue #126). На Windows 8+ композиция включена
        /// всегда, но в RDP/при отключённых эффектах она может быть недоступна — тогда
        /// системного скругления нет и рамку не нужно скруглять «наугад». Если API
        /// недоступен, считаем композицию включённой (современные ОС).
        /// </summary>
        private static bool TryGetDwmCompositionEnabled()
            => TryGetDwmCompositionEnabled(out _);

        /// <summary>
        /// Включена ли композиция DWM, с кодом возврата API (issue #126): код пишется
        /// в журнал frame-corner (dwmCompositionHr=…), но сбой не ломает отрисовку —
        /// при недоступном API считаем композицию включённой (современные ОС).
        /// </summary>
        private static bool TryGetDwmCompositionEnabled(out int hresult)
        {
            hresult = 0;
            try
            {
                hresult = DwmIsCompositionEnabled(out bool enabled);
                if (hresult == 0)
                    return enabled;
            }
            catch
            {
                // API недоступен — на поддерживаемых ОС композиция включена.
            }
            return true;
        }

        /// <summary>
        /// Пишет в журнал (категория <see cref="LogCategory.Themes"/>) диагностику
        /// скругления рамки: тип окна, наличие HWND, фактическое состояние DWM,
        /// состояние композиции, радиус содержимого, вычисленный радиус, причину
        /// пересчёта и принятую политику (issue #126). При выключенной категории
        /// INFO-запись не пишется; предупреждение — только при реальной проблеме:
        /// показанное окно на Windows 11, но DWM не сообщил состояние, либо поздний
        /// пересчёт изменил радиус (исходный дефект — рамка была рассчитана до показа).
        /// </summary>
        private static void LogFrameCornerRadius(
            Window window, string reason, IntPtr hwnd, bool shown, bool stateKnown, bool dwmRounded,
            bool osRounds, bool compositionEnabled, double contentRadius, bool maximized, double radius,
            WindowSizeMath.FrameCornerPolicy policy, double? previous, bool changed,
            bool remote, WindowFrameVisual frameVisual, double frameContrast,
            bool regionApplied, WindowSizeMath.WindowRegionResult region,
            int cornerHr, int compositionHr)
        {
            try
            {
                var message =
                    $"frame-corner reason={reason} window={window.GetType().Name} " +
                    $"hwnd=0x{hwnd.ToInt64():X} shown={shown} " +
                    $"dwmState={(stateKnown ? (dwmRounded ? "rounded" : "do-not-round") : "unknown")} " +
                    $"osRounds={osRounds} compositionEnabled={compositionEnabled} " +
                    $"dwmCornerHr=0x{cornerHr:X8} dwmCompositionHr=0x{compositionHr:X8} " +
                    $"contentRadius={contentRadius:0.#} maximized={maximized} " +
                    $"radius={radius:0.#} policy={policy} " +
                    $"previous={(previous.HasValue ? previous.Value.ToString("0.#") : "-")} " +
                    $"changed={changed} remote={remote} " +
                    $"frameThickness={frameVisual.ThicknessDip:0.#} " +
                    $"frameContrast={frameContrast:0.00} " +
                    $"frameStrengthened={frameVisual.StrengthenContrast} " +
                    $"frameShadowLine={frameVisual.AddOuterShadowLine} " +
                    $"regionApplied={regionApplied} " +
                    $"regionRadius={(regionApplied ? radius : 0):0.#} " +
                    $"regionDiameter={(regionApplied ? region.DiameterPixels : 0)} " +
                    $"frameOptOut={!GetShowWindowFrame(window)}";

                if (Log.Info(LogCategory.Themes, message))
                    return;

                // Диагностика проблемы должна попадать в журнал и при выключенных
                // категориях. Предупреждаем только о реальных аномалиях и не чаще
                // одного раза на окно — иначе Activated/DPI спамили бы журнал:
                //  * показанное окно, но DWM не сообщил состояние (RDP/нет композиции);
                //  * поздний пересчёт изменил радиус (исходный дефект #126 — рамка была
                //    рассчитана до показа окна). Штатные переходы Apply/StateChanged
                //    (максимизация, восстановление) сюда не входят.
                var tracking = GetFrameTracking(window);
                bool lateReason = reason is not "apply" and not "state-changed";
                // На Windows 10 атрибут состояния DWM не поддерживается — это норма,
                // а не аномалия: предупреждаем только на Windows 11 (osRounds).
                bool unknownStateFirstTime = osRounds && shown && !maximized && !stateKnown &&
                                             tracking is { WarnedUnknownState: false };
                if (unknownStateFirstTime)
                    tracking!.WarnedUnknownState = true;
                bool anomaly = unknownStateFirstTime ||
                               (shown && changed && previous.HasValue && lateReason);
                if (anomaly)
                    AppServices.TryGetService<IAppLogger>()?.Warn($"[{LogCategory.Themes}] {message}");
            }
            catch
            {
                // Логирование не должно ломать показ окна.
            }
        }

        /// <summary>
        /// Применяет видимость рамки окна (issue #126). Если DWM рисует собственное
        /// ребро/тень (Windows 11), остаётся прежняя привязка рамки к ресурсу темы
        /// <c>BorderBrushColor</c> — поведение Windows 11 не меняется. Иначе цвет
        /// рамки поднимается до WCAG-контраста ≥ 3:1 к фону содержимого, рамка
        /// привязывается к пикселям, а рядом добавляется полупрозрачная линия тени.
        /// Решение принимает чистая политика <see cref="WindowFramePolicy.Resolve"/>.
        /// </summary>
        private static WindowFrameVisual ApplyWindowFrameVisual(Window window, Border frame)
        {
            try
            {
                var (frameColor, backgroundColor) = ReadFrameThemeColors(window);
                var hwnd = TryGetWindowHandle(window);
                bool stateKnown = TryReadDwmCornerState(hwnd, out bool dwmRounded, out _);
                var inputs = new WindowFrameInputs(
                    CompositionEnabled: TryGetDwmCompositionEnabled(),
                    OsRoundsWindows: Environment.OSVersion.Version.Build >= 22000,
                    DwmStateKnown: stateKnown,
                    DwmRounded: dwmRounded,
                    Maximized: window.WindowState == WindowState.Maximized,
                    RemoteSession: IsRemoteSession(),
                    DpiScale: GetWindowDpiScale(window).X);
                var visual = WindowFramePolicy.Resolve(inputs);

                var tracking = GetFrameTracking(window);
                var colors = (Frame: PackColor(frameColor), Background: PackColor(backgroundColor));
                if (tracking is { LastFrameVisual: { } lastVisual, LastFrameColors: { } lastColors } &&
                    lastVisual == visual && lastColors == colors)
                {
                    // Решение и цвета не изменились — не переустанавливаем кисть,
                    // иначе рамка мерцала бы на каждом Activated/DPI.
                    return visual;
                }

                if (visual.StrengthenContrast)
                {
                    var strengthened = WindowFramePolicy.EnsureContrast(
                        frameColor, backgroundColor, WindowFramePolicy.MinContrastRatio);
                    frame.BorderBrush = new SolidColorBrush(
                        Color.FromRgb(strengthened.R, strengthened.G, strengthened.B));
                }
                else
                {
                    // Поведение Windows 11: рамка перекрашивается из темы (issue #126).
                    frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrushColor");
                }

                frame.BorderThickness = new Thickness(visual.ThicknessDip);
                frame.SnapsToDevicePixels = visual.SnapToPixels;
                frame.UseLayoutRounding = visual.SnapToPixels;
                UpdateOuterShadowLine(window, frame, visual.AddOuterShadowLine);

                if (tracking is not null)
                {
                    tracking.LastFrameVisual = visual;
                    tracking.LastFrameColors = colors;
                    tracking.LastFrameContrast = WindowFramePolicy.ContrastRatio(
                        visual.StrengthenContrast
                            ? WindowFramePolicy.EnsureContrast(
                                frameColor, backgroundColor, WindowFramePolicy.MinContrastRatio)
                            : frameColor,
                        backgroundColor);
                }

                return visual;
            }
            catch
            {
                // Видимость рамки — некритичное улучшение: не ломаем показ окна.
                return default;
            }
        }

        /// <summary>Фактические цвета рамки и фона содержимого из ресурсов темы (issue #126).</summary>
        private static (WindowFrameColor Frame, WindowFrameColor Background) ReadFrameThemeColors(Window window)
        {
            var frame = ReadColorResource(window, "BorderColor");
            if (frame is null && window.TryFindResource("BorderBrushColor") is SolidColorBrush frameBrush)
                frame = frameBrush.Color;

            var background = ReadColorResource(window, "ContentBackgroundColor");
            if (background is null && window.TryFindResource("ContentBackgroundBrush") is SolidColorBrush backgroundBrush)
                background = backgroundBrush.Color;

            return (
                ToFrameColor(frame ?? Color.FromRgb(0xE2, 0xE8, 0xF0)),
                ToFrameColor(background ?? Color.FromRgb(0xF1, 0xF5, 0xF9)));
        }

        private static Color? ReadColorResource(Window window, string key)
            => window.TryFindResource(key) is Color color ? color : null;

        private static WindowFrameColor ToFrameColor(Color color) => new(color.R, color.G, color.B);

        private static uint PackColor(WindowFrameColor color)
            => ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;

        private static IntPtr TryGetWindowHandle(Window window)
        {
            try
            {
                return new System.Windows.Interop.WindowInteropHelper(window).Handle;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Дополнительная полупрозрачная линия тени рядом с рамкой (issue #126):
        /// создаётся один раз и только когда DWM не рисует собственное ребро/тень.
        /// Линия не перехватывает мышь (ZIndex 1999 — под видимой рамкой) и
        /// скрывается в maximized-окне, чтобы не «утолщать» рамку у краёв экрана.
        /// </summary>
        private static void UpdateOuterShadowLine(Window window, Border frame, bool visible)
        {
            try
            {
                var line = GetOuterShadowLine(window);
                if (line is null)
                {
                    if (!visible)
                        return;
                    if (frame.Parent is not Panel host)
                        return;
                    line = new Border
                    {
                        IsHitTestVisible = false,
                        Focusable = false,
                        // Линия рисуется сразу под видимой рамкой (на 1 DIP внутрь),
                        // поэтому остаётся видимой на любом DPI и не обрезается
                        // границей клиентской области окна.
                        BorderThickness = new Thickness(1),
                        Margin = new Thickness(1),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0x00, 0x00))
                    };
                    SetOuterShadowLine(window, line);
                    Panel.SetZIndex(line, 1999);
                    host.Children.Add(line);
                }

                line.CornerRadius = frame.CornerRadius;
                line.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                // Линия тени — косметика: при сбое остаётся усиленная рамка.
            }
        }

        /// <summary>
        /// Подписывается на события окна, после которых радиус рамки мог устареть
        /// (issue #126): <c>ContentRendered</c> (окно реально показано),
        /// <c>Activated</c>, <c>DpiChanged</c> и сообщения DWM о смене темы/композиции.
        /// Дополнительно ставит отложенный повтор (1 кадр и ~200 мс), чтобы поймать
        /// медленное применение скругления DWM (холодный старт, RDP, виртуализация).
        /// Идемпотентно: повторный вызов не дублирует подписки.
        /// </summary>
        private static void HookFrameCornerTracking(Window window)
        {
            if (GetFrameTracking(window) is not null)
                return;

            var tracking = new FrameCornerTracking();
            SetFrameTracking(window, tracking);

            try
            {
                window.ContentRendered += (_, _) => ScheduleFrameCornerRadiusRefresh(window);
                window.Activated += (_, _) => RefreshFrameCornerRadius(window, "activated");
                window.DpiChanged += (_, _) => RefreshFrameCornerRadius(window, "dpi-changed");
                window.Closed += (_, _) =>
                {
                    tracking.Closed = true;
                    tracking.StopTimer();
                };
            }
            catch
            {
                // Подписки — диагностическое улучшение: сбой не ломает окно.
            }

            HookFrameCornerMessages(window);

            // Окно могло быть показано до применения стиля (например, главное окно,
            // оформленное собственными средствами): планируем повтор сразу.
            if (window.IsVisible)
                ScheduleFrameCornerRadiusRefresh(window);
        }

        /// <summary>
        /// Ставит хук HwndSource на WM_THEMECHANGED и WM_DWMCOMPOSITIONCHANGED:
        /// после смены системной темы или режима композиции (включение/выключение
        /// эффектов, вход/выход из RDP) DWM может пересоздать скругление — рамка
        /// должна обновиться без перезапуска приложения (issue #126). Обработка
        /// откладывается через Dispatcher, чтобы не трогать визуал внутри WndProc.
        /// </summary>
        private static void HookFrameCornerMessages(Window window)
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                    return;
                System.Windows.Interop.HwndSource.FromHwnd(hwnd)?.AddHook((_, msg, _, _, ref handled) =>
                {
                    // Регион окна (issue #126) задан в координатах HWND: после
                    // изменения размера его нужно пересоздать, иначе углы обрежутся
                    // по прежнему размеру окна. Пересчёт — только если регион реально
                    // применён (на Windows 11/maximized он не нужен).
                    if (msg == WmSize)
                    {
                        if (GetFrameTracking(window) is { RegionApplied: true })
                        {
                            try
                            {
                                window.Dispatcher.BeginInvoke(DispatcherPriority.Background,
                                    new Action(() => RefreshFrameCornerRadius(window, "size-changed")));
                            }
                            catch
                            {
                                // Окно закрывается — пересчёт не нужен.
                            }
                        }
                        return IntPtr.Zero;
                    }

                    if (msg == WmThemeChanged || msg == WmDwmCompositionChanged)
                    {
                        var reason = msg == WmThemeChanged ? "theme-changed" : "composition-changed";
                        try
                        {
                            window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                            {
                                ApplyCornerPreference(window);
                                RefreshFrameCornerRadius(window, reason);
                                // Смена темы/композиции влияет и на видимость рамки
                                // (issue #126): пересчитываем контраст вместе с радиусом.
                                if (GetFrameBorder(window) is { } reasonFrame)
                                    ApplyWindowFrameVisual(window, reasonFrame);
                            }));
                        }
                        catch
                        {
                            // Окно уже закрывается — пересчёт не нужен.
                        }
                    }
                    return IntPtr.Zero;
                });
            }
            catch
            {
                // Хук недоступен — останутся пересчёты по ContentRendered/Activated.
            }
        }

        /// <summary>
        /// Планирует отложенный пересчёт радиуса после показа окна (issue #126):
        /// один кадр (Dispatcher) и повтор через ~200 мс. Повтор одноразовый и
        /// выполняется только если окно ещё открыто.
        /// </summary>
        private static void ScheduleFrameCornerRadiusRefresh(Window window)
        {
            try
            {
                window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                    RefreshFrameCornerRadius(window, "content-rendered")));
            }
            catch
            {
                // Dispatcher недоступен (окно закрывается) — не критично.
            }

            try
            {
                var tracking = GetFrameTracking(window);
                if (tracking is null || tracking.Closed)
                    return;
                tracking.StopTimer();
                var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(FrameCornerDeferredDelayMs)
                };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    if (tracking.PendingTimer == timer)
                        tracking.PendingTimer = null;
                    RefreshFrameCornerRadius(window, "deferred");
                };
                tracking.PendingTimer = timer;
                timer.Start();
            }
            catch
            {
                // Таймер — страховка: без него останется пересчёт по ContentRendered.
            }
        }

        /// <summary>
        /// Пересчитывает радиус рамки, если окно ещё открыто (issue #126).
        /// </summary>
        private static void RefreshFrameCornerRadius(Window window, string reason)
        {
            try
            {
                if (GetFrameTracking(window) is { Closed: true })
                    return;
                ApplyFrameCornerRadius(window, reason);
            }
            catch
            {
                // Скругление — некритичное улучшение.
            }
        }

        /// <summary>
        /// Подключает синхронизацию скругления рамки для окна, оформленного
        /// собственными средствами (главное окно: <see cref="Apply"/> для него не
        /// вызывается). Задаёт элементы рамки и запускает тот же механизм
        /// пересчёта после показа/смены темы/композиции (issue #126).
        /// Идемпотентно: повторный вызов только обновляет ссылки на элементы.
        /// </summary>
        /// <param name="window">Окно, которому принадлежит рамка.</param>
        /// <param name="frame">Видимый оверлей-рамка окна (Border поверх содержимого).</param>
        /// <param name="titleBar">Полоса заголовка (необязательно) — скругляются верхние углы.</param>
        public static void AttachFrameCornerTracking(Window window, Border frame, Border? titleBar = null)
        {
            if (window is null || frame is null)
                return;
            SetFrameBorder(window, frame);
            if (titleBar is not null)
                SetTitleBarBorder(window, titleBar);

            bool firstHook = GetFrameTracking(window) is null;
            HookFrameCornerTracking(window);
            if (firstHook)
            {
                try
                {
                    // У окон вне Apply нет общего StateChanged-хука: при максимизации
                    // DWM рисует прямые углы — радиус должен обнулиться (issue #126).
                    window.StateChanged += (_, _) => RefreshFrameCornerRadius(window, "state-changed");
                }
                catch
                {
                    // Подписка — улучшение: сбой не ломает окно.
                }
            }

            ApplyFrameCornerRadius(window, "attach");
            // Видимость рамки главного окна (issue #126): та же политика, что у диалогов.
            ApplyWindowFrameVisual(window, frame);
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
                // Код возврата DwmSetWindowAttribute не глушим (issue #126): он пишется
                // в журнал, чтобы по диагностике было видно, применилось ли скругление.
                int hresult = DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref pref, sizeof(int));
                Log.Info(LogCategory.Themes,
                    $"frame-corner dwm-set window={window.GetType().Name} " +
                    $"hwnd=0x{hwnd.ToInt64():X} cornerPreference={pref} hr=0x{hresult:X8}");
            }
            catch
            {
                // Игнорируем: скругление — некритичное улучшение.
            }
        }

        /// <summary>
        /// Полная геометрия рамки по фактическому положению окна и его DPI — та же
        /// политика, что у главного окна (issue #172, дефект «пустое поле внизу
        /// программы» 1.0.1.122). Развёрнутое окно при любом масштабе (100%, 125%, 150%)
        /// получает нулевую стеклянную рамку, нулевую рамку ресайза и прямые углы —
        /// иначе на HiDPI <c>ResizeBorderThickness</c> (6 DIP) пересчитывался в
        /// физические пиксели, и вокруг максимизированного окна оставалась прозрачная
        /// полоса. Окно, низ которого прижат к нижней границе рабочей области (на всю
        /// её высоту), — тоже 0; иначе — -1 (acrylic сохраняется). Прижатие только
        /// к верхней границе рамку НЕ обнуляет (регрессия #170). Расчёт — в чистой
        /// <see cref="WindowGlassFramePolicy.ResolveState"/>.
        /// </summary>
        private static void UpdateGlassFrameForMaximize(Window window)
        {
            try
            {
                var chrome = WindowChrome.GetWindowChrome(window);
                if (chrome is null)
                    return;

                var workArea = WindowWorkAreaLimiter.GetWorkAreaDip(window);
                var work = workArea is { } area
                    ? (area.Left, area.Top, area.Width, area.Height)
                    : (double.NaN, double.NaN, double.NaN, double.NaN);

                var state = WindowGlassFramePolicy.ResolveState(
                    window.WindowState == WindowState.Maximized,
                    (window.Left, window.Top, window.ActualWidth, window.ActualHeight),
                    work,
                    GetWindowDpiScale(window).X);

                var glass = new Thickness(state.GlassFrameThickness);
                if (chrome.GlassFrameThickness != glass)
                    chrome.GlassFrameThickness = glass;

                var resize = new Thickness(state.ResizeBorderThickness);
                if (chrome.ResizeBorderThickness != resize)
                    chrome.ResizeBorderThickness = resize;

                if (state.CornerRadiusOverride is { } corner)
                {
                    var radius = new CornerRadius(corner);
                    if (chrome.CornerRadius != radius)
                        chrome.CornerRadius = radius;
                }
            }
            catch
            {
                // Игнорируем: если рамку не удалось поправить, окно всё равно работает.
            }
        }
    }
}
#endif
