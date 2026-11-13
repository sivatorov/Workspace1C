using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Расширения конфигурации (выпуск 1.0.0.69): ключи командной строки конфигуратора,
/// разбор списка расширений, безопасность значений и логика окна.
/// </summary>
public sealed class ConfigExtensionServiceTests
{
    [Fact]
    public void InstallArguments_LoadAndUpdateExtension()
    {
        // issue #87 («Загрузка расширения конфигурации в клиент-серверную базу»): у
        // /UpdateDBCfg ключа -Extension НЕТ — повторный ключ у операции применения давал
        // «Error in command-line options» (код 1) на клиент-серверной базе. Расширение
        // применяется последующим /UpdateDBCfg без повторения ключа.
        var args = ConfigExtensionService.BuildInstallArguments(@"C:\cfe\Доработки.cfe", " Доработки ");
        Assert.Equal("/LoadCfg\"C:\\cfe\\Доработки.cfe\" -Extension \"Доработки\" /UpdateDBCfg", args);
        Assert.DoesNotContain("-Extension",
            args.Substring(args.IndexOf("/UpdateDBCfg", StringComparison.Ordinal)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void InstallArguments_ServerInfobase_NoDefectiveOptions()
    {
        // issue #87 (юнит-тест сборки аргументов для клиент-серверной базы): полная
        // строка DESIGNER для серверной базы — подключение в форме /S"srv\db", а в
        // ключах операции НЕ повторяется -Extension у /UpdateDBCfg («Error in
        // command-line options») и нет лишних ключей вроде /AppAutoCheckMode.
        var infobase = new Infobase
        {
            Name = "Серверная",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "srv-1c",
                Port = 2541,
                DatabaseName = "buh"
            }
        };
        var connectionArg = OneCLauncher.BuildConnectionArgument(infobase);
        var args = ConfigExtensionService.BuildInstallArguments(@"C:\cfe\Доработки.cfe", "Доработки")!;

        Assert.Equal("/S\"srv-1c:2541\\buh\"", connectionArg);
        var full = DesignerCommandRunner.BuildArguments(connectionArg, "", args, "C:\\tmp\\out.log");
        Assert.StartsWith("DESIGNER /S\"srv-1c:2541\\buh\"", full);
        var updateIndex = full.IndexOf("/UpdateDBCfg", StringComparison.Ordinal);
        Assert.True(updateIndex > 0, "/UpdateDBCfg не найден в строке запуска");
        Assert.DoesNotContain("-Extension", full.Substring(updateIndex), StringComparison.Ordinal);
        Assert.DoesNotContain("/AppAutoCheckMode", full, StringComparison.Ordinal);
    }

    [Fact]
    public void IsCommandLineOptionsError_DetectsDesignerParameterFailure()
    {
        // issue #87: fallback на ibcmd при отказе конфигуратора «Error in command-line options».
        Assert.True(ConfigExtensionService.IsCommandLineOptionsError(
            new DesignerCommandResult { ExitCode = 1, Error = "Конфигуратор завершился с кодом 1: Error in command-line options" }));
        Assert.True(ConfigExtensionService.IsCommandLineOptionsError(
            new DesignerCommandResult { ExitCode = 1, Log = "… Error in command-line options …" }));
        Assert.False(ConfigExtensionService.IsCommandLineOptionsError(
            new DesignerCommandResult { Success = true, ExitCode = 0 }));
        Assert.False(ConfigExtensionService.IsCommandLineOptionsError(
            new DesignerCommandResult { ExitCode = 1, Error = "База занята" }));
        Assert.False(ConfigExtensionService.IsCommandLineOptionsError(null!));
    }

    [Fact]
    public void IsAuthenticationError_DetectsInfobaseUserNotAuthenticated()
    {
        // issue #87: база без собственных учётных данных при незаданной авторизации
        // запуска — конфигуратор падает с «The infobase user is not authenticated».
        Assert.True(ConfigExtensionService.IsAuthenticationError(
            new DesignerCommandResult { ExitCode = 1, Error = "РД_001 3.0 (агенты): Конфигуратор завершился с кодом 1: The infobase user is not authenticated" }));
        Assert.True(ConfigExtensionService.IsAuthenticationError(
            new DesignerCommandResult { ExitCode = 1, Log = "… The infobase user is not authenticated …" }));
        Assert.False(ConfigExtensionService.IsAuthenticationError(
            new DesignerCommandResult { Success = true, ExitCode = 0 }));
        Assert.False(ConfigExtensionService.IsAuthenticationError(
            new DesignerCommandResult { ExitCode = 1, Error = "Error in command-line options" }));
        Assert.False(ConfigExtensionService.IsAuthenticationError(null!));
    }

    [Fact]
    public void ShouldFallbackToIbcmd_CoversBothFailureReasons()
    {
        // issue #87: переход на ibcmd-ветку нужен и при «command-line options», и при
        // сбое аутентификации пользователя ИБ.
        Assert.True(ConfigExtensionService.ShouldFallbackToIbcmd(
            new DesignerCommandResult { ExitCode = 1, Error = "Error in command-line options" }));
        Assert.True(ConfigExtensionService.ShouldFallbackToIbcmd(
            new DesignerCommandResult { ExitCode = 1, Error = "The infobase user is not authenticated" }));
        Assert.False(ConfigExtensionService.ShouldFallbackToIbcmd(
            new DesignerCommandResult { Success = true, ExitCode = 0 }));
        Assert.False(ConfigExtensionService.ShouldFallbackToIbcmd(
            new DesignerCommandResult { ExitCode = 1, Error = "База занята" }));
        Assert.False(ConfigExtensionService.ShouldFallbackToIbcmd(null!));
    }

    [Fact]
    public void DumpAndDeleteArguments()
    {
        Assert.Equal("/DumpCfg\"/tmp/x.cfe\" -Extension \"Ext\"", ConfigExtensionService.BuildDumpArguments("/tmp/x.cfe", "Ext"));
        Assert.Equal("/DeleteCfg -Extension \"Ext\"", ConfigExtensionService.BuildDeleteArguments("Ext"));
    }

    [Theory]
    [InlineData("bad\"name")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("line\nbreak")]
    public void UnsafeValues_AreRejected(string name)
    {
        Assert.Null(ConfigExtensionService.BuildDeleteArguments(name));
        Assert.Null(ConfigExtensionService.BuildInstallArguments("/tmp/a.cfe", name));
    }

    [Fact]
    public void SuggestName_FromFileName()
    {
        Assert.Equal("Доработки_1.2", ConfigExtensionService.SuggestName("/x/Доработки_1.2.cfe"));
        Assert.Equal(string.Empty, ConfigExtensionService.SuggestName(null));
    }

    [Fact]
    public void ParseList_ReturnsNamesWithoutServiceLines()
    {
        var log = "\uFEFFДоработки\r\nИсправления_ЗУП\r\n\r\nДоработки\r\nОбновление конфигурации успешно завершено.\r\n";
        var names = ConfigExtensionService.ParseList(log);
        Assert.Equal(new[] { "Доработки", "Исправления_ЗУП" }, names.ToArray());
        Assert.Empty(ConfigExtensionService.ParseList(null));
    }

    [Fact]
    public void DesignerArguments_ContainOutAndDisableDialogs()
    {
        var args = DesignerCommandRunner.BuildArguments("/F\"C:\\db\"", " /N\"Admin\"", "/DeleteCfg -Extension \"E\"", "C:\\tmp\\out.log");
        Assert.StartsWith("DESIGNER /F\"C:\\db\" /N\"Admin\" /DeleteCfg", args);
        Assert.Contains("/DisableStartupDialogs", args);
        Assert.EndsWith("/Out\"C:\\tmp\\out.log\"", args);
    }

    [Fact]
    public void DecodeLog_Utf8AndBom()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("Привет")).ToArray();
        Assert.Equal("Привет", DesignerCommandRunner.DecodeLog(bytes));
        Assert.Equal(string.Empty, DesignerCommandRunner.DecodeLog(System.Array.Empty<byte>()));
    }

