using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #174/#175 «Linux — отступы от края и разброс вкладок настроек»:
/// содержимое вкладок Linux-настроек должно иметь такие же боковые отступы, как
/// в Windows (12 DIP), группы — видимую рамку, кнопка «Синхронизация настроек» —
/// размер по содержимому, а порядок вкладок — совпадать с Windows.
/// Тесты — стражи исходников Avalonia-версии окна настроек.
/// </summary>
public sealed class Issue174LinuxSettingsLayoutTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var path = Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"Файл проекта не найден: {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Tabs_HaveTwelveDipSidePadding()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");

        // Issue #174: галочки и поля не должны сливаться с краем окна.
        Assert.Contains("new Thickness(12, 12, 12, 0)", code);
        Assert.DoesNotContain("new Thickness(4, 12, 4, 0)", code);
    }

    [Fact]
    public void LoggingGroups_UseBorderedSettingsCards()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.Logging.cs");

        // Issue #175: группы вкладки «Логирование» — карточки с видимой рамкой.
        Assert.Contains("SettingsGroup(", code);
        Assert.Contains("Settings.Logging.Directions", code);
        Assert.Contains("Settings.Logging.Diagnostics", code);
    }

    [Fact]
    public void SettingsSyncButton_DoesNotStretchAcrossFooter()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");

        // Issue #175 п.8: кнопка синхронизации — по содержимому, у левого края.
        Assert.Contains("HorizontalAlignment = HorizontalAlignment.Left", code);
    }

    [Fact]
    public void TabOrder_LoggingBeforeBackupLikeWindows()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");

        var logging = code.IndexOf("tabs.Items.Add(tabLogging)", StringComparison.Ordinal);
        var profile = code.IndexOf("tabs.Items.Add(tabProfile)", StringComparison.Ordinal);
        Assert.True(logging > 0, "Вкладка логирования не добавляется в TabControl");
        Assert.True(profile > 0, "Вкладка резервного копирования не добавляется в TabControl");
        // Issue #175 п.6: «Логирование» идёт перед «Резервным копированием», как в Windows.
        Assert.True(logging < profile, "Порядок вкладок не совпадает с Windows");
    }

    [Fact]
    public void TabHeaders_HaveWidthAndPaddingToAvoidClipping()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");

        // Issue #175 п.8 (1.0.1.128): заголовки вкладок не обрезаются.
        Assert.Contains("tab.Width = UiMetrics.Scaled(260)", code);
        Assert.Contains("tab.MinWidth = UiMetrics.Scaled(240)", code);
        Assert.Contains("tab.Padding = new Thickness(12, 10)", code);
    }

    [Fact]
    public void LoggingGroups_HaveInnerPaddingForCheckboxes()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.Logging.cs");

        // Issue #175 п.7 (1.0.1.128): галочки не сливаются с краем карточки.
        Assert.Contains("new Thickness(10, 8)", code);
        Assert.DoesNotContain("new Thickness(8), 0", code);
    }

    [Fact]
    public void ContentRightInset_IsGreaterThanTwelveAndIncludesScrollBar()
    {
        var metrics = ReadProjectFile("Workspace1C", "Controls", "UiMetrics.Avalonia.cs");
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");

        // Issue #174 (1.0.1.129): правый отступ контента больше 12 DIP
        // и складывается из бокового отступа и ширины скроллбара.
        Assert.Contains("public const double ScrollBarWidth = 12", metrics);
        Assert.Contains("public const double SettingsSideInset = 12", metrics);
        Assert.Contains("public const double SettingsContentRightInset = 12 + ScrollBarWidth", metrics);
        Assert.Contains(
            "public const double SettingsContentRightPadding = SettingsContentRightInset + SettingsSideInset",
            metrics);
        Assert.Contains(
            "public const double SettingsContentTotalRightInset = SettingsSideInset + SettingsContentRightPadding",
            metrics);
        // Итоговый padding складывается из базового запаса и бокового поля,
        // то есть гарантированно больше прежних 12 DIP.
        // Единый помощник правого padding вкладок настроек.
        Assert.Contains("private static Thickness SettingsScrollPadding =>", code);
        Assert.Contains("new(0, 0, UiMetrics.SettingsContentRightPadding, 0)", code);

        // Ширина вкладок и отсутствие обрезки «Резервное копирование» сохранены.
        Assert.Contains("tab.Width = UiMetrics.Scaled(260)", code);
        Assert.Contains("tab.MinWidth = UiMetrics.Scaled(240)", code);
    }

    [Fact]
    public void EverySettingsTab_UsesUnifiedRightPadding()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");

        // Issue #174 (1.0.1.129): один и тот же правый padding у всех вкладок
        // (клавиши, меню «Утилиты», логирование, о программе, ibases.v8i, темы,
        // платформы, цветовое оформление). Раньше часть вкладок имела 0/4/12 DIP.
        var applied = CountOccurrences(code, "Padding = SettingsScrollPadding");
        Assert.True(applied >= 8,
            $"Единый правый padding применён только к {applied} прокруткам вкладок (ожидалось ≥ 8)");
        Assert.DoesNotContain("Padding = new Thickness(0, 0, 4, 0)", code);
        Assert.DoesNotContain("Padding = new Thickness(0, 0, 12, 0)", code);

        // Вкладки без собственной прокрутки задают полный правый отступ полем.
        Assert.Contains("UiMetrics.SettingsContentTotalRightInset", code);
    }

    [Fact]
    public void SubTabs_UseUnifiedRightInset()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");

        // Issue #174 (1.0.1.129): вложенные подвкладки («Настройки», «Отображение»,
        // «Базы») тоже получают единый правый отступ, а не прежние 4 DIP.
        Assert.Contains("bool nestedInInset = false", code);
        Assert.Contains("UiMetrics.SettingsContentTotalRightInset, 4", code);
        Assert.Contains("nestedInInset: true", code);
    }

    [Fact]
    public void RightAlignedSwitches_UseFullWidthRowTemplate()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");

        // Issue #174 (1.0.1.129): баг выравнивания — переключатель в строке меню
        // лежал в колонке фиксированной ширины 210 DIP, и дорожка оказывалась
        // в середине строки, а не у правого края.
        var start = code.IndexOf("private static Control MenuSettingsRow(", StringComparison.Ordinal);
        Assert.True(start > 0, "Не найдена строка настроек меню MenuSettingsRow");
        var end = code.IndexOf("\n        }", start, StringComparison.Ordinal);
        Assert.True(end > start, "Не найден конец MenuSettingsRow");
        var block = code[start..end];

        Assert.DoesNotContain("Width = 210", block);
        Assert.DoesNotContain("ColumnDefinitions(\"210,*\")", block);
        Assert.Contains("ColumnDefinitions = new ColumnDefinitions(\"Auto,*\")", block);
        Assert.Contains("HorizontalAlignment = HorizontalAlignment.Stretch", block);
    }

    [Fact]
    public void SubTabs_DisableHorizontalScrollingSoLongLabelsWrap()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");

        // Issue #174 (1.0.1.130): пока у ScrollViewer подвкладки горизонтальная
        // прокрутка была Auto, содержимое мерилось бесконечной шириной, TextWrapping
        // у длинной подписи переключателя не срабатывал, и дорожка уезжала за край.
        var start = code.IndexOf("private static TabItem SubTab(", StringComparison.Ordinal);
        Assert.True(start > 0, "Не найден метод SubTab");
        var end = code.IndexOf("\n        }", start, StringComparison.Ordinal);
        Assert.True(end > start, "Не найден конец метода SubTab");
        var block = code[start..end];

        Assert.Contains("HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled", block);

        // Все прокрутки окна настроек ограничивают ширину — иначе перенос
        // подписей снова отключится. Исключение одно: просмотр картинки
        // donat.png в полном размере (ShowDonatImageFullAsync) — там
        // горизонтальная прокрутка нужна намеренно.
        var scrollViewers = CountOccurrences(code, "new ScrollViewer");
        var disabled = CountOccurrences(
            code, "HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled");
        Assert.True(scrollViewers > 0);
        Assert.True(disabled >= scrollViewers - 1,
            $"Горизонтальная прокрутка выключена у {disabled} из {scrollViewers} ScrollViewer");
        var donat = code.IndexOf("ShowDonatImageFullAsync", StringComparison.Ordinal);
        Assert.True(donat > 0, "Не найден просмотр donat.png");
        var donatBlock = code[donat..];
        Assert.Contains("HorizontalScrollBarVisibility = ScrollBarVisibility.Auto",
            donatBlock, StringComparison.Ordinal);

        // Подписи переключателей переносятся по словам, а не обрезаются.
        var switchStart = code.IndexOf("private static ToggleButton SettingsSwitch(", StringComparison.Ordinal);
        Assert.True(switchStart > 0, "Не найден метод SettingsSwitch");
        var switchEnd = code.IndexOf("\n        }", switchStart, StringComparison.Ordinal);
        Assert.True(switchEnd > switchStart, "Не найден конец метода SettingsSwitch");
        Assert.Contains("TextWrapping = TextWrapping.Wrap", code[switchStart..switchEnd],
            StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsToggleTemplate_PinsShrinkableCaptionColumnAndFixedTrack()
    {
        var controls = ReadProjectFile("Workspace1C", "Themes", "Controls.axaml");
        var block = ExtractBetween(controls, "x:Key=\"SettingsToggle\"",
            "x:Key=\"ColumnVisibilitySwitch\"");

        // Issue #174 (1.0.1.132): причина «улетающей» галочки — звёздная колонка
        // подписи в шаблоне переключателя не была закреплена и могла раздуваться
        // содержимым, вынося дорожку за правый край. Теперь колонки заданы явно:
        // подпись — «*» с MinWidth=0, дорожка — Auto с фиксированной шириной.
        Assert.Contains("<ColumnDefinition Width=\"*\" MinWidth=\"0\"/>", block);
        Assert.Contains("<ColumnDefinition Width=\"Auto\"/>", block);
        Assert.DoesNotContain("ColumnDefinitions=\"*,Auto\"", block);

        // Содержимое растягивается по колонке, чтобы перенос подписи получал
        // конечную ширину; дорожка прижата к правому краю и не сжимается.
        Assert.Contains("HorizontalAlignment=\"Stretch\"", block);
        Assert.Contains(
            "HorizontalContentAlignment=\"{TemplateBinding HorizontalContentAlignment}\"", block);
        Assert.Contains("Name=\"PART_Track\"", block);
        Assert.Contains("Width=\"40\"", block);
        Assert.Contains("HorizontalAlignment=\"Right\"", block);
    }

    [Fact]
    public void SettingsSwitch_CaptionColumnCanShrinkAndWraps()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");
        var start = code.IndexOf("private static ToggleButton SettingsSwitch(", StringComparison.Ordinal);
        Assert.True(start > 0, "Не найден метод SettingsSwitch");
        var end = code.IndexOf("private static ToggleButton DisplayCheck(", start, StringComparison.Ordinal);
        Assert.True(end > start, "Не найден конец метода SettingsSwitch");
        var block = code[start..end];

        // Issue #174 (1.0.1.132): подпись лежит в звёздной колонке, которая может
        // сжиматься (MinWidth=0), и переносится по словам; сам переключатель
        // растягивается по ширине строки.
        Assert.Contains("ColumnDefinitions(\"Auto,*,Auto\")", block);
        Assert.Contains("grid.ColumnDefinitions[1].MinWidth = 0", block);
        Assert.Contains("TextWrapping = TextWrapping.Wrap", block);
        Assert.Contains("MinWidth = 0", block);
        Assert.Contains("HorizontalAlignment = HorizontalAlignment.Stretch", block);
        Assert.Contains("HorizontalContentAlignment = HorizontalAlignment.Stretch", block);
    }

    [Fact]
    public void SwitchTrack_FitsInsideContentAtCommonWindowWidths()
    {
        var metrics = ReadProjectFile("Workspace1C", "Controls", "UiMetrics.Avalonia.cs");
        var controls = ReadProjectFile("Workspace1C", "Themes", "Controls.axaml");
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");

        // Итоговый правый отступ подвкладки (базовый запас под скроллбар + боковое поле).
        var rightInset = ParseConstant(metrics, "SettingsContentTotalRightInset");
        Assert.True(rightInset > 0, "Правый отступ подвкладки должен быть положительным");

        // Ширина дорожки и её левый отступ из шаблона SettingsToggle.
        var toggleTheme = ExtractBetween(controls, "x:Key=\"SettingsToggle\"",
            "x:Key=\"ColumnVisibilitySwitch\"");
        var trackMatch = Regex.Match(toggleTheme, @"Name=""PART_Track""\s+Width=""(\d+)""");
        Assert.True(trackMatch.Success, "Не найдена ширина дорожки PART_Track");
        var track = double.Parse(trackMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        var trackGap = 12; // Margin="12,0,0,0" у панели дорожки

        // Ширина колонки вкладок и зазор контента из шаблона ConnTabControl.
        var navMatch = Regex.Match(code, @"tab\.Width = UiMetrics\.Scaled\((\d+)\)");
        Assert.True(navMatch.Success, "Не найдена ширина колонки вкладок");
        var navWidth = double.Parse(navMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        var connTheme = ExtractBetween(controls, "x:Key=\"ConnTabControl\"",
            "x:Key=\"ConnTabItem\"");
        var navGapMatch = Regex.Match(connTheme, @"Margin=""(\d+),0,0,0""");
        var navGap = navGapMatch.Success
            ? double.Parse(navGapMatch.Groups[1].Value, CultureInfo.InvariantCulture)
            : 8;

        // Внешнее поле корня окна настроек (BuildRoot: new Grid { Margin = new Thickness(16) }).
        var marginMatch = Regex.Match(code, @"new Grid \{ Margin = new Thickness\((\d+)\) \}");
        var windowMargin = marginMatch.Success
            ? double.Parse(marginMatch.Groups[1].Value, CultureInfo.InvariantCulture)
            : 16;

        foreach (var window in new[] { 640d, 800d, 1024d })
        {
            var content = window - 2 * windowMargin - navWidth - navGap - rightInset;
            Assert.True(content > track + trackGap,
                $"При ширине окна {window} DIP контент {content} DIP не вмещает дорожку " +
                $"{track + trackGap} DIP — галочка выйдет за правый край");

            var caption = content - track - trackGap;
            Assert.True(caption >= 160,
                $"При ширине окна {window} DIP подписи остаётся только {caption} DIP — " +
                "длинный текст не переносится, а выталкивает дорожку");
        }
    }

    [Fact]
    public void SubTabs_ReserveScrollBarGutterIndependentlyOfVisibility()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");
        var start = code.IndexOf("private static TabItem SubTab(", StringComparison.Ordinal);
        Assert.True(start > 0, "Не найден метод SubTab");
        var end = code.IndexOf("\n        }", start, StringComparison.Ordinal);
        Assert.True(end > start, "Не найден конец метода SubTab");
        var block = code[start..end];

        // Issue #174 (1.0.1.132): вертикальная полоса — оверлей (AllowAutoHide),
        // поэтому её появление не отнимает ширину у строк; правый отступ уже
        // включает ScrollBarWidth и не зависит от видимости скроллбара.
        Assert.Contains("AllowAutoHide = true", block);
        Assert.Contains("HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled", block);
        Assert.Contains("UiMetrics.SettingsContentTotalRightInset", block);
    }

    private static string ExtractBetween(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Не найден маркер «{startMarker}»");
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Не найден маркер «{endMarker}» после «{startMarker}»");
        return text[start..end];
    }

    /// <summary>
    /// Достаёт числовое значение константы из исходника UiMetrics. Поддерживает
    /// простые суммы вида <c>12 + ScrollBarWidth</c> и ссылки на другие константы.
    /// </summary>
    private static double ParseConstant(string source, string name)
    {
        var match = Regex.Match(source, $@"public const double {Regex.Escape(name)} = ([^;]+);");
        Assert.True(match.Success, $"Не найдена константа UiMetrics.{name}");
        var expression = match.Groups[1].Value.Trim();

        var total = 0d;
        foreach (var part in expression.Split('+'))
        {
            var token = part.Trim();
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                total += value;
            else
                total += ParseConstant(source, token);
        }

        return total;
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
