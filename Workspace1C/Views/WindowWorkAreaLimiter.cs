#if WINDOWS
using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Workspace1C.Services;

namespace Workspace1C
{
    /// <summary>
    /// Issue #119 (1.0.1.119): единая точка ограничения размеров WPF-окон рабочей
    /// областью монитора. Регистрируется глобально в <see cref="App.OnStartup"/>,
    /// поэтому покрывает и окна с XAML, и окна, собранные кодом через
    /// <see cref="WpfUi.SetupWindow"/>.
    /// <para>
    /// Логика расчёта — чистый <see cref="WindowBoundsPolicy"/>; здесь только
    /// определение монитора (по HWND, а не по основному экрану), перевод рабочей
    /// области в DIP и применение размеров/позиции.
    /// </para>
    /// </summary>
    public static class WindowWorkAreaLimiter
    {
        private static readonly ConditionalWeakTable<Window, object> Attached = new();
        private static readonly object Marker = new();

        /// <summary>
        /// Глобальная регистрация: при загрузке любого окна применяется ограничение
        /// по рабочей области. Идемпотентна — повторный вызов не дублирует обработчик.
        /// </summary>
        public static void RegisterGlobal()
        {
            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnWindowLoaded));
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Window window)
                Attach(window);
        }

        /// <summary>
        /// Применяет ограничение к конкретному окну. Повторный вызов для того же окна
        /// ничего не делает (защита от двойной подписки).
        /// </summary>
        public static void Attach(Window window)
        {
            if (window is null)
                return;

            if (Attached.TryGetValue(window, out _))
                return;

            try
            {
                Attached.Add(window, Marker);
            }
            catch (ArgumentException)
            {
                // Окно уже добавлено параллельным вызовом — второй раз не подписываемся.
                return;
            }

            window.Loaded += (_, _) => Apply(window);
            window.SourceInitialized += (_, _) => Apply(window);
            // Issue #171/#176: ограничения стартового размера (рабочая область минус
            // отступ) не должны действовать на развёрнутое окно. WPF учитывает
            // MaxWidth/MaxHeight при максимизации, из-за чего окно разворачивалось
            // «не до конца» — справа и снизу оставались белые полосы.
            window.StateChanged += (_, _) => OnStateChanged(window);
            Apply(window);
        }

        /// <summary>
        /// Реакция на смену состояния окна (issue #171/#176): при развороте снимаем
        /// ограничения максимума, при возврате в обычное состояние — применяем снова.
        /// </summary>
        private static void OnStateChanged(Window window)
        {
            try
            {
                if (window.WindowState == WindowState.Maximized)
                {
                    ClearSizeConstraints(window);
                    return;
                }

                if (window.WindowState == WindowState.Normal)
                    Apply(window);
            }
            catch
            {
                // Геометрия окна — вспомогательная возможность.
            }
        }

        /// <summary>
        /// Снимает ограничения максимума, наложенные по рабочей области
        /// (<see cref="WindowBoundsPolicy"/> вычитает отступ). Без этого
        /// максимизированное окно не заполняет экран (issue #171/#176).
        /// </summary>
        private static void ClearSizeConstraints(Window window)
        {
            window.MaxWidth = double.PositiveInfinity;
            window.MaxHeight = double.PositiveInfinity;
        }

        private static void Apply(Window window)
        {
            try
            {
                // Issue #171/#176: развёрнутое окно ограничивать максимумом нельзя —
                // его границы задаёт оконный менеджер, а MaxWidth/MaxHeight (рабочая
                // область минус отступ) оставляли полосы справа и снизу.
                if (window.WindowState == WindowState.Maximized)
                {
                    ClearSizeConstraints(window);
                    return;
                }

                if (window.WindowState != WindowState.Normal)
                    return;

                var area = GetWorkAreaDip(window);
                if (area is not { } work || work.Width <= 0 || work.Height <= 0)
                    return;

                var designWidth = window.Width;
                if (double.IsNaN(designWidth) || designWidth <= 0)
                    designWidth = window.ActualWidth > 0 ? window.ActualWidth : window.MinWidth;

                var designHeight = window.Height;
                if (double.IsNaN(designHeight) || designHeight <= 0)
                    designHeight = window.ActualHeight > 0 ? window.ActualHeight : window.MinHeight;

                var bounds = WindowBoundsPolicy.Recommend(
                    designWidth,
                    designHeight,
                    window.MinWidth,
                    window.MinHeight,
                    work.Width,
                    work.Height);

                if (!double.IsPositiveInfinity(bounds.MaxWidth) && bounds.MaxWidth > 0)
                    window.MaxWidth = bounds.MaxWidth;
                if (!double.IsPositiveInfinity(bounds.MaxHeight) && bounds.MaxHeight > 0)
                    window.MaxHeight = bounds.MaxHeight;

                // Минимум не должен превышать максимум, иначе WPF отдаст приоритет
                // минимуму и окно всё равно выйдет за экран.
                if (window.MinWidth > window.MaxWidth)
                    window.MinWidth = window.MaxWidth;
                if (window.MinHeight > window.MaxHeight)
                    window.MinHeight = window.MaxHeight;

                if (!double.IsNaN(window.Width) && window.Width > bounds.MaxWidth)
                    window.Width = bounds.MaxWidth;
                if (!double.IsNaN(window.Height) && window.Height > bounds.MaxHeight)
                    window.Height = bounds.MaxHeight;

                var actualWidth = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
                var actualHeight = window.ActualHeight > 0 ? window.ActualHeight : window.Height;

                if (!double.IsNaN(actualWidth) && actualWidth > 0)
                    window.Left = WindowSizeMath.FitLeft(window.Left, actualWidth, work.Left, work.Right);
                if (!double.IsNaN(actualHeight) && actualHeight > 0)
                    window.Top = WindowSizeMath.FitTop(window.Top, actualHeight, work.Top, work.Bottom);
            }
            catch
            {
                // Геометрия окна — вспомогательная возможность: сбой определения
                // монитора не должен мешать показу окна.
            }
        }

        /// <summary>
        /// Рабочая область монитора окна в DIP. Координаты WinForms —
        /// физические пиксели, поэтому переводятся через DPI окна.
        /// Открыт для <see cref="WindowGlassFramePolicy"/> (дефект «пустое поле
        /// внизу программы», 1.0.1.122): политика толщины стеклянной рамки
        /// сравнивает границы окна именно с рабочей областью его монитора.
        /// </summary>
        public static Rect? GetWorkAreaDip(Window window)
        {
            try
            {
                var handle = new WindowInteropHelper(window).Handle;
                if (handle != IntPtr.Zero)
                {
                    var screen = System.Windows.Forms.Screen.FromHandle(handle);

                    // Issue #172: масштаб DPI окна берём из VisualTreeHelper.GetDpi —
                    // он достоверен и до первого показа, когда PresentationSource ещё
                    // null. Прежний код в этом случае молча подставлял 1.0, и на HiDPI
                    // (125%/150%) физические пиксели рабочей области сравнивались с DIP
                    // окна — рамка ошибочно считалась «не прижатой» и оставалась
                    // расширенной (прозрачной). TransformFromDevice — запасной источник.
                    var (scaleX, scaleY) = GetDpiScale(window);
                    if (scaleX <= 0 || double.IsNaN(scaleX) || double.IsInfinity(scaleX))
                        scaleX = 1.0;
                    if (scaleY <= 0 || double.IsNaN(scaleY) || double.IsInfinity(scaleY))
                        scaleY = 1.0;

                    var area = screen.WorkingArea;
                    return new Rect(
                        area.Left * scaleX,
                        area.Top * scaleY,
                        area.Width * scaleX,
                        area.Height * scaleY);
                }
            }
            catch
            {
                // Ниже — запасной вариант по основному монитору.
            }

            var fallback = SystemParameters.WorkArea;
            return fallback.Width > 0 && fallback.Height > 0 ? fallback : null;
        }

        /// <summary>
        /// Фактический масштаб DPI окна (issue #172): сначала
        /// <see cref="VisualTreeHelper.GetDpi"/> (работает до показа окна), затем
        /// <c>TransformFromDevice</c> активного <see cref="PresentationSource"/>.
        /// Если ничего не доступно — 1.0.
        /// </summary>
        private static (double X, double Y) GetDpiScale(Window window)
        {
            try
            {
                var dpi = VisualTreeHelper.GetDpi(window);
                if (dpi.DpiScaleX > 0 && dpi.DpiScaleY > 0)
                    return (dpi.DpiScaleX, dpi.DpiScaleY);
            }
            catch
            {
                // Visual недоступен — пробуем PresentationSource ниже.
            }

            try
            {
                var source = PresentationSource.FromVisual(window);
                var transform = source?.CompositionTarget?.TransformFromDevice;
                if (transform is { } m && m.M11 > 0 && m.M22 > 0)
                    return (m.M11, m.M22);
            }
            catch
            {
                // Нет источника композиции — остаётся 1.0.
            }

            return (1.0, 1.0);
        }
    }
}
#endif