    [Fact]
    public void ViewModel_SelectedBaseIsCheckedAndFilterWorks()
    {
        var a = new Infobase { Id = "a", Name = "Бухгалтерия" };
        var b = new Infobase { Id = "b", Name = "ЗУП" };
        var vm = new ExtensionsViewModel(new[] { a, b }, b);
        Assert.Equal(2, vm.Targets.Count);
        Assert.Single(vm.CheckedTargets);
        Assert.Same(b, vm.CheckedTargets[0].Infobase);
        Assert.Same(b, vm.SelectedTarget!.Infobase);

        vm.SearchText = "бух";
        Assert.Single(vm.Targets);
        vm.SetAllChecked(true);
        Assert.Equal(2, vm.CheckedTargets.Count);
    }

    /// <summary>
    /// Issue #117 п.2 (v1.0.1.49): имя расширения перезаполняется при КАЖДОМ выборе
    /// файла, пока пользователь не ввёл имя вручную (см. V100149IssuesTests —
    /// ручное имя не затирается).
    /// </summary>
    [Fact]
    public void ViewModel_CfePathRewritesSuggestedNameOnEveryFileChoice()
    {
        var vm = new ExtensionsViewModel(System.Array.Empty<Infobase>(), null);
        vm.CfePath = "/x/Первое.cfe";
        Assert.Equal("Первое", vm.ExtensionName);
        vm.CfePath = "/x/Второе.cfe";
        Assert.Equal("Второе", vm.ExtensionName);
        // Повторный выбор того же файла после очистки имени перезаполняет его.
        vm.ExtensionName = "";
        vm.CfePath = "/x/Второе.cfe";
        Assert.Equal("Второе", vm.ExtensionName);
    }

