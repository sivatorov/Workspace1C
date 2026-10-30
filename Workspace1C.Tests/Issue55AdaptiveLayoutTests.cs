using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Разметочные тесты адаптивной раскладки форм (issue #55, повторная жалоба ArtyoZ:
/// «Все те же формы как обрезались, так и обрезаются»). После фикса 1.0.0.77/1.0.0.66
/// обрезание сохранялось из-за отключённой горизонтальной прокрутки и жёстких ширин
/// комбобоксов/полей во вкладках. Тесты читают исходники из репозитория (как
/// Issue41WindowLayoutTests) и проверяют, что:
///  - контент вкладок имеет горизонтальную прокрутку как fallback (Auto вместо Disabled);
///  - жёсткие Width у полей ввода заменены на MinWidth (Auto+MinWidth для колонок);
///  - длинные подписи переносятся (TextWrapping), длинное имя базы обрезается многоточием;
///  - правки синхронизированы между WPF (*.xaml) и Avalonia (*.Avalonia.cs).
/// </summary>
public sealed class Issue55AdaptiveLayoutTests
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
    // «Настройки» (WPF): горизонтальная полоса вкладок - fallback, жёсткие
    // ширины полей -> MinWidth, длинные подписи колонок хоткеев -> Auto+MinWidth.
    // ------------------------------------------------------------------

    [Fact]
    public void SettingsWindow_Wpf_TabContent_NoHorizontalScroll_And_MinWidth()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));

        // Минимальные размеры окна (issue #71): сузить окно мышкой ниже MinWidth нельзя,
        // поэтому всё содержимое вкладок помещается по ширине без горизонтальной полосы.
        Assert.Contains("ResizeMode=\"CanResizeWithGrip\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"960\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"720\"", xaml, StringComparison.Ordinal);

        // Горизонтальная прокрутка вкладок отключена: контентные ScrollViewer вкладок
        // (17) и полоса списка вкладок слева имеют Disabled.
        Assert.True(
            Count(xaml, "HorizontalScrollBarVisibility=\"Disabled\"") >= 18,
            "Контентные ScrollViewer вкладок должны иметь горизонтальную полосу Disabled (issue #71).");
        // Единственная Auto — вложенная полоса списка каталогов шаблонов (данные списка).
        Assert.Equal(1, Count(xaml, "HorizontalScrollBarVisibility=\"Auto\""));

        // Фиксированные ширины комбобоксов/полей во вкладках заменены MinWidth.
        Assert.Contains("x:Name=\"LanguageComboBox\" MinWidth=\"280\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AfterLaunchActionCombo\" MinWidth=\"230\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SchemeComboBox\" MinWidth=\"240\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"IconSetOverrideComboBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"240\" Height=\"34\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DefaultArchComboBox\" Grid.Column=\"1\" MinWidth=\"200\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DoubleClickActionGlobalCombo\" Height=\"30\" MinWidth=\"200\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MaxLaunchHistoryDepthBox\" Height=\"30\" MinWidth=\"90\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ComDetectTimeoutMsBox\" Height=\"32\" MinWidth=\"120\"", xaml, StringComparison.Ordinal);

        // Жёстких ширин 220/230/240/280/200/120/90/70/60/50 в разметке больше нет
        // (MinWidth/MaxWidth исключаются отрицательным lookbehind).
        var hard = Regex.Matches(xaml, @"(?<![A-Za-z])Width=""(230|240|280|220|200|120|90|70|60|50)""").Count;
        Assert.True(hard == 0, $"В SettingsWindow.xaml остались жёсткие ширины полей ввода: {hard}");

        // Колонки подписей горячих клавиш: фиксированная ширина 260 (issue #86 —
        // «гребёнка» полей HotkeyBox устранена выравниванием старта полей).
        Assert.True(
            Count(xaml, "<ColumnDefinition Width=\"260\"/>") >= 30,
            "Колонки подписей горячих клавиш должны иметь единую фиксированную ширину 260 (issue #86).");
    }

    [Fact]
    public void SettingsWindow_Avalonia_TabContent_NoHorizontalScroll_And_MinWidth()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs"));

        // Минимальные размеры окна (issue #71): сужение ниже MinWidth запрещено.
        Assert.Contains("MinWidth = 960;", code, StringComparison.Ordinal);
        Assert.Contains("MinHeight = 720;", code, StringComparison.Ordinal);

        // Контентные ScrollViewer вкладок горизонтальной прокрутки не имеют (Disabled);
        // единственная Auto — окно просмотра картинки «О программе» (donat.png).
        Assert.True(
            Count(code, "HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled") >= 10,
            "Контентные ScrollViewer вкладок должны иметь горизонтальную полосу Disabled (issue #71).");
        Assert.Equal(1, Count(code, "HorizontalScrollBarVisibility = ScrollBarVisibility.Auto"));

        // Жёсткие ширины полей -> MinWidth (в том числе язык, действие после запуска, ОА).
        Assert.Contains("MinWidth = 280,", code, StringComparison.Ordinal);
        Assert.Contains("afterLaunchBox = new ComboBox { MinWidth = 230, Height = 30 }", code, StringComparison.Ordinal);
        Assert.Contains("MinWidth = 260,", code, StringComparison.Ordinal);
        Assert.Contains("MinWidth = 120,", code, StringComparison.Ordinal);

        // Колонка подписей шрифтов: Auto + MinWidth (было жёсткое 160).
        Assert.Contains("new ColumnDefinition(GridLength.Auto) { MinWidth = 150 }", code, StringComparison.Ordinal);

        var hard = Regex.Matches(code, @"(?<![A-Za-z])Width = (230|240|280|260|250|220|200|120|90|80|70|60|50),").Count;
        Assert.True(hard == 0, $"В SettingsWindow.Avalonia.cs остались жёсткие ширины полей: {hard}");
    }

    [Fact]
    public void SettingsWindow_Avalonia_HotkeyRows_LabelColumnFixed()
    {
        // issue #86: фиксированная ширина колонки подписей (260) — без Auto/MinWidth-«гребёнки».
        // Avalonia 11 не имеет неявного преобразования double → GridLength, поэтому
        // фиксированная колонка 260 записывается как new GridLength(260).
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.Hotkeys.cs"));
        Assert.Contains("new ColumnDefinition(new GridLength(260))", code, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth = 150", code, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // «Настройка информационной базы»: горизонтальная полоса вкладок - fallback,
    // комбобокс двойного клика без фиксированной ширины.
    // ------------------------------------------------------------------

    [Fact]
    public void ConnectionSettingsWindow_Wpf_TabsNoHorizontalScroll_And_ComboMinWidth()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ConnectionSettingsWindow.xaml"));

        // Минимальные размеры окна (issue #71): сужение ниже MinWidth запрещено.
        Assert.Contains("ResizeMode=\"CanResizeWithGrip\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"760\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"780\"", xaml, StringComparison.Ordinal);

        // Все 8 вкладок обёрнуты в ScrollViewer с вертикальной прокруткой и БЕЗ
        // горизонтальной (полоса Disabled, «размахивание» #55 устранено).
        Assert.Equal(8, Count(xaml, "<ScrollViewer"));
        Assert.Equal(8, Count(xaml, "HorizontalScrollBarVisibility=\"Disabled\""));
        Assert.Equal(0, Count(xaml, "HorizontalScrollBarVisibility=\"Auto\""));
        Assert.Contains("x:Name=\"DoubleClickActionCombo\" MinWidth=\"220\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionSettingsWindow_Avalonia_TabsNoHorizontalScroll_And_ComboMinWidth()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ConnectionSettingsWindow.Avalonia.cs"));
        // Минимальные размеры окна (issue #71).
        Assert.Contains("MinWidth = 760;", code, StringComparison.Ordinal);
        Assert.Contains("MinHeight = 780;", code, StringComparison.Ordinal);
        // Общая обёртка вкладок (метод Tab) горизонтальной прокрутки не имеет.
        Assert.Contains("HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled", code, StringComparison.Ordinal);
        Assert.DoesNotContain("HorizontalScrollBarVisibility = ScrollBarVisibility.Auto", code, StringComparison.Ordinal);
        // Комбобокс двойного клика без фиксированной ширины.
        Assert.Contains("Content = new ScrollViewer", code, StringComparison.Ordinal);
        Assert.Contains("MinWidth = 220,", code, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Аудит других окон (по образцу фикса 1.0.0.77): колонки подписей
    // Auto+MinWidth, горизонтальная полоса fallback, перенос длинных подписей.
    // ------------------------------------------------------------------

    [Fact]
    public void ServerEditWindow_LabelColumnAuto_WpfAndAvalonia()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerEditWindow.xaml"));
        Assert.Equal(5, Count(xaml, "Width=\"Auto\" MinWidth=\"130\""));

        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerEditWindow.Avalonia.cs"));
        Assert.Contains("GridLength.Auto) { MinWidth = 130 }", code, StringComparison.Ordinal);
    }

    [Fact]
    public void LaunchParameterEditWindow_LabelColumnAuto_WpfAndAvalonia()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "LaunchParameterEditWindow.xaml"));
        Assert.Equal(2, Count(xaml, "Width=\"Auto\" MinWidth=\"130\""));

        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "LaunchParameterEditWindow.Avalonia.cs"));
        Assert.Contains("GridLength.Auto) { MinWidth = 130 }", code, StringComparison.Ordinal);
    }

    [Fact]
    public void RacInfobaseCreateWindow_HorizontalFallback_WpfAndAvalonia()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "RacInfobaseCreateWindow.xaml"));
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\" HorizontalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);

        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "RacInfobaseCreateWindow.Avalonia.cs"));
        Assert.Contains("GridLength.Auto) { MinWidth = 140 }", code, StringComparison.Ordinal);
    }

    [Fact]
    public void RacInfobasePropertiesWindow_HorizontalFallback_WpfAndAvalonia()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "RacInfobasePropertiesWindow.xaml"));
        Assert.True(
            Count(xaml, "<ScrollViewer VerticalScrollBarVisibility=\"Auto\" HorizontalScrollBarVisibility=\"Auto\">") >= 2,
            "Вкладки свойств ИБ должны иметь горизонтальную полосу Auto.");
        // Длинное имя базы в заголовке не должно обрезаться содержимым.
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", xaml, StringComparison.Ordinal);

        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "RacInfobasePropertiesWindow.Avalonia.cs"));
        Assert.True(
            Count(code, "HorizontalScrollBarVisibility = ScrollBarVisibility.Auto") >= 2,
            "Обе вкладки Avalonia должны иметь горизонтальную полосу Auto.");
    }

    [Fact]
    public void ScriptScenarioEditWindow_WrapCheckboxLabels_And_HorizontalFallback_WpfAndAvalonia()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ScriptScenarioEditWindow.xaml"));
        // Строка «Интерпретатор + галки»: комбобокс без жёсткой широкой ширины,
        // метки галок переносятся по словам, полоса fallback включена.
        Assert.Contains("MinWidth=\"220\"", xaml, StringComparison.Ordinal);
        Assert.True(Count(xaml, "TextWrapping=\"Wrap\" MaxWidth=\"150\"") >= 2,
            "Метки «Скрывать окно скрипта» и «Уведомлять о запуске» должны переноситься.");
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);

        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ScriptScenarioEditWindow.Avalonia.cs"));
        Assert.Contains("_shellCombo.MinWidth = 220;", code, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility = ScrollBarVisibility.Auto", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptPickWindow_Resizable_WithWrappingPreview()
    {
        // Окно «Выбор скрипта»: изменяемое, минимальные размеры есть (глобальный тест),
        // комадная строка превью переносится — не обрезается при сужении окна.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ScriptPickWindow.xaml"));
        Assert.Contains("MinWidth=\"560\" MinHeight=\"400\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PreviewBox\" IsReadOnly=\"True\" TextWrapping=\"Wrap\"", xaml, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Монитор серверов (issue #55/#75): минимумы окна 1120x560 и горизонтальная
    // прокрутка таблиц — при сужении колонки не сжимаются и не обрезаются,
    // а уходят в прокрутку вместе с заголовком (стратегия чтения данных).
    // ------------------------------------------------------------------

    [Fact]
    public void ServerMonitorWindow_MinSizes_And_TablesHorizontalScroll_WpfAndAvalonia()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.xaml"));
        Assert.Contains("MinWidth=\"1120\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"560\"", xaml, StringComparison.Ordinal);
        Assert.True(
            Count(xaml, "ScrollViewer.HorizontalScrollBarVisibility=\"Auto\"") >= 4,
            "Все четыре таблицы монитора серверов должны иметь горизонтальную полосу Auto (issue #75).");

        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.Avalonia.cs"));
        Assert.Contains("MinWidth = 1120;", code, StringComparison.Ordinal);
        Assert.Contains("MinHeight = 560;", code, StringComparison.Ordinal);
        // Заголовок и строки лежат в общем горизонтальном ScrollViewer (issue #75).
        Assert.Contains("HorizontalScrollBarVisibility = ScrollBarVisibility.Auto", code, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Гарантированный минимум ВСЕХ изменяемых окон (issue #55, последний
    // комментарий 7OH «окна сжимаются почти в ноль»): главное окно на обеих
    // платформах получает минимум тем же общим механизмом, что и диалоги —
    // WPF: хук WM_GETMINMAXINFO (WindowChromeHelper), Avalonia: зажим ClientSize
    // (ModalWindowBase.ClampWindowToMinSize).
    // ------------------------------------------------------------------

    [Fact]
    public void MainWindow_MinSizesGuaranteed_OnBothPlatforms()
    {
        // WPF: главное окно объявляет минимумы и подключено к общему хуку
        // минимума (оформление у него собственное, поэтому хук ставится
        // отдельной веткой OnWindowLoaded).
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        Assert.Contains("MinWidth=\"900\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"600\"", xaml, StringComparison.Ordinal);

        var helper = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));
        Assert.Contains("if (window is MainWindow)", helper, StringComparison.Ordinal);
        Assert.Contains("HookMinTrackAndMaximize(window);", helper, StringComparison.Ordinal);

        // Avalonia: минимумы объявлены, окно вызывает общий зажим базы диалогов
        // (MainWindow не наследует ModalWindowBase — без вызова минимум не гарантирован).
        var mainAvalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
        Assert.Contains("MinWidth = 900;", mainAvalonia, StringComparison.Ordinal);
        Assert.Contains("MinHeight = 600;", mainAvalonia, StringComparison.Ordinal);
        Assert.Contains("ModalWindowBase.ClampWindowToMinSize(this);", mainAvalonia, StringComparison.Ordinal);
    }
}