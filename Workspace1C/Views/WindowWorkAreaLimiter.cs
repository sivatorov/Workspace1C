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
            // Дефект «серая область внизу программы» (1.0.1.131): размер окна может
            // изменить оконный менеджер (пристёгивание Win+стрелками на всю высоту
            // рабочей области, перенос на монитор с другой рабочей областью).
            // Пересчитываем верхние границы, чтобы они не оказались меньше
            // фактического размера: иначе WPF ограничит разметку, и часть клиентской
            // области останется незакрашенной. Позицию здесь не трогаем — во время
            // перетаскивания окно может законно выходить за рабочую область.
            window.SizeChanged += (_, _) => RefreshSizeConstraints(window);
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

                var work = ApplySizeConstraints(window);
                if (work is not { } area)
                    return;

                var actualWidth = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
                var actualHeight = window.ActualHeight > 0 ? window.ActualHeight : window.Height;

                if (!double.IsNaN(actualWidth) && actualWidth > 0)
                    window.Left = WindowSizeMath.FitLeft(window.Left, actualWidth, area.Left, area.Right);
                if (!double.IsNaN(actualHeight) && actualHeight > 0)
                    window.Top = WindowSizeMath.FitTop(window.Top, actualHeight, area.Top, area.Bottom);
            }
            catch
            {
                // Геометрия окна — вспомогательная возможность: сбой определения
                // монитора не должен мешать показу окна.
            }
        }

        /// <summary>
        /// Пересчитывает только верхние границы окна после изменения его размера
        /// (дефект «серая область внизу программы», 1.0.1.131). Позиция не трогается:
        /// во время перетаскивания окно может законно выходить за рабочую область.
        /// </summary>
        private static void RefreshSizeConstraints(Window window)
        {
            try
            {
                if (window.WindowState != WindowState.Normal)
                    return;

                ApplySizeConstraints(window);
            }
            catch
            {
                // Геометрия окна — вспомогательная возможность.
            }
        }

        /// <summary>
        /// Применяет верхние границы (<c>MaxWidth</c>/<c>MaxHeight</c>) и при
        /// необходимости уменьшает стартовый размер окна по рабочей области.
        /// Возвращает рабочую область в DIP, если ограничение применимо, иначе
        /// <c>null</c>.
        /// <para>
        /// Дефект «серая область внизу программы» (1.0.1.131): главному окну отступ
        /// от рабочей области не нужен — пользователь пристёгивает окно к краям
        /// (Win+стрелки), и размер задаёт оконный менеджер. Прежний отступ 48 DIP
        /// (рабочая область минус отступ) оставлял снизу незакрашенную полосу, когда
        /// окно занимало всю высоту рабочей области. Для диалогов отступ сохранён.
        /// </para>
        /// </summary>
        private static Rect? ApplySizeConstraints(Window window)
        {
            var area = GetWorkAreaDip(window);
            if (area is not { } work || work.Width <= 0 || work.Height <= 0)
                return null;

            var designWidth = window.Width;
            if (double.IsNaN(designWidth) || designWidth <= 0)
                designWidth = window.ActualWidth > 0 ? window.ActualWidth : window.MinWidth;

            var designHeight = window.Height;
            if (double.IsNaN(designHeight) || designHeight <= 0)
                designHeight = window.ActualHeight > 0 ? window.ActualHeight : window.MinHeight;

            // Главное окно пристёгивается к краям рабочей области — отступ не нужен.
            var margin = window is MainWindow ? 0 : WindowBoundsPolicy.DefaultMargin;
            var bounds = WindowBoundsPolicy.Recommend(
                designWidth,
                designHeight,
                window.MinWidth,
                window.MinHeight,
                work.Width,
                work.Height,
                margin);

            // Предел не может быть меньше фактического размера уже показанного окна:
            // иначе WPF ограничит разметку, и часть клиентской области останется
            // незакрашенной (полоса снизу или справа).
            var maxWidth = WindowBoundsPolicy.EffectiveMax(
                bounds.MaxWidth, window.ActualWidth, window.IsLoaded);
            var maxHeight = WindowBoundsPolicy.EffectiveMax(
                bounds.MaxHeight, window.ActualHeight, window.IsLoaded);

            if (!double.IsPositiveInfinity(maxWidth) && maxWidth > 0)
                window.MaxWidth = maxWidth;
            if (!double.IsPositiveInfinity(maxHeight) && maxHeight > 0)
                window.MaxHeight = maxHeight;

            // Минимум не должен превышать максимум, иначе WPF отдаст приоритет
            // минимуму и окно всё равно выйдет за экран.
            if (window.MinWidth > window.MaxWidth)
                window.MinWidth = window.MaxWidth;
            if (window.MinHeight > window.MaxHeight)
                window.MinHeight = window.MaxHeight;

            if (!double.IsNaN(window.Width) && window.Width > maxWidth)
                window.Width = maxWidth;
            if (!double.IsNaN(window.Height) && window.Height > maxHeight)
                window.Height = maxHeight;

            return work;
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
