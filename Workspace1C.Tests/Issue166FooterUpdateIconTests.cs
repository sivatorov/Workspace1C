using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #166 «Linux — кнопка обновления»: в Avalonia-подвале значок обновления слева
/// от версии был невидим — спецификация «@AccentBrush» передавалась в перегрузку
/// <c>IconHelper.MakeIcon(string, double, string)</c>, которая трактует аргумент как
/// <b>ключ ресурса</b> для <c>DynamicResource</c>; ключа с префиксом «@» нет, поэтому
/// <c>Path.Fill</c> не разрешался. Дополнительно: контейнер значка не имел
/// прозрачного фона (клик работал только по глифам), а
/// <c>RefreshFooterUpdateState</c> при отсутствии службы обновления оставлял
/// <c>IsVisible=false</c> навсегда.
/// </summary>
/// <remarks>
/// <para>Avalonia-код собирается только под <c>#if LINUX</c>, поэтому проверки —
/// source-guard'ы по исходникам (по образцу <see cref="Issue168StatusBarLayoutTests"/>
/// и <see cref="Issue116UpdateModesAuditTests"/>).</para>
/// <para>Существующие стражи <c>Issue116*</c> не ослаблены: они по-прежнему требуют
/// акцентный/зелёный цвет значков — изменилась только форма записи кисти
/// (<c>ThemeBrushes.FromSpec</c> вместо ключа с «@»).</para>
/// </remarks>
public sealed class Issue166FooterUpdateIconTests
{
    private const string WpfWindowPath = "Workspace1C/Views/MainWindow.xaml";
    private const string AvaloniaWindowPath = "Workspace1C/Views/MainWindow.Avalonia.cs";

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

    private static string AvaloniaSource() => ReadProjectFile(AvaloniaWindowPath).Replace("\r\n", "\n");

    /// <summary>Метод BuildStatusBar из Avalonia-версии главного окна.</summary>
    private static string AvaloniaStatusBar()
    {
        var code = AvaloniaSource();
        const string signature = "private Control BuildStatusBar()";
        var start = code.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "MainWindow.Avalonia.cs: не найден BuildStatusBar");

