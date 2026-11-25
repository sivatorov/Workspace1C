using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты issue #162 «Скачивание версии платформы — баги» (релиз 1.0.1.120):
/// <list type="bullet">
/// <item>имя сохраняемого файла совпадает с именем из журнала/каталога — без префикса
/// версии (раньше получалось «8.5.1.1621_server64_8_5_1_1621.zip»);</item>
/// <item>запасное имя из версии, когда имя файла пустое; отсутствие дублирования версии;</item>
/// <item>контраст полей «Учётная запись ИТС» и «Папка загрузки» в тёмной теме:
/// WPF — кисти текста/фона/рамки, Avalonia — рамка и первичная кисть текста.</item>
/// </list>
/// </summary>
public sealed class Issue162PlatformDownloadTests
{
    // ===================== BuildTargetFileName =====================

    [Theory]
    // Пример из issue #162: в журнале «server64_8_5_1_1621.zip» — под этим именем и сохраняем.
    [InlineData("8.5.1.1621", "server64_8_5_1_1621.zip", "server64_8_5_1_1621.zip")]
    [InlineData("8.3.27.2214", "8.3.27.2214_x64.zip", "8.3.27.2214_x64.zip")]
    // Разные платформы: Linux-пакет и тонкий клиент.
    [InlineData("8.3.27.2214", "deb64_8_3_27_2214.tar.gz", "deb64_8_3_27_2214.tar.gz")]
    [InlineData("8.3.27.2214", "thin.client_8.3.27.2214.zip", "thin.client_8.3.27.2214.zip")]
    public void BuildTargetFileName_NoVersionPrefix(string version, string fileName, string expected)
    {
        var actual = OneCUpdatesService.BuildTargetFileName(version, fileName);

        Assert.Equal(expected, actual);
        // Имя файла из каталога может само начинаться с версии (например
        // «8.3.27.2214_x64.zip») — это не префикс. Проверяем, что версия не добавлена
        // сверху: если исходное имя не начиналось с «<версия>_», итоговое тоже не должно.
        if (!fileName.StartsWith($"{version}_", StringComparison.Ordinal))
        {
            Assert.False(actual.StartsWith($"{version}_", StringComparison.Ordinal),
                $"К имени «{fileName}» добавлен префикс версии: «{actual}»");
        }
        // Дублирования версии нет ни в каком случае.
        Assert.DoesNotContain($"{version}_{version}", actual, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTargetFileName_EmptyName_UsesVersion()
    {
        Assert.Equal("8.5.1.1621", OneCUpdatesService.BuildTargetFileName("8.5.1.1621", string.Empty));
        Assert.Equal("8.5.1.1621", OneCUpdatesService.BuildTargetFileName("8.5.1.1621", null!));
        Assert.Equal("8.5.1.1621", OneCUpdatesService.BuildTargetFileName("8.5.1.1621", "   "));
        Assert.Equal(string.Empty, OneCUpdatesService.BuildTargetFileName(string.Empty, string.Empty));
    }

    [Fact]
    public void BuildTargetFileName_AlreadyPrefixed_NoDuplicate()
    {
        // Имя уже содержит версию — второй префикс не добавляется.
        Assert.Equal("8.5.1.1621_server64_8_5_1_1621.zip",
            OneCUpdatesService.BuildTargetFileName("8.5.1.1621", "8.5.1.1621_server64_8_5_1_1621.zip"));
    }

    // ===================== ViewModel: имя совпадает с журналом =====================

    [Fact]
    public async Task PlatformDownloadViewModel_SavesUnderLogName()
    {
        // issue #162 п.2: в журнале «Скачивание «server64_8_5_1_1621.zip» в «…»», и на диск
        // файл должен писаться под тем же именем — без префикса выбранной версии.
        var file = new PlatformReleaseFile
        {
            FileName = "server64_8_5_1_1621.zip",
            Url = "https://releases.1c.ru/dist/server64_8_5_1_1621.zip",
            Architecture = "x64",
            Kind = PlatformDistributionKind.WindowsSetupZip,
            SizeBytes = 100,
        };
        var release = new PlatformRelease { Version = "8.5.1.1621" };
        release.Files.Add(file);

        var service = new FakeCatalogService
        {
            AvailableResult = new PlatformCatalogResult
            {
                Status = PortalFetchStatus.Ok,
                Releases = new[] { release },
            },
        };

        var directory = Path.Combine(Path.GetTempPath(), "cm_issue162_" + Guid.NewGuid().ToString("N"));
        string? requestedTarget = null;
        var vm = new PlatformDownloadViewModel(
            service,
            () => null,
            (url, target, progress, ct) =>
            {
                requestedTarget = target;
                return Task.FromResult<string?>(target);
            },
            _ => true,
            is64Bit: true,
            defaultDirectory: directory,
            isWindows: true);

        try
        {
            await vm.LoadCatalogAsync();
            await vm.DownloadAsync();

            Assert.NotNull(requestedTarget);
            var savedName = Path.GetFileName(requestedTarget!);
            Assert.Equal("server64_8_5_1_1621.zip", savedName);
            Assert.Equal(Path.Combine(directory, "server64_8_5_1_1621.zip"), requestedTarget);

            // Итоговый путь совпадает с именем, объявленным в каталоге/логе загрузки.
            Assert.Equal(requestedTarget, vm.DownloadedPath);
            Assert.Equal("server64_8_5_1_1621.zip", Path.GetFileName(vm.DownloadedPath));
            // Текст журнала в тестовой среде — ключи локализации (LocalizationManager.T
            // не инициализирован), поэтому проверяем фактический путь сохранения: именно
            // он показывается пользователю и именно под ним файл пишется на диск.
            Assert.DoesNotContain("8.5.1.1621_server64_8_5_1_1621.zip", requestedTarget, StringComparison.Ordinal);
            Assert.DoesNotContain("8.5.1.1621_server64_8_5_1_1621.zip", vm.DownloadedPath, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // Временный каталог мог не создаться — очистка не критична для теста.
            }
        }
    }

    // ===================== Контраст полей: WPF =====================

    [Fact]
    public void Wpf_DarkTheme_FieldsHaveContrastBrushes()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "PlatformDownloadWindow.xaml");

        // Поле «Папка загрузки»: фон, текст, каретка и рамка — кисти темы.
        Assert.Contains("Background=\"{DynamicResource ContentBackgroundBrush}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{DynamicResource TextPrimaryBrush}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CaretBrush=\"{DynamicResource TextPrimaryBrush}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BorderBrush=\"{DynamicResource BorderBrushColor}\"", xaml, StringComparison.Ordinal);

        // Значение «Учётная запись ИТС» — в рамке и на первичной кисти (issue #162 п.1).
        var accountFrame = ExtractXamlElement(xaml, "<Border Grid.Column=\"1\" VerticalAlignment=\"Center\"");
        Assert.Contains("BorderBrush=\"{DynamicResource BorderBrushColor}\"", accountFrame, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{DynamicResource TextPrimaryBrush}\"", accountFrame, StringComparison.Ordinal);
        Assert.Contains("Binding AccountName", accountFrame, StringComparison.Ordinal);
    }

