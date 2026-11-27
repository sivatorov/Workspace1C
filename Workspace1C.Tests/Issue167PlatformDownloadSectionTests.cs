using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты issue #167 «Скачивание платформы — разделы» (релиз 1.0.1.121):
/// <list type="bullet">
/// <item>галочка «Только моя разрядность»: список файлов фильтруется по x64/x86 только
/// при установленной галочке (с 1.0.1.127 по умолчанию — снята);</item>
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
        "PlatformDownload.SectionSuffix",
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
        vm.FilterByArchitecture = true;

        // Установленная галочка — только выбранная битность.
        Assert.Contains(vm.DistributionOptions, o => o.File.FileName == "8.3.27.2214_x64.zip");
        Assert.DoesNotContain(vm.DistributionOptions, o => o.File.FileName == "8.3.27.2214_x86.zip");
    }

    [Fact]
    public async Task Vm_Defaults_ArchitectureFilterOff_SectionFilterOn()
    {
        var vm = CreateVm(Release("8.3.27.2214",
            File("8.3.27.2214_x64.zip", "x64"),
            File("8.3.27.2214_x86.zip", "x86")));

        await vm.LoadCatalogAsync();

        // Issue #167 (1.0.1.127): по умолчанию галочка разрядности снята — видны файлы
        // обеих разрядностей, а фильтр по разделу остаётся включённым.
        Assert.False(vm.FilterByArchitecture);
        Assert.True(vm.FilterBySection);
        Assert.Contains(vm.DistributionOptions, o => o.File.FileName == "8.3.27.2214_x64.zip");
        Assert.Contains(vm.DistributionOptions, o => o.File.FileName == "8.3.27.2214_x86.zip");
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

        // Issue #167 (1.0.1.124): раздел по умолчанию включён — выбран первый раздел
        // страницы, список сразу отфильтрован по нему.
        Assert.True(vm.FilterBySection);
        Assert.Equal(GroupPlatform, vm.SelectedSection);
        var onlyPlatform = Assert.Single(vm.DistributionOptions);
        Assert.Equal("8.3.27.2214_x64.zip", onlyPlatform.File.FileName);
        Assert.Equal(GroupPlatform, vm.PickedFile!.Group);

        // Снятие галочки возвращает оба файла.
        vm.FilterBySection = false;
        Assert.Equal(2, vm.DistributionOptions.Count);

        // Повторное включение фильтра сохраняет выбранный раздел.
        vm.FilterBySection = true;
        Assert.Equal(GroupPlatform, vm.SelectedSection);
        Assert.Single(vm.DistributionOptions);

        // Смена раздела фильтрует список и выбирает файл выбранного раздела.
        vm.SelectedSection = GroupThin;
        var onlyThin = Assert.Single(vm.DistributionOptions);
        Assert.Equal("8.3.27.2214_thin_x64.zip", onlyThin.File.FileName);
        Assert.Equal("8.3.27.2214_thin_x64.zip", vm.PickedFile!.FileName);
        Assert.Equal(GroupThin, vm.PickedFile.Group);

        // Снятие галочки снова возвращает оба файла.
        vm.FilterBySection = false;
        Assert.Equal(2, vm.DistributionOptions.Count);
    }

    [Fact]
    public async Task Vm_SectionFilterOnByDefault_SelectsFirstSection()
    {
        var vm = CreateVm(Release("8.3.27.2214",
            File("a_x64.zip", "x64", GroupPlatform),
            File("b_x64.zip", "x64", GroupThin)));

        await vm.LoadCatalogAsync();

        // Issue #167 (1.0.1.127): фильтр раздела включён по умолчанию, раздел — первый;
        // галочка разрядности по умолчанию снята.
        Assert.False(vm.FilterByArchitecture);
        Assert.True(vm.FilterBySection);
        Assert.Equal(GroupPlatform, vm.SelectedSection);
        Assert.Equal("a_x64.zip", Assert.Single(vm.DistributionOptions).File.FileName);
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

    [Fact]
    public void WpfDialog_SectionComboShowsRawNames_WithoutSuffix()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "PlatformDownloadWindow.xaml");

        // ItemsSource/SelectedItem — исходные строки без суффикса (issue #167, 1.0.1.126).
        Assert.Contains("ItemsSource=\"{Binding AvailableSections}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedSection, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        // В ComboBox разделов пометки « (раздел)» больше нет: ни конвертера, ни шаблона.
        Assert.DoesNotContain("PlatformSectionDisplayNameConverter", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SectionNameConv", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PlatformDownload.SectionSuffix", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaDialog_SectionComboShowsRawNames_WithoutSuffix()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "PlatformDownloadWindow.Avalonia.cs");

        // ItemsSource/SelectedItem остаются исходными строками — фильтр не ломается.
        Assert.Contains("ItemsSource = _viewModel.AvailableSections", code, StringComparison.Ordinal);
        Assert.Contains("SelectedItem = _viewModel.SelectedSection", code, StringComparison.Ordinal);
        // ItemTemplate с суффиксом « (раздел)» убран (issue #167, 1.0.1.126).
        Assert.DoesNotContain("sectionBox.ItemTemplate", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PlatformDownload.SectionSuffix", code, StringComparison.Ordinal);
    }

    // ===================== Issue #167 (1.0.1.126): подпись и сортировка =====================

    /// <summary>Разрядность показывается только при снятом учёте разрядности.</summary>
    [Theory]
    [InlineData(true, "x64 - file.zip")]
    [InlineData(false, "file.zip")]
    public void Compose_ArchitectureOnly(bool includeArchitecture, string expected)
    {
        var file = File("file.zip", "x64", "Windows (64-bit)");

        var text = PlatformFileDisplayName.Compose(file, includeArchitecture);

        Assert.Equal(expected, text);
    }

    [Fact]
    public void Compose_SkipsMissingParts_AndAlwaysShowsFileName()
    {
        // Нет разрядности — часть пропускается, имя файла выводится всегда.
        var noArch = File("file.deb", null, "Linux (64-bit)");
        Assert.Equal("file.deb", PlatformFileDisplayName.Compose(noArch, true));
        Assert.Equal("file.deb", PlatformFileDisplayName.Compose(noArch, false));

        var noGroup = File("file.zip", "x86");
        Assert.Equal("x86 - file.zip", PlatformFileDisplayName.Compose(noGroup, true));

        Assert.Equal(string.Empty, PlatformFileDisplayName.Compose(null, true));
    }

    [Fact]
    public async Task Vm_DisplayName_OmitsSectionAlways_AndArchitectureOnlyWhenFilterOff_AndSortedByFinalString()
    {
        var vm = CreateVm(Release("8.3.27.2214",
            File("b_x64.zip", "x64", "Windows (64-bit)"),
            File("a_x86.zip", "x86", "Windows (32-bit)")));

        await vm.LoadCatalogAsync();

        // Issue #167 (1.0.1.127): по умолчанию галочка разрядности снята — включаем
        // её явно; фильтр раздела включён, выбран первый раздел.
        vm.FilterByArchitecture = true;
        Assert.True(vm.FilterByArchitecture);
        Assert.True(vm.FilterBySection);
        Assert.Equal("Windows (64-bit)", vm.SelectedSection);
        var onlyX64 = Assert.Single(vm.DistributionOptions);
        Assert.Equal("b_x64.zip", onlyX64.DisplayName);
        Assert.DoesNotContain("Полный клиент", onlyX64.DisplayName, StringComparison.Ordinal);

        // Issue #167 (1.0.1.126): снятие галочки раздела НЕ добавляет имя раздела к
        // подписи файла — раздел в списке уже отделён строкой-разделителем.
        vm.FilterBySection = false;
        var sectionShown = Assert.Single(vm.DistributionOptions);
        Assert.Equal("b_x64.zip", sectionShown.DisplayName);
        Assert.DoesNotContain("Windows (64-bit)", sectionShown.DisplayName, StringComparison.Ordinal);

        // Обе галочки сняты: «разрядность - имя_файла», сортировка по итоговой строке
        // (OrdinalIgnoreCase: «x64…» идёт перед «x86…»).
        vm.FilterByArchitecture = false;
        Assert.Equal(
            new[]
            {
                "x64 - b_x64.zip",
                "x86 - a_x86.zip",
            },
            vm.DistributionOptions.Select(o => o.DisplayName).ToArray());

        // Только раздел включён: разрядность показывается (учёт выключен).
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
    public void Compose_FileLabelNeverHasSectionName()
    {
        var file = File("file.zip", "x64", "Windows (64-bit)");

        // Имя раздела не дописывается к подписи файла ни при каком состоянии галочки.
        Assert.Equal("x64 - file.zip", PlatformFileDisplayName.Compose(file, includeArchitecture: true));
        Assert.Equal("file.zip", PlatformFileDisplayName.Compose(file, includeArchitecture: false));
        Assert.DoesNotContain("Windows (64-bit)", PlatformFileDisplayName.Compose(file, true), StringComparison.Ordinal);
        Assert.DoesNotContain("Windows (64-bit)", PlatformFileDisplayName.Compose(file, false), StringComparison.Ordinal);
    }

    [Fact]
    public void SectionDisplayName_AddsSuffixOnlyToSectionNames()
    {
        Assert.Equal("Windows (64-bit) (раздел)",
            PlatformSectionDisplayName.Compose("Windows (64-bit)", " (раздел)"));
        Assert.Equal("Windows (64-bit)",
            PlatformSectionDisplayName.Compose("  Windows (64-bit)  ", null));
        Assert.Equal(string.Empty, PlatformSectionDisplayName.Compose(null, " (раздел)"));
        Assert.Equal(string.Empty, PlatformSectionDisplayName.Compose("   ", " (раздел)"));
    }

    [Fact]
    public async Task Vm_AvailableSections_AreRawNames_WithoutSuffix()
    {
        var vm = CreateVm(Release("8.3.27.2214",
            File("a_x64.zip", "x64", GroupPlatform),
            File("b_x64.zip", "x64", GroupThin)));

        await vm.LoadCatalogAsync();

        // Issue #167 (1.0.1.126): список выбора раздела показывает исходные названия
        // без пометки « (раздел)»; SelectedSection — тоже исходная строка.
        Assert.Equal(new[] { GroupPlatform, GroupThin }, vm.AvailableSections);
        Assert.Equal(GroupPlatform, vm.SelectedSection);
        Assert.All(vm.AvailableSections, s => Assert.DoesNotContain("(раздел)", s, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Vm_GroupHeaders_HaveSuffix_ButFileOptionsDoNot()
    {
        var vm = CreateVm(Release("8.3.27.2214",
            File("a_x64.zip", "x64", GroupPlatform),
            File("b_x64.zip", "x64", GroupThin)));

        await vm.LoadCatalogAsync();
        // Обе группы видны в списке файлов.
        vm.FilterBySection = false;

        var suffix = LocalizationManager.T("PlatformDownload.SectionSuffix");
        var headers = vm.DistributionOptionsGrouped.OfType<PlatformFileGroupHeaderItem>().ToList();
        Assert.Equal(2, headers.Count);
        Assert.Equal(GroupPlatform + suffix, headers[0].Title);
        Assert.Equal(GroupThin + suffix, headers[1].Title);

        // Подписи файлов — без суффикса.
        Assert.All(
            vm.DistributionOptionsGrouped.OfType<PlatformDistributionOption>(),
            o => Assert.DoesNotContain(suffix, o.DisplayName, StringComparison.Ordinal));
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

    // ===================== Issue #167 (1.0.1.126): отсев не-дистрибутивов =====================

    [Theory]
    [InlineData("checksum", true)]
    [InlineData("CHECKSUM", true)]
    [InlineData("checksum.zip", true)]
    [InlineData("release.sha256", true)]
    [InlineData("release.md5", true)]
    [InlineData("release.sha1", true)]
    [InlineData("release.sha512", true)]
    [InlineData("release.sig", true)]
    [InlineData("release.asc", true)]
    [InlineData("readme.txt", true)]
    [InlineData("hashes", true)]
    [InlineData("win64.zip", false)]
    [InlineData("linux_amd64.deb", false)]
    [InlineData("", false)]
    public void IsNonDistributionFile_RecognizesChecksumsAndReadme(string name, bool expected)
    {
        Assert.Equal(expected, PlatformDistributionPicker.IsNonDistributionFile(name));
    }

    [Fact]
    public void Parser_ChecksumAndHashFiles_AreFiltered_FromHtml()
    {
        const string html = """
            <h2>Windows (64-bit)</h2>
            <a href="/d/win64.zip">Скачать</a>
            <a href="/d/checksum.zip">checksum</a>
            <a href="/d/win64.sha256">hash</a>
            <a href="/d/win64.md5">hash</a>
            """;

        var files = OneCPlatformCatalogParser.ParseDistributionFiles(html);

        var only = Assert.Single(files);
        Assert.Equal("win64.zip", only.FileName);
    }

    [Fact]
    public void Parser_ChecksumFile_IsFiltered_FromJson()
    {
        const string json = """
            {"files":[
              {"name":"win64.zip","url":"/d/win64.zip","size":100},
              {"name":"checksum","url":"/d/checksum.zip","size":10},
              {"name":"win64.md5","url":"/d/win64.md5","size":10}
            ]}
            """;

        var files = OneCPlatformCatalogParser.ParseDistributionFiles(json);

        var only = Assert.Single(files);
        Assert.Equal("win64.zip", only.FileName);
    }

    [Fact]
    public void Parser_ChecksumEndpoint_IsFiltered()
    {
        const string html = """
            <a href="/version_files/transfer_file?path=Distr%2Fwin64.zip">Скачать</a>
            <a href="/version_files/transfer_file?path=Distr%2Fchecksum.zip">checksum</a>
            """;

        var files = OneCPlatformCatalogParser.ParseDistributionFiles(html);

        var only = Assert.Single(files);
        Assert.Equal("win64.zip", only.FileName);
    }

    [Fact]
    public async Task Vm_OnlyChecksum_NoFallbackGroup_AndNoFileOption()
    {
        var vm = CreateVm(Release("8.3.27.2214",
            File("checksum", null),
            File("win64.sha256", "x64")));

        await vm.LoadCatalogAsync();

        // Не-дистрибутивы отсеяны: ни варианта файла, ни фолбэк-группы «Файлы релиза».
        Assert.Empty(vm.DistributionOptions);
        Assert.Empty(vm.DistributionFileGroups);
        Assert.DoesNotContain(
            vm.DistributionOptionsGrouped,
            i => i is PlatformFileGroupHeaderItem header && header.Title == "Файлы релиза");
    }

    [Fact]
    public void Build_EmptyFallbackGroup_IsNotEmitted()
    {
        var checksum = new PlatformDistributionOption(File("checksum", null));
        var groups = PlatformFileGroupViewModel.Build(new[] { checksum }, "Файлы релиза");

        Assert.Empty(groups);
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

    // ===================== Запоминание выбора (1.0.1.127) =====================

    [Fact]
    public void AppSettings_PlatformDownloadSelectionDefaults()
    {
        var settings = new AppSettings();

        // Галочка разрядности по умолчанию снята, фильтр раздела включён.
        Assert.False(settings.PlatformDownloadFilterByArchitecture);
        Assert.True(settings.PlatformDownloadFilterBySection);
        Assert.Equal(string.Empty, settings.PlatformDownloadSelectedSection);
        Assert.Equal("Auto", settings.PlatformDownloadType);
        // 1.0.1.128: разрядность не задана — используется разрядность ОС.
        Assert.Null(settings.PlatformDownloadIs64Bit);
    }

    [Theory]
    [InlineData("PlatformDownloadWindow.xaml.cs")]
    [InlineData("PlatformDownloadWindow.Avalonia.cs")]
    public void Windows_RestoreAndPersistDownloadSelection(string fileName)
    {
        var code = ReadProjectFile("Workspace1C", "Views", fileName);

        // Issue #167 (1.0.1.127): выбор между сессиями восстанавливается...
        Assert.Contains("PlatformDownloadFilterByArchitecture", code);
        Assert.Contains("PlatformDownloadFilterBySection", code);
        Assert.Contains("PlatformDownloadSelectedSection", code);
        Assert.Contains("PlatformDownloadType", code);
        // Issue #167 (1.0.1.128): значения (а не только галочки) восстанавливаются через
        // ViewModel, разрядность сохраняется; раздел не пишется, пока нет разделов.
        Assert.Contains("RestoreSelection", code);
        Assert.Contains("PlatformDownloadIs64Bit", code);
        Assert.Contains("HasSections", code);
        // ...и сохраняется при изменении (паритет WPF/Avalonia).
        Assert.Contains("SaveSettings", code);
    }

    // ===================== Issue #167 (1.0.1.128): применение после загрузки каталога =====================

    [Fact]
    public async Task Vm_RestoreSelection_SectionAndArchitecture_AppliedAfterLoadCatalog()
    {
        var vm = CreateVm(Release("8.3.27.2214",
            File("win_x64.zip", "x64", GroupPlatform),
            File("win_x86.zip", "x86", GroupPlatform),
            File("thin_x64.zip", "x64", GroupThin)));

        vm.RestoreSelection(
            filterByArchitecture: true,
            filterBySection: true,
            selectedSection: GroupThin,
            downloadType: PlatformDownloadType.Auto,
            is64Bit: true);

        // До загрузки каталога намерение сохранено и не сбрасывается (раньше раздел
        // «исчезал» здесь — RefreshSections с пустым списком файлов обнулял выбор).
        Assert.Equal(GroupThin, vm.SelectedSection);
        Assert.True(vm.FilterByArchitecture);

        await vm.LoadCatalogAsync();

        // После загрузки каталога и вычисления разделов сохранённые значения применены.
        Assert.True(vm.FilterByArchitecture);
        Assert.True(vm.FilterBySection);
        Assert.True(vm.Is64Bit);
        Assert.Equal(GroupThin, vm.SelectedSection);
        var only = Assert.Single(vm.DistributionOptions);
        Assert.Equal("thin_x64.zip", only.File.FileName);
    }

    [Fact]
    public async Task Vm_RestoreSelection_ArchitectureCheckboxAndValue_Restored()
    {
        var vm = CreateVm(Release("8.3.27.2214",
            File("a_x64.zip", "x64"),
            File("b_x86.zip", "x86")));

        // Галочка включена, значение — x86 (в 1.0.1.127 сохранялась только галочка).
        vm.RestoreSelection(
            filterByArchitecture: true,
            filterBySection: false,
            selectedSection: null,
            downloadType: PlatformDownloadType.Auto,
            is64Bit: false);

        await vm.LoadCatalogAsync();

        Assert.True(vm.FilterByArchitecture);
        Assert.False(vm.Is64Bit);
        Assert.DoesNotContain(vm.DistributionOptions, o => o.File.FileName == "a_x64.zip");
        Assert.Contains(vm.DistributionOptions, o => o.File.FileName == "b_x86.zip");
        Assert.Equal("b_x86.zip", vm.PickedFile!.FileName);
    }

    [Fact]
    public async Task Vm_RestoreSelection_SavedSectionMissing_FallsBackToFirstSection()
    {
        var vm = CreateVm(Release("8.3.27.2214",
            File("a_x64.zip", "x64", GroupPlatform),
            File("b_x64.zip", "x64", GroupThin)));

        // Сохранённого раздела в новом каталоге нет — загрузка не падает,
        // фолбэк на первый раздел, старое значение не «залипает».
        vm.RestoreSelection(
            filterByArchitecture: false,
            filterBySection: true,
            selectedSection: "Windows (64-bit)",
            downloadType: PlatformDownloadType.Auto,
            is64Bit: true);

        await vm.LoadCatalogAsync();

        Assert.True(vm.HasSections);
        Assert.True(vm.FilterBySection);
        Assert.Equal(GroupPlatform, vm.SelectedSection);
        Assert.Single(vm.DistributionOptions);
        Assert.NotNull(vm.PickedFile);
    }

    [Fact]
    public async Task Vm_RestoreSelection_NoSections_KeepsIntentAndLoads()
    {
        var vm = CreateVm(Release("8.3.27.2214",
            File("a_x64.zip", "x64"),
            File("b_x86.zip", "x86")));

        // Разделов на странице нет: выбор раздела не применяется, но намерение
        // не теряется (окно не перезапишет настройку пустым значением).
        vm.RestoreSelection(
            filterByArchitecture: false,
            filterBySection: true,
            selectedSection: "Linux (64-bit)",
            downloadType: PlatformDownloadType.Auto,
            is64Bit: false);

        await vm.LoadCatalogAsync();

        Assert.False(vm.HasSections);
        Assert.False(vm.Is64Bit);
        Assert.Equal("Linux (64-bit)", vm.SelectedSection);
        Assert.Equal(2, vm.DistributionOptions.Count);
        Assert.NotNull(vm.PickedFile);
    }

    [Fact]
    public async Task Vm_RestoreSelection_TypeRestoredAfterLoad_WhenAvailable()
    {
        var vm = CreateVm(Release("8.3.27.2214",
            File("setup_x64.zip", "x64", GroupPlatform),
            File("thin_x64.zip", "x64", GroupPlatform)));

        vm.RestoreSelection(
            filterByArchitecture: false,
            filterBySection: false,
            selectedSection: null,
            downloadType: PlatformDownloadType.ThinClient,
            is64Bit: true);

        await vm.LoadCatalogAsync();

        Assert.Contains(PlatformDownloadType.ThinClient, vm.AvailableDownloadTypes);
        Assert.Equal(PlatformDownloadType.ThinClient, vm.DownloadType);
        Assert.Equal("thin_x64.zip", vm.PickedFile!.FileName);
    }

    [Fact]
    public async Task Vm_Reopen_RestoresLastSelection()
    {
        var release = Release("8.3.27.2214",
            File("a_x64.zip", "x64", GroupPlatform),
            File("b_x86.zip", "x86", GroupThin),
            File("c_x64.zip", "x64", GroupThin));

        // Первое открытие: пользователь меняет выбор.
        var first = CreateVm(release);
        await first.LoadCatalogAsync();
        first.FilterByArchitecture = true;
        first.Is64Bit = false;
        first.SelectedSection = GroupThin;
        first.FilterBySection = true;

        // Значения, которые окно сохраняет в AppSettings.
        var savedArchitectureFilter = first.FilterByArchitecture;
        var savedSectionFilter = first.FilterBySection;
        var savedSection = first.SelectedSection;
        var savedType = first.DownloadType;
        var savedIs64Bit = first.Is64Bit;

        // Повторное открытие: выбор восстанавливается после загрузки каталога.
        var second = CreateVm(release);
        second.RestoreSelection(
            savedArchitectureFilter, savedSectionFilter, savedSection, savedType, savedIs64Bit);
        await second.LoadCatalogAsync();

        Assert.True(second.FilterByArchitecture);
        Assert.False(second.Is64Bit);
        Assert.True(second.FilterBySection);
        Assert.Equal(GroupThin, second.SelectedSection);
        Assert.Equal("b_x86.zip", second.PickedFile!.FileName);
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
