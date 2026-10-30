using System;
using System.IO;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты-стражи окна «Серверы 1С», вкладка «Лицензии» (issue #83 «Информация о
/// лицензиях в Серверы 1С»): (1) при недоступной утилите ring/licensetool во вкладке
/// показывается понятное сообщение (а не пустота), а в общий журнал пишется INFO,
/// а не WARN-спам на каждой загрузке; (2) вкладка «Лицензии» идёт ПЕРЕД
/// «Информация о кластере» на обеих платформах (WPF XAML + Avalonia-код).
/// </summary>
public sealed class Issue83LicenseTabTests
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

    [Fact]
    public void RingUnavailableMessage_IsReadable_AndExplainsWhereToFindRing()
    {
        // issue #83: во вкладке «Лицензии» при отсутствии утилиты — понятное сообщение
        // («Не удалось получить список лицензий: не найдена утилита ring …»), а не пустота.
        var message = RingLicenseService.BuildUnavailableMessage();

        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.Contains("ring", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rac", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PATH", message, StringComparison.Ordinal);
        Assert.Contains("Обновить", message, StringComparison.Ordinal);
    }

    [Fact]
    public void RingUnavailableTool_IsLoggedAsInfo_NotWarn()
    {
        // issue #83 (комментарий 7OH): при автообновлении каждые ~5 с rac/ring недоступность
        // утилиты лицензий больше не пишется WARN'ом («ring (PATH) — не удалось получить
        // лицензии: … Не удается найти указанный файл») — уровень INFO/DEBUG + понятное
        // сообщение в RingLicenseResult.Warning для вкладки.
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Services", "RingLicenseService.cs"));

        // issue #80: записи направления RING идут через категорийный гейт
        // Log.Info(LogCategory.Rac, …) — при выключенном CM_ON/CM_RAC в журнал не пишутся.
        Assert.Contains("Log.Info(LogCategory.Rac", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_logger?.Info($\"RING:", source, StringComparison.Ordinal);
        // В цикле кандидатов не осталось Warn-сообщений о неудаче получения лицензий.
        Assert.DoesNotContain("_logger?.Warn($\"RING: {source} — не удалось получить лицензии", source);
        // Понятное сообщение формируется единым методом (используется во вкладке).
        Assert.True(source.Contains("BuildUnavailableMessage()", StringComparison.Ordinal),
            "RingLicenseService: должен использоваться BuildUnavailableMessage() (issue #83).");
        // issue #83 (последний комментарий 7OH от 2026-10-29): утилита НЕ ищется ручным
        // перебором PATH/PATHEXT с подменой полного пути — доступность проверяется запуском
        // «where.exe ring»/«which ring» (запасной способ — «ring --version»), а вызывается
        // «ring» как есть.
        Assert.Contains("FindRingInPathAsync", source, StringComparison.Ordinal);
        Assert.Contains("where.exe", source, StringComparison.Ordinal);
        Assert.Contains("which", source, StringComparison.Ordinal);
        Assert.Contains("--version", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveRingInPath", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RingPathExtensions", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RingCandidate_IsInvokedAsIs_AndSkippedWhenPathProbeFailed()
    {
        // issue #83 (комментарий 7OH «Зачем её искать?»): команда «ring» вызывается как
        // есть — без ручного резолва полного пути по PATH/PATHEXT.
        var candidates = RingLicenseService.CommandCandidates(includePathRing: true);
        Assert.Contains(candidates,
            c => c.Command == "ring"
                 && c.Arguments.Length == 2
                 && c.Arguments[0] == "license"
                 && c.Arguments[1] == "list"
                 && c.Source == "ring (PATH)");

        // Если проверка where/which показала, что ring в PATH нет — кандидат не добавляется
        // (не порождает StartError «Не удается найти указанный файл» при каждом обновлении).
        var skipped = RingLicenseService.CommandCandidates(includePathRing: false);
        Assert.DoesNotContain(skipped, c => c.Source == "ring (PATH)");
        Assert.DoesNotContain(skipped, c => c.Command == "ring");
    }

    [Fact]
    public void ToLaunchCommand_Exe_IsInvokedDirectly()
    {
        // issue #83: .exe-утилита запускается напрямую по полному пути из where.exe.
        var (command, arguments) = RingLicenseService.ToLaunchCommand("C:\\bin\\ring.exe", "license", "list");

        Assert.Equal("C:\\bin\\ring.exe", command);
        Assert.Equal(new[] { "license", "list" }, arguments);
    }

#if !LINUX
    [Fact]
    public void ToLaunchCommand_CmdFile_IsInvokedViaCmdExe()
    {
        // issue #83: командные файлы (.cmd/.bat) прямому CreateProcess недоступны
        // (поиск по PATHEXT не выполняется — «Не удается найти указанный файл»),
        // поэтому запускаются через cmd.exe /c — как в командной строке пользователя.
        var (command, arguments) = RingLicenseService.ToLaunchCommand("C:\\bin\\ring.cmd", "license", "list");

        Assert.Equal("cmd.exe", command);
        Assert.Equal(new[] { "/c", "C:\\bin\\ring.cmd", "license", "list" }, arguments);
    }
#endif

    [Fact]
    public void ToLaunchCommand_NativeLinux_IsInvokedDirectly()
    {
        // Без расширения (нативные бинарники) запускаются напрямую по полному пути.
        var (command, arguments) = RingLicenseService.ToLaunchCommand("/usr/bin/ring", "license", "list");

        Assert.Equal("/usr/bin/ring", command);
        Assert.Equal(new[] { "license", "list" }, arguments);
    }

    [Fact]
    public void RingCandidate_FromWherePath_UsesResolvedCommand()
    {
        // issue #83: найденный where.exe/which полный путь попадает в кандидата
        // «ring (PATH)» как «исполняемый вызов», а не константная команда «ring».
        var candidates = RingLicenseService.CommandCandidates(
            includePathRing: true, ringPath: "C:\\bin\\ring.exe");
        Assert.Contains(candidates,
            c => c.Command == "C:\\bin\\ring.exe"
                 && c.Arguments.Length == 2
                 && c.Arguments[0] == "license"
                 && c.Arguments[1] == "list"
                 && c.Source == "ring (PATH)");
    }

    [Fact]
    public void FirstNonEmptyLine_ReturnsFirstMeaningfulLine()
    {
        // Разбор вывода where/which: первый найденный путь — маркер доступности ring.
        Assert.Null(RingLicenseService.FirstNonEmptyLine(string.Empty));
        Assert.Null(RingLicenseService.FirstNonEmptyLine("  \n\t\n"));
        Assert.Equal("C:\\bin\\ring.exe",
            RingLicenseService.FirstNonEmptyLine("\nC:\\bin\\ring.exe\r\nD:\\other\\ring.exe"));
        Assert.Equal("/usr/bin/ring",
            RingLicenseService.FirstNonEmptyLine("/usr/bin/ring\n/opt/ring"));
    }

    // ===================== Порядок вкладок (issue #83) =====================

    [Fact]
    public void Wpf_LicensesTab_Precedes_ClusterInfoTab()
    {
        // issue #83: «Лицензии» перед «Информация о кластере» — данные по лицензиям
        // явно интереснее свойств кластера.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.xaml"));

        var licensesIndex = xaml.IndexOf("<TabItem Header=\"{Binding LicensesTabText}\">", StringComparison.Ordinal);
        var infoIndex = xaml.IndexOf("<TabItem Header=\"{loc:Loc ServerMonitor.Tabs.Info}\">", StringComparison.Ordinal);
        Assert.True(licensesIndex >= 0, "XAML: вкладка «Лицензии» не найдена.");
        Assert.True(infoIndex >= 0, "XAML: вкладка «Информация о кластере» не найдена.");
        Assert.True(licensesIndex < infoIndex,
            "XAML: вкладка «Лицензии» должна идти ПЕРЕД «Информация о кластере» (issue #83).");
    }

    [Fact]
    public void Avalonia_LicensesTab_Precedes_ClusterInfoTab()
    {
        // issue #83: тот же порядок в Avalonia-версии окна монитора.
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.Avalonia.cs"));

        var licensesAdd = source.IndexOf("tabs.Items.Add(licensesTab);", StringComparison.Ordinal);
        var infoAdd = source.IndexOf("tabs.Items.Add(infoTab);", StringComparison.Ordinal);
        Assert.True(licensesAdd >= 0, "Avalonia: добавление вкладки «Лицензии» не найдено.");
        Assert.True(infoAdd >= 0, "Avalonia: добавление вкладки «Информация о кластере» не найдено.");
        Assert.True(licensesAdd < infoAdd,
            "Avalonia: вкладка «Лицензии» должна идти ПЕРЕД «Информация о кластере» (issue #83).");
    }
}
