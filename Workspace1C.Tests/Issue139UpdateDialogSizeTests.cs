using System;
using System.IO;
using System.Text.RegularExpressions;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #139 (1.0.1.84): окно «Доступна новая версия приложения для загрузки»
/// обрезалось справа при клике по границе — оставалась видна только кнопка
/// «Перезапустить». Проверяем чистую политику размеров, разметку WPF (перенос
/// кнопок) и платформенные оболочки (запрет resize и ограничение рабочей областью).
/// </summary>
public sealed class Issue139UpdateDialogSizeTests
{
    // ===================== Чистая политика размеров =====================

    [Fact]
    public void Clamp_RespectsMaxWidthAndMaxHeight()
    {
        var size = UpdateDialogSizePolicy.Clamp(1000, 900, 1920, 1080);

        Assert.Equal(UpdateDialogSizePolicy.MaxWidth, size.Width);
        Assert.Equal(UpdateDialogSizePolicy.MaxHeight, size.Height);
    }

    [Fact]
    public void Clamp_RespectsSmallWorkArea()
    {
        var size = UpdateDialogSizePolicy.Clamp(1000, 900, 700, 500);

        var expectedWidth = 700 - 2 * UpdateDialogSizePolicy.WorkAreaMargin;
        var expectedHeight = 500 - 2 * UpdateDialogSizePolicy.WorkAreaMargin;
        Assert.Equal(expectedWidth, size.Width);
        Assert.Equal(expectedHeight, size.Height);
        // Окно целиком помещается в рабочую область с отступами по краям.
        Assert.True(size.Width <= 700 - 2 * UpdateDialogSizePolicy.WorkAreaMargin);
        Assert.True(size.Height <= 500 - 2 * UpdateDialogSizePolicy.WorkAreaMargin);
    }

    [Fact]
    public void Clamp_NeverBelowMinWidth()
    {
        var size = UpdateDialogSizePolicy.Clamp(300, 200, 1920, 1080);

        Assert.Equal(UpdateDialogSizePolicy.MinWidth, size.Width);
    }

    [Fact]
    public void Clamp_UnknownWorkArea_UsesMaxLimits()
    {
        var size = UpdateDialogSizePolicy.Clamp(1000, 900, 0, 0);

        Assert.Equal(UpdateDialogSizePolicy.MaxWidth, size.Width);
        Assert.Equal(UpdateDialogSizePolicy.MaxHeight, size.Height);
    }

    [Fact]
    public void Clamp_NegativeWorkArea_TreatedAsUnknown()
    {
        // Отрицательные размеры рабочей области означают «неизвестно».
        var size = UpdateDialogSizePolicy.Clamp(1000, 900, -1, -1);

        Assert.Equal(UpdateDialogSizePolicy.MaxWidth, size.Width);
        Assert.Equal(UpdateDialogSizePolicy.MaxHeight, size.Height);
    }

    [Fact]
    public void Clamp_NaN_DesiredSize_UsesSafeDefaults()
    {
        // NaN появляется у SizeToContent, когда размер ещё не вычислен:
        // ширина не должна стать меньше MinWidth, высота — больше MaxHeight.
        var size = UpdateDialogSizePolicy.Clamp(double.NaN, double.NaN, 1920, 1080);

        Assert.Equal(UpdateDialogSizePolicy.MinWidth, size.Width);
        Assert.Equal(UpdateDialogSizePolicy.MaxHeight, size.Height);
    }

    [Fact]
    public void Clamp_TinyWorkArea_KeepsMinWidthForContent()
    {
        // Экран меньше контента: ширина не опускается ниже MinWidth (иначе кнопки
        // обрезаются), высота ограничена рабочей областью с отступами.
        var size = UpdateDialogSizePolicy.Clamp(1000, 900, 300, 200);

        Assert.Equal(UpdateDialogSizePolicy.MinWidth, size.Width);
        Assert.Equal(200 - 2 * UpdateDialogSizePolicy.WorkAreaMargin, size.Height);
    }

