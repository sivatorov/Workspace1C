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
    }
}
