using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Релиз 1.0.1.24 (issues #89, #92, #98, #101):
/// <list type="bullet">
/// <item>#89 — перезапуск после обновления на Linux через helper-сценарий
/// (<see cref="UpdateRestartScriptBuilder"/>): ожидание выхода, бэкап, чистое
/// окружение, перехват вывода, liveness-проверка и автооткат;</item>
/// <item>#92 — поиск-фильтр списка баз в окне массовых операций
/// (<see cref="BatchOperationsViewModel"/>), переименования пунктов меню и
/// пункт «Загрузить из CF…» в подменю «Для выделенных»;</item>
/// <item>#98 — порядок кнопок в окне удаления старых версий платформы
/// («Удалить» слева, «Отмена» справа);</item>
/// <item>#101 — подложка-бейдж под значками режимов запуска в строке списка баз.</item>
/// </list>
/// </summary>
public sealed class Release100124Tests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    // ===================== Issue #89: helper-сценарий перезапуска =====================

    [Fact]
    public void RestartScript_RestartMode_ContainsBackupCleanEnvOutputAndRollback()
    {
        var script = UpdateRestartScriptBuilder.Build(
            "/opt/ws/Workspace1C", "/tmp/ws.new", currentPid: 4242, restart: true,
            logPath: "/tmp/helper.log", restartLogPath: "/tmp/restart.log");

        // Ожидание выхода старого процесса ДО замены — корень регресса v1.0.1.23.
        Assert.Contains("kill -0 \"$PID_TARGET\"", script);

        // Бэкап прежнего бинаря до замены и путь бэкапа по конвенции.
        Assert.Contains("cm-update-backup", script);
        Assert.Equal("/opt/ws/Workspace1C.cm-update-backup",
            UpdateRestartScriptBuilder.GetBackupPath("/opt/ws/Workspace1C"));

        // Чистое окружение: DOTNET_*/COREHOST_*/LD_* снимаются перед запуском.
        Assert.Contains("DOTNET_", script);
        Assert.Contains("COREHOST_", script);
        Assert.Contains("LD_LIBRARY_PATH", script);
        Assert.Contains("LD_PRELOAD", script);
        Assert.Contains("unset \"$VAR\"", script);

        // Маркер успешного апдейта и перехват вывода нового процесса в журнал.
        Assert.Contains("CM_UPDATE_CHILD=1", script);
        Assert.Contains(">>\"$RESTART_LOG\" 2>&1", script);

        // Liveness-проверка 10 с и ветка автоотката.
        Assert.Contains("10", script);
        Assert.Contains("ROLLBACK", script);
        Assert.Contains("mv -f \"$BACKUP\" \"$TARGET\"", script);
    }

    [Fact]
    public void RestartScript_AfterCloseMode_DoesNotRestartAndDoesNotBackup()
    {
        var script = UpdateRestartScriptBuilder.Build(
            "/opt/ws/Workspace1C", "/tmp/ws.new", currentPid: 1, restart: false,
            logPath: "/tmp/helper.log", restartLogPath: "/tmp/restart.log");

        Assert.Contains("Перезапуск не требуется", script);
        // Бэкап и откат имеют смысл только для режима «Перезапустить сейчас».
        Assert.DoesNotContain("ROLLBACK", script);
        Assert.DoesNotContain("CM_UPDATE_CHILD=1", script);
    }

    [Fact]
    public void RestartScript_EscapesSingleQuotesInPaths()
    {
        var script = UpdateRestartScriptBuilder.Build(
            "/opt/it's/Workspace1C", "/tmp/ws.new", currentPid: 7, restart: true,
            logPath: "/tmp/h.log", restartLogPath: "/tmp/r.log");

        // Экранирование одинарных кавычек в пути цели: ' → '\''.
        Assert.Contains("/opt/it'\\''s/Workspace1C", script);
    }

    [Fact]
    public void UpdateService_AppliesRestartNow_UsesHelperFirst()
    {
        // Регресс v1.0.1.23: прямой запуск нового процесса из живого старого —
        // больше не основной путь. Проверяем исходник: в ApplyRestartNow запуск
        // помощника идёт ДО TryInstallDirect/StartDetached.
        var source = ReadProjectFile("Workspace1C/Services/UpdateService.Avalonia.cs");
        var methodIndex = source.IndexOf("internal bool ApplyRestartNow", StringComparison.Ordinal);
        Assert.True(methodIndex >= 0, "ApplyRestartNow не найден");

        var nextMethodIndex = source.IndexOf("internal bool TryInstallDirect", StringComparison.Ordinal);
        var body = source[methodIndex..(nextMethodIndex > 0 ? nextMethodIndex : source.Length)];

        var helperLaunch = body.IndexOf("CreateUpdaterScript(target, newBinary, Environment.ProcessId, restart: true)", StringComparison.Ordinal);
        var directInstall = body.IndexOf("TryInstallDirect(target, newBinary)", StringComparison.Ordinal);
        Assert.True(helperLaunch >= 0, "вызов помощника не найден в ApplyRestartNow");
        Assert.True(directInstall >= 0, "запасной путь TryInstallDirect потерян");
        Assert.True(helperLaunch < directInstall, "помощник должен запускаться раньше прямой замены");
    }

    [Fact]
    public void ReleaseWorkflow_ContainsLinuxSmokeTest()
    {
        var yml = ReadProjectFile(".github/workflows/release.yml");
        Assert.Contains("Смок-тест запуска Linux-бинаря", yml);
        Assert.Contains("xvfb-run", yml);
    }

    // ===================== Issue #92: поиск-фильтр и меню =====================

    [Fact]
    public void MatchesFilter_EmptyQuery_MatchesEverything()
    {
        Assert.True(BatchOperationsViewModel.MatchesFilter("Любая база", ""));
        Assert.True(BatchOperationsViewModel.MatchesFilter("Любая база", "   "));
        Assert.True(BatchOperationsViewModel.MatchesFilter("Любая база", null));
    }

    [Fact]
    public void MatchesFilter_CaseInsensitiveSubstring()
    {
        Assert.True(BatchOperationsViewModel.MatchesFilter("Demo 27 File", "demo"));
        Assert.True(BatchOperationsViewModel.MatchesFilter("Demo 27 File", "27"));
        Assert.True(BatchOperationsViewModel.MatchesFilter("Бухгалтерия", "галтер"));
        // Кириллица в разном регистре.
        Assert.True(BatchOperationsViewModel.MatchesFilter("Бухгалтерия", "БУХ"));
        Assert.False(BatchOperationsViewModel.MatchesFilter("Demo 27 File", "demo3"));
        Assert.False(BatchOperationsViewModel.MatchesFilter(null, "demo"));
    }

    [Fact]
    public void BatchOperationsViewModel_SearchText_FiltersVisibleList_KeepsSelection()
    {
        var vm = new BatchOperationsViewModel(
            new[] { new Infobase { Name = "Demo 27" }, new Infobase { Name = "Бухгалтерия" } },
            BatchOperationKind.DumpDt);

        Assert.Equal(2, vm.FilteredItems.Count);
        Assert.Equal(2, vm.Items.Count);

        vm.Items[0].IsChecked = true; // выбор до фильтрации

        vm.SearchText = "бух";
        Assert.Single(vm.FilteredItems);
        Assert.Equal("Бухгалтерия", vm.FilteredItems[0].Title);
        Assert.Equal(2, vm.Items.Count);              // полный список не тронут
        Assert.True(vm.Items[0].IsChecked);           // выбор не сбрасывается

        vm.SearchText = "";
        Assert.Equal(2, vm.FilteredItems.Count);
    }

    [Fact]
    public void BatchOperationsViewModel_SetAll_AppliesToVisibleRowsOnly()
    {
        var vm = new BatchOperationsViewModel(
            new[] { new Infobase { Name = "Alpha" }, new Infobase { Name = "Beta" } },
            BatchOperationKind.DumpDt);

        // По умолчанию все базы отмечены (IsChecked = !IsRunning): сначала снимаем.
        vm.SetAll(false);
        Assert.All(vm.Items, i => Assert.False(i.IsChecked));

        vm.SearchText = "alp";
        vm.SetAll(true);
        Assert.True(vm.Items[0].IsChecked);
        Assert.False(vm.Items[1].IsChecked);          // «Beta» скрыта фильтром

        vm.SearchText = "";
        vm.SetAll(false);
        Assert.All(vm.Items, i => Assert.False(i.IsChecked));
    }

    [Fact]
    public void Localization_BatchOpsTitles_RenamedPer7OH()
    {
        // Словари имеют корень { code, name, strings: { ... } } — ключи в «strings».
        var ru = JsonDocument.Parse(ReadProjectFile("Workspace1C/Localization/Languages/ru.json")).RootElement.GetProperty("strings");
        var en = JsonDocument.Parse(ReadProjectFile("Workspace1C/Localization/Languages/en.json")).RootElement.GetProperty("strings");

        Assert.Equal("Массовая выгрузка в DT/CF", ru.GetProperty("BatchOps.DumpDtTitle").GetString());
        Assert.Equal("Массовая загрузка из CF", ru.GetProperty("BatchOps.LoadCfTitle").GetString());
        Assert.Equal("Загрузить из CF…", ru.GetProperty("Main.BatchLoadCf").GetString());
        Assert.Equal("Bulk dump to DT/CF", en.GetProperty("BatchOps.DumpDtTitle").GetString());
        Assert.Equal("Bulk load from CF", en.GetProperty("BatchOps.LoadCfTitle").GetString());
        Assert.Equal("Load from CF…", en.GetProperty("Main.BatchLoadCf").GetString());
    }

    [Fact]
    public void MainWindowBatchMenus_ContainLoadCfItem()
    {
        // WPF: пункт подменю «Для выделенных» с обработчиком LoadConfig.
        var wpfMenu = ReadProjectFile("Workspace1C/Views/MainWindow.ContextMenu.cs");
        Assert.Contains("\"Main.BatchLoadCf\"", wpfMenu);
        var wpfEvents = ReadProjectFile("Workspace1C/Views/MainWindow.Events.cs");
        Assert.Contains("ExecuteBatchLoadCfForSelected", wpfEvents);

        // Avalonia: тот же пункт и тот же вызов VM.
        var avMenu = ReadProjectFile("Workspace1C/Views/MainWindow.Avalonia.Tree.cs");
        Assert.Contains("\"Main.BatchLoadCf\"", avMenu);
        Assert.Contains("ExecuteBatchLoadCfForSelected", avMenu);
    }

    // ===================== Issue #98: порядок кнопок =====================

    [Fact]
    public void OldVersionsWindow_DeleteButtonComesBeforeCancel()
    {
        // Регресс из скриншота ArtyoZ: «Отмена» стояла первой. Конвенция
        // (issue #98): главная кнопка слева, «Отмена» — последней.
        var wpf = ReadProjectFile("Workspace1C/Views/PlatformOldVersionsWindow.xaml");
        var deleteIndex = wpf.IndexOf("x:Name=\"DeleteButton\"", StringComparison.Ordinal);
        var cancelIndex = wpf.IndexOf("{loc:Loc Common.Cancel}", StringComparison.Ordinal);
        Assert.True(deleteIndex >= 0 && cancelIndex >= 0);
        Assert.True(deleteIndex < cancelIndex, "«Удалить» должна идти раньше «Отмены» в WPF-окне");

        var avalonia = ReadProjectFile("Workspace1C/Views/PlatformOldVersionsWindow.Avalonia.cs");
        var avDelete = avalonia.IndexOf("buttons.Children.Add(_deleteButton)", StringComparison.Ordinal);
        var avCancel = avalonia.IndexOf("buttons.Children.Add(cancel)", StringComparison.Ordinal);
        Assert.True(avDelete >= 0 && avCancel >= 0);
        Assert.True(avDelete < avCancel, "«Удалить» должна идти раньше «Отмены» в Avalonia-окне");
    }

    // ===================== Issue #101: подложка-бейдж значков =====================

    [Fact]
    public void RowRunningIcons_HaveBadgeBackdrop_BothPlatforms()
    {
        // Avalonia: значки режимов обёрнуты в Border-чип (CardBackgroundBrush).
        var avalonia = ReadProjectFile("Workspace1C/Views/MainWindow.Avalonia.Tree.cs");
        Assert.Contains("enterpriseChip", avalonia);
        Assert.Contains("configuratorChip", avalonia);
        Assert.Contains("\"CardBackgroundBrush\"", avalonia);

        // WPF: та же подложка в шаблоне строки.
        var wpf = ReadProjectFile("Workspace1C/Views/MainWindow.xaml");
        var playIndex = wpf.IndexOf("Kind=\"Play\"", StringComparison.Ordinal);
        Assert.True(playIndex > 0);
        var backdropBeforePlay = wpf.LastIndexOf("CardBackgroundBrush", playIndex, StringComparison.Ordinal);
        Assert.True(backdropBeforePlay > 0, "значок Play без подложки-бейджа");
    }

    // ===================== Версия релиза =====================

    [Fact]
    public void Csproj_VersionBumpedTo100124_AllFourProperties()
    {
        var csproj = ReadProjectFile("Workspace1C/Workspace1C.csproj");
        Assert.Contains("<Version>1.0.1.24</Version>", csproj);
        Assert.Contains("<AssemblyVersion>1.0.1.24</AssemblyVersion>", csproj);
        Assert.Contains("<FileVersion>1.0.1.24</FileVersion>", csproj);
        Assert.Contains("<InformationalVersion>1.0.1.24</InformationalVersion>", csproj);
        Assert.DoesNotContain("1.0.1.23</Version>", csproj);
    }

    [Fact]
    public void Changelog_HasSection100124()
    {
        var changelog = ReadProjectFile("CHANGELOG.md");
        Assert.Contains("## [1.0.1.24]", changelog);
        var section = changelog["## [1.0.1.24]".Length..];
        Assert.Contains("#89", section[..section.IndexOf("## [1.0.1.23]", StringComparison.Ordinal)]);
    }
}
