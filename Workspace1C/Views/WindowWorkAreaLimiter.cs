#if WINDOWS
using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
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
            Apply(window);
        }

        private static void Apply(Window window)
        {
            try
            {
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
        /// </summary>
        private static Rect? GetWorkAreaDip(Window window)
        {
            try
            {
                var handle = new WindowInteropHelper(window).Handle;
                if (handle != IntPtr.Zero)
                {
                    var screen = System.Windows.Forms.Screen.FromHandle(handle);
                    var source = PresentationSource.FromVisual(window);
                    var transform = source?.CompositionTarget?.TransformFromDevice;
                    var scaleX = transform?.M11 ?? 1.0;
                    var scaleY = transform?.M22 ?? 1.0;
                    if (scaleX <= 0 || double.IsNaN(scaleX))
                        scaleX = 1.0;
                    if (scaleY <= 0 || double.IsNaN(scaleY))
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
    }
}
#endif
