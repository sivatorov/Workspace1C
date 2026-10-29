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
        // issue #83 (комментарий 7OH «Давайте просто вызывать ring, а не искать её рядом»):
        // ring резолвится по PATH с учётом PATHEXT и не подменяется первым кандидатом
        // рядом с rac.
        Assert.Contains("ResolveRingInPath", source, StringComparison.Ordinal);
        Assert.Contains("RingPathExtensions", source, StringComparison.Ordinal);
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