    [Theory]
    [InlineData(10)] // HTLEFT
    [InlineData(11)] // HTRIGHT — именно эта граница давала обрезание справа
    [InlineData(12)] // HTTOP
    [InlineData(13)] // HTTOPLEFT
    [InlineData(14)] // HTTOPRIGHT
    [InlineData(15)] // HTBOTTOM
    [InlineData(16)] // HTBOTTOMLEFT
    [InlineData(17)] // HTBOTTOMRIGHT
    public void ShouldBlockResize_BlocksEachBorderCode(int code)
    {
        Assert.True(UpdateDialogSizePolicy.IsBorderHitTestCode(code));
        Assert.True(UpdateDialogSizePolicy.ShouldBlockResize(code, true, true),
            $"Код {code} должен блокироваться");
    }

    [Theory]
    [InlineData(0)]  // HTNOWHERE
    [InlineData(1)]  // HTCLIENT
    [InlineData(2)]  // HTCAPTION
    [InlineData(9)]  // HTVSCROLL — не граница окна
    [InlineData(18)] // HTBORDER — системная рамка, не кнопки ресайза
    public void ShouldBlockResize_DoesNotBlockNonBorderCodes(int code)
    {
        Assert.False(UpdateDialogSizePolicy.IsBorderHitTestCode(code));
        Assert.False(UpdateDialogSizePolicy.ShouldBlockResize(code, true, true));
    }

    [Fact]
    public void ShouldBlockResize_BlocksAllBorderCodes_WhenNoResizeAndSizeToContent()
    {
        // HTLEFT=10 … HTBOTTOMRIGHT=17 — все границы окна.
        for (var code = UpdateDialogSizePolicy.HtLeft;
             code <= UpdateDialogSizePolicy.HtBottomRight;
             code++)
        {
            Assert.True(UpdateDialogSizePolicy.ShouldBlockResize(code, true, true),
                $"Код {code} должен блокироваться");
        }
    }

    [Fact]
    public void ShouldBlockResize_DoesNotBlockWithoutBothFlags()
    {
        Assert.False(UpdateDialogSizePolicy.ShouldBlockResize(
            UpdateDialogSizePolicy.HtRight, sizeToContent: false, noResize: true));
        Assert.False(UpdateDialogSizePolicy.ShouldBlockResize(
            UpdateDialogSizePolicy.HtRight, sizeToContent: true, noResize: false));
    }

    [Fact]
    public void ShouldBlockResize_AllowsClientAndCaption()
    {
        Assert.False(UpdateDialogSizePolicy.ShouldBlockResize(
            UpdateDialogSizePolicy.HtClient, true, true));
        Assert.False(UpdateDialogSizePolicy.ShouldBlockResize(
            UpdateDialogSizePolicy.HtCaption, true, true));
    }

    [Fact]
    public void ContentMinWidth_ButtonRow()
    {
        // 150 + 150 + 170 + 100 + 3×10 (отступы) + 120 (иконка, поля, рамка).
        var width = UpdateDialogSizePolicy.ContentMinWidth(
            new double[] { 150, 150, 170, 100 }, spacing: 10, chrome: 120);

        Assert.Equal(720, width);
    }

    [Fact]
    public void ContentMinWidth_SingleButton()
    {
        // Одна кнопка: отступы между кнопками не добавляются.
        var width = UpdateDialogSizePolicy.ContentMinWidth(
            new double[] { 150 }, spacing: 10, chrome: 120);

        Assert.Equal(270, width);
    }

    [Fact]
    public void MinWidth_FitsButtonRowsInSingleRow()
    {
        // Issue #139 (1.0.1.97): минимальная ширина окна должна вмещать обе
        // строки кнопок без переноса, иначе «Отмена»/вторая кнопка уезжает
        // на новую строку. Chrome (поля, иконка, рамка) — 120 DIP.
        var offerRow = UpdateDialogSizePolicy.ContentMinWidth(
            new double[] { 150, 170, 100 }, spacing: 10, chrome: 120);
        var restartRow = UpdateDialogSizePolicy.ContentMinWidth(
            new double[] { 230, 240 }, spacing: 10, chrome: 120);

        Assert.True(UpdateDialogSizePolicy.MinWidth >= offerRow,
            $"MinWidth {UpdateDialogSizePolicy.MinWidth} меньше строки предложения {offerRow}");
        Assert.True(UpdateDialogSizePolicy.MinWidth >= restartRow,
            $"MinWidth {UpdateDialogSizePolicy.MinWidth} меньше строки перезапуска {restartRow}");
    }

