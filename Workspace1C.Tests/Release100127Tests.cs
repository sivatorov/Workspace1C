using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты-стражи релиза v1.0.1.27 (issues #102 и #83):
/// - #102: переносимый текст тумблера «Системные уведомления…» и кнопок «О программе»
///   (WPF XAML + Avalonia-код) — Grid с колонкой «*» и TextWrapping="Wrap";
/// - #83: парсер файла *.lic (Наименование по «:» → Версия/Владелец, «Срок действия»,
///   «Количество пользователей», дата выдачи по маркерам), обогащение лицензий новыми
///   полями и FilePath, читабельная выжимка BuildReadableDetails без начального ключа,
///   состав/порядок колонок таблицы «Установленные лицензии» на обеих платформах.
/// </summary>
public sealed class Release100127Tests
{
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

    // ===================== Версия (актуальная — 1.0.1.33, issue #79) =====================

    [Fact]
    public void Csproj_Version_Is_Current_In_AllFourProperties()
    {
        var csproj = ReadProjectFile(Path.Combine("Workspace1C", "Workspace1C.csproj"));

        Assert.Contains("<Version>1.0.1.33</Version>", csproj, StringComparison.Ordinal);
        Assert.Contains("<AssemblyVersion>1.0.1.33</AssemblyVersion>", csproj, StringComparison.Ordinal);
        Assert.Contains("<FileVersion>1.0.1.33</FileVersion>", csproj, StringComparison.Ordinal);
        Assert.Contains("<InformationalVersion>1.0.1.33</InformationalVersion>", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("1.0.1.32", csproj, StringComparison.Ordinal);
    }

    // ===================== Issue #102: переносимый текст =====================

    [Fact]
    public void Wpf_SystemNotificationsToggle_UsesGridWithWrappingText()
    {
        // issue #102: горизонтальный StackPanel отдаёт подписи бесконечную ширину —
        // текст обрезался. Должен быть Grid (Auto + *) и TextWrapping="Wrap".
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));

        var toggleStart = xaml.IndexOf("ShowSystemNotificationsCheck", StringComparison.Ordinal);
        Assert.True(toggleStart > 0, "SettingsWindow.xaml: тумблер ShowSystemNotificationsCheck не найден.");
        var fragment = xaml.Substring(toggleStart, Math.Min(900, xaml.Length - toggleStart));

        Assert.Contains("<Grid>", fragment, StringComparison.Ordinal);
        Assert.Contains("Width=\"*\"", fragment, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", fragment, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_AboutButtons_StretchAndWrapText()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));

        var aboutStart = xaml.IndexOf("AboutButtonsColumn", StringComparison.Ordinal);
        Assert.True(aboutStart > 0, "SettingsWindow.xaml: блок AboutButtonsColumn не найден.");
        var fragment = xaml.Substring(aboutStart, Math.Min(2400, xaml.Length - aboutStart));

        // Обе кнопки «О программе»: подпись переносится, кнопка растягивается по колонке.
        Assert.Equal(2, CountOccurrences(fragment, "TextWrapping=\"Wrap\""));
        Assert.Equal(2, CountOccurrences(fragment, "HorizontalAlignment=\"Stretch\""));
        Assert.Equal(2, CountOccurrences(fragment, "Width=\"*\""));
    }

    [Fact]
    public void Avalonia_SettingsSwitch_BuildsGridInsteadOfHorizontalStackPanel()
    {
        // issue #102: в SettingsSwitch подпись кладётся в колонку «*» Grid —
        // горизонтальный StackPanel больше не используется для значка и подписи.
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs"));

        var methodStart = source.IndexOf("private static ToggleButton SettingsSwitch", StringComparison.Ordinal);
        Assert.True(methodStart > 0, "SettingsWindow.Avalonia.cs: SettingsSwitch не найден.");
        var methodEnd = source.IndexOf("private static ToggleButton DisplayCheck", StringComparison.Ordinal);
        var fragment = source.Substring(methodStart, methodEnd - methodStart);

        Assert.Contains("ColumnDefinitions(\"Auto,*,Auto\")", fragment, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(caption, captionColumn)", fragment, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_AboutButtons_UseGridWithWrapAndStretch()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs"));

        var copyStart = source.IndexOf("Settings.About.CopyTechInfo", StringComparison.Ordinal);
        Assert.True(copyStart > 0, "SettingsWindow.Avalonia.cs: кнопка копирования не найдена.");
        var diagStart = source.IndexOf("Settings.About.CreateDiagnostics", StringComparison.Ordinal);
        var fragment = source.Substring(copyStart, Math.Min(3200, source.Length - copyStart));

        Assert.Contains("TextWrapping = TextWrapping.Wrap", fragment, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinitions(\"Auto,*\")", fragment, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment.Stretch", fragment, StringComparison.Ordinal);
        Assert.True(diagStart > copyStart, "Кнопка «Собрать диагностику» должна идти после «Копировать».");
    }

    // ===================== Issue #83: парсер *.lic =====================

    /// <summary>Синтетический образец файла *.lic по формату 1С (полный набор полей).</summary>
    private const string FullLicContent = """
        5DF4B9B9EFB33FE4397AB6C21D4E9F2C1A2B3C4D5E6F708192A3B4C5D6E7F809

        Рег. номер: 499459654270590-500000000037
        Наименование: 8.3.25.1257:ООО «Ромашка»
        Тип лицензии: Клиентская
        Серия: soft
        Выдана: 07.09.2026
        Срок действия: 07.09.2027
        Количество пользователей: 5
        Компьютер: WORK01
        Пользователь: Иванов И.И.
        """;

    [Fact]
    public void ParseLicFile_NameSplitsToVersionAndOwner()
    {
        // issue #83, п.2-3: «Наименование» разбивается по первому «:»:
        // левая часть → Версия, правая → Владелец.
        var license = RingLicenseParser.ParseLicFile(FullLicContent, "20260907085731.lic");

        Assert.Equal("8.3.25.1257", license.Version);
        Assert.Equal("ООО «Ромашка»", license.Owner);
    }

    [Fact]
    public void ParseLicFile_ExpiryAndCount_AreParsed()
    {
        // issue #83, п.4-5: «Срок действия» → ExpiryDate, «Количество пользователей» → LicenseCount.
        var license = RingLicenseParser.ParseLicFile(FullLicContent, "20260907085731.lic");

        Assert.NotNull(license.ExpiryDate);
        Assert.Equal(new DateTime(2027, 9, 7), license.ExpiryDate);
        Assert.Equal(5, license.LicenseCount);
        // «Срок действия» не должен подменять дату выдачи (раньше бралась первая дата файла).
        Assert.NotNull(license.IssueDate);
        Assert.Equal(new DateTime(2026, 9, 7), license.IssueDate);
    }

    [Fact]
    public void ParseLicFile_MissingFields_AreEmpty_WithoutExceptions()
    {
        // issue #83: если поля в *.lic отсутствуют — колонки пустые, никаких исключений.
        var license = RingLicenseParser.ParseLicFile("Рег. номер: 123-456\n", "minimal.lic");

        Assert.Equal("123-456", license.RegNumber);
        Assert.Equal(string.Empty, license.Version);
        Assert.Equal(string.Empty, license.Owner);
        Assert.Null(license.ExpiryDate);
        Assert.Equal(0, license.LicenseCount);
    }

    [Fact]
    public void ParseLicFile_EmptyOrGarbage_DoesNotThrow()
    {
        Assert.NotNull(RingLicenseParser.ParseLicFile(string.Empty));
        Assert.NotNull(RingLicenseParser.ParseLicFile("   \n\n  "));
        // Двоеточие внутри «чужого» значения не ломает разбор «Наименования».
        var license = RingLicenseParser.ParseLicFile("Наименование: 8.3.20.1:Иванов Иван: отдел", "x.lic");
        Assert.Equal("8.3.20.1", license.Version);
        Assert.Equal("Иванов Иван: отдел", license.Owner);
    }

    [Fact]
    public void ParseLicFile_ExplicitOwner_IsNotOverriddenByName()
    {
        // Явный владелец из ключа «владел/организац» приоритетнее правой части наименования.
        var content = "Владелец: ООО «Лютик»\nНаименование: 8.3.25.1:ООО «Ромашка»\n";
        var license = RingLicenseParser.ParseLicFile(content, "x.lic");

        Assert.Equal("ООО «Лютик»", license.Owner);
        Assert.Equal("8.3.25.1", license.Version);
    }

    // ===================== Issue #83: читабельная выжимка =====================

    [Fact]
    public void BuildReadableDetails_ContainsReadableInfo_WithoutLeadingKey()
    {
        var license = RingLicenseParser.ParseLicFile(FullLicContent, "20260907085731.lic");
        license.FilePath = @"C:\1C\licenses\20260907085731.lic";

        var details = RingLicenseParser.BuildReadableDetails(license);

        // Issue #83 (v1.0.1.30, 7OH: «строки до "Файл лицензии" лишние»): текст деталей
        // НАЧИНАЕТСЯ со строки «Файл лицензии: <путь>» — распознанные поля перед ней
        // не добавляются (они уже есть в колонках таблицы; похожие строки внутри
        // оригинального содержимого файла остаются — это формат файла).
        Assert.StartsWith("Файл лицензии: C:\\1C\\licenses\\20260907085731.lic", details, StringComparison.Ordinal);
        // Начальный длинный ключ (20+ символов подряд) вырезан — пожелание п.8 issue #83.
        Assert.DoesNotContain("5DF4B9B9EFB33FE4397AB6C21D4E9F2C", details, StringComparison.Ordinal);
        // Оригинальное содержимое файла присутствует.
        Assert.Contains("Рег. номер: 499459654270590-500000000037", details, StringComparison.Ordinal);
        Assert.Contains("Серия: soft", details, StringComparison.Ordinal);
    }

    [Fact]
    public void StripLeadingKey_RemovesLongLeadingToken_KeepsRest()
    {
        var text = "5DF4B9B9EFB33FE4397AB6C21D4E9F2C1A2B  •  Рег. номер: 123";
        var stripped = RingLicenseParser.StripLeadingKey(text);

        Assert.StartsWith("Рег. номер", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildReadableDetails_NullAndEmpty_AreSafe()
    {
        Assert.Equal(string.Empty, RingLicenseParser.BuildReadableDetails(null));
        // Issue #83 (v1.0.1.28): пустая лицензия (файл не прочитан) даёт явный текст,
        // а не пустую строку — в окне деталей видно, что читать было нечего.
        var empty = new ProgramLicense();
        var details = RingLicenseParser.BuildReadableDetails(empty);
        Assert.Contains("Файл не прочитан или пуст", details);
    }

    // ===================== Issue #83: обогащение и FilePath =====================

    [Fact]
    public void EnrichWithLicFiles_CopiesNewFields_AndFilePath()
    {
        var fileLicense = RingLicenseParser.ParseLicFile(FullLicContent, "20260907085731.lic");
        // Module и FilePath в реальном потоке заполняет TryReadLicFile.
        fileLicense.Module = "файл лицензии: 20260907085731.lic";
        fileLicense.FilePath = @"C:\1C\licenses\20260907085731.lic";

        var ringLicense = new ProgramLicense
        {
            SerialNumber = "AABBCCDD-1122-3344-5566-77889900AABB",
            Module = "файл лицензии: 20260907085731.lic"
        };
        // RingLicenseService запечатан: подменяем внутренний провайдер файлов напрямую.
        var service = new RingLicenseService();
        service.LicFilesProvider = () => new[] { fileLicense };
        service.EnrichWithLicFiles(new[] { ringLicense });

        Assert.Equal("8.3.25.1257", ringLicense.Version);
        Assert.Equal("ООО «Ромашка»", ringLicense.Owner);
        // Приоритет типа: серия («soft») → тип («Клиентская»).
        Assert.Equal("soft", ringLicense.Type);
        Assert.Equal(new DateTime(2027, 9, 7), ringLicense.ExpiryDate);
        Assert.Equal(5, ringLicense.LicenseCount);
        Assert.Equal(@"C:\1C\licenses\20260907085731.lic", ringLicense.FilePath);
    }

    [Fact]
    public void TryReadLicFile_FillsFilePath_EvenForUnreadableFile()
    {
        var root = FindRepoRoot();
        var missing = Path.Combine(root, "Workspace1C.Tests", "_no_such_lic_dir_", "x.lic");

        var license = RingLicenseService.TryReadLicFile(missing, "x.lic");

        Assert.NotNull(license);
        Assert.Equal(missing, license!.FilePath);
        Assert.Equal("файл лицензии: x.lic", license.Module);
    }

    // ===================== Issue #83: хелпер «открыть папку» =====================

    [Fact]
    public void LicenseFolderOpener_ReturnsFalse_ForMissingFile()
    {
        Assert.Null(LicenseFolderOpener.GetExistingFilePath(null));
        Assert.Null(LicenseFolderOpener.GetExistingFilePath(new ProgramLicense()));
        Assert.False(LicenseFolderOpener.TryOpenFolderWithFile(null));
        Assert.False(LicenseFolderOpener.TryOpenFolderWithFile(
            Path.Combine(FindRepoRoot(), "_no_such_lic_dir_", "x.lic")));
    }

    [Fact]
    public void LicenseFolderOpener_StartInfo_UsesExplorerSelect_OnWindows()
    {
#if WINDOWS
        System.Diagnostics.ProcessStartInfo? captured = null;
        LicenseFolderOpener.ProcessStarter = psi =>
        {
            captured = psi;
            return true;
        };
        try
        {
            var existing = Path.Combine(FindRepoRoot(), "CHANGELOG.md");
            var opened = LicenseFolderOpener.TryOpenFolderWithFile(existing);

            Assert.True(opened);
            Assert.NotNull(captured);
            Assert.Equal("explorer.exe", captured!.FileName);
            Assert.Contains("/select", captured.Arguments, StringComparison.Ordinal);
            Assert.Contains("CHANGELOG.md", captured.Arguments, StringComparison.Ordinal);
        }
        finally
        {
            LicenseFolderOpener.ResetProcessStarter();
        }
#endif
    }

    // ===================== Issue #83: состав колонок (WPF) =====================

    [Fact]
    public void Wpf_InstalledLicensesGrid_ColumnsAreSerialModuleTypeVersionOwnerExpiryUsers()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.xaml"));

        var gridStart = xaml.IndexOf("InstalledLicensesGrid", StringComparison.Ordinal);
        Assert.True(gridStart > 0, "ServerMonitorWindow.xaml: InstalledLicensesGrid не найден.");
        var gridEnd = xaml.IndexOf("</DataGrid>", gridStart, StringComparison.Ordinal);
        var fragment = xaml.Substring(gridStart, gridEnd - gridStart);

        // Порядок заголовков: Serial → Module → Type → Version → Owner → Expiry → Users.
        var expectedOrder = new[]
        {
            "ServerMonitor.Licenses.Columns.Serial",
            "ServerMonitor.Licenses.Columns.Module",
            "ServerMonitor.Licenses.Columns.Type",
            "ServerMonitor.Licenses.Columns.Version",
            "ServerMonitor.Licenses.Columns.Owner",
            "ServerMonitor.Licenses.Columns.Expiry",
            "ServerMonitor.Licenses.Columns.Users"
        };
        var positions = expectedOrder
            .Select(h => fragment.IndexOf($"Header=\"{{loc:Loc {h}}}\"", StringComparison.Ordinal))
            .ToList();
        Assert.All(positions, p => Assert.True(p > 0, $"Колонка {expectedOrder[positions.IndexOf(p)]} не найдена."));
        Assert.Equal(positions.OrderBy(p => p), positions);

        // «Пользователь» и «Состояние» убраны из таблицы установленных лицензий.
        Assert.DoesNotContain("Binding=\"{Binding User}\"", fragment, StringComparison.Ordinal);
        Assert.DoesNotContain("Binding=\"{Binding State}\"", fragment, StringComparison.Ordinal);

        // «Серийный номер» и «Модуль» кликабельны (обработчики кликов в code-behind).
        Assert.Contains("OnLicenseSerial_Click", fragment, StringComparison.Ordinal);
        Assert.Contains("OnLicenseModule_Click", fragment, StringComparison.Ordinal);
    }

    // ===================== Issue #83: состав колонок (Avalonia) =====================

    [Fact]
    public void Avalonia_InstalledLicenses_PropertyPaths_MatchWpfOrder()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.Avalonia.cs"));

        var pathsStart = source.IndexOf("new[] { \"SerialNumber\", \"Module\"", StringComparison.Ordinal);
        Assert.True(pathsStart > 0, "Avalonia: propertyPaths таблицы лицензий не найдены.");
        var pathsEnd = source.IndexOf("}", pathsStart, StringComparison.Ordinal);
        var fragment = source.Substring(pathsStart, pathsEnd - pathsStart);

        Assert.Contains("\"SerialNumber\"", fragment, StringComparison.Ordinal);
        Assert.Contains("\"Module\"", fragment, StringComparison.Ordinal);
        Assert.Contains("\"Type\"", fragment, StringComparison.Ordinal);
        Assert.Contains("\"Version\"", fragment, StringComparison.Ordinal);
        Assert.Contains("\"Owner\"", fragment, StringComparison.Ordinal);
        Assert.Contains("\"ExpiryDate\"", fragment, StringComparison.Ordinal);
        Assert.Contains("\"LicenseCount\"", fragment, StringComparison.Ordinal);
        // «Пользователь» и «Состояние» убраны.
        Assert.DoesNotContain("\"User\"", fragment, StringComparison.Ordinal);
        Assert.DoesNotContain("\"State\"", fragment, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_LicenseCells_AreClickable_WithHandCursor()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.Avalonia.cs"));

        Assert.Contains("StandardCursorType.Hand", source, StringComparison.Ordinal);
        Assert.Contains("ShowLicenseDetails(license)", source, StringComparison.Ordinal);
        Assert.Contains("LicenseFolderOpener.TryOpenFolderWithFile(license.FilePath)", source, StringComparison.Ordinal);
        // Клик по «Модулю» без файла — тихое сообщение в статус вкладки, не диалог.
        Assert.Contains("ShowTransientLicenseStatus", source, StringComparison.Ordinal);
    }

    // ===================== Issue #83: окно деталей и локализация =====================

    [Fact]
    public void LicenseDetailsWindow_Exists_ForBothPlatforms()
    {
        var wpf = ReadProjectFile(Path.Combine("Workspace1C", "Views", "LicenseDetailsWindow.xaml"));
        Assert.Contains("x:Class=\"Workspace1C.LicenseDetailsWindow\"", wpf, StringComparison.Ordinal);
        Assert.Contains("OnOpenFolder_Click", wpf, StringComparison.Ordinal);

        var wpfCode = ReadProjectFile(Path.Combine("Workspace1C", "Views", "LicenseDetailsWindow.xaml.cs"));
        Assert.Contains("BuildReadableDetails", wpfCode, StringComparison.Ordinal);

        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "LicenseDetailsWindow.Avalonia.cs"));
        Assert.Contains("ModalWindowBase", avalonia, StringComparison.Ordinal);
        Assert.Contains("BuildReadableDetails", avalonia, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_KeysExist_InRuAndEn()
    {
        var ru = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        var en = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));

        var keys = new[]
        {
            "ServerMonitor.Licenses.Columns.Users",
            "ServerMonitor.Licenses.DetailsTitle",
            "ServerMonitor.Licenses.OpenFolder",
            "ServerMonitor.Licenses.FileNotFound"
        };
        foreach (var key in keys)
        {
            Assert.Contains($"\"{key}\"", ru, StringComparison.Ordinal);
            Assert.Contains($"\"{key}\"", en, StringComparison.Ordinal);
        }
    }

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }
        return count;
    }
}
