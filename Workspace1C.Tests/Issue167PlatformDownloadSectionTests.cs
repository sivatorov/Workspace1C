using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты issue #167 «Скачивание платформы — разделы» (релиз 1.0.1.121):
/// <list type="bullet">
/// <item>галочка «Только моя разрядность»: список файлов фильтруется по x64/x86 только
/// при установленной галочке (по умолчанию — включена, прежнее поведение);</item>
/// <item>выбор раздела страницы загрузок (сворачиваемые группы) с галочкой фильтрации;
/// при отсутствии разделов строка скрыта (<c>HasSections</c> = false);</item>
/// <item>чистые помощники <see cref="PlatformDistributionPicker.ExtractSections"/> и
/// <see cref="PlatformDistributionPicker.FilterBySection"/>;</item>
/// <item>паритет WPF/Avalonia и ключи локализации ru/en.</item>
/// </list>
/// </summary>
public sealed class Issue167PlatformDownloadSectionTests
{
    private const string GroupPlatform = "Технологическая платформа";
    private const string GroupThin = "Тонкий клиент";

    private static readonly string[] FilterKeys =
    {
        "PlatformDownload.FilterArchitecture",
        "PlatformDownload.FilterArchitectureTooltip",
        "PlatformDownload.Section",
        "PlatformDownload.FilterSection",
        "PlatformDownload.FilterSectionTooltip",
    };

    // ===================== Чистые помощники =====================

    [Fact]
    public void ExtractSections_ReturnsOrderedDistinctGroups()
    {
        var files = new[]
        {
            File("a.zip", "x64", GroupThin),
            File("b.zip", "x64", "  " + GroupPlatform + "  "),
            File("c.zip", "x86", "тонкий клиент"), // дубль без учёта регистра
            File("d.zip", "x64", "   "),
            File("e.zip", "x64", null),
            File("f.zip", "x64", GroupPlatform),
        };

        var sections = PlatformDistributionPicker.ExtractSections(files);

        // Порядок — первого появления на странице; значения обрезаны, дубли убраны.
        Assert.Equal(new[] { GroupThin, GroupPlatform }, sections);
    }

    [Fact]
    public void ExtractSections_EmptyWhenPageHasNoGroups()
    {
        var files = new[]
        {
            File("a.zip", "x64", null),
            File("b.zip", "x86", "   "),
        };

        Assert.Empty(PlatformDistributionPicker.ExtractSections(files));
        Assert.Empty(PlatformDistributionPicker.ExtractSections(Array.Empty<PlatformReleaseFile>()));
        Assert.Empty(PlatformDistributionPicker.ExtractSections(null!));
    }

    [Fact]
    public void FilterBySection_KeepsOnlySelectedGroup_CaseInsensitive_EmptyMeansNoFilter()
    {
        var platform = File("a.zip", "x64", GroupPlatform);
        var thin = File("b.zip", "x64", GroupThin);
        var noGroup = File("c.zip", "x64");
        var files = new[] { platform, thin, noGroup };

        var filtered = PlatformDistributionPicker.FilterBySection(files, "  технологическая платформа ");

        Assert.Same(platform, Assert.Single(filtered));

        // Пустой/пробельный раздел — фильтр не применяется (все файлы).
        Assert.Equal(3, PlatformDistributionPicker.FilterBySection(files, null).Count);
        Assert.Equal(3, PlatformDistributionPicker.FilterBySection(files, "   ").Count);
        Assert.Empty(PlatformDistributionPicker.FilterBySection(files, "Нет такого раздела"));
    }

    // ===================== ViewModel: разрядность =====================

    [Fact]
    public async Task Vm_FilterByArchitectureOn_FiltersOtherArchitecture()
    {
        var vm = CreateVm(Release("8.3.27.2214",
            File("8.3.27.2214_x64.zip", "x64"),
            File("8.3.27.2214_x86.zip", "x86")));

        await vm.LoadCatalogAsync();

        // По умолчанию галочка включена — прежнее поведение: только выбранная битность.
        Assert.True(vm.FilterByArchitecture);
        Assert.Contains(vm.DistributionOptions, o => o.File.FileName == "8.3.27.2214_x64.zip");
        Assert.DoesNotContain(vm.DistributionOptions, o => o.File.FileName == "8.3.27.2214_x86.zip");
    }