    // ---------------------------------------------------------------------------
    // Issue #117: настоящее имя расширения из .cfe и фактический результат установки.
    // ---------------------------------------------------------------------------

    private static string TempCfeDir =>
        Path.Combine(Path.GetTempPath(), "ws1c_cfe_tests");

    /// <summary>
    /// Собирает синтетический .cfe — нативный контейнер 1С (не zip!) с одним элементом,
    /// содержащим текстовое дерево корневого элемента метаданных расширения. Формат
    /// проверен на реальных .cfe (см. <see cref="ConfigExtensionService.TryReadExtensionName"/>).
    /// </summary>
    internal static byte[] BuildCfeContainer(string extensionName) =>
        BuildCfeContainerFromPayload(Encoding.UTF8.GetBytes(BuildMetadataText(extensionName)));

    /// <summary>Текстовое дерево метаданных расширения (как в реальном .cfe).</summary>
    internal static string BuildMetadataText(string extensionName)
    {
        const string rootGuid = "3354911e-a8f6-4d0c-9df2-e48caec5f502";
        const string configurationClass = "9cd510cd-abfc-11d4-9434-004095e12fc7";
        const string rootClassGuid = "cea2b2f0-16d8-41f9-b375-a44b8da27f74";
        return "\uFEFF{2,\r\n{" + rootGuid + "},7,\r\n{" + configurationClass + ",\r\n{1,\r\n{79,\r\n{0,\r\n{4,\r\n{1,0," +
               rootClassGuid + "},\"" + extensionName + "\",\r\n{1,\"ru\",\"Тест\"},1}";
    }

    /// <summary>
    /// Собирает контейнер с произвольным содержимым элемента: payload может быть
    /// текстом метаданных, вложенным контейнером или крупным блоком (issue #117).
    /// </summary>
    internal static byte[] BuildCfeContainerFromPayload(byte[] payload, bool compress = true)
    {
        var elementGuid = "009e27bc-bd81-4498-9b31-171e075e6ac1";
        var elementHeader = new byte[20 + elementGuid.Length * 2];
        Encoding.Unicode.GetBytes(elementGuid).CopyTo(elementHeader, 20);

        var headerBlock = Block(Deflate(elementHeader));
        var payloadBlock = Block(compress ? Deflate(payload) : payload);

        // Таблица адресов: одна запись «hdrAddr, dataAddr, 0x7FFFFFFF» + терминирующие
        // нули. Адрес зависят от длины deflate-таблицы, которая зависит от содержимого
        // (адресов) — сходится итеративно.
        var table = new byte[24];
        BitConverter.GetBytes(0x7FFFFFFFu).CopyTo(table, 8);
        int headerAddr = 0, dataAddr = 0, previousLength = -1;
        byte[] tableBlock = Array.Empty<byte>();
        for (var iteration = 0; iteration < 16; iteration++)
        {
            tableBlock = Block(Deflate(table));
            if (tableBlock.Length == previousLength)
                break;
            previousLength = tableBlock.Length;
            headerAddr = 16 + tableBlock.Length;
            dataAddr = headerAddr + headerBlock.Length;
            BitConverter.GetBytes((uint)headerAddr).CopyTo(table, 0);
            BitConverter.GetBytes((uint)dataAddr).CopyTo(table, 4);
        }

        var container = new byte[16 + tableBlock.Length + headerBlock.Length + payloadBlock.Length];
        BitConverter.GetBytes(0x7FFFFFFFu).CopyTo(container, 0);   // сигнатура контейнера
        BitConverter.GetBytes(512u).CopyTo(container, 4);          // размер страницы
        BitConverter.GetBytes(1u).CopyTo(container, 8);            // версия хранилища
        tableBlock.CopyTo(container, 16);
        headerBlock.CopyTo(container, 16 + tableBlock.Length);
        payloadBlock.CopyTo(container, 16 + tableBlock.Length + headerBlock.Length);
        return container;
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(data, 0, data.Length);
        return output.ToArray();
    }

