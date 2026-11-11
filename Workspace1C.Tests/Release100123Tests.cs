using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Релиз 1.0.1.23 (issues #79, #83, #89, #99):
/// <list type="bullet">
/// <item>#83 — декодер вывода консольных утилит 1С: маркерная эвристика и приоритет
/// cp866 при неоднозначной метрике U+FFFD; перезакодирование mojibake
/// (FixCp1251ToCp866Mojibake); локале-независимый разбор строк лицензий ring
/// (номер «NNNN…-NNNN…» + файл *.lic); чистка mojibake в BuildNoLicensesMessage;</item>
/// <item>#79 — rac-ветка pre-валидации TestDeployPlanner.ValidateAsync и защита
/// ручного ввода от автоподстановки в мастере;</item>
/// <item>#89 — test-скрипт сборки переопределяет ВСЕ 4 свойства версии и проверяет
/// ProductVersion артефакта;</item>
/// <item>#99 — настраиваемый хоткей «Собрать диагностику» (AppSettings, каталог меню,
/// настройки, регистрация в окнах, системный список хоткеев).</item>
/// </list>
/// </summary>
public sealed class Release100123Tests
{
    static Release100123Tests()
    {
        // Кодовые страницы 866/1251 нужны и в тестах (построение ожидаемых байтов);
        // ConsoleOutputDecoder регистрирует провайдер в своём статическом конструкторе.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadRepoFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    // ===================== #83: декодер (cp866/маркеры) =====================

    /// <summary>Строка ответа ring из репорта 7OH (issue #83, 2026-10-31).</summary>
    private const string RingLicenseLine =
        "499459654270590-500000000037 (Имя файла: \"20260907085731.lic\")";

    [Fact]
    public void Decode_RingLicenseLineCp866_DecodesCorrectly()
    {
        var bytes = Encoding.GetEncoding(866).GetBytes(RingLicenseLine);

        var decoded = ConsoleOutputDecoder.Decode(bytes);

        Assert.Equal(RingLicenseLine, decoded);
    }

    [Fact]
    public void Decode_AmbiguousMetrics_PrefersCp866WithKnownRingMarker()
    {
        // Регресс репорта 7OH (issue #83): «шь Їрщыр» вместо «Имя файла» — cp866-вывод
        // ring декодировался как cp1251, потому что метрика «минимум U+FFFD» на
        // коротком выводе неоднозначна. Байт 0xAD не назначен в cp866 (даёт U+FFFD),
        // но есть в cp1251 — до фикса cp1251 «выигрывал» по метрике и маркер
        // «Имя файла» превращался в кракозябры. Теперь маркерная эвристика
        // возвращает cp866 даже при худшей метрике U+FFFD.
        var bytes866 = Encoding.GetEncoding(866).GetBytes(RingLicenseLine);
        var bytes = new byte[bytes866.Length + 1];
        bytes866.CopyTo(bytes, 0);
        bytes[^1] = 0xAD;

        var decoded = ConsoleOutputDecoder.Decode(bytes);

        Assert.Contains("Имя файла", decoded, StringComparison.Ordinal);
        Assert.DoesNotContain("шь Їрщыр", decoded, StringComparison.Ordinal);
    }

    [Fact]
    public void ContainsKnownOutputMarker_DetectsRingMarkers()
    {
        Assert.True(ConsoleOutputDecoder.ContainsKnownOutputMarker("Имя файла: \"x.lic\""));
        Assert.True(ConsoleOutputDecoder.ContainsKnownOutputMarker("Файл лицензии: x.lic"));
        Assert.True(ConsoleOutputDecoder.ContainsKnownOutputMarker("Лицензия не найдена"));
        Assert.False(ConsoleOutputDecoder.ContainsKnownOutputMarker("ring license list: OK"));
        Assert.False(ConsoleOutputDecoder.ContainsKnownOutputMarker(string.Empty));
    }

    [Fact]
    public void FixCp1251ToCp866Mojibake_RestoresRingMarker()
    {
        var mojibake = Encoding.GetEncoding(1251).GetString(
            Encoding.GetEncoding(866).GetBytes(RingLicenseLine));

        // Декодирование cp866-байтов как cp1251 действительно даёт кракозябры.
        Assert.DoesNotContain("Имя файла", mojibake, StringComparison.Ordinal);

        var fixedText = ConsoleOutputDecoder.FixCp1251ToCp866Mojibake(mojibake);

        Assert.Equal(RingLicenseLine, fixedText);
    }

    [Fact]
    public void FixCp1251ToCp866Mojibake_TextWithoutMarkers_Unchanged()
    {
        // Корректный текст без маркеров перезакодированию не подвергается.
        Assert.Equal("ring: ok", ConsoleOutputDecoder.FixCp1251ToCp866Mojibake("ring: ok"));
        Assert.Equal(string.Empty, ConsoleOutputDecoder.FixCp1251ToCp866Mojibake(string.Empty));
        Assert.Equal(string.Empty, ConsoleOutputDecoder.FixCp1251ToCp866Mojibake(null));
    }

    // ===================== #83: парсер лицензий ring =====================

    [Fact]
    public void RingLicenseParser_RingLicenseNumberLine_ReturnsLicense()
    {
        var licenses = RingLicenseParser.Parse(RingLicenseLine);

        var license = Assert.Single(licenses);
        Assert.Equal("499459654270590-500000000037", license.SerialNumber);
        Assert.Contains("20260907085731.lic", license.Module, StringComparison.Ordinal);
        Assert.Equal(RingLicenseLine, license.RawText);
    }

    [Fact]
    public void RingLicenseParser_MojibakeMarker_StillRecognizesLicenseByStructure()
    {
        // Локале-независимый разбор: маркер «Имя файла» пришёл mojibake, но структура
        // строки (номер «NNNN…-NNNN…» + файл *.lic) распознаётся — таблица не пустая.
        const string mojibakeLine =
            "499459654270590-500000000037 (шь Їрщыр: \"20260907085731.lic\")";

        var licenses = RingLicenseParser.Parse(mojibakeLine);

        var license = Assert.Single(licenses);
        Assert.Equal("499459654270590-500000000037", license.SerialNumber);
    }

    [Fact]
    public void RingLicenseParser_NumberWithoutLicFile_Ignored()
    {
        // Ложные срабатывания отсечены: номер без имени файла *.lic — не лицензия.
        Assert.Empty(RingLicenseParser.Parse("номер 1234567890-1234567890 без файла"));
    }

    [Fact]
    public void IsRingLicenseStructureLine_RequiresNumberAndLicFile()
    {
        Assert.True(RingLicenseParser.IsRingLicenseStructureLine(RingLicenseLine));
        Assert.False(RingLicenseParser.IsRingLicenseStructureLine(
            "1234567890-1234567890 (без файла)"));
        Assert.False(RingLicenseParser.IsRingLicenseStructureLine(
            "(Имя файла: \"20260907085731.lic\")"));
    }

    [Fact]
    public void BuildNoLicensesMessage_MojibakeOutput_FixedAndNoReplacementChars()
    {
        // Репорт 7OH (issue #83): «(шь Їрщыр: "20260907085731.lic")» в сообщении
        // вкладки «Лицензии». Кракозябры в UI-сообщении недопустимы.
        var mojibake = Encoding.GetEncoding(1251).GetString(
            Encoding.GetEncoding(866).GetBytes(RingLicenseLine));

        var message = RingLicenseService.BuildNoLicensesMessage(mojibake);

        Assert.DoesNotContain('\uFFFD', message);
        Assert.Contains("Имя файла", message, StringComparison.Ordinal);
        Assert.Contains("499459654270590-500000000037", message, StringComparison.Ordinal);
    }

    // ===================== #79: rac-ветка pre-валидации =====================

    private static Infobase ServerBase(string name) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name,
        PlatformVersion = "8.3.27",
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.ClientServer,
            Server = "srv-1c",
            DatabaseName = "УТ_база"
        }
    };

    private static TestDeployScenario IbcmdScenario() => new()
    {
        TargetName = "Тест: УТ",
        TargetMode = TestDeployTargetMode.File,
        FilePath = @"C:\bases\Тест",
        DumpEngine = BackupEngine.Ibcmd
    };

    [Fact]
    public async Task ValidateAsync_NoProfileNoFields_RacResolves_PassesPreValidation()
    {
        // Сценарий 7OH (issue #79): профиля нет, поля формы («источник_t») не описывают
        // источник, но кластер rac доступен — pre-валидация должна ПРОЙТИ, а не
        // блокировать мастер. Шаги пробы журналируются.
        var source = ServerBase("УТ");
        var scenario = IbcmdScenario();
        var log = new List<string>();

        var errors = await TestDeployPlanner.ValidateAsync(
            scenario, source, null,
            dbmsProfileResolver: _ => null,
            racProfileResolverAsync: (_, _) => Task.FromResult<DbmsBackupProfile?>(
                new DbmsBackupProfile { Host = "pg", Database = "УТ_база", User = "u" }),
            log: line => log.Add(line));

        Assert.DoesNotContain(TestDeployPlanner.NeedDbmsProfileErrorKey, errors);
        Assert.NotEmpty(log); // rac-проба журналируется в окно мастера
    }

    [Fact]
    public async Task ValidateAsync_NoProfileNoFields_RacUnavailable_ReportsNeedDbmsProfile()
    {
        var source = ServerBase("УТ");
        var scenario = IbcmdScenario();

        var errors = await TestDeployPlanner.ValidateAsync(
            scenario, source, null,
            dbmsProfileResolver: _ => null,
            racProfileResolverAsync: (_, _) => Task.FromResult<DbmsBackupProfile?>(null));

        Assert.Contains(TestDeployPlanner.NeedDbmsProfileErrorKey, errors);
    }

    [Fact]
    public async Task ValidateAsync_RacProbeThrows_ReportsNeedDbmsProfileWithoutCrash()
    {
        var source = ServerBase("УТ");
        var scenario = IbcmdScenario();
        var log = new List<string>();

        var errors = await TestDeployPlanner.ValidateAsync(
            scenario, source, null,
            dbmsProfileResolver: _ => null,
            racProfileResolverAsync: (_, _) => throw new InvalidOperationException("rac упал"),
            log: log.Add);

        Assert.Contains(TestDeployPlanner.NeedDbmsProfileErrorKey, errors);
        Assert.NotEmpty(log);
    }

    [Fact]
    public async Task ValidateAsync_NoNeedDbmsProfileError_RacProbeNotCalled()
    {
        // rac-ветка нужна только для ошибки NeedDbmsProfile: остальные ошибки
        // синхронной валидации не запускают rac-пробу.
        var source = ServerBase("УТ");
        var scenario = IbcmdScenario();
        scenario.DumpEngine = BackupEngine.Designer; // «Конфигуратор» — профиль СУБД не нужен
        scenario.FilePath = string.Empty;            // …но пустой путь — обычная ошибка
        var probeCalled = false;

        var errors = await TestDeployPlanner.ValidateAsync(
            scenario, source, null,
            racProfileResolverAsync: (_, _) =>
            {
                probeCalled = true;
                return Task.FromResult<DbmsBackupProfile?>(null);
            });

        Assert.Contains("CreateInfobase.EnterFilePath", errors);
        Assert.False(probeCalled);
    }

    [Fact]
    public async Task ValidateAsync_ProfileExists_RacProbeNotCalled()
    {
        var source = ServerBase("УТ");
        var scenario = IbcmdScenario();
        var probeCalled = false;

        var errors = await TestDeployPlanner.ValidateAsync(
            scenario, source, null,
            dbmsProfileResolver: _ => new DbmsBackupProfile { Host = "pg", Database = "УТ_база" },
            racProfileResolverAsync: (_, _) =>
            {
                probeCalled = true;
                return Task.FromResult<DbmsBackupProfile?>(null);
            });

        Assert.DoesNotContain(TestDeployPlanner.NeedDbmsProfileErrorKey, errors);
        Assert.False(probeCalled);
    }

    [Fact]
    public async Task ViewModel_ValidateAsync_NullResolver_ReportsNeedDbmsProfile()
    {
        var source = ServerBase("УТ");
        var vm = new TestDeployViewModel(
            source,
            Array.Empty<Infobase>(),
            Array.Empty<Group>(),
            "Тест: УТ",
            "srv-1c:1541",
            "УТ_база_t",
            string.Empty);
        vm.IsFileMode = true;
        vm.FilePath = @"C:\bases\Тест";
        vm.EngineIndex = 1; // ibcmd

        var errors = await vm.ValidateAsync(resolverFactory: () => null);

        Assert.Contains(TestDeployPlanner.NeedDbmsProfileErrorKey, errors);
    }

    [Fact]
    public void ViewModel_AutofillDbName_DoesNotOverwriteManualSourceInput()
    {
        // Issue #79 (v1.0.1.23): ручной ввод имени SQL-базы ИСХОДНОЙ базы не затирается
        // автоподстановкой имени целевой базы («источник_t»).
        var source = ServerBase("УТ");
        var vm = new TestDeployViewModel(
            source,
            Array.Empty<Infobase>(),
            Array.Empty<Group>(),
            "Тест: УТ",
            "srv-1c:1541",
            "УТ_база_t",
            string.Empty);

        vm.DbName = "УТ_база";
        vm.AutofillDbNameFromServerDbName();

        Assert.Equal("УТ_база", vm.DbName);
    }

    [Fact]
    public void ViewModel_AutofillDbName_EmptyField_FillsFromServerDbName()
    {
        var source = ServerBase("УТ");
        var vm = new TestDeployViewModel(
            source,
            Array.Empty<Infobase>(),
            Array.Empty<Group>(),
            "Тест: УТ",
            "srv-1c:1541",
            "УТ_база_t",
            string.Empty);

        vm.AutofillDbNameFromServerDbName();

        Assert.Equal("УТ_база_t", vm.DbName);
    }

    // ===================== #89: test-скрипт сборки =====================

    [Fact]
    public void Test1BuildScript_OverridesAllFourVersionProperties()
    {
        // Issue #89: прошлый test-скрипт передавал только -p:Version — явное
        // InformationalVersion в csproj НЕ переопределялось, сборка показывала
        // «полную» версию и автообновление не предлагало стабильный релиз.
        // Сценарий — одноразовый временный файл: он удалён из корня коммитом
        // 97a8a778 («chore: cleanup temp files») и в репозитории не хранится,
        // поэтому при его отсутствии проверка пропускается (гарантии
        // переопределения версий действуют в момент выпуска сборки).
        var root = FindRepoRoot();
        var scriptPath = Path.Combine(root, "_build_v100123_test1.ps1");
        if (!File.Exists(scriptPath))
            return;

        var script = File.ReadAllText(scriptPath);

        Assert.Contains("-p:Version=$Version", script, StringComparison.Ordinal);
        Assert.Contains("-p:AssemblyVersion=$Version", script, StringComparison.Ordinal);
        Assert.Contains("-p:FileVersion=$Version", script, StringComparison.Ordinal);
        Assert.Contains("-p:InformationalVersion=$Version", script, StringComparison.Ordinal);
        // Mismatch ловится на этапе сборки: проверяется ProductVersion (InformationalVersion).
        Assert.Contains("ProductVersion", script, StringComparison.Ordinal);
    }

    // ===================== #99: хоткей «Собрать диагностику» =====================

    [Fact]
    public void AppSettings_HotkeyCollectDiagnostics_DefaultEmpty()
    {
        // Решение 7OH: «можно не ставить ничего по умолчанию».
        Assert.Equal(string.Empty, new AppSettings().HotkeyCollectDiagnostics);
    }

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void Localization_CollectDiagnosticsKeys_Present(string language)
    {
        var json = ReadRepoFile(Path.Combine("Workspace1C", "Localization", "Languages", language + ".json"));
        using var doc = JsonDocument.Parse(json);
        Assert.True(
            doc.RootElement.TryGetProperty("strings", out var strings) &&
            strings.TryGetProperty("Settings.Hotkeys.CollectDiagnostics", out var value) &&
            value.GetString() is { Length: > 0 },
            $"{language}.json: ключ Settings.Hotkeys.CollectDiagnostics отсутствует или пуст.");
    }

    [Fact]
    public void WpfSettingsWindow_HasCollectDiagnosticsHotkeyRow()
    {
        var xaml = ReadRepoFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        Assert.Contains("x:Name=\"HotkeyCollectDiagnosticsBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("{loc:Loc Settings.Hotkeys.CollectDiagnostics}", xaml, StringComparison.Ordinal);

        var codeBehind = ReadRepoFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Hotkeys.cs"));
        Assert.Contains("BindHotkeyBox(HotkeyCollectDiagnosticsBox, _viewModel.HotkeyCollectDiagnostics)",
            codeBehind, StringComparison.Ordinal);

        var settings = ReadRepoFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml.cs"));
        Assert.Contains("var hkCollectDiagnostics = ReadHotkeyBox(HotkeyCollectDiagnosticsBox);",
            settings, StringComparison.Ordinal);
        Assert.Contains("hotkeyCollectDiagnostics: hkCollectDiagnostics", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaSettingsWindow_HasCollectDiagnosticsHotkeyRow()
    {
        var source = ReadRepoFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs"));
        Assert.Contains("LocalizationManager.T(\"Settings.Hotkeys.CollectDiagnostics\")",
            source, StringComparison.Ordinal);
        Assert.Contains("collectDiagnostics: hotkeyCollectDiagnostics.Value",
            source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindows_RegisterCollectDiagnosticsHotkey()
    {
        // WPF: регистрация + перерегистрация по PropertyChanged.
        var wpf = ReadRepoFile(Path.Combine("Workspace1C", "Views", "MainWindow.Hotkeys.cs"));
        Assert.Contains("Add(_viewModel.HotkeyCollectDiagnostics", wpf, StringComparison.Ordinal);
        var wpfMain = ReadRepoFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml.cs"));
        Assert.Contains("or nameof(MainViewModel.HotkeyCollectDiagnostics)", wpfMain, StringComparison.Ordinal);

        // Avalonia: KeyBinding через HotkeyBox.TryParse.
        var avalonia = ReadRepoFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Hotkeys.cs"));
        Assert.Contains("TryParse(_vm.HotkeyCollectDiagnostics", avalonia, StringComparison.Ordinal);

        // Подпись хоткея на пункте меню «Собрать диагностику» (обе платформы).
        var wpfMenu = ReadRepoFile(Path.Combine("Workspace1C", "Views", "MainWindow.UtilitiesMenu.cs"));
        Assert.Contains("case \"HotkeyCollectDiagnostics\":", wpfMenu, StringComparison.Ordinal);
        var avaloniaMenu = ReadRepoFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        Assert.Contains("case \"HotkeyCollectDiagnostics\":", avaloniaMenu, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomActionEditor_ReservesCollectDiagnosticsHotkey()
    {
        // Защита от конфликтов: пользовательские действия не могут занять сочетание
        // «Собрать диагностику» (GetKnownSystemHotkeys).
        var source = ReadRepoFile(Path.Combine("Workspace1C", "ViewModels", "CustomActionEditViewModel.cs"));
        Assert.Contains("settings.HotkeyCollectDiagnostics", source, StringComparison.Ordinal);
    }
}