    [Fact]
    public async Task Vm_FilterByArchitectureOff_ShowsBothArchitectures()
    {
        var vm = CreateVm(Release("8.3.27.2214",
            File("8.3.27.2214_x64.zip", "x64"),
            File("8.3.27.2214_x86.zip", "x86")));

        await vm.LoadCatalogAsync();
        vm.FilterByArchitecture = false;

        // Снятая галочка — доступны оба варианта; выбор пользователя сохраняется,
        // если выбранный файл проходит фильтры (x64 остаётся выбранным).
        Assert.Contains(vm.DistributionOptions, o => o.File.FileName == "8.3.27.2214_x64.zip");
        Assert.Contains(vm.DistributionOptions, o => o.File.FileName == "8.3.27.2214_x86.zip");
        Assert.Equal("8.3.27.2214_x64.zip", vm.PickedFile!.FileName);

        // Обратное включение снова оставляет только x64.
        vm.FilterByArchitecture = true;
        Assert.DoesNotContain(vm.DistributionOptions, o => o.File.FileName == "8.3.27.2214_x86.zip");
        Assert.Equal("8.3.27.2214_x64.zip", vm.PickedFile!.FileName);
    }

    // ===================== ViewModel: разделы =====================

    [Fact]
    public async Task Vm_AvailableSections_BuiltFromReleaseInPageOrder()
    {
        var vm = CreateVm(Release("8.3.27.2214",
            File("8.3.27.2214_thin_x64.zip", "x64", GroupThin),
            File("8.3.27.2214_x64.zip", "x64", GroupPlatform),
            File("8.3.27.2214_thin_x86.zip", "x86", "тонкий клиент"),
            File("legacy.zip", "x64", null)));

        await vm.LoadCatalogAsync();

        Assert.True(vm.HasSections);
        // Разделы строятся из СЫРЫХ файлов релиза, включая отфильтрованные по разрядности.
        Assert.Equal(new[] { GroupThin, GroupPlatform }, vm.AvailableSections);
    }

    [Fact]
    public async Task Vm_FilterBySection_LimitsOptionsAndPicksFile()
    {
        var vm = CreateVm(Release("8.3.27.2214",
            File("8.3.27.2214_x64.zip", "x64", GroupPlatform),
            File("8.3.27.2214_thin_x64.zip", "x64", GroupThin)));

        await vm.LoadCatalogAsync();
        Assert.Equal(2, vm.DistributionOptions.Count);

        // Включение фильтра без выбора — выбирается первый раздел страницы.
        vm.FilterBySection = true;
        Assert.Equal(GroupPlatform, vm.SelectedSection);
        var onlyPlatform = Assert.Single(vm.DistributionOptions);
        Assert.Equal("8.3.27.2214_x64.zip", onlyPlatform.File.FileName);
        Assert.Equal(GroupPlatform, vm.PickedFile!.Group);

        // Смена раздела фильтрует список и выбирает файл выбранного раздела.
        vm.SelectedSection = GroupThin;
        var onlyThin = Assert.Single(vm.DistributionOptions);
        Assert.Equal("8.3.27.2214_thin_x64.zip", onlyThin.File.FileName);
        Assert.Equal("8.3.27.2214_thin_x64.zip", vm.PickedFile!.FileName);
        Assert.Equal(GroupThin, vm.PickedFile.Group);

        // Снятие галочки возвращает оба файла.
        vm.FilterBySection = false;
        Assert.Equal(2, vm.DistributionOptions.Count);
    }

    [Fact]
    public async Task Vm_NoSections_HasSectionsFalse_AndFilterIgnored()
    {
        var vm = CreateVm(Release("8.3.27.2214",
            File("8.3.27.2214_x64.zip", "x64"),
            File("8.3.27.2214_thin_x64.zip", "x64")));

        await vm.LoadCatalogAsync();

        Assert.False(vm.HasSections);
        Assert.Empty(vm.AvailableSections);

        // Фильтр нельзя «залипить» на странице без разделов: он игнорируется.
        vm.FilterBySection = true;
        Assert.Null(vm.SelectedSection);
        Assert.Equal(2, vm.DistributionOptions.Count);
        Assert.NotNull(vm.PickedFile);
    }

    [Fact]
    public async Task Vm_SectionDisappearsOnVersionChange_ResetsFilter()
    {
        var first = Release("8.3.27.2214", File("a_x64.zip", "x64", GroupPlatform));
        var second = Release("8.3.27.1688", File("b_x64.zip", "x64"));
        var vm = CreateVm(first, second);

        await vm.LoadCatalogAsync();
        vm.FilterBySection = true;
        Assert.Equal(GroupPlatform, vm.SelectedSection);

        // Переход на версию без разделов: список разделов пуст, фильтр снят,
        // выбор не «залипает» на исчезнувшем разделе.
        vm.SelectedRelease = vm.Releases[1];

        Assert.False(vm.HasSections);
        Assert.Empty(vm.AvailableSections);
        Assert.Null(vm.SelectedSection);
        Assert.False(vm.FilterBySection);
        Assert.Equal("b_x64.zip", vm.PickedFile!.FileName);
    }

