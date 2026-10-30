using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Разметочные тесты окна «Настройки» и «Настройка информационной базы» (issue #71,
/// последний комментарий 7OH): после фикса #55 настроечные вкладки «размахнулись» —
/// многие элементы стали шире окна и почти на всех вкладках появилась горизонтальная
/// прокрутка. Принята единая с #55 стратегия «минимальные размеры + запрет сужения»:
/// окнам заданы MinWidth/MinHeight, при которых всё содержимое вкладок помещается по
/// ширине, а горизонтальная прокрутка вкладок отключена (Disabled).
/// Тесты читают исходники из репозитория (как Issue55WindowLayoutTests) и поэтому
/// работают на обеих платформах без запуска UI.
/// </summary>
public sealed class Issue71SettingsWindowLayoutTests
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

    private static int Count(string text, string needle) =>
        Regex.Matches(text, Regex.Escape(needle)).Count;

    // ------------------------------------------------------------------
    // Минимальные размеры окон настроек (issue #71): сузить окно мышкой ниже
    // MinWidth/MinHeight нельзя, поэтому содержимое вкладок не «уезжает»
    // за правый край и горизонтальная прокрутка не появляется.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("SettingsWindow.xaml", "960", "720")]
    [InlineData("ConnectionSettingsWindow.xaml", "760", "780")]
    public void WpfSettingsWindows_HaveMinSizesPreventingShrink(string fileName, string minWidth, string minHeight)
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", fileName));

        Assert.Contains("ResizeMode=\"CanResizeWithGrip\"", xaml, StringComparison.Ordinal);
        Assert.Contains($"MinWidth=\"{minWidth}\"", xaml, StringComparison.Ordinal);
        Assert.Contains($"MinHeight=\"{minHeight}\"", xaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SettingsWindow.Avalonia.cs", "960", "720")]
    [InlineData("ConnectionSettingsWindow.Avalonia.cs", "760", "780")]
    public void AvaloniaSettingsWindows_HaveMinSizesPreventingShrink(string fileName, string minWidth, string minHeight)
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", fileName));

        Assert.Contains($"MinWidth = {minWidth};", code, StringComparison.Ordinal);
        Assert.Contains($"MinHeight = {minHeight};", code, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Стартовые размеры окон (issue #71, комментарий RizvanShikhammatov):
    // окно открывается с комфортными размерами, а не сразу в минимуме —
    // «такого сужения тоже не хочется». Минимум остаётся страховкой от
    // обрезания контента при ручном сужении.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("SettingsWindow.xaml", "1100", "760")]
    [InlineData("ConnectionSettingsWindow.xaml", "900", "800")]
    public void WpfSettingsWindows_HaveComfortableStartSizes(string fileName, string width, string height)
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", fileName));

        Assert.Contains($"Width=\"{width}\"", xaml, StringComparison.Ordinal);
        Assert.Contains($"Height=\"{height}\"", xaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SettingsWindow.Avalonia.cs", "1100", "760")]
    [InlineData("ConnectionSettingsWindow.Avalonia.cs", "900", "800")]
    public void AvaloniaSettingsWindows_HaveComfortableStartSizes(string fileName, string width, string height)
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", fileName));

        Assert.Contains($"Width = {width};", code, StringComparison.Ordinal);
        Assert.Contains($"Height = {height};", code, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Горизонтальная прокрутка вкладок настроек отключена (issue #71):
    // контентные ScrollViewer вкладок имеют Disabled, вертикальная полоса
    // остаётся. Auto допустима только у вложенных списков/просмотрщиков.
    // ------------------------------------------------------------------

    [Fact]
    public void SettingsWindow_Wpf_TabContentHasNoHorizontalScroll()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));

        // 17 контентных ScrollViewer вкладок + полоса списка вкладок слева — Disabled.
        Assert.True(
            Count(xaml, "HorizontalScrollBarVisibility=\"Disabled\"") >= 18,
            "Контентные ScrollViewer вкладок должны иметь горизонтальную полосу Disabled (issue #71).");
        // Единственная Auto — вложенная полоса списка каталогов шаблонов (данные списка).
        Assert.Equal(1, Count(xaml, "HorizontalScrollBarVisibility=\"Auto\""));
    }

    [Fact]
    public void SettingsWindow_Avalonia_TabContentHasNoHorizontalScroll()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs"));

        // Контентные ScrollViewer вкладок — Disabled; единственная Auto — отдельное
        // окно просмотра картинки «О программе» (donat.png), не раскладка вкладок.
        Assert.True(
            Count(code, "HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled") >= 10,
            "Контентные ScrollViewer вкладок должны иметь горизонтальную полосу Disabled (issue #71).");
        Assert.Equal(1, Count(code, "HorizontalScrollBarVisibility = ScrollBarVisibility.Auto"));
    }

    [Fact]
    public void ConnectionSettingsWindow_Wpf_TabsHaveNoHorizontalScroll()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ConnectionSettingsWindow.xaml"));

        // 8 вкладок, каждая обёрнута в собственный ScrollViewer без горизонтальной полосы.
        Assert.Equal(8, Count(xaml, "<ScrollViewer"));
        Assert.Equal(8, Count(xaml, "HorizontalScrollBarVisibility=\"Disabled\""));
        Assert.Equal(0, Count(xaml, "HorizontalScrollBarVisibility=\"Auto\""));
    }

    [Fact]
    public void ConnectionSettingsWindow_Avalonia_TabsHaveNoHorizontalScroll()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ConnectionSettingsWindow.Avalonia.cs"));

        // Общая обёртка вкладок (метод Tab) горизонтальной прокрутки не имеет.
        Assert.Contains("HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled", code, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Issue #86 (комментарий 7OH): вкладка «Клавиши» — «гребёнка» полей
    // HotkeyBox. Принято: фиксированная ширина колонки подписей (260 px,
    // достаточно для самой длинной) — поля всех строк начинаются в одной
    // позиции на обеих платформах.
    // ------------------------------------------------------------------

    private static int CountOccurrences(string text, string needle) =>
        System.Text.RegularExpressions.Regex.Matches(text,
            System.Text.RegularExpressions.Regex.Escape(needle)).Count;

    [Fact]
    public void Hotkeys_Wpf_HotkeyBoxColumnsAlignedFixedWidth()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        // Секция «Клавиши»: колонка подписей фиксирована (260) и одинаковa во всех строках
        // (старый MinWidth=150 в ней не остался).
        Assert.True(CountOccurrences(xaml, "<ColumnDefinition Width=\"260\"/>") >= 30,
            "WPF: колонки подписей хоткеев должны иметь единую фиксированную ширину 260 (issue #86).");
        // Легаси-колонка Auto/MinWidth=150 осталась только в блоке «Шрифт» (одно вхождение).
        Assert.Equal(1, CountOccurrences(xaml, "<ColumnDefinition Width=\"Auto\" MinWidth=\"150\"/>"));
    }

    [Fact]
    public void Hotkeys_Avalonia_HotkeyBoxColumnsAlignedFixedWidth()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.Hotkeys.cs"));
        // Avalonia 11 не имеет неявного преобразования double → GridLength, поэтому
        // фиксированная колонка подписей 260 записывается как new GridLength(260).
        Assert.Contains("new ColumnDefinition(new GridLength(260))", code, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth = 150", code, StringComparison.Ordinal);
        Assert.DoesNotContain("HorizontalScrollBarVisibility = ScrollBarVisibility.Auto", code, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Восстановление сохранённых размеров главного окна (issue #71, последние
    // комментарии RizvanShikhammatov «нет» + 455×822; 7OH «Я даже меньше скажу»):
    // профиль мог сохранить размер из старой версии или с маленького монитора — окно
    // открывалось «сжатым» (сужение ниже минимума прямо на скриншотах). Теперь
    // ApplySavedWindowLayout зажимает восстановленные размеры к MinWidth/MinHeight.
    // ------------------------------------------------------------------

    [Fact]
    public void MainWindow_Wpf_RestoreClampsToMinSizes()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Events.cs"));
        Assert.Contains("Math.Max(width, MinWidth)", code, StringComparison.Ordinal);
        Assert.Contains("Math.Max(height, MinHeight)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_Avalonia_RestoreClampsToMinSizes()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
        Assert.Contains("Math.Max(settings.WindowWidth, MinWidth)", code, StringComparison.Ordinal);
        Assert.Contains("Math.Max(settings.WindowHeight, MinHeight)", code, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Гарантия минимума на уровне ОС/общей базы окон (issue #71, последний
    // комментарий 7OH «окно спокойно ужимается до размера заголовка»):
    // зажим на SizeChanged/ClientSize не успевает, когда ресайз идёт мышью
    // (а при кастомном WindowChrome WPF может не перевести MinWidth/MinHeight
    // в системный трек-размер). WPF-хук WM_GETMINMAXINFO зажимает ptMinTrackSize,
    // Avalonia-окна получают общий зажим ClientSize в ModalWindowBase.
    // ------------------------------------------------------------------

    [Fact]
    public void WindowChromeHelper_Wpf_EnforcesMinTrackSizeInGetMinMaxInfo()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
        Assert.Contains("MinTrackSize", code, StringComparison.Ordinal);
        Assert.Contains("window.MinWidth", code, StringComparison.Ordinal);
        Assert.Contains("window.MinHeight", code, StringComparison.Ordinal);
        Assert.Contains("HookMinTrackAndMaximize", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ModalWindowBase_Avalonia_ClampsAllDialogsToMinSizes()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ModalWindowBase.cs"));
        // Общий механизм зажима (issues #71/#55): публичный статический метод вызывается
        // базовым классом для всех диалогов и окнами вне иерархии (главное окно).
        Assert.Contains("ClampWindowToMinSize", code, StringComparison.Ordinal);
        Assert.Contains("GetObservable(ClientSizeProperty)", code, StringComparison.Ordinal);
        Assert.Contains("window.Width = window.MinWidth;", code, StringComparison.Ordinal);
        Assert.Contains("window.Height = window.MinHeight;", code, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Issue #55 (комментарий 7OH «окна сжимаются почти в ноль»): гарантия
    // минимума распространяется на ВСЕ изменяемые окна, включая главное.
    // WPF: главное окно оформляется собственными средствами, но хук
    // WM_GETMINMAXINFO получает тем же общим механизмом. Avalonia: главное
    // окно не наследует ModalWindowBase, поэтому вызывает общий
    // ClampWindowToMinSize в конструкторе.
    // ------------------------------------------------------------------

    [Fact]
    public void MainWindow_Wpf_GetsMinTrackHookViaCommonHelper()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
        // OnWindowLoaded: для главного окна оформление не применяется, но хук минимума
        // ставится (HookMinTrackAndMaximize), иначе ресайз сжимал бы окно «почти в ноль».
        Assert.Contains("if (window is MainWindow)", code, StringComparison.Ordinal);
        Assert.Contains("HookMinTrackAndMaximize(window);", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_Avalonia_GetsCommonMinSizeClamp()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
        Assert.Contains("ModalWindowBase.ClampWindowToMinSize(this);", code, StringComparison.Ordinal);
    }
}
