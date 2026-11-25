using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #165 (1.0.1.120): окно «Как купить ПРО-версию» — нижняя панель кнопок
/// всегда видна, прокручивается только содержимое.
/// <para>
/// Причина обрезки: окно было 620×600 DIP, а <c>ScrollViewer</c> стоял в
/// <c>Auto</c>-строке сетки (<c>Rows(0, -1, 0)</c>). В Auto-строке ScrollViewer
/// измеряется по полной высоте текста и выталкивает строку статуса и кнопки за
/// нижнюю границу окна. Теперь содержимое занимает звёздную строку
/// (<c>Rows(-1, 0, 0)</c>), а панель кнопок закреплена в Auto-строке вне прокрутки;
/// размеры окна считает общая <see cref="DialogWorkAreaPolicy"/>.
/// </para>
/// </summary>
public sealed class Issue165HowToBuyLayoutTests
{
    private const string WpfWindowPath = "Workspace1C/Views/HowToBuyWindow.cs";
    private const string AvaloniaWindowPath = "Workspace1C/Views/HowToBuyWindow.Avalonia.cs";

    // ------------------------------------------------------------ политика размеров

    /// <summary>
    /// 1366×768@100 %: рабочая область ~728 DIP, окно клампится до 680 DIP —
    /// кнопки видны, прокручивается только текст.
    /// </summary>
    [Fact]
    public void Window_ClampsToWorkArea_1366x768_100()
    {
        var size = DialogWorkAreaPolicy.Recommend(DialogWorkAreaPolicy.HowToBuy, 728);

        Assert.Equal(680, size.Height, 3);
        Assert.Equal(680, size.MaxHeight, 3);
        Assert.Equal(520, size.MinHeight, 3);
        Assert.True(size.MinHeight <= size.MaxHeight,
            "MinHeight не должен превышать MaxHeight — иначе окно выйдет за экран.");
        // Текст инструкции длиннее окна — прокручивается только он, кнопки вне прокрутки.
        Assert.True(size.RequiresScrolling);
    }

    /// <summary>
    /// 1920×1080@125 %: рабочая область ~832 DIP — окно открывается целиком 720 DIP,
    /// прокрутка не требуется.
    /// </summary>
    [Fact]
    public void Window_FitsWithoutScroll_1920x1080_125()
    {
        var size = DialogWorkAreaPolicy.Recommend(DialogWorkAreaPolicy.HowToBuy, 832);

        Assert.Equal(720, size.Height, 3);
        Assert.Equal(520, size.MinHeight, 3);
        Assert.Equal(784, size.MaxHeight, 3);
        Assert.False(size.RequiresScrolling);
    }

    /// <summary>
    /// 1366×768@150 %: рабочая область ~485 DIP — окно клампится по экрану,
    /// но минимальная высота тоже не превышает максимум (кнопки остаются видны,
    /// прокручивается текст).
    /// </summary>
    [Fact]
    public void Window_ClampsToWorkArea_1366x768_150()
    {
        var size = DialogWorkAreaPolicy.Recommend(DialogWorkAreaPolicy.HowToBuy, 485.33);

        Assert.True(size.Height <= 437.33 + 0.01);
        Assert.Equal(size.MaxHeight, size.MinHeight, 3);
        Assert.True(size.RequiresScrolling);
    }

    /// <summary>Ширина окна тоже клампится по рабочей области (общий WindowSizeMath).</summary>
    [Fact]
    public void WindowWidth_ClampsToWorkArea()
    {
        Assert.Equal(400, WindowSizeMath.ClampToWorkArea(720, 400), 3);
        Assert.Equal(720, WindowSizeMath.ClampToWorkArea(720, 0), 3);
    }

    /// <summary>Профиль окна задан в общей политике: 720 по содержимому, минимум 520.</summary>
    [Fact]
    public void DialogWorkAreaPolicy_HasHowToBuyProfile()
    {
        var profile = DialogWorkAreaPolicy.HowToBuy;

        Assert.Equal(720, profile.DefaultHeight, 3);
        Assert.Equal(520, profile.MinHeight, 3);
        Assert.Equal(720, profile.ContentHeight, 3);
        Assert.True(profile.MinHeight < profile.DefaultHeight,
            "Окно должно допускать уменьшение ниже стартового размера.");
    }

