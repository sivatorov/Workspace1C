using System;
using System.Collections.Generic;
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
/// Тесты поиска по имени и фильтра по типу «Обозревателя метаданных» (0.3.9.134,
/// этап 3): регистронезависимость, подсветка <c>IsMatch</c>, автораскрытие путей,
/// фильтр по типу (<c>TypeFilter</c>/<c>ApplyFilters</c>), совместное действие
/// поиск+фильтр, лимит 500 результатов, пустой/короткий запрос, ленивый индекс
/// (догружает недостающие типы один раз), debounce ~300 мс. Чистая логика — fake
/// <see cref="IMetadataExplorerService"/> + временная выгрузка на лету
/// (образец <see cref="MetadataExplorerViewModelTests"/>).
/// </summary>
public sealed class MetadataExplorerSearchTests : IDisposable
{
    private readonly string _tempRoot;

    public MetadataExplorerSearchTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "cm_metasearch_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* ignore */ }
    }

    private static Infobase CreateBase(string name = "Бухгалтерия") => new()
    {
        Id = "test-id",
        Name = name,
        PlatformVersion = "8.3.27.1644"
    };

    /// <summary>Шапка XML-файла объекта выгрузки (namespace из реальной выгрузки 8.3.27).</summary>
    private const string Ns =
        "<MetaDataObject xmlns=\"http://v8.1c.ru/8.3/MDClasses\" " +
        "xmlns:app=\"http://v8.1c.ru/8.2/managed-application/core\" " +
        "xmlns:v8=\"http://v8.1c.ru/8.1/data/core\" " +
        "xmlns:xr=\"http://v8.1c.ru/8.3/xcf/readable\" version=\"2.20\">";

    private static MetadataExplorerViewModel CreateVm(IMetadataExplorerService fake) =>
        new(new[] { CreateBase() }, CreateBase(), fake, new RecordingDialogs());

    // ===================== Регистронезависимость и подсветка =====================

    [Fact]
    public async Task Search_CaseInsensitive_FindsObjectAndMarksIsMatch()
    {
        var dumpDir = CreateDumpWithoutSubsystems();
        var vm = CreateVm(new FakeMetadataExplorerService
        {
            BaseResult = new MetadataDump(dumpDir, "Простая", "2.0.0.1")
        });
        await vm.LoadAsync();

        // Верхний регистр против имени «Контрагенты» (без учёта регистра).
        vm.SearchText = "КОНТРАГЕНТЫ";
        vm.ApplySearch();

        Assert.True(vm.IsSearchActive);
        Assert.Equal(1, vm.SearchResultsCount);
        Assert.Equal(2, vm.SearchIndex.Count); // индекс построен при первом поиске (все объекты)

        var objectNode = FindObject(vm, "Контрагенты");
        Assert.NotNull(objectNode);
        Assert.True(objectNode!.IsMatch);
        Assert.False(FindObject(vm, "ЗаказКлиента")!.IsMatch);
        Assert.Contains("FoundFormat", vm.StatusText);
    }

    [Fact]
    public async Task Search_TooManyResults_StatusWarning_LimitedHighlight()
    {
        var dumpDir = CreateDumpWithManyDocuments(count: 600);
        var vm = CreateVm(new FakeMetadataExplorerService
        {
            BaseResult = new MetadataDump(dumpDir, "Большая", "9.9.9.9")
        });
        await vm.LoadAsync();

        vm.SearchText = "docu"; // 600 документов Document0001..Document0600
        vm.ApplySearch();

        Assert.Equal(600, vm.SearchResultsCount);
        Assert.Contains("TooManyResults", vm.StatusText);

        // Подсветка ограничена лимитом 500.
        var highlighted = CountMatches(vm.TreeNodes[0]);
        Assert.Equal(MetadataExplorerViewModel.MaxSearchResults, highlighted);
    }

    // ===================== Пустой / короткий запрос =====================

    [Fact]
    public async Task Search_ShortQuery_TreeModeWithoutHighlights()
    {
        var dumpDir = CreateDumpWithoutSubsystems();
        var vm = CreateVm(new FakeMetadataExplorerService
        {
            BaseResult = new MetadataDump(dumpDir, "Простая", "2.0.0.1")
        });
        await vm.LoadAsync();

        // Сначала полноценный поиск с подсветкой.
        vm.SearchText = "агент";
        vm.ApplySearch();
        Assert.True(vm.IsSearchActive);
        Assert.True(FindObject(vm, "Контрагенты")!.IsMatch);

        // Короткий запрос (1 символ) — режим дерева без подсветки.
        vm.SearchText = "а";
        vm.ApplySearch();

        Assert.False(vm.IsSearchActive);
        Assert.Equal(0, vm.SearchResultsCount);
        Assert.False(FindObject(vm, "Контрагенты")!.IsMatch);
    }

    [Fact]
    public async Task Search_EmptyQuery_TreeMode()
    {
        var dumpDir = CreateDumpWithoutSubsystems();
        var vm = CreateVm(new FakeMetadataExplorerService
        {
            BaseResult = new MetadataDump(dumpDir, "Простая", "2.0.0.1")
        });
        await vm.LoadAsync();

        // Полноценный поиск раскрывает дерево и подсвечивает совпадение.
        vm.SearchText = "агент";
        vm.ApplySearch();
        Assert.True(vm.IsSearchActive);
        Assert.True(FindObject(vm, "Контрагенты")!.IsMatch);

        // Пустой запрос — режим дерева без подсветки.
        vm.SearchText = string.Empty;
        vm.ApplySearch();

        Assert.False(vm.IsSearchActive);
        Assert.Equal(0, vm.SearchResultsCount);
        Assert.False(FindObject(vm, "Контрагенты")!.IsMatch);
        Assert.False(FindObject(vm, "ЗаказКлиента")!.IsMatch);
    }

    // ===================== Автораскрытие путей =====================

    [Fact]
    public async Task Search_ExpandsPathToMatch()
    {
        var dumpDir = CreateDumpWithoutSubsystems();
        var vm = CreateVm(new FakeMetadataExplorerService
        {
            BaseResult = new MetadataDump(dumpDir, "Простая", "2.0.0.1")
        });
        await vm.LoadAsync();

        vm.SearchText = "заказ";
        vm.ApplySearch();

        var match = FindObject(vm, "ЗаказКлиента");
        Assert.NotNull(match);
        Assert.True(match!.IsMatch);

        // Все предки от корня раскрыты.
        for (var current = match.Parent; current is not null; current = current.Parent)
            Assert.True(current.IsExpanded);
    }

    // ===================== Фильтр по типу =====================

    [Fact]
    public async Task TypeFilter_Catalog_HidesDocuments_ShowsCatalogs()
    {
        var dumpDir = CreateDumpWithoutSubsystems();
        var vm = CreateVm(new FakeMetadataExplorerService
        {
            BaseResult = new MetadataDump(dumpDir, "Простая", "2.0.0.1")
        });
        await vm.LoadAsync();

        // Раскрываем «Все объекты» → типы → объекты (нужны загруженные узлы).
        ExpandAll(vm.TreeNodes[0]);

        vm.TypeFilter = vm.TypeFilterOptions.First(o => o.Value == "Catalog");

        var catalogType = FindType(vm, "Catalog");
        var documentType = FindType(vm, "Document");
        Assert.NotNull(catalogType);
        Assert.NotNull(documentType);
        Assert.True(catalogType!.IsVisible);
        Assert.False(documentType!.IsVisible);
        Assert.True(FindObject(vm, "Контрагенты")!.IsVisible);
        Assert.False(FindObject(vm, "ЗаказКлиента")!.IsVisible);
    }

    [Fact]
    public async Task TypeFilter_AllTypes_EverythingVisible()
    {
        var dumpDir = CreateDumpWithoutSubsystems();
        var vm = CreateVm(new FakeMetadataExplorerService
        {
            BaseResult = new MetadataDump(dumpDir, "Простая", "2.0.0.1")
        });
        await vm.LoadAsync();
        ExpandAll(vm.TreeNodes[0]);

        vm.TypeFilter = vm.TypeFilterOptions.First(o => o.Value == "Document");
        Assert.False(FindType(vm, "Catalog")!.IsVisible);

        vm.TypeFilter = vm.TypeFilterOptions.First(o => o.Value.Length == 0); // «Все типы»

        Assert.True(FindType(vm, "Catalog")!.IsVisible);
        Assert.True(FindType(vm, "Document")!.IsVisible);
        Assert.True(FindObject(vm, "Контрагенты")!.IsVisible);
        Assert.True(FindObject(vm, "ЗаказКлиента")!.IsVisible);
    }

    [Fact]
    public async Task SearchPlusTypeFilter_WorkTogether()
    {
        var dumpDir = CreateDumpWithoutSubsystems();
        var vm = CreateVm(new FakeMetadataExplorerService
        {
            BaseResult = new MetadataDump(dumpDir, "Простая", "2.0.0.1")
        });
        await vm.LoadAsync();

        // Поиск помечает совпадение, фильтр скрывает неподходящий тип — обе метки
        // независимы: IsMatch сохраняется, IsVisible отражает выбранный тип.
        vm.SearchText = "агент";
        vm.ApplySearch();
        var matched = FindObject(vm, "Контрагенты");
        Assert.True(matched!.IsMatch);

        vm.TypeFilter = vm.TypeFilterOptions.First(o => o.Value == "Document");

        Assert.True(matched.IsMatch);            // подсветка поиска не сброшена
        Assert.False(matched.IsVisible);         // но тип скрыт фильтром
        Assert.False(FindType(vm, "Catalog")!.IsVisible);
        Assert.True(FindType(vm, "Document")!.IsVisible);
    }

    // ===================== Порядок и отбор (issue #365) =====================

    [Fact]
    public async Task TypeFilterOptions_SortedByDisplayName()
    {
        // issue #365: список фильтра должен быть по алфавиту ОТОБРАЖАЕМОГО имени,
        // а не по внутреннему порядку каталогов. В тестах локализация отдаёт ключ
        // as-is («ConfigDiff.Type.Catalog» < «ConfigDiff.Type.Document»).
        var dumpDir = CreateDumpWithoutSubsystems();
        var vm = CreateVm(new FakeMetadataExplorerService
        {
            BaseResult = new MetadataDump(dumpDir, "Простая", "2.0.0.1")
        });
        await vm.LoadAsync();

        var values = vm.TypeFilterOptions.Where(o => o.Value.Length > 0).Select(o => o.Value).ToList();
        Assert.Equal(new[] { "Catalog", "Document" }, values);
    }

    [Fact]
    public async Task Tree_TypesSortedByDisplayName()
    {
        // issue #365: типы в дереве («Все объекты») — по алфавиту отображаемого имени.
        var dumpDir = CreateDumpWithoutSubsystems();
        var vm = CreateVm(new FakeMetadataExplorerService
        {
            BaseResult = new MetadataDump(dumpDir, "Простая", "2.0.0.1")
        });
        await vm.LoadAsync();

        var root = vm.TreeNodes[0];
        var group = root.Children[0]; // «Все объекты»
        group.EnsureLoaded();

        var typeDirs = group.Children
            .Where(c => c.Kind == MetadataTreeNodeKind.Type)
            .Select(c => c.TypeDir)
            .ToList();
        Assert.Equal(new[] { "Catalog", "Document" }, typeDirs);
    }

    [Fact]
    public async Task Subsystems_SortedByDisplayName()
    {
        // issue #365: подсистемы сортируются по синониму (отображаемому имени),
        // а не по имени файла/каталога выгрузки.
        var dumpDir = CreateDumpWithTwoSubsystems();
        var vm = CreateVm(new FakeMetadataExplorerService
        {
            BaseResult = new MetadataDump(dumpDir, "Разведка", "1.2.3.4")
        });
        await vm.LoadAsync();

        var root = vm.TreeNodes[0];
        var subsystemsContainer = root.Children[0]; // «Подсистемы»
        subsystemsContainer.EnsureLoaded();

        var names = subsystemsContainer.Children
            .Where(c => !c.IsPlaceholder)
            .Select(c => c.DisplayName)
            .ToList();
        Assert.Equal(new[] { "Альфа", "Бета" }, names);
    }

    [Fact]
    public async Task TypeFilter_HidesSubsystemWithoutMatchingObjects()
    {
        // issue #365: «странно отбор сработал» — подсистема, у которой фильтр скрыл
        // ВСЕ типы, прячется целиком (не остаётся пустой веткой).
        var dumpDir = CreateDumpWithTwoSubsystems();
        var vm = CreateVm(new FakeMetadataExplorerService
        {
            BaseResult = new MetadataDump(dumpDir, "Разведка", "1.2.3.4")
        });
        await vm.LoadAsync();

        var root = vm.TreeNodes[0];
        var subsystemsContainer = root.Children[0];
        subsystemsContainer.EnsureLoaded();
        var subsystemNodes = subsystemsContainer.Children.Where(c => !c.IsPlaceholder).ToList();
        Assert.Equal(2, subsystemNodes.Count);

        // Раскрываем обе подсистемы ДО фильтра, чтобы узлы были загружены.
        foreach (var sub in subsystemNodes)
            sub.EnsureLoaded();

        vm.TypeFilter = vm.TypeFilterOptions.First(o => o.Value == "Catalog");

        var alpha = subsystemNodes.First(s => s.DisplayName == "Альфа"); // Catalog.Контрагенты
        var beta = subsystemNodes.First(s => s.DisplayName == "Бета");   // Document.ЗаказКлиента
        Assert.True(alpha.IsVisible);
        Assert.False(beta.IsVisible);
    }

    [Fact]
    public async Task RefreshNodeVisibility_AfterLazyLoad_HidesEmptyContainer()
    {
        // issue #365: фильтр применён ДО раскрытия подсистемы; после ленивой загрузки
        // детей окно зовёт RefreshNodeVisibility — подсистема без видимых потомков прячется.
        var dumpDir = CreateDumpWithTwoSubsystems();
        var vm = CreateVm(new FakeMetadataExplorerService
        {
            BaseResult = new MetadataDump(dumpDir, "Разведка", "1.2.3.4")
        });
        await vm.LoadAsync();

        vm.TypeFilter = vm.TypeFilterOptions.First(o => o.Value == "Catalog");

        var root = vm.TreeNodes[0];
        var subsystemsContainer = root.Children[0];
        subsystemsContainer.EnsureLoaded();
        var subsystemNodes = subsystemsContainer.Children.Where(c => !c.IsPlaceholder).ToList();
        Assert.Equal(2, subsystemNodes.Count);

        // До раскрытия обе подсистемы видимы (заглушки не участвуют в фильтре).
        Assert.All(subsystemNodes, s => Assert.True(s.IsVisible));

        // Раскрываем «Бета» (только Документы) — после загрузки и RefreshNodeVisibility
        // подсистема скрывается, так как фильтр «Справочники» скрыл всех её потомков.
        var beta = subsystemNodes.First(s => s.DisplayName == "Бета");
        beta.EnsureLoaded();
        vm.RefreshNodeVisibility(beta);

        Assert.False(beta.IsVisible);
    }

    [Fact]
    public void SortTypesByDisplayName_SortsByLocalizedDisplayName()
    {
        Func<string, string> fakeT = key => key switch
        {
            "ConfigDiff.Type.Catalog" => "Справочники",
            "ConfigDiff.Type.Document" => "Документы",
            "ConfigDiff.Type.Enum" => "Перечисления",
            _ => key
        };

        var sorted = MetadataTypeLocalizer.SortTypesByDisplayName(
            new[] { "Catalog", "Document", "Enum" }, fakeT).ToList();

        // Документы < Перечисления < Справочники (по алфавиту отображаемого имени).
        Assert.Equal(new[] { "Document", "Enum", "Catalog" }, sorted);
    }

    // ===================== Ленивый индекс =====================

    [Fact]
    public async Task Search_IndexLoadsMissingTypesOnce_SecondSearchReusesIndex()
    {
        var dumpDir = CreateDumpWithoutSubsystems();
        var vm = CreateVm(new FakeMetadataExplorerService
        {
            BaseResult = new MetadataDump(dumpDir, "Простая", "2.0.0.1")
        });
        await vm.LoadAsync();

        var root = vm.TreeNodes[0];
        // До поиска дерево не раскрыто (группа «Все объекты» — только заглушка).
        Assert.False(root.Children[0].IsLoaded);

        vm.SearchText = "агент";
        vm.ApplySearch();

        // Первый поиск догрузил всю недостающую часть дерева: узлы с ленивыми
        // загрузчиками (группы и типы) загружены; объекты — листья без загрузчика.
        Assert.All(root.Children, c => Assert.True(c.IsLoaded));
        var typeNodes = FindTypes(root).ToList();
        Assert.NotEmpty(typeNodes);
        Assert.All(typeNodes, t => Assert.True(t.IsLoaded));
        var objectsBefore = CountObjects(root);

        // Второй поиск идёт по индексу: дерево не перестраивается, детей не дублирует.
        vm.SearchText = "заказ";
        vm.ApplySearch();

        Assert.Equal(1, vm.SearchResultsCount);
        Assert.True(FindObject(vm, "ЗаказКлиента")!.IsMatch);
        Assert.Equal(objectsBefore, CountObjects(root));
    }

    [Fact]
    public async Task Search_NoSubsystemObjects_IncludedInIndex()
    {
        var dumpDir = CreateDumpWithSubsystems();
        var vm = CreateVm(new FakeMetadataExplorerService
        {
            BaseResult = new MetadataDump(dumpDir, "Разведка", "1.2.3.4")
        });
        await vm.LoadAsync();

        // Объект вне подсистем (Documents/ЗаказКлиента) попадает в индекс.
        vm.SearchText = "заказ";
        vm.ApplySearch();

        Assert.Equal(1, vm.SearchResultsCount);
        Assert.True(FindObject(vm, "ЗаказКлиента")!.IsMatch);
        Assert.Contains("ЗаказКлиента", vm.SearchIndex.Select(s => s.Name));
    }

    // ===================== Debounce =====================

    [Fact]
    public async Task SearchText_Debounce_AppliesAfterDelay()
    {
        var dumpDir = CreateDumpWithoutSubsystems();
        var vm = CreateVm(new FakeMetadataExplorerService
        {
            BaseResult = new MetadataDump(dumpDir, "Простая", "2.0.0.1")
        });
        await vm.LoadAsync();

        // Установка текста планирует поиск через ~300 мс; сразу он ещё не применён.
        vm.SearchText = "заказ";
        Assert.False(vm.IsSearchActive);

        // Ждём срабатывания debounce опросом, а не фиксированной паузой: под нагрузкой
        // полного прогона таймер Task.Delay может сработать заметно позже 300 мс,
        // и жёсткий Task.Delay(500) давал ложные падения (flaky).
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!vm.IsSearchActive && DateTime.UtcNow < deadline)
            await Task.Delay(25);

        Assert.True(vm.IsSearchActive, "Поиск после debounce не применился за 10 с.");
        Assert.Equal(1, vm.SearchResultsCount);
        Assert.True(FindObject(vm, "ЗаказКлиента")!.IsMatch);
    }

    // ===================== Помощники =====================

    private static MetadataTreeNodeViewModel? FindObject(MetadataExplorerViewModel vm, string name) =>
        FindObjects(vm.TreeNodes[0]).FirstOrDefault(n => n.ObjectSummary?.Name == name);

    private static MetadataTreeNodeViewModel? FindType(MetadataExplorerViewModel vm, string typeDir) =>
        FindTypes(vm.TreeNodes[0]).FirstOrDefault(n => n.TypeDir == typeDir);

    private static IEnumerable<MetadataTreeNodeViewModel> FindObjects(MetadataTreeNodeViewModel node)
    {
        if (node.Kind == MetadataTreeNodeKind.Object && node.ObjectSummary is not null)
            yield return node;
        foreach (var child in node.Children)
            foreach (var found in FindObjects(child))
                yield return found;
    }

    private static IEnumerable<MetadataTreeNodeViewModel> FindTypes(MetadataTreeNodeViewModel node)
    {
        if (node.Kind == MetadataTreeNodeKind.Type)
            yield return node;
        foreach (var child in node.Children)
            foreach (var found in FindTypes(child))
                yield return found;
    }

    private static int CountObjects(MetadataTreeNodeViewModel node) => FindObjects(node).Count();

    private static int CountMatches(MetadataTreeNodeViewModel node) => FindObjects(node).Count(n => n.IsMatch);

    /// <summary>Раскрывает дерево целиком (EnsureLoaded для каждого узла).</summary>
    private static void ExpandAll(MetadataTreeNodeViewModel node)
    {
        node.EnsureLoaded();
        foreach (var child in node.Children)
            ExpandAll(child);
    }

    // ===================== Фикстуры =====================

    /// <summary>Выгрузка с подсистемами: Subsystems/Основное + Catalogs/Контрагенты + Documents/ЗаказКлиента.</summary>
    private string CreateDumpWithSubsystems()
    {
        var dir = Path.Combine(_tempRoot, "dump_sys_" + Guid.NewGuid().ToString("N"));
        WriteFile(Path.Combine(dir, "Configuration.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Configuration><Properties><Name>Разведка</Name><Synonym/><Comment/>" +
            "<Version>1.2.3.4</Version></Properties><ChildObjects/></Configuration></MetaDataObject>");
        WriteFile(Path.Combine(dir, "Subsystems", "Основное.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Subsystem><Properties><Name>Основное</Name>" +
            "<Synonym><v8:item><v8:content>Основное</v8:content></v8:item></Synonym>" +
            "<Comment/><Content><v8:item><v8:content>Catalog.Контрагенты</v8:content></v8:item></Content>" +
            "</Properties><ChildObjects/></Subsystem></MetaDataObject>");
        WriteFile(Path.Combine(dir, "Catalogs", "Контрагенты.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Catalog><Properties><Name>Контрагенты</Name>" +
            "<Synonym><v8:item><v8:content>Контрагенты</v8:content></v8:item></Synonym>" +
            "<Comment>Справочник контрагентов</Comment><Hierarchical>true</Hierarchical>" +
            "</Properties><ChildObjects/></Catalog></MetaDataObject>");
        WriteFile(Path.Combine(dir, "Documents", "ЗаказКлиента.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Document><Properties><Name>ЗаказКлиента</Name><Synonym/><Comment/></Properties>" +
            "<ChildObjects/></Document></MetaDataObject>");
        return dir;
    }

    /// <summary>
    /// Выгрузка с двумя подсистемами (issue #365): «Zeta» с синонимом «Альфа»
    /// (состав — Catalog.Контрагенты) и «Alpha» с синонимом «Бета» (состав —
    /// Document.ЗаказКлиента). Имена файлов идут в порядке, отличном от порядка
    /// синонимов, чтобы проверять сортировку по отображаемому имени.
    /// </summary>
    private string CreateDumpWithTwoSubsystems()
    {
        var dir = Path.Combine(_tempRoot, "dump_two_" + Guid.NewGuid().ToString("N"));
        WriteFile(Path.Combine(dir, "Configuration.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Configuration><Properties><Name>Разведка</Name><Synonym/><Comment/>" +
            "<Version>1.2.3.4</Version></Properties><ChildObjects/></Configuration></MetaDataObject>");
        WriteFile(Path.Combine(dir, "Subsystems", "Zeta.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Subsystem><Properties><Name>Zeta</Name>" +
            "<Synonym><v8:item><v8:content>Альфа</v8:content></v8:item></Synonym>" +
            "<Comment/><Content><v8:item><v8:content>Catalog.Контрагенты</v8:content></v8:item></Content>" +
            "</Properties><ChildObjects/></Subsystem></MetaDataObject>");
        WriteFile(Path.Combine(dir, "Subsystems", "Alpha.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Subsystem><Properties><Name>Alpha</Name>" +
            "<Synonym><v8:item><v8:content>Бета</v8:content></v8:item></Synonym>" +
            "<Comment/><Content><v8:item><v8:content>Document.ЗаказКлиента</v8:content></v8:item></Content>" +
            "</Properties><ChildObjects/></Subsystem></MetaDataObject>");
        WriteFile(Path.Combine(dir, "Catalogs", "Контрагенты.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Catalog><Properties><Name>Контрагенты</Name>" +
            "<Synonym><v8:item><v8:content>Контрагенты</v8:content></v8:item></Synonym>" +
            "<Comment>Справочник контрагентов</Comment><OrderedHierarchical>true</OrderedHierarchical>" +
            "</Properties><ChildObjects/></Catalog></MetaDataObject>");
        WriteFile(Path.Combine(dir, "Documents", "ЗаказКлиента.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Document><Properties><Name>ЗаказКлиента</Name><Synonym/><Comment/></Properties>" +
            "<ChildObjects/></Document></MetaDataObject>");
        return dir;
    }

    /// <summary>Выгрузка без подсистем: Catalogs/Контрагенты + Documents/ЗаказКлиента.</summary>
    private string CreateDumpWithoutSubsystems()
    {
        var dir = Path.Combine(_tempRoot, "dump_obj_" + Guid.NewGuid().ToString("N"));
        WriteFile(Path.Combine(dir, "Configuration.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Configuration><Properties><Name>Простая</Name><Synonym/><Comment/>" +
            "<Version>2.0.0.1</Version></Properties><ChildObjects/></Configuration></MetaDataObject>");
        WriteFile(Path.Combine(dir, "Catalogs", "Контрагенты.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Catalog><Properties><Name>Контрагенты</Name>" +
            "<Synonym><v8:item><v8:content>Контрагенты</v8:content></v8:item></Synonym>" +
            "<Comment>Справочник контрагентов</Comment><OrderedHierarchical>true</OrderedHierarchical>" +
            "</Properties><ChildObjects/></Catalog></MetaDataObject>");
        WriteFile(Path.Combine(dir, "Documents", "ЗаказКлиента.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Document><Properties><Name>ЗаказКлиента</Name><Synonym/><Comment/></Properties>" +
            "<ChildObjects/></Document></MetaDataObject>");
        return dir;
    }

    /// <summary>Выгрузка со множеством документов (для теста лимита результатов поиска).</summary>
    private string CreateDumpWithManyDocuments(int count)
    {
        var dir = Path.Combine(_tempRoot, "dump_many_" + Guid.NewGuid().ToString("N"));
        WriteFile(Path.Combine(dir, "Configuration.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Configuration><Properties><Name>Большая</Name><Synonym/><Comment/>" +
            "<Version>9.9.9.9</Version></Properties><ChildObjects/></Configuration></MetaDataObject>");
        for (var i = 1; i <= count; i++)
        {
            WriteFile(Path.Combine(dir, "Documents", $"Document{i:D4}.xml"),
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
                "<Document><Properties><Name>" + $"Document{i:D4}" + "</Name><Synonym/><Comment/></Properties>" +
                "<ChildObjects/></Document></MetaDataObject>");
        }
        return dir;
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    // ===================== Fakes =====================

    /// <summary>Fake-сервис выгрузки: фиксирует вызовы, возвращает заданные результаты.</summary>
    private sealed class FakeMetadataExplorerService : IMetadataExplorerService
    {
        public MetadataDump? BaseResult { get; set; }
        public MetadataDump? CfResult { get; set; }
        public Exception? BaseException { get; set; }
        public Exception? CfException { get; set; }

        public int BaseCalls { get; private set; }
        public int CfCalls { get; private set; }
        public string? LastCfPath { get; private set; }

        public Task<MetadataDump> DumpFromBaseAsync(
            Infobase infobase, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            BaseCalls++;
            if (BaseException is not null)
                throw BaseException;
            progress?.Report("dump");
            return Task.FromResult(BaseResult!);
        }

        public Task<MetadataDump> DumpFromCfAsync(
            string cfPath, string platformVersion, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            CfCalls++;
            LastCfPath = cfPath;
            if (CfException is not null)
                throw CfException;
            progress?.Report("dump");
            return Task.FromResult(CfResult!);
        }
    }

    /// <summary>Запись диалогов для тестов: ничего не показывает, выбор файла — настраиваемый.</summary>
    private sealed class RecordingDialogs : IDialogService
    {
        public string? OpenFileResult { get; set; }

        public void ShowInfo(string message, string title = "") { }
        public void ShowWarning(string message, string title = "") { }
        public void ShowError(string message, string title = "") { }
        public bool Confirm(string message, string title = "") => true;
        public string? OpenFileDialog(string title = "", string filter = "", string? initialDirectory = null) => OpenFileResult;
        public string? SaveFileDialog(string title = "", string defaultFileName = "", string filter = "", string? initialDirectory = null) => null;
        public string? OpenFolderDialog(string title = "", string? initialDirectory = null) => null;
    }
}