    // ===================== Паритет UI: WPF и Avalonia =====================

    [Fact]
    public void WpfDialog_HasArchitectureAndSectionFilters()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "PlatformDownloadWindow.xaml");

        Assert.Contains("IsChecked=\"{Binding FilterByArchitecture, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding FilterBySection, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding AvailableSections}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedSection, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding FilterBySection}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding HasSections, Converter={StaticResource BoolToVis}}\"",
            xaml, StringComparison.Ordinal);
        Assert.Contains("PlatformDownload.FilterArchitecture", xaml, StringComparison.Ordinal);
        Assert.Contains("PlatformDownload.Section", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaDialog_HasArchitectureAndSectionFilters()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "PlatformDownloadWindow.Avalonia.cs");

        Assert.Contains("nameof(PlatformDownloadViewModel.FilterByArchitecture)", code, StringComparison.Ordinal);
        Assert.Contains("nameof(PlatformDownloadViewModel.FilterBySection)", code, StringComparison.Ordinal);
        Assert.Contains("nameof(PlatformDownloadViewModel.AvailableSections)", code, StringComparison.Ordinal);
        Assert.Contains("nameof(PlatformDownloadViewModel.HasSections)", code, StringComparison.Ordinal);
        Assert.Contains("nameof(PlatformDownloadViewModel.SelectedSection)", code, StringComparison.Ordinal);
        Assert.Contains("T(\"PlatformDownload.Section\")", code, StringComparison.Ordinal);
        // Issue #167: длинный текст галочки убран — остаётся только подсказка.
        Assert.Contains("T(\"PlatformDownload.FilterSectionTooltip\")", code, StringComparison.Ordinal);
        Assert.Contains("T(\"PlatformDownload.FilterArchitectureTooltip\")", code, StringComparison.Ordinal);
    }

    // ===================== Issue #167 (1.0.1.123): подпись и сортировка =====================

    /// <summary>Четыре комбинации галочек: часть, уже выбранную галочкой, не дублируем.</summary>
    [Theory]
    [InlineData(true, true, "Windows (64-bit) - x64 - file.zip")]
    [InlineData(false, true, "Windows (64-bit) - file.zip")]
    [InlineData(true, false, "x64 - file.zip")]
    [InlineData(false, false, "file.zip")]
    public void Compose_FourCheckboxCombinations(bool includeArchitecture, bool includeSection, string expected)
    {
        var file = File("file.zip", "x64", "Windows (64-bit)");

        var text = PlatformFileDisplayName.Compose(file, includeArchitecture, includeSection);

        Assert.Equal(expected, text);
    }

    [Fact]
    public void Compose_SkipsMissingParts_AndAlwaysShowsFileName()
    {
        // Нет разрядности/раздела — соответствующие части пропускаются.
        var noArch = File("file.deb", null, "Linux (64-bit)");
        Assert.Equal("Linux (64-bit) - file.deb", PlatformFileDisplayName.Compose(noArch, true, true));
        Assert.Equal("file.deb", PlatformFileDisplayName.Compose(noArch, false, false));

        var noGroup = File("file.zip", "x86");
        Assert.Equal("x86 - file.zip", PlatformFileDisplayName.Compose(noGroup, true, true));

        Assert.Equal(string.Empty, PlatformFileDisplayName.Compose(null, true, true));
    }

    [Fact]
    public async Task Vm_DisplayName_OmitsArchitectureAndSection_WhenFiltersOn_AndSortedByFinalString()
    {
        var vm = CreateVm(Release("8.3.27.2214",
            File("b_x64.zip", "x64", "Windows (64-bit)"),
            File("a_x86.zip", "x86", "Windows (32-bit)")));

        await vm.LoadCatalogAsync();

        // Галочка разрядности включена (по умолчанию), раздела — нет:
        // «Раздел - имя_файла» (разрядность не дублируется).
        var onlyX64 = Assert.Single(vm.DistributionOptions);
        Assert.Equal("Windows (64-bit) - b_x64.zip", onlyX64.DisplayName);
        Assert.DoesNotContain("Полный клиент", onlyX64.DisplayName, StringComparison.Ordinal);

        // Обе галочки сняты: «Раздел - разрядность - имя_файла», сортировка по строке.
        vm.FilterByArchitecture = false;
        Assert.Equal(
            new[]
            {
                "Windows (32-bit) - x86 - a_x86.zip",
                "Windows (64-bit) - x64 - b_x64.zip",
            },
            vm.DistributionOptions.Select(o => o.DisplayName).ToArray());

        // Галочка раздела включена (выбран первый раздел страницы), разрядность снята:
        // «x64 - имя_файла» (раздел не дублируется).
        vm.FilterBySection = true;
        Assert.Equal("Windows (64-bit)", vm.SelectedSection);
        var sectionOnly = Assert.Single(vm.DistributionOptions);
        Assert.Equal("x64 - b_x64.zip", sectionOnly.DisplayName);

        // Обе галочки: только имя файла.
        vm.FilterByArchitecture = true;
        var both = Assert.Single(vm.DistributionOptions);
        Assert.Equal("b_x64.zip", both.DisplayName);
    }

    [Fact]
    public void WpfDialog_ArchitectureCheckboxHasNoText_AndSectionRowHasCheckbox()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "PlatformDownloadWindow.xaml");

        // Длинные тексты галочек убраны (issue #167).
        Assert.DoesNotContain("Content=\"{loc:Loc PlatformDownload.FilterArchitecture}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"{loc:Loc PlatformDownload.FilterSection}\"", xaml, StringComparison.Ordinal);
        // Подсказки остались; галочки привязаны к фильтрам.
        Assert.Contains("PlatformDownload.FilterArchitectureTooltip", xaml, StringComparison.Ordinal);
        Assert.Contains("PlatformDownload.FilterSectionTooltip", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaDialog_ArchitectureCheckboxHasNoText_AndSectionRowHasCheckbox()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "PlatformDownloadWindow.Avalonia.cs");

        Assert.DoesNotContain("Content = T(\"PlatformDownload.FilterArchitecture\")", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Content = T(\"PlatformDownload.FilterSection\")", code, StringComparison.Ordinal);
        Assert.Contains("ToolTip.SetTip(archFilter, T(\"PlatformDownload.FilterArchitectureTooltip\"))", code, StringComparison.Ordinal);
        Assert.Contains("ToolTip.SetTip(sectionFilter, T(\"PlatformDownload.FilterSectionTooltip\"))", code, StringComparison.Ordinal);
    }

    // ===================== Issue #167: определение разделов парсером =====================

    [Fact]
    public void Parser_H2Headers_AreDetectedAsSections()
    {
        const string html = """
            <h2>Windows (64-bit)</h2>
            <a href="/d/win64.zip">Скачать</a>
            <h2>Linux (64-bit)</h2>
            <a href="/d/linux_amd64.deb">Скачать</a>
            """;

        var files = OneCPlatformCatalogParser.ParseDistributionFiles(html);

        Assert.Equal("Windows (64-bit)", files.Single(f => f.FileName == "win64.zip").Group);
        Assert.Equal("Linux (64-bit)", files.Single(f => f.FileName == "linux_amd64.deb").Group);
    }

    [Fact]
    public void Parser_SummaryHeaders_AreDetectedAsSections()
    {
        const string html = """
            <details>
              <summary>Linux (64-bit)</summary>
              <a href="/d/linux_amd64.deb">Скачать</a>
            </details>
            """;

        var files = OneCPlatformCatalogParser.ParseDistributionFiles(html);

        Assert.Equal("Linux (64-bit)", Assert.Single(files).Group);
    }

    [Fact]
    public void Parser_ClassBasedHeaders_AreDetectedAsSections()
    {
        const string html = """
            <div class="download-section">Windows (32-bit)</div>
            <a href="/d/win32.zip">Скачать</a>
            """;

        var files = OneCPlatformCatalogParser.ParseDistributionFiles(html);

        Assert.Equal("Windows (32-bit)", Assert.Single(files).Group);
    }

    [Fact]
    public void Parser_KnownSectionNamesInPlainText_AreDetected()
    {
        const string html = """
            <p>Windows (64-bit)</p>
            <a href="/d/win64.zip">Скачать</a>
            <p>Linux (64-bit)</p>
            <a href="/d/linux_amd64.deb">Скачать</a>
            """;

        var files = OneCPlatformCatalogParser.ParseDistributionFiles(html);

        Assert.Equal("Windows (64-bit)", files.Single(f => f.FileName == "win64.zip").Group);
        Assert.Equal("Linux (64-bit)", files.Single(f => f.FileName == "linux_amd64.deb").Group);
    }

    [Fact]
    public void Parser_NoHeaders_SingleOsFamily_NoSections()
    {
        // Одно семейство (Windows) — разделы не синтезируются, строка скрыта.
        const string html = """
            <a href="/d/win64.zip">Скачать</a>
            <a href="/d/win32.zip">Скачать</a>
            """;

        var files = OneCPlatformCatalogParser.ParseDistributionFiles(html);

        Assert.Equal(2, files.Count);
        Assert.All(files, f => Assert.Null(f.Group));
        Assert.Empty(PlatformDistributionPicker.ExtractSections(files));
    }

    [Fact]
    public void Parser_NoHeaders_DifferentOsFamilies_SynthesizesSections()
    {
        const string html = """
            <a href="/d/win64.zip">Скачать</a>
            <a href="/d/linux_amd64.deb">Скачать</a>
            """;

        var files = OneCPlatformCatalogParser.ParseDistributionFiles(html);

        Assert.Equal("Windows (64-bit)", files.Single(f => f.FileName == "win64.zip").Group);
        Assert.Equal("Linux (64-bit)", files.Single(f => f.FileName == "linux_amd64.deb").Group);
        Assert.Equal(
            new[] { "Windows (64-bit)", "Linux (64-bit)" },
            PlatformDistributionPicker.ExtractSections(files));
    }

    [Fact]
    public void Parser_PageTitleWithVersion_IsNotMistakenForSection()
    {
        // Заголовок страницы «Файлы версии 8.5.1.1522» разделом не считается.
        const string html = """
            <h1>Файлы версии 8.5.1.1522</h1>
            <a href="/d/win64.zip">Скачать</a>
            <a href="/d/linux_amd64.deb">Скачать</a>
            """;

        var files = OneCPlatformCatalogParser.ParseDistributionFiles(html);

        // Семантический заголовок отброшен — работает фолбэк по семействам.
        Assert.Equal("Windows (64-bit)", files.Single(f => f.FileName == "win64.zip").Group);
        Assert.Equal("Linux (64-bit)", files.Single(f => f.FileName == "linux_amd64.deb").Group);
    }

    // ===================== Локализация ru/en =====================

    [Theory]
    [InlineData("ru.json")]
    [InlineData("en.json")]
    public void Localization_HasFilterKeys(string fileName)
    {
        var strings = ReadLanguageStrings(fileName);

        foreach (var key in FilterKeys)
        {
            Assert.True(strings.TryGetValue(key, out var value), $"{fileName}: нет ключа {key}");
            Assert.False(string.IsNullOrWhiteSpace(value), $"{fileName}: пустой ключ {key}");
            Assert.NotEqual(key, value);
        }
    }

    // ===================== Вспомогательные методы =====================

    private static PlatformReleaseFile File(
        string name,
        string? arch,
        string? group = null,
        PlatformDistributionKind kind = PlatformDistributionKind.WindowsSetupZip)
        => new()
        {
            FileName = name,
            Url = $"https://releases.1c.ru/dist/{name}",
            Architecture = arch,
            Kind = kind,
            Group = group,
            SizeBytes = 100,
        };

    private static PlatformRelease Release(string version, params PlatformReleaseFile[] files)
    {
        var release = new PlatformRelease { Version = version };
        release.Files.AddRange(files);
        return release;
    }

    private static PlatformDownloadViewModel CreateVm(params PlatformRelease[] releases)
    {
        var service = new FakeCatalogService
        {
            AvailableResult = new PlatformCatalogResult
            {
                Status = PortalFetchStatus.Ok,
                Releases = releases,
            },
        };

        return new PlatformDownloadViewModel(
            service,
            () => null,
            (url, target, progress, ct) => Task.FromResult<string?>(target),
            _ => true,
            is64Bit: true,
            defaultDirectory: Path.Combine(Path.GetTempPath(), "cm_issue167_" + Guid.NewGuid().ToString("N")),
            isWindows: true);
    }

    private static Dictionary<string, string> ReadLanguageStrings(string fileName)
    {
        var path = Path.Combine(FindRepoRoot(), "Workspace1C", "Localization", "Languages", fileName);
        Assert.True(System.IO.File.Exists(path), $"Файл локализации не найден: {path}");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var strings = doc.RootElement.GetProperty("strings");
        // В словарях встречаются исторические дубли ключей (например, CacheClean.ProgramCache),
        // поэтому используем индексер, а не ToDictionary (иначе ArgumentException).
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in strings.EnumerateObject())
            result[property.Name] = property.Value.GetString() ?? string.Empty;
        return result;
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var path = Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray());
        Assert.True(System.IO.File.Exists(path), $"Файл проекта не найден: {path}");
        return System.IO.File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
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