    // ------------------------------------------------------------ структура окна

    /// <summary>
    /// Панель кнопок находится вне <c>ScrollViewer</c> на обеих платформах:
    /// ScrollViewer содержит только <c>panel</c>, кнопки добавлены в корневую сетку
    /// отдельной строкой.
    /// </summary>
    [Fact]
    public void Buttons_AreOutsideScrollViewer_BothPlatforms()
    {
        foreach (var source in ReadBothPlatforms())
        {
            var scrollStart = source.IndexOf("new ScrollViewer", StringComparison.Ordinal);
            Assert.True(scrollStart >= 0, "Не найден ScrollViewer.");

            var scrollEnd = source.IndexOf("};", scrollStart, StringComparison.Ordinal);
            Assert.True(scrollEnd > scrollStart, "Не найден конец инициализатора ScrollViewer.");
            var scrollBlock = source[scrollStart..scrollEnd];

            // Внутри ScrollViewer — только содержимое инструкции.
            Assert.Contains("Content = panel", scrollBlock, StringComparison.Ordinal);
            Assert.DoesNotContain("buttons", scrollBlock, StringComparison.Ordinal);
            Assert.DoesNotContain("ButtonRow", scrollBlock, StringComparison.Ordinal);

            // Кнопки создаются после ScrollViewer и кладутся в отдельную строку корня.
            var buttonsStart = source.IndexOf("var buttons", StringComparison.Ordinal);
            Assert.True(buttonsStart > scrollEnd,
                "Панель кнопок должна создаваться после прокручиваемого содержимого.");
            Assert.Contains("Place(root, buttons, 2)", source, StringComparison.Ordinal);

            // Ни содержимое, ни кнопки не добавляются в panel.
            Assert.DoesNotContain("panel.Children.Add(buttons", source, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Прокручиваемая строка — звёздная, статус и кнопки — Auto. Это гарантия,
    /// что длинный текст не «съедает» нижнюю панель (при Auto у ScrollViewer он
    /// выталкивает кнопки за границу окна).
    /// </summary>
    [Fact]
    public void LongText_DoesNotPushFooter_ScrollViewerIsStarRow()
    {
        foreach (var source in ReadBothPlatforms())
        {
            Assert.Matches(@"Rows\(\s*-1\s*,\s*0\s*,\s*0\s*\)", source);
            Assert.Contains("Place(root, scroll, 0)", source, StringComparison.Ordinal);
            Assert.Contains("Place(root, buttons, 2)", source, StringComparison.Ordinal);
        }

        // Вертикальная прокрутка включается по необходимости; горизонтальной нет —
        // длинные строки переносятся.
        var wpf = ReadProjectFile(WpfWindowPath.Split('/'));
        Assert.Contains("VerticalScrollBarVisibility = ScrollBarVisibility.Auto", wpf, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled", wpf, StringComparison.Ordinal);

        var avalonia = ReadProjectFile(AvaloniaWindowPath.Split('/'));
        Assert.Contains("VerticalScrollBarVisibility = ScrollBarVisibility.Auto", avalonia, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled", avalonia, StringComparison.Ordinal);
    }

    /// <summary>Стартовый размер окна — не меньше 720×640 DIP на обеих платформах.</summary>
    [Fact]
    public void StartSize_AtLeast_720x640()
    {
        var wpf = ReadProjectFile(WpfWindowPath.Split('/'));
        var setup = Regex.Match(wpf,
            @"WpfUi\.SetupWindow\(this,\s*""[^""]+"",\s*(?<w>[\d.]+),\s*(?<h>[\d.]+)\)");
        Assert.True(setup.Success, "WPF: не найдена настройка размера через WpfUi.SetupWindow.");
        Assert.True(double.Parse(setup.Groups["w"].Value, CultureInfo.InvariantCulture) >= 720);
        Assert.True(double.Parse(setup.Groups["h"].Value, CultureInfo.InvariantCulture) >= 640);

        var avalonia = ReadProjectFile(AvaloniaWindowPath.Split('/'));
        var widthMatch = Regex.Match(avalonia, @"(?m)^[ \t]+Width\s*=\s*(?<v>[0-9]+);");
        var heightMatch = Regex.Match(avalonia, @"(?m)^[ \t]+Height\s*=\s*(?<v>[0-9]+);");
        Assert.True(widthMatch.Success && heightMatch.Success,
            "Avalonia: не найдены стартовые Width/Height.");
        Assert.True(double.Parse(widthMatch.Groups["v"].Value, CultureInfo.InvariantCulture) >= 720);
        Assert.True(double.Parse(heightMatch.Groups["v"].Value, CultureInfo.InvariantCulture) >= 640);

        // Минимум по высоте — из общего профиля (520 DIP).
        Assert.Contains("MinHeight = DialogWorkAreaPolicy.HowToBuy.MinHeight;",
            avalonia, StringComparison.Ordinal);
    }

    /// <summary>
    /// Обе платформы применяют общую политику размеров и кламп по рабочей области
    /// монитора (<see cref="DialogWorkAreaPolicy"/> / <see cref="WindowSizeMath"/>).
    /// </summary>
    [Fact]
    public void Window_UsesSharedSizePolicy_BothPlatforms()
    {
        var wpf = ReadProjectFile(WpfWindowPath.Split('/'));
        Assert.Contains("DialogWorkAreaPolicy.Recommend(DialogWorkAreaPolicy.HowToBuy", wpf,
            StringComparison.Ordinal);
        Assert.Contains("MinHeight = size.MinHeight;", wpf, StringComparison.Ordinal);
        Assert.Contains("MaxHeight = size.MaxHeight;", wpf, StringComparison.Ordinal);
        Assert.Contains("Height = size.Height;", wpf, StringComparison.Ordinal);

        var avalonia = ReadProjectFile(AvaloniaWindowPath.Split('/'));
        Assert.Contains("ApplyWorkAreaLimits();", avalonia, StringComparison.Ordinal);
        Assert.Contains("DialogWorkAreaPolicy.Recommend(DialogWorkAreaPolicy.HowToBuy, dipHeight)",
            avalonia, StringComparison.Ordinal);
        Assert.Contains("Screens.ScreenFromWindow(this)", avalonia, StringComparison.Ordinal);
        Assert.Contains("WindowSizeMath.ClampToWorkArea(Width, maxWidth)", avalonia,
            StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ кнопки и ссылки

    /// <summary>
    /// Кнопки «Активировать», «Закрыть», копирование кода и контактные ссылки
    /// остаются подключёнными на обеих платформах.
    /// </summary>
    [Fact]
    public void Actions_RemainWired_BothPlatforms()
    {
        var wpf = ReadProjectFile(WpfWindowPath.Split('/'));
        Assert.Contains("Pro.Activate", wpf, StringComparison.Ordinal);
        Assert.Contains("Common.Close", wpf, StringComparison.Ordinal);
        Assert.Contains("new LicenseWindow().ShowDialog()", wpf, StringComparison.Ordinal);
        Assert.Contains("ReportCodeCopied()", wpf, StringComparison.Ordinal);
        Assert.Contains("OpenContact(ProFeatures.TelegramUrl)", wpf, StringComparison.Ordinal);
        Assert.Contains("OpenContact(ProFeatures.IssuesUrl)", wpf, StringComparison.Ordinal);
        Assert.Contains("OpenContact(ProFeatures.BuyUrl)", wpf, StringComparison.Ordinal);

        var avalonia = ReadProjectFile(AvaloniaWindowPath.Split('/'));
        Assert.Contains("Pro.Activate", avalonia, StringComparison.Ordinal);
        Assert.Contains("Common.Close", avalonia, StringComparison.Ordinal);
        Assert.Contains("new LicenseWindow().ShowDialogSync(this)", avalonia, StringComparison.Ordinal);
        Assert.Contains("ReportCodeCopied()", avalonia, StringComparison.Ordinal);
        Assert.Contains("OpenContact(ProFeatures.TelegramUrl)", avalonia, StringComparison.Ordinal);
        Assert.Contains("OpenContact(ProFeatures.IssuesUrl)", avalonia, StringComparison.Ordinal);
        Assert.Contains("OpenContact(ProFeatures.BuyUrl)", avalonia, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ helpers

    private static string[] ReadBothPlatforms() =>
        new[]
        {
            ReadProjectFile(WpfWindowPath.Split('/')),
            ReadProjectFile(AvaloniaWindowPath.Split('/'))
        };

    private static string ReadProjectFile(params string[] parts)
    {
        var path = Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"Файл проекта не найден: {path}");
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