    [Fact]
    public void ContentMinWidth_Empty_ReturnsChrome()
    {
        var width = UpdateDialogSizePolicy.ContentMinWidth(
            Array.Empty<double>(), spacing: 10, chrome: 120);

        Assert.Equal(120, width);
    }

    [Fact]
    public void ContentMinWidth_RestartRow()
    {
        // «Перезапустить сейчас» 230 + «Обновить после закрытия» 240 + отступ 10 + chrome.
        var width = UpdateDialogSizePolicy.ContentMinWidth(
            new double[] { 230, 240 }, spacing: 10, chrome: 120);

        Assert.Equal(600, width);
    }

    [Fact]
    public void ContentMinWidth_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            UpdateDialogSizePolicy.ContentMinWidth(null!, spacing: 10, chrome: 120));
    }

    // ===================== Высота панели кнопок с переносом =====================

    [Fact]
    public void ButtonPanelHeight_TwoButtonsWrapAtNarrowWidth()
    {
        // «Перезапустить сейчас» 230 + «Обновить после закрытия» 240 при ширине 400:
        // вторая кнопка не помещается (230 + 10 + 240 > 400) → две строки.
        var height = UpdateDialogSizePolicy.ButtonPanelHeight(
            new double[] { 230, 240 }, availableWidth: 400, spacing: 10,
            rowHeight: UpdateDialogSizePolicy.ButtonHeight);

        Assert.Equal(
            2 * UpdateDialogSizePolicy.ButtonHeight + UpdateDialogSizePolicy.ButtonRowGap,
            height);
    }

    [Fact]
    public void ButtonPanelHeight_TwoButtonsSingleRowAtWideWidth()
    {
        // При ширине 700 обе кнопки помещаются в одну строку.
        var height = UpdateDialogSizePolicy.ButtonPanelHeight(
            new double[] { 230, 240 }, availableWidth: 700, spacing: 10,
            rowHeight: UpdateDialogSizePolicy.ButtonHeight);

        Assert.Equal(UpdateDialogSizePolicy.ButtonHeight, height);
    }

    [Fact]
    public void ButtonPanelHeight_AccountsForSpacingBetweenButtons()
    {
        // 230 + 10 + 240 = 480: ровно при 480 — одна строка, при 479 — перенос.
        var oneRow = UpdateDialogSizePolicy.ButtonPanelHeight(
            new double[] { 230, 240 }, availableWidth: 480, spacing: 10,
            rowHeight: UpdateDialogSizePolicy.ButtonHeight);
        var twoRows = UpdateDialogSizePolicy.ButtonPanelHeight(
            new double[] { 230, 240 }, availableWidth: 479, spacing: 10,
            rowHeight: UpdateDialogSizePolicy.ButtonHeight);

        Assert.Equal(UpdateDialogSizePolicy.ButtonHeight, oneRow);
        Assert.Equal(
            2 * UpdateDialogSizePolicy.ButtonHeight + UpdateDialogSizePolicy.ButtonRowGap,
            twoRows);
    }

    [Fact]
    public void ButtonPanelHeight_NoButtons_IsZero()
    {
        Assert.Equal(0, UpdateDialogSizePolicy.ButtonPanelHeight(
            Array.Empty<double>(), availableWidth: 400, spacing: 10,
            rowHeight: UpdateDialogSizePolicy.ButtonHeight));
    }

    [Fact]
    public void ButtonPanelHeight_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            UpdateDialogSizePolicy.ButtonPanelHeight(
                null!, availableWidth: 400, spacing: 10,
                rowHeight: UpdateDialogSizePolicy.ButtonHeight));
    }

    // ===================== Итоговая высота окна =====================

    [Fact]
    public void ClampWindowHeight_NeverBelowMinHeight()
    {
        Assert.Equal(
            UpdateDialogSizePolicy.MinHeight,
            UpdateDialogSizePolicy.ClampWindowHeight(50, 1080));
    }

    [Fact]
    public void ClampWindowHeight_NeverAboveMaxHeight()
    {
        Assert.Equal(
            UpdateDialogSizePolicy.MaxHeight,
            UpdateDialogSizePolicy.ClampWindowHeight(2000, 1080));
    }

    [Fact]
    public void ClampWindowHeight_NeverAboveWorkArea()
    {
        Assert.Equal(
            500 - 2 * UpdateDialogSizePolicy.WorkAreaMargin,
            UpdateDialogSizePolicy.ClampWindowHeight(2000, 500));
    }

    [Fact]
    public void ClampWindowHeight_TinyWorkArea_StillFitsScreen()
    {
        // Экран ниже минимальной высоты: приоритет у рабочей области, длинное
        // содержимое прокручивается, но окно не выходит за экран.
        var height = UpdateDialogSizePolicy.ClampWindowHeight(1000, 150);

        Assert.Equal(150 - 2 * UpdateDialogSizePolicy.WorkAreaMargin, height);
        Assert.True(height < UpdateDialogSizePolicy.MinHeight);
    }

    // ===================== WPF: разметка =====================

    [Fact]
    public void Wpf_Xaml_ButtonRowsUseWrapPanel()
    {
        var xaml = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "UpdateAvailableWindow.xaml"));

        // Issue #139: оба блока кнопок (предложение и перезапуск) — WrapPanel.
        Assert.Equal(2, Regex.Matches(xaml, "<WrapPanel").Count);
        Assert.DoesNotContain("<StackPanel Orientation=\"Horizontal\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DownloadButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RestartNowButton\"", xaml, StringComparison.Ordinal);

        // Issue #139 (1.0.1.95): авторазмер по обеим осям убран — он и давал
        // обрезание снизу после переноса кнопок.
        Assert.DoesNotContain("SizeToContent=\"WidthAndHeight\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ResizeMode=\"NoResize\"", xaml, StringComparison.Ordinal);
        // Issue #139 (1.0.1.97): 600 DIP — строка кнопок перезапуска без переноса.
        Assert.Contains("MinWidth=\"600\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"760\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"640\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"140\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_Xaml_ButtonsAreOutsideScrollViewer()
    {
        var xaml = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "UpdateAvailableWindow.xaml"));

        // Issue #139 (1.0.1.97): панели кнопок закреплены в DockPanel снизу и
        // не участвуют в прокрутке текста. Иначе при длинном описании релиза
        // появлялась полоса прокрутки, а «Отмена» оказывалась ниже остальных.
        var scroll = xaml.IndexOf("<ScrollViewer", StringComparison.Ordinal);
        Assert.True(scroll > 0, "ScrollViewer не найден");
        foreach (var button in new[]
                 {
                     "x:Name=\"DownloadButton\"", "x:Name=\"CancelButton\"",
                     "x:Name=\"RestartNowButton\"", "x:Name=\"UpdateAfterCloseButton\"",
                 })
        {
            var index = xaml.IndexOf(button, StringComparison.Ordinal);
            Assert.True(index > 0 && index < scroll,
                $"{button} должен находиться до ScrollViewer (вне прокручиваемого текста)");
        }

        Assert.Contains("DockPanel.Dock=\"Bottom\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_Xaml_ContentScrollerStartsWithoutVerticalScrollBar()
    {
        var xaml = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "UpdateAvailableWindow.xaml"));

        // Issue #139 (1.0.1.102): прокрутка тела по умолчанию выключена — полоса
        // включается кодом только при реальном переполнении. Безусловной Visible нет.
        var start = xaml.IndexOf("x:Name=\"ContentScroller\"", StringComparison.Ordinal);
        Assert.True(start > 0, "ContentScroller не найден");
        var end = xaml.IndexOf('>', start);
        var tag = xaml.Substring(start, end - start);

        Assert.Contains("VerticalScrollBarVisibility=\"Disabled\"", tag, StringComparison.Ordinal);
        Assert.DoesNotContain("VerticalScrollBarVisibility=\"Visible\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", tag, StringComparison.Ordinal);
        Assert.Contains("Focusable=\"False\"", tag, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_Xaml_WrapPanelsHoldAllButtons()
    {
        var xaml = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "UpdateAvailableWindow.xaml"));

        var panels = Regex.Matches(xaml, "<WrapPanel[^>]*>(.*?)</WrapPanel>",
            RegexOptions.Singleline);
        Assert.Equal(2, panels.Count);

        // Блок предложения: «Обновить» (скачать/установить), пропуск, отмена.
        Assert.Contains("x:Name=\"DownloadButton\"", panels[0].Groups[1].Value,
            StringComparison.Ordinal);
        Assert.Contains("x:Name=\"InstallButton\"", panels[0].Groups[1].Value,
            StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SkipVersionButton\"", panels[0].Groups[1].Value,
            StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CancelButton\"", panels[0].Groups[1].Value,
            StringComparison.Ordinal);

        // Блок перезапуска: «Перезапустить сейчас» и «Обновить после закрытия».
        Assert.Contains("x:Name=\"RestartNowButton\"", panels[1].Groups[1].Value,
            StringComparison.Ordinal);
        Assert.Contains("x:Name=\"UpdateAfterCloseButton\"", panels[1].Groups[1].Value,
            StringComparison.Ordinal);

        // Кнопки не фиксируют ширину окна: у пропуска — MinWidth, перенос делает WrapPanel.
        Assert.Contains("x:Name=\"SkipVersionButton\" MinWidth=\"170\"", xaml,
            StringComparison.Ordinal);
    }

    // ===================== WPF: код-бихайнд =====================

    [Fact]
    public void Wpf_Code_UsesScrollBarPolicy()
    {
        var source = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "UpdateAvailableWindow.xaml.cs"));

        // Issue #139 (1.0.1.102): решение о полосе — только через чистую политику,
        // при отсутствии переполнения выставляется Disabled.
        Assert.Contains("UpdateDialogSizePolicy.ShouldShowVerticalScrollBar", source,
            StringComparison.Ordinal);
        Assert.Contains("UpdateDialogSizePolicy.MeasureContentWidth", source,
            StringComparison.Ordinal);
        Assert.Contains("UpdateDialogSizePolicy.MeasureContentHeight", source,
            StringComparison.Ordinal);
        Assert.Contains("ScrollBarVisibility.Disabled", source, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.VerticalScrollBarWidth", source,
            StringComparison.Ordinal);

        // Повторное измерение после прохода раскладки — защита от обратной связи.
        Assert.Contains("UpdateLayout();", source, StringComparison.Ordinal);
        Assert.Contains("StabilizeVerticalScrollBar", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_Code_BlocksBorderResizeWithoutForbiddenIdentifiers()
    {
        var source = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "UpdateAvailableWindow.xaml.cs"));

        // Issue #139: хук WM_NCHITTEST и возврат HTCLIENT для границ.
        Assert.Contains("AddHook", source, StringComparison.Ordinal);
        Assert.Contains("UpdateDialogSizePolicy.ShouldBlockResize", source, StringComparison.Ordinal);
        Assert.Contains("HtClient", source, StringComparison.Ordinal);
        Assert.Contains("SourceInitialized", source, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.WorkArea", source, StringComparison.Ordinal);

        // Запрещённые тестами Issue116 идентификаторы не используются.
        Assert.DoesNotContain("WndProc", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WmGetMinMaxInfo", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SizeToContent = SizeToContent.Manual", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureFixedSize", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_Code_ComputesInitialSizeBeforeShow()
    {
        var source = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "UpdateAvailableWindow.xaml.cs"));

        // Первичный клампинг — в конструкторе, ДО подписки на SourceInitialized:
        // первый кадр уже ограничен рабочей областью (issue #139).
        var initialClamp = source.IndexOf("ApplyWorkAreaLimits(IntPtr.Zero);",
            StringComparison.Ordinal);
        var subscribe = source.IndexOf("SourceInitialized += OnSourceInitialized;",
            StringComparison.Ordinal);
        Assert.True(initialClamp >= 0, "Первичный ApplyWorkAreaLimits не найден");
        Assert.True(subscribe > initialClamp,
            "Размер должен вычисляться до подписки на SourceInitialized");

        // При создании HWND лимиты уточняются до подключения хука сообщений.
        var initialized = ExtractMethod(source, "private void OnSourceInitialized(");
        var handleClamp = initialized.IndexOf("ApplyWorkAreaLimits(handle);",
            StringComparison.Ordinal);
        var hook = initialized.IndexOf("AddHook(OnWindowMessage)", StringComparison.Ordinal);
        Assert.True(handleClamp >= 0 && hook > handleClamp,
            "Лимиты должны выставляться до AddHook");
    }

    [Fact]
    public void Wpf_Code_UsesOwnerMonitorWorkArea()
    {
        var source = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "UpdateAvailableWindow.xaml.cs"));

        // Многомониторные конфигурации: рабочая область монитора владельца в DIP,
        // fallback — рабочая область основного монитора.
        Assert.Contains("System.Windows.Forms.Screen.FromHandle", source, StringComparison.Ordinal);
        Assert.Contains("WorkingArea", source, StringComparison.Ordinal);
        Assert.Contains("DpiScaleX", source, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.WorkArea", source, StringComparison.Ordinal);
        Assert.Contains("UpdateDialogSizePolicy.Clamp", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_Code_ComputesExplicitHeightWithButtonWrap()
    {
        var source = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "UpdateAvailableWindow.xaml.cs"));

        // Issue #139 (1.0.1.95): высота считается явно — измерением контента при
        // итоговой ширине (WrapPanel уже перенёс кнопки) и клампом политикой.
        Assert.Contains(".Measure(", source, StringComparison.Ordinal);
        Assert.Contains("UpdateDialogSizePolicy.ClampWindowHeight", source, StringComparison.Ordinal);
        Assert.Contains("Height = height;", source, StringComparison.Ordinal);
        Assert.Contains("Width = width;", source, StringComparison.Ordinal);
        Assert.Contains("ApplyExplicitSize", source, StringComparison.Ordinal);

        // Ручного сброса в Manual нет (размер явный и без скачка), WndProc не возвращается.
        Assert.DoesNotContain("SizeToContent = SizeToContent.Manual", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WndProc", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WmGetMinMaxInfo", source, StringComparison.Ordinal);
    }

    // ===================== Avalonia =====================

    [Fact]
    public void Avalonia_FitContent_ClampsToScreen()
    {
        var window = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "MaterialMessageWindow.Avalonia.cs"));

        Assert.Contains("ClampToScreen", window, StringComparison.Ordinal);
        Assert.Contains("Screens.ScreenFromWindow", window, StringComparison.Ordinal);
        Assert.Contains("WorkingArea", window, StringComparison.Ordinal);
        Assert.Contains("RenderScaling", window, StringComparison.Ordinal);
        Assert.Contains("UpdateDialogSizePolicy.Clamp", window, StringComparison.Ordinal);
        // Кнопки диалога — в WrapPanel, а не в горизонтальном StackPanel.
        Assert.Contains("new WrapPanel", window, StringComparison.Ordinal);

        // Issue #139 (1.0.1.97): ширина согласована с политикой (600 DIP).
        Assert.Contains("MinWidth = 600;", window, StringComparison.Ordinal);
        Assert.Contains("MaxWidth = 760;", window, StringComparison.Ordinal);
        // Issue #139 (1.0.1.95): высота ограничена и считается явно, авторазмер
        // по обеим осям убран (он не учитывал перенос кнопок WrapPanel).
        Assert.Contains("MaxHeight = 640;", window, StringComparison.Ordinal);
        Assert.DoesNotContain("SizeToContent.WidthAndHeight", window, StringComparison.Ordinal);
        Assert.Contains("UpdateDialogSizePolicy.ClampWindowHeight", window, StringComparison.Ordinal);
        Assert.Contains("CanResize = false;", window, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_ClampToScreen_CoversFixedWidthDialogs()
    {
        var window = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "MaterialMessageWindow.Avalonia.cs"));

        // Issue #139: политика применяется и к диалогам с фиксированной шириной
        // (обычные сообщения, диалог перезапуска): ширина тоже клампится.
        Assert.Contains("!double.IsNaN(Width)", window, StringComparison.Ordinal);
        Assert.Contains("Width = clamped.Width", window, StringComparison.Ordinal);
        Assert.Contains("Height = clamped.Height", window, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_FitContent_UsesExplicitHeightAndBodyScroll()
    {
        var window = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "MaterialMessageWindow.Avalonia.cs"));

        // Issue #139 (1.0.1.95): явный размер вместо авторазмера по обеим осям,
        // тело прокручивается, панель кнопок закреплена снизу.
        Assert.Contains("SizeToContent.Manual", window, StringComparison.Ordinal);
        Assert.DoesNotContain("SizeToContent.WidthAndHeight", window, StringComparison.Ordinal);
        Assert.Contains(".Measure(", window, StringComparison.Ordinal);
        Assert.Contains("UpdateDialogSizePolicy.ClampWindowHeight", window, StringComparison.Ordinal);
        Assert.Contains("ScrollBarVisibility.Auto", window, StringComparison.Ordinal);
        Assert.Contains("DockPanel.SetDock(buttonsPanel, Dock.Bottom)", window, StringComparison.Ordinal);

        // Issue #139 (1.0.1.102): по умолчанию тело без полосы, решение о ней —
        // через общую политику (паритет с WPF).
        Assert.Contains("VerticalScrollBarVisibility = ScrollBarVisibility.Disabled", window,
            StringComparison.Ordinal);
        Assert.Contains("UpdateDialogSizePolicy.ShouldShowVerticalScrollBar", window,
            StringComparison.Ordinal);
        Assert.Contains("UpdateDialogSizePolicy.MeasureContentWidth", window,
            StringComparison.Ordinal);
        Assert.Contains("UpdateDialogSizePolicy.MeasureContentHeight", window,
            StringComparison.Ordinal);
        Assert.Contains("ApplyVerticalScrollBarPolicy", window, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_ConfirmUpdateWithSkip_PassesFitContent()
    {
        var service = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "AvaloniaDialogService.cs"));

        var method = ExtractMethod(service, "public UpdateDialogResult ConfirmUpdateWithSkip(");
        Assert.Contains("fitContent: true", method, StringComparison.Ordinal);

        // Issue #139: диалог перезапуска использует тот же режим окна.
        Assert.Contains("public bool ConfirmFitContent(", service, StringComparison.Ordinal);
        var restart = ExtractMethod(service, "public bool ConfirmFitContent(");
        Assert.Contains("fitContent: true", restart, StringComparison.Ordinal);
    }

    // ===================== Политика вертикальной полосы (1.0.1.102) =====================

    [Fact]
    public void ShouldShowVerticalScrollBar_WhenContentFits_ReturnsFalse()
    {
        // Issue #139 (1.0.1.102): в обычном состоянии полосы быть не должно.
        Assert.False(UpdateDialogSizePolicy.ShouldShowVerticalScrollBar(300, 300));
        Assert.False(UpdateDialogSizePolicy.ShouldShowVerticalScrollBar(250, 300));
        Assert.False(UpdateDialogSizePolicy.ShouldShowVerticalScrollBar(0, 300));
    }

    [Fact]
    public void ShouldShowVerticalScrollBar_WhenContentOverflows_ReturnsTrue()
    {
        Assert.True(UpdateDialogSizePolicy.ShouldShowVerticalScrollBar(301, 300));
        Assert.True(UpdateDialogSizePolicy.ShouldShowVerticalScrollBar(600, 300));
    }

    [Fact]
    public void ShouldShowVerticalScrollBar_EpsilonHandlesRounding()
    {
        // Разница в пределах epsilon — округление раскладки/DPI, полоса не нужна.
        Assert.False(UpdateDialogSizePolicy.ShouldShowVerticalScrollBar(300.4, 300));
        Assert.False(UpdateDialogSizePolicy.ShouldShowVerticalScrollBar(300.5, 300));
        // Больше epsilon — уже реальное переполнение.
        Assert.True(UpdateDialogSizePolicy.ShouldShowVerticalScrollBar(300.6, 300));
        // Явно заданный epsilon соблюдается.
        Assert.False(UpdateDialogSizePolicy.ShouldShowVerticalScrollBar(301, 300, epsilon: 2));
    }

    [Fact]
    public void ShouldShowVerticalScrollBar_NaN_ReturnsFalse()
    {
        Assert.False(UpdateDialogSizePolicy.ShouldShowVerticalScrollBar(double.NaN, 300));
        Assert.False(UpdateDialogSizePolicy.ShouldShowVerticalScrollBar(300, double.NaN));
    }

    [Fact]
    public void ScrollBarWidth_ReturnsZeroWhenHidden()
    {
        Assert.Equal(0, UpdateDialogSizePolicy.ScrollBarWidth(false, 17));
        Assert.Equal(0, UpdateDialogSizePolicy.ScrollBarWidth(true, 0));
        Assert.Equal(0, UpdateDialogSizePolicy.ScrollBarWidth(true, -5));
    }

    [Fact]
    public void ScrollBarWidth_ReturnsWidthWhenVisible()
    {
        Assert.Equal(17, UpdateDialogSizePolicy.ScrollBarWidth(true, 17));
        Assert.Equal(UpdateDialogSizePolicy.DefaultScrollBarWidth,
            UpdateDialogSizePolicy.ScrollBarWidth(true, UpdateDialogSizePolicy.DefaultScrollBarWidth));
    }

    [Fact]
    public void MeasureContentWidth_ReservesScrollBarWidthOnlyWhenVisible()
    {
        // Issue #139 (1.0.1.102): при измерении под полосу ширина уменьшается на
        // её ширину — текст переносится так же, как при реально видимой полосе.
        Assert.Equal(700, UpdateDialogSizePolicy.MeasureContentWidth(700, false, 17));
        Assert.Equal(683, UpdateDialogSizePolicy.MeasureContentWidth(700, true, 17));
        // Ширина не уходит в минус на узком теле.
        Assert.Equal(0, UpdateDialogSizePolicy.MeasureContentWidth(10, true, 17));
    }

    [Fact]
    public void MeasureContentHeight_SubtractsPaddingButtonsAndGap()
    {
        // Высота тела = клиентская высота − отступы − панель кнопок − зазор.
        Assert.Equal(
            506,
            UpdateDialogSizePolicy.MeasureContentHeight(
                clientHeight: 600, verticalPadding: 40, buttonPanelHeight: 34, buttonGap: 20));
    }

    [Fact]
    public void MeasureContentHeight_NeverNegative()
    {
        Assert.Equal(0, UpdateDialogSizePolicy.MeasureContentHeight(
            clientHeight: 30, verticalPadding: 40, buttonPanelHeight: 34, buttonGap: 20));
        Assert.Equal(0, UpdateDialogSizePolicy.MeasureContentHeight(
            clientHeight: double.NaN, verticalPadding: 40, buttonPanelHeight: 34, buttonGap: 20));
    }

    [Fact]
    public void ScrollBarPolicy_IsSharedByWpfAndAvalonia()
    {
        // Паритет: обе оболочки принимают решение о полосе через одну политику,
        // а не собственные пороги.
        var wpf = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "UpdateAvailableWindow.xaml.cs"));
        var avalonia = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "MaterialMessageWindow.Avalonia.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            Assert.Contains("UpdateDialogSizePolicy.ShouldShowVerticalScrollBar", source,
                StringComparison.Ordinal);
            Assert.Contains("UpdateDialogSizePolicy.MeasureContentWidth", source,
                StringComparison.Ordinal);
            Assert.Contains("UpdateDialogSizePolicy.MeasureContentHeight", source,
                StringComparison.Ordinal);
            Assert.Contains("ScrollBarVisibility.Disabled", source, StringComparison.Ordinal);
        }

        // Диалог подтверждения перезапуска использует тот же режим fitContent.
        var dialogs = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "AvaloniaDialogService.cs"));
        Assert.Contains("fitContent: true", ExtractMethod(
            dialogs, "public bool ConfirmFitContent("), StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_RestartDialog_UsesSameSizePolicy()
    {
        var update = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "UpdateService.Avalonia.cs"));

        // Оба диалога перезапуска (готовый файл и после скачивания) идут через
        // ConfirmRestartNow → ConfirmFitContent (определение + два вызова).
        Assert.True(Regex.Matches(update, "ConfirmRestartNow\\(").Count >= 3);
        var helper = ExtractMethod(update, "private bool ConfirmRestartNow(");
        Assert.Contains("ConfirmFitContent", helper, StringComparison.Ordinal);
        Assert.Contains("Update.RestartNowPrompt", update, StringComparison.Ordinal);
    }

    // ===================== Вспомогательное =====================

    /// <summary>Вырезает тело метода по сигнатуре (до парной закрывающей скобки).</summary>
    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Метод не найден: {signature}");

        var brace = source.IndexOf('{', start);
        Assert.True(brace > start, $"Тело метода не найдено: {signature}");

        var depth = 0;
        for (var i = brace; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source.Substring(start, i - start + 1);
            }
        }

        Assert.Fail($"Не найдена закрывающая скобка метода: {signature}");
        return string.Empty;
    }

    private static string ReadProjectFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var fullPath = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(fullPath))
                return File.ReadAllText(fullPath);
            dir = dir.Parent;
        }

        Assert.Fail($"Файл проекта не найден: {relativePath}");
        return string.Empty;
    }
}