    /// <summary>ASCII-заголовок блока контейнера 1С + данные (одна страница).</summary>
    private static byte[] Block(byte[] payload)
    {
        var header = "\r\n" + payload.Length.ToString("X8") + " " +
                     payload.Length.ToString("X8") + " 7FFFFFFF \r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);
        var result = new byte[headerBytes.Length + payload.Length];
        headerBytes.CopyTo(result, 0);
        payload.CopyTo(result, headerBytes.Length);
        return result;
    }

    [Fact]
    public void TryReadExtensionName_ReadsRealNameFromContainer()
    {
        // Issue #117: настоящее имя расширения читается из .cfe даже когда оно
        // отличается от имени файла (сценарий «дубль под другим именем» у RafaIS).
        Directory.CreateDirectory(TempCfeDir);
        var path = Path.Combine(TempCfeDir, "ИмяФайла_СовсемДругое.cfe");
        File.WriteAllBytes(path, BuildCfeContainer("НастоящееИмя_Расширения"));
        try
        {
            Assert.Equal("НастоящееИмя_Расширения", ConfigExtensionService.TryReadExtensionName(path));
            Assert.Equal("НастоящееИмя_Расширения", ConfigExtensionService.ResolveName(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryReadExtensionName_ReturnsNullForGarbageOrMissingFile()
    {
        Directory.CreateDirectory(TempCfeDir);
        var garbage = Path.Combine(TempCfeDir, "garbage.cfe");
        File.WriteAllBytes(garbage, new byte[] { 1, 2, 3, 4, 5 });
        var missing = Path.Combine(TempCfeDir, "missing.cfe");
        try
        {
            Assert.Null(ConfigExtensionService.TryReadExtensionName(garbage));
            Assert.Null(ConfigExtensionService.TryReadExtensionName(missing));
            Assert.Null(ConfigExtensionService.TryReadExtensionName(null));
            Assert.Equal(string.Empty, ConfigExtensionService.ResolveName(null));
        }
        finally
        {
            File.Delete(garbage);
        }
    }

    [Fact]
    public void FindRefusalMessage_DetectsRefusalAndIgnoresSuccess()
    {
        // Issue #117: «загрузка конфигурации успешно завершена» при фактическом отказе —
        // журнал не должен считать это успехом.
        var refusedLog = "Загрузка конфигурации успешно завершена\r\nОбновление информационной базы не выполнено.\r\n";
        Assert.Equal("Обновление информационной базы не выполнено.",
            ConfigExtensionService.FindRefusalMessage(refusedLog));
        Assert.NotNull(ConfigExtensionService.FindRefusalMessage("… The infobase update has failed …"));
        Assert.NotNull(ConfigExtensionService.FindRefusalMessage("Обновление выполнено с ошибками."));
        Assert.Null(ConfigExtensionService.FindRefusalMessage(
            "Загрузка конфигурации успешно завершена\nОбновление информационной базы выполнено успешно."));
        Assert.Null(ConfigExtensionService.FindRefusalMessage(string.Empty));
        Assert.Null(ConfigExtensionService.FindRefusalMessage(null));
    }

    [Fact]
    public void ResolveInstallName_RealCfeNameWinsOverFileName_PreventsDuplicate()
    {
        // Issue #117 (защита от дубля): решение о имени установки — чистая функция.
        // По умолчанию установка выполняется под НАСТОЯЩИМ именем из .cfe, чтобы
        // /LoadCfg -Extension обновил существующее расширение, а не создал дубль
        // (сценарий RafaIS: то же расширение установилось под именем файла).
        Assert.Equal("НастоящееИмя",
            ConfigExtensionService.ResolveInstallName("НастоящееИмя", "ИмяФайла", nameEdited: false));
        Assert.Equal("НастоящееИмя",
            ConfigExtensionService.ResolveInstallName("НастоящееИмя", string.Empty, nameEdited: false));
        // Совпадение с настоящим именем (без учёта регистра) — дубля не будет,
        // имя из поля остаётся как есть.
        Assert.Equal("настоящееИмя",
            ConfigExtensionService.ResolveInstallName("НастоящееИмя", "настоящееИмя", nameEdited: false));
        // Ручной ввод имени пользователем — остаётся введённое имя (отдельное расширение,
        // в журнале окна выводится предупреждение о дубликате).
        Assert.Equal("СвоёИмя",
            ConfigExtensionService.ResolveInstallName("НастоящееИмя", "СвоёИмя", nameEdited: true));
        // Настоящее имя прочитать не удалось — остаётся имя файла/поля.
        Assert.Equal("ИмяФайла", ConfigExtensionService.ResolveInstallName(null, "ИмяФайла", nameEdited: false));
        Assert.Equal("ИмяФайла", ConfigExtensionService.ResolveInstallName("  ", "ИмяФайла", nameEdited: true));
    }

    /// <summary>
    /// Issue #117 (сценарий дубля RafaIS): имя файла «ИмяФайла_СовсемДругое», настоящее
    /// имя расширения в .cfe «НастоящееИмя_Расширения». При выборе файла поле имени
    /// заполняется настоящим именем — установка под ним обновит существующее расширение,
    /// а не создаст копию; повторный выбор того же файла имя не меняет.
    /// </summary>
    [Fact]
    public void ViewModel_DuplicateScenario_InstallsUnderRealCfeName()
    {
        Directory.CreateDirectory(TempCfeDir);
        var path = Path.Combine(TempCfeDir, "ИмяФайла_СовсемДругое.cfe");
        File.WriteAllBytes(path, BuildCfeContainer("НастоящееИмя_Расширения"));
        try
        {
            var vm = new ExtensionsViewModel(System.Array.Empty<Infobase>(), null);
            vm.CfePath = path;
            Assert.Equal("НастоящееИмя_Расширения", vm.ExtensionName);
            Assert.NotEqual("ИмяФайла_СовсемДругое", vm.ExtensionName);

            // Повторный выбор того же файла (по «Обзор») — имя остаётся настоящим.
            vm.CfePath = path;
            Assert.Equal("НастоящееИмя_Расширения", vm.ExtensionName);

            // Решение о имени установки: ручное имя из поля НЕ считается ручным вводом,
            // если совпадает с настоящим — установка пройдёт под именем из .cfe.
            Assert.Equal("НастоящееИмя_Расширения",
                ConfigExtensionService.ResolveInstallName(
                    ConfigExtensionService.TryReadExtensionName(path), vm.ExtensionName,
                    nameEdited: false));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Issue #117: при выборе .cfe «Обзором» подставляется НАСТОЯЩЕЕ имя расширения
    /// из файла, а не имя файла; вручную введённое имя не перезаписывается.
    /// </summary>
    [Fact]
    public void ViewModel_CfePath_SubstitutesRealExtensionNameFromCfe()
    {
        Directory.CreateDirectory(TempCfeDir);
        var first = Path.Combine(TempCfeDir, "ИмяФайла_Первый.cfe");
        var second = Path.Combine(TempCfeDir, "ИмяФайла_Второй.cfe");
        File.WriteAllBytes(first, BuildCfeContainer("НастоящееИмя_Первый"));
        File.WriteAllBytes(second, BuildCfeContainer("НастоящееИмя_Второй"));
        try
        {
            var vm = new ExtensionsViewModel(System.Array.Empty<Infobase>(), null);
            vm.CfePath = first;
            Assert.Equal("НастоящееИмя_Первый", vm.ExtensionName);

            // Ручной ввод имени уважается даже при выборе другого файла.
            vm.ExtensionName = "МоёИмя";
            vm.CfePath = second;
            Assert.Equal("МоёИмя", vm.ExtensionName);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    // ---------------------------------------------------------------------------
    // Issue #117 («ИР»): устойчивость разбора .cfe к разным раскладкам контейнера.
    // ---------------------------------------------------------------------------

    [Fact]
    public void TryReadExtensionName_ReadsUtf16Metadata()
    {
        // Часть .cfe хранит текстовое дерево в UTF-16LE (с BOM) — прежний разбор
        // искал GUID-класс «Конфигурация» только в UTF-8 и имя не находил.
        Directory.CreateDirectory(TempCfeDir);
        var path = Path.Combine(TempCfeDir, "utf16.cfe");
        var metadata = BuildMetadataText("Имя_UTF16");
        File.WriteAllBytes(path, BuildCfeContainerFromPayload(Encoding.Unicode.GetBytes(metadata)));
        try
        {
            Assert.Equal("Имя_UTF16", ConfigExtensionService.TryReadExtensionName(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryReadExtensionName_ReadsFromNestedContainer()
    {
        // Метаданные могут лежать во вложенном контейнере глубже первого уровня
        // (раскладка, на которой ломался прежний maxDepth: 1).
        Directory.CreateDirectory(TempCfeDir);
        var path = Path.Combine(TempCfeDir, "nested.cfe");
        var inner = BuildCfeContainer("Имя_Вложенное");
        File.WriteAllBytes(path, BuildCfeContainerFromPayload(inner, compress: false));
        try
        {
            Assert.Equal("Имя_Вложенное", ConfigExtensionService.TryReadExtensionName(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryReadExtensionName_ReadsFromLargeElement()
    {
        // У «ИР» элемент метаданных может быть больше прежнего предела 1 МБ.
        Directory.CreateDirectory(TempCfeDir);
        var path = Path.Combine(TempCfeDir, "large.cfe");
        var payload = Encoding.UTF8.GetBytes(new string(' ', 1_200_000) + BuildMetadataText("Имя_Большое"));
        Assert.True(payload.Length > 1024 * 1024);
        File.WriteAllBytes(path, BuildCfeContainerFromPayload(payload, compress: false));
        try
        {
            Assert.Equal("Имя_Большое", ConfigExtensionService.TryReadExtensionName(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryReadExtensionName_ReadsNameNearPurposeMarker()
    {
        // Альтернативная раскладка: имени нет сразу после класса «Конфигурация»,
        // зато оно стоит рядом с маркером ConfigurationExtensionPurpose.
        Directory.CreateDirectory(TempCfeDir);
        var path = Path.Combine(TempCfeDir, "purpose.cfe");
        var metadata = "\uFEFF{2,{3354911e-a8f6-4d0c-9df2-e48caec5f502},7," +
                       "{\"ConfigurationExtensionPurpose\",1," +
                       "{1,0,cea2b2f0-16d8-41f9-b375-a44b8da27f74},\"Имя_Из_Назначения\"}}";
        File.WriteAllBytes(path,
            BuildCfeContainerFromPayload(Encoding.UTF8.GetBytes(metadata), compress: true));
        try
        {
            Assert.Equal("Имя_Из_Назначения", ConfigExtensionService.TryReadExtensionName(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryReadExtensionName_ReadsFromZipContainer()
    {
        // Перепакованный .cfe может быть обычным zip-архивом.
        Directory.CreateDirectory(TempCfeDir);
        var path = Path.Combine(TempCfeDir, "zipped.cfe");
        using (var memory = new MemoryStream())
        {
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = archive.CreateEntry("config");
                using var entryStream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(BuildMetadataText("Имя_Из_Zip"));
                entryStream.Write(bytes, 0, bytes.Length);
            }
            File.WriteAllBytes(path, memory.ToArray());
        }
        try
        {
            Assert.Equal("Имя_Из_Zip", ConfigExtensionService.TryReadExtensionName(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
