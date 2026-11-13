using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests
{
    /// <summary>
    /// Issue #126 «Рамки для окон 2»: у главного окна рамка есть (issue #273,
    /// MainWindow.xaml — оверлей Border с BorderBrushColor), а внутренние окна на
    /// системах без теней/эффектов DWM (другая тема/ОС, терминал, виртуализация)
    /// сливались с фоном. Рамка задаётся централизованно, не разово:
    /// WPF — <see cref="WindowChromeHelper"/> (общее оформление всех диалоговых окон),
    /// Avalonia — базовый класс ModalWindowBase. Тесты-стражи не дают рамке исчезнуть.
    /// </summary>
    public class Issue126WindowFrameTests
    {
        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Views")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "Workspace1C.Tests")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            throw new InvalidOperationException("Корень репозитория не найден из тестового каталога.");
        }

        private static string ReadProjectFile(string relativePath)
        {
            var fullPath = Path.Combine(FindRepoRoot(), relativePath);
            Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
            return File.ReadAllText(fullPath);
        }

        // ---------- Эталон: главное окно (WPF) ----------

        [Fact]
        public void MainWindow_Wpf_HasVisibleFrameOverlay()
        {
            // Эталон рамки (issue #273): оверлей Border поверх всего окна, не
            // перехватывающий мышь, кисть и толщина — из темы.
            var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
            Assert.Contains("BorderBrush=\"{DynamicResource BorderBrushColor}\"", xaml, StringComparison.Ordinal);
            Assert.Contains("IsHitTestVisible=\"False\"", xaml, StringComparison.Ordinal);
        }

        // ---------- WPF: базовый стиль всех диалоговых окон (WindowChromeHelper) ----------

        [Fact]
        public void WindowChromeHelper_Wpf_DefaultsWindowFrameOn()
        {
            // Рамка включена по умолчанию у ВСЕХ окон: вложенное свойство ShowWindowFrame
            // создаётся с PropertyMetadata(true) — новые окна получают рамку автоматически.
            var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
            Assert.Contains("ShowWindowFrameProperty", code, StringComparison.Ordinal);
            Assert.Contains("new PropertyMetadata(true)", code, StringComparison.Ordinal);
        }

        [Fact]
        public void WindowChromeHelper_Wpf_BuildChromeDrawsFrameWithThemeBrush()
        {
            // BuildChrome оборачивает содержимое окна оверлей-рамкой: ненулевая
            // BorderThickness и кисть из темы (BorderBrushColor, как у MainWindow).
            var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
            Assert.Contains("BorderThickness = new Thickness(1)", code, StringComparison.Ordinal);
            Assert.Contains("Border.BorderBrushProperty, \"BorderBrushColor\"", code, StringComparison.Ordinal);
            // Рамка поверх шапки и содержимого, но не перехватывает мышь.
            Assert.Contains("IsHitTestVisible = false", code, StringComparison.Ordinal);
        }

        [Fact]
        public void WindowChromeHelper_Wpf_AllowsFrameOptOut()
        {
            // Специальные окна (полноэкранные, просмотрщики) могут отключить рамку:
            // SetShowWindowFrame доступен извне класса.
            var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
            Assert.Contains("public static void SetShowWindowFrame", code, StringComparison.Ordinal);
            Assert.Contains("public static bool GetShowWindowFrame", code, StringComparison.Ordinal);
        }

        [Fact]
        public void WpfThemes_DefineBorderBrushColor()
        {
            // Кисть рамки существует во всех темах: рамка не «теряет цвет» ни в
            // светлой, ни в тёмной палитре (и их схемах — цвет выводится из BorderColor).
            foreach (var theme in new[] { "LightTheme.xaml", "DarkTheme.xaml" })
            {
                var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", theme));
                Assert.Contains("<SolidColorBrush x:Key=\"BorderBrushColor\"", xaml, StringComparison.Ordinal);
            }
        }

        // ---------- Avalonia: базовый класс всех диалоговых окон (ModalWindowBase) ----------

        [Fact]
        public void ModalWindowBase_Avalonia_GlassRootHasFrame()
        {
            // «Стеклянный» корень диалога — рамка 1px, кисть из темы (BorderColorBrush).
            var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ModalWindowBase.cs"));
            Assert.Contains("BorderThickness = new Thickness(1)", code, StringComparison.Ordinal);
            Assert.Contains("Border.BorderBrushProperty, \"BorderColorBrush\"", code, StringComparison.Ordinal);
        }

        [Fact]
        public void MainWindow_Avalonia_GlassRootHasFrame()
        {
            // Главное окно (Avalonia) — тоже рамка из темы: эталон совпадает с диалогами.
            var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
            Assert.Contains("BorderThickness = new Thickness(1)", code, StringComparison.Ordinal);
            Assert.Contains("Border.BorderBrushProperty, \"BorderColorBrush\"", code, StringComparison.Ordinal);
        }

        // ---------- WPF: рамка главного окна следует системному скруглению (1.0.1.61) ----------

        [Fact]
        public void MainWindow_Wpf_FrameOverlayHasCornerRadius()
        {
            // Главное окно не проходит через WindowChromeHelper.Apply (собственный
            // каркас), поэтому его рамка раньше оставалась прямоугольной при
            // скруглённых углах DWM — «скругление с квадратной рамкой» (issue #126).
            // Рамке даётся имя и стартовый CornerRadius, актуальный радиус выставляет
            // WindowChromeHelper.AttachFrameCornerTracking.
            var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
            Assert.Contains("x:Name=\"WindowFrameBorder\"", xaml, StringComparison.Ordinal);
            Assert.Contains("BorderThickness=\"1\" CornerRadius=\"0\"", xaml, StringComparison.Ordinal);

            var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml.cs"));
            Assert.Contains("WindowChromeHelper.AttachFrameCornerTracking(this, WindowFrameBorder)",
                code, StringComparison.Ordinal);
        }

        [Fact]
        public void WindowChromeHelper_Wpf_AttachSharesDeferredRefresh()
        {
            // Публичный Attach для окон с собственным каркасом использует тот же
            // механизм: подписки на показ/смену темы/композиции и пересчёт по состоянию.
            var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
            Assert.Contains("public static void AttachFrameCornerTracking", code, StringComparison.Ordinal);
            Assert.Contains("HookFrameCornerMessages(window)", code, StringComparison.Ordinal);
            Assert.Contains("SetFrameBorder(window, frame)", code, StringComparison.Ordinal);
        }

        [Fact]
        public void WindowChromeHelper_Wpf_DialogsTrackFrameAfterShow()
        {
            // Диалоги (общий стиль Apply) получают слежение за рамкой сразу после
            // применения CornerPreference — до первого расчёта радиуса.
            var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
            var hook = code.IndexOf("HookFrameCornerTracking(window);", StringComparison.Ordinal);
            var apply = code.IndexOf("ApplyFrameCornerRadius(window);", StringComparison.Ordinal);
            Assert.True(hook >= 0, "HookFrameCornerTracking не вызывается в Apply.");
            Assert.True(apply > hook, "Состояние слежения должно создаваться до первого расчёта радиуса.");
        }

        // ---------- Единый радиус рамки WPF/Avalonia в RDP/Win10 (1.0.1.62) ----------

        [Fact]
        public void WindowChromeHelper_Wpf_UsesSharedContentRadiusForRdpFallback()
        {
            // WPF-фолбэк при неизвестном DWM (RDP/Windows 10) берёт радиус из общей
            // константы WindowSizeMath.DefaultContentCornerRadius, а не литерала —
            // тот же радиус использует Avalonia, поэтому рамка и содержимое совпадают.
            var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
            Assert.Contains("FrameContentCornerRadius = WindowSizeMath.DefaultContentCornerRadius", code,
                StringComparison.Ordinal);
            // В политику передаётся фактический радиус содержимого: у диалогов — общая
            // константа, у окон с собственным каркасом — 0 (явный opt-out).
            Assert.Contains("ContentCornerRadius: contentRadius", code, StringComparison.Ordinal);
            Assert.Contains("GetContentBorder(window) is not null ? FrameContentCornerRadius : 0", code,
                StringComparison.Ordinal);
            Assert.Contains("WindowSizeMath.ResolveFrameCornerRadius(", code, StringComparison.Ordinal);
        }

        [Fact]
        public void Avalonia_WindowsUseSharedContentRadius()
        {
            // «Стеклянный» корень главного окна и модальных диалогов Avalonia
            // использует ту же константу радиуса, что и WPF-фолбэк (issue #126):
            // рамка (Border стеклянного корня) и содержимое скруглены одинаково.
            var main = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
            Assert.Contains("WindowSizeMath.DefaultContentCornerRadius", main, StringComparison.Ordinal);

            var modal = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ModalWindowBase.cs"));
            Assert.Contains("WindowSizeMath.DefaultContentCornerRadius", modal, StringComparison.Ordinal);
        }

        [Fact]
        public void WindowChromeHelper_Wpf_HandlesUnknownDwmWithoutSquareFrame()
        {
            // Ключевая правка #126 (RDP/Win10): при dwmState=unknown и osRounds=false
            // рамка не должна получать radius=0, если композиция включена и у
            // содержимого есть своё скругление. Проверяем наличие политики и её
            // входов в WPF-слое.
            var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
            Assert.Contains("WindowSizeMath.FrameRoundingInputs(", code, StringComparison.Ordinal);
            Assert.Contains("DwmStateKnown: stateKnown", code, StringComparison.Ordinal);
            Assert.Contains("CompositionEnabled: compositionEnabled", code, StringComparison.Ordinal);
            Assert.Contains("policy.Policy", code, StringComparison.Ordinal);
        }

        // ---------- Единая политика и пересчёт после показа (1.0.1.62) ----------

        [Fact]
        public void WindowChromeHelper_Wpf_UsesDwmCompositionApi()
        {
            // Состояние композиции определяется DwmIsCompositionEnabled, а не только
            // версией ОС (issue #126): в RDP/при отключённых эффектах скругление
            // «наугад» не рисуется.
            var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
            Assert.Contains("DwmIsCompositionEnabled", code, StringComparison.Ordinal);
            Assert.Contains("TryGetDwmCompositionEnabled", code, StringComparison.Ordinal);
            Assert.Contains("CompositionEnabled: compositionEnabled", code, StringComparison.Ordinal);
        }

        [Fact]
        public void WindowChromeHelper_Wpf_ReadsCornerStateOnlyOnWin11()
        {
            // DWMWA_WINDOW_CORNER_STATE читается только на Windows 11 (build >= 22000):
            // на Windows 10 атрибут не поддерживается, состояние остаётся неизвестным
            // и решение принимает политика по радиусу содержимого.
            var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
            Assert.Contains("DwmWindowCornerState", code, StringComparison.Ordinal);
            Assert.Contains("Environment.OSVersion.Version.Build < 22000", code, StringComparison.Ordinal);
        }

        [Fact]
        public void WindowChromeHelper_Wpf_RecomputesAfterShowAndSystemMessages()
        {
            // Пересчёт после ContentRendered/Activated/DpiChanged, отложенный повтор и
            // обработка WM_THEMECHANGED/WM_DWMCOMPOSITIONCHANGED — поведение 1.0.1.61
            // не должно быть потеряно (issue #126).
            var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
            Assert.Contains("window.ContentRendered", code, StringComparison.Ordinal);
            Assert.Contains("window.Activated", code, StringComparison.Ordinal);
            Assert.Contains("window.DpiChanged", code, StringComparison.Ordinal);
            Assert.Contains("ScheduleFrameCornerRadiusRefresh", code, StringComparison.Ordinal);
            Assert.Contains("WmThemeChanged", code, StringComparison.Ordinal);
            Assert.Contains("WmDwmCompositionChanged", code, StringComparison.Ordinal);
        }

        [Fact]
        public void WpfAndAvalonia_RoundContentWithSameRadius()
        {
            // Единый радиус содержимого WPF/Avalonia (issue #126): WPF оборачивает
            // содержимое диалога в скруглённый контейнер, Avalonia скругляет
            // «стеклянный» корень — обе платформы берут одну константу
            // WindowSizeMath.DefaultContentCornerRadius, поэтому рамка и содержимое
            // совпадают на обеих платформах.
            var wpf = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
            Assert.Contains("GetContentBorder(window) is not null ? FrameContentCornerRadius : 0", wpf,
                StringComparison.Ordinal);
            Assert.Contains("FrameContentCornerRadius = WindowSizeMath.DefaultContentCornerRadius", wpf,
                StringComparison.Ordinal);

            var main = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
            Assert.Contains("WindowSizeMath.DefaultContentCornerRadius", main, StringComparison.Ordinal);

            var modal = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ModalWindowBase.cs"));
            Assert.Contains("WindowSizeMath.DefaultContentCornerRadius", modal, StringComparison.Ordinal);
        }
    }
}