        var end = code.IndexOf("\n        }\n", start, StringComparison.Ordinal);
        Assert.True(end > start, "MainWindow.Avalonia.cs: не найден конец BuildStatusBar");
        return code.Substring(start, end - start);
    }

    /// <summary>Фрагмент BuildStatusBar с контейнером значка и тремя глифами.</summary>
    private static string AvaloniaFooterIconBlock()
    {
        var bar = AvaloniaStatusBar();
        var start = bar.IndexOf("_footerUpdateIcon = new StackPanel", StringComparison.Ordinal);
        Assert.True(start >= 0, "MainWindow.Avalonia.cs: не найден контейнер значка обновления");

        var end = bar.IndexOf("footerPanel.Children.Add(_footerUpdateIcon);", start, StringComparison.Ordinal);
        Assert.True(end > start, "MainWindow.Avalonia.cs: не найден конец блока значка обновления");
        return bar.Substring(start, end - start);
    }

    /// <summary>Метод RefreshFooterUpdateState из Avalonia-версии главного окна.</summary>
    private static string AvaloniaRefreshMethod()
    {
        var code = AvaloniaSource();
        var start = code.IndexOf("private void RefreshFooterUpdateState()", StringComparison.Ordinal);
        Assert.True(start >= 0, "MainWindow.Avalonia.cs: не найден RefreshFooterUpdateState");

        var end = code.IndexOf("private static void LogFooterUpdateWarning", start, StringComparison.Ordinal);
        Assert.True(end > start, "MainWindow.Avalonia.cs: не найден конец RefreshFooterUpdateState");
        return code.Substring(start, end - start);
    }

    /// <summary>Метод LogFooterUpdateWarning из Avalonia-версии главного окна.</summary>
    private static string AvaloniaLogWarningMethod()
    {
        var code = AvaloniaSource();
        var start = code.IndexOf("private static void LogFooterUpdateWarning", StringComparison.Ordinal);
        Assert.True(start >= 0, "MainWindow.Avalonia.cs: не найден LogFooterUpdateWarning");

        var end = code.IndexOf("private void RefreshFooterVersionText()", start, StringComparison.Ordinal);
        Assert.True(end > start, "MainWindow.Avalonia.cs: не найден конец LogFooterUpdateWarning");
        return code.Substring(start, end - start);
    }

    [Fact]
    public void AvaloniaFooterIcons_UseResolvableBrushes()
    {
        var block = AvaloniaFooterIconBlock();

        // Перегрузка MakeIcon(..., string) подписывает Fill на DynamicResource по
        // ключу. Строка «@AccentBrush» — это спецификация FromSpec, а не ключ:
        // раньше Fill не разрешался и значок был невидим.
        Assert.False(
            Regex.IsMatch(block, @"IconHelper\.MakeIcon\([^;]*?,\s*""@[^""]*"""),
            "MakeIcon не должен получать «@...» как ключ ресурса — используйте ThemeBrushes.FromSpec");

        Assert.Contains(
            "IconHelper.MakeIcon(\"IconSync\", UiMetrics.Scaled(UiMetrics.IconControl), ThemeBrushes.FromSpec(\"@AccentBrush\"))",
            block);
        Assert.Contains(
            "IconHelper.MakeIcon(\"IconWarning\", UiMetrics.Scaled(16), ThemeBrushes.FromSpec(\"@AccentBrush\"))",
            block);
        Assert.Contains(
            "IconHelper.MakeIcon(\"IconCheck\", UiMetrics.Scaled(16), ThemeBrushes.FromSpec(\"@SuccessBrush\"))",
            block);

        // WPF-паритет по цветам: акцент для стрелок/«внимания», зелёный для готовности.
        var wpf = ReadProjectFile(WpfWindowPath).Replace("\r\n", "\n");
        var start = wpf.IndexOf("x:Name=\"FooterUpdateIcon\"", StringComparison.Ordinal);
        Assert.True(start > 0, "MainWindow.xaml: не найден FooterUpdateIcon");
        var end = wpf.IndexOf("</StackPanel>", start, StringComparison.Ordinal);
        var wpfBlock = wpf.Substring(start, end - start);
        Assert.Contains("Foreground=\"{DynamicResource AccentBrush}\"", wpfBlock);
        Assert.Contains("Foreground=\"{DynamicResource SuccessBrush}\"", wpfBlock);
    }

    [Fact]
    public void AvaloniaStatusBar_HasColumnForDownloads()
    {
        var bar = AvaloniaStatusBar();

        var outerColumns = Regex.Matches(bar, @"grid\.ColumnDefinitions\.Add\(new ColumnDefinition").Count;
        Assert.True(outerColumns >= 5, $"Ожидалось не менее 5 колонок статус-бара, найдено: {outerColumns}");

        // Фантомная колонка 5 (issue #166/#168) не должна вернуться.
        Assert.DoesNotContain("Grid.SetColumn(downloadsPanel, 5)", bar);

        var match = Regex.Match(bar, @"Grid\.SetColumn\(downloadsPanel, (\d+)\)");
        Assert.True(match.Success, "MainWindow.Avalonia.cs: не найден Grid.SetColumn для downloadsPanel");
        var index = int.Parse(match.Groups[1].Value);
        Assert.InRange(index, 0, outerColumns - 1);

        // Issue #168: панель загрузок перенесена внутрь infoPanel (колонка 2),
        // поэтому индекс обязан укладываться и в колонки вложенной панели.
        Assert.Contains("infoPanel.Children.Add(downloadsPanel);", bar);
        var infoColumns = Regex.Matches(bar, @"infoPanel\.ColumnDefinitions\.Add\(new ColumnDefinition").Count;
        Assert.True(infoColumns > index,
            $"Grid.SetColumn(downloadsPanel, {index}) вне колонок infoPanel (создано {infoColumns})");
    }

    [Fact]
    public void FooterUpdateIcon_IsHitTestVisible()
    {
        var block = AvaloniaFooterIconBlock();

        // Прозрачный фон делает кликабельной всю площадь контейнера, а не только
        // глифы; курсор-рука — паритет с WPF.
        Assert.Contains("Background = Brushes.Transparent", block);
        Assert.Contains("Cursor = new Cursor(StandardCursorType.Hand)", block);
        Assert.Contains(
            "_footerUpdateIcon.PointerReleased += (_, _) => _ = RunFooterCheckUpdatesAsync()",
            block);

        // WPF-паритет: контейнер с Background="Transparent" и Cursor="Hand"
        // (MainWindow.xaml, FooterUpdateIcon).
        var wpf = ReadProjectFile(WpfWindowPath).Replace("\r\n", "\n");
        var start = wpf.IndexOf("x:Name=\"FooterUpdateIcon\"", StringComparison.Ordinal);
        Assert.True(start > 0, "MainWindow.xaml: не найден FooterUpdateIcon");
        var end = wpf.IndexOf("</StackPanel>", start, StringComparison.Ordinal);
        var wpfBlock = wpf.Substring(start, end - start);
        Assert.Contains("Background=\"Transparent\"", wpfBlock);
        Assert.Contains("Cursor=\"Hand\"", wpfBlock);
    }

    [Fact]
    public void RefreshFooterUpdateState_DoesNotHideIcon_WhenServiceMissing()
    {
        var method = AvaloniaRefreshMethod();

        // Видимость выставляется ДО обращения к службе обновления: при её отсутствии
        // значок остаётся доступным для ручной проверки.
        var showIndex = method.IndexOf("_footerUpdateIcon.IsVisible = true", StringComparison.Ordinal);
        var syncIndex = method.IndexOf("_footerUpdateSyncIcon.IsVisible = true", StringComparison.Ordinal);
        var serviceIndex = method.IndexOf("AppServices.TryGetService<UpdateService>()", StringComparison.Ordinal);
        Assert.True(showIndex >= 0 && syncIndex >= 0,
            "RefreshFooterUpdateState должен выставлять IsVisible=true для контейнера и стрелок");
        Assert.True(serviceIndex > showIndex && serviceIndex > syncIndex,
            "Видимость значка должна выставляться до обращения к службе обновления");

        // Ветка «службы нет» оставляет стрелки и базовую подсказку ручной проверки.
        Assert.Contains("if (updateService is null)", method);
        Assert.Contains(
            "ToolTip.SetTip(_footerUpdateIcon, LocalizationManager.T(\"Main.Footer.CheckUpdates\"))",
            method);

        // В catch — предупреждение в лог (не молчаливое проглатывание), значок не скрывается.
        Assert.Contains("LogFooterUpdateWarning(ex);", method);

        var code = AvaloniaSource();
        Assert.DoesNotContain("_footerUpdateIcon.IsVisible = false", code);
    }

    [Fact]
    public void FooterUpdateWarning_LogsWithoutTokens()
    {
        var method = AvaloniaLogWarningMethod();

        Assert.Contains("AppServices.TryGetService<Services.IAppLogger>()?.Warn(", method);
        Assert.Contains("ex.GetType().Name", method);

        // Сообщения службы обновления могут содержать URL с токенами — в лог их не пишем.
        Assert.DoesNotContain("ex.Message", method);
        Assert.DoesNotContain("ex.ToString()", method);
    }
}