    // ===================== Контраст полей: Avalonia =====================

    [Fact]
    public void Avalonia_AccountRow_HasBorderAndPrimaryForeground()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "PlatformDownloadWindow.Avalonia.cs");

        // Значение учётной записи привязано к первичной кисти и обёрнуто в Border.
        Assert.Contains("ThemeBrushes.Bind(accountLine, TextBlock.ForegroundProperty, \"TextPrimaryBrush\")",
            code, StringComparison.Ordinal);
        Assert.Contains("Child = accountLine", code, StringComparison.Ordinal);
        Assert.Contains("ThemeBrushes.Bind(accountFrame, Border.BorderBrushProperty, \"BorderColorBrush\")",
            code, StringComparison.Ordinal);

        // Поле папки загрузки — кисти фона/текста/каретки/рамки, как в остальных окнах Avalonia.
        Assert.Contains("ThemeBrushes.Bind(dirBox, TextBox.BackgroundProperty, \"ContentBackgroundColorBrush\")",
            code, StringComparison.Ordinal);
        Assert.Contains("ThemeBrushes.Bind(dirBox, TextBox.ForegroundProperty, \"TextPrimaryBrush\")",
            code, StringComparison.Ordinal);
        Assert.Contains("ThemeBrushes.Bind(dirBox, TextBox.CaretBrushProperty, \"TextPrimaryBrush\")",
            code, StringComparison.Ordinal);
        Assert.Contains("ThemeBrushes.Bind(dirBox, TextBox.BorderBrushProperty, \"BorderColorBrush\")",
            code, StringComparison.Ordinal);
    }

    // ===================== Ресурсы темы: обе темы не сломаны =====================

    [Theory]
    [InlineData("DarkTheme.xaml")]
    [InlineData("LightTheme.xaml")]
    public void Theme_Fields_HaveReadableContrast(string themeFile)
    {
        var xaml = ReadProjectFile("Workspace1C", "Themes", themeFile);

        // Все кисти, использованные полями окна скачивания (issue #162), объявлены в теме.
        Assert.Contains("x:Key=\"ContentBackgroundBrush\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TextPrimaryBrush\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"BorderBrushColor\"", xaml, StringComparison.Ordinal);

        // Контраст основного текста на фоне полей — не ниже WCAG AA для обычного текста.
        var background = ParseThemeColor(xaml, "ContentBackgroundColor");
        var foreground = ParseThemeColor(xaml, "TextPrimaryColor");
        var ratio = ContrastRatio(foreground, background);
        Assert.True(ratio >= 4.5,
            $"{themeFile}: контраст текста полей на фоне {ratio:0.00}:1 ниже 4.5:1");
    }

    // ===================== Вспомогательные методы =====================

    private static string ReadProjectFile(params string[] parts)
    {
        var path = Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"Файл проекта не найден: {path}");
        return File.ReadAllText(path);
    }

    /// <summary>Читает цвет <c><Color x:Key="...">#RRGGBB</Color></c> из ресурсов темы.</summary>
    private static (byte R, byte G, byte B) ParseThemeColor(string xaml, string key)
    {
        var marker = $"x:Key=\"{key}\">#";
        var start = xaml.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"В теме не найден цвет «{key}»");
        start += marker.Length;
        var hex = xaml.Substring(start, 6);
        return (
            byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    /// <summary>Контраст по WCAG 2.1 (отношение яркостей, от 1:1 до 21:1).</summary>
    private static double ContrastRatio((byte R, byte G, byte B) first, (byte R, byte G, byte B) second)
    {
        static double Luminance((byte R, byte G, byte B) color)
        {
            static double Channel(byte value)
            {
                var c = value / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        }

        var l1 = Luminance(first);
        var l2 = Luminance(second);
        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>Выделяет фрагмент XAML от указанного открывающего тега до закрытия элемента.</summary>
    private static string ExtractXamlElement(string xaml, string openingTag)
    {
        var start = xaml.IndexOf(openingTag, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Не найден элемент XAML: {openingTag}");
        var end = xaml.IndexOf("</Border>", start, StringComparison.Ordinal);
        Assert.True(end > start, "Не найден конец элемента Border");
        return xaml.Substring(start, end - start);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Fake каталога версий (образец PlatformDownloadViewModelTests).</summary>
    private sealed class FakeCatalogService : IPlatformUpdateService
    {
        public PlatformCatalogResult AvailableResult { get; set; } = new() { Status = PortalFetchStatus.Ok };

        public Task<PlatformCatalogResult> GetAvailableReleasesAsync(CancellationToken ct = default)
            => Task.FromResult(AvailableResult);

        public Task<PlatformCatalogResult> GetAllAvailableReleasesAsync(CancellationToken ct = default)
            => Task.FromResult(AvailableResult);

        public Task<PlatformCatalogResult> GetAvailableReleasesForNickAsync(string nick, CancellationToken ct = default)
            => Task.FromResult(AvailableResult);

        public Task<PlatformCatalogResult> LoadReleaseFilesAsync(PlatformRelease release, CancellationToken ct = default)
            => Task.FromResult(new PlatformCatalogResult { Status = PortalFetchStatus.Ok, Release = release });

        public Task<PlatformCatalogResult> LoadReleaseFilesForNickAsync(PlatformRelease release, string nick, CancellationToken ct = default)
            => Task.FromResult(new PlatformCatalogResult { Status = PortalFetchStatus.Ok, Release = release });

        public PlatformReleaseFile? PickDistribution(IReadOnlyList<PlatformReleaseFile> files)
            => files.FirstOrDefault();
    }
}
