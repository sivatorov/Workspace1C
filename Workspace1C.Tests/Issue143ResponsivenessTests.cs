using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #143 (комментарий RafaIS 2026-11-23): окно «Резервные копии СУБД» всё ещё
/// «наглухо» зависало на «Проверить подключение», «Проверить каталог», «Сохранить»
/// и «Сделать копию сейчас». Причины: синхронная запись профиля на UI-потоке,
/// синхронная часть резолвера кластера, отсутствие предела у проверок и бесконечное
/// ожидание вывода процесса, чей потомок держит унаследованную трубу.
/// </summary>
public sealed class Issue143ResponsivenessTests
{
    [Fact]
    public void Save_RunsOffUiThread_WithTimeout()
    {
        var code = ReadProjectFile("Workspace1C", "ViewModels", "DbmsBackupViewModel.cs");

        Assert.Contains("Task.Run(Save).WaitAsync(SaveTimeout)", code, StringComparison.Ordinal);
        Assert.Contains("public async Task<bool> SaveAsync()", code, StringComparison.Ordinal);
        Assert.Contains("WaitAsync(SaveTimeout, _cts.Token)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Checks_AreBoundedByTimeout()
    {
        var code = ReadProjectFile("Workspace1C", "ViewModels", "DbmsBackupViewModel.cs");

        Assert.Contains("WaitAsync(CheckTimeout)", code, StringComparison.Ordinal);
        Assert.Contains("catch (TimeoutException)", code, StringComparison.Ordinal);
        Assert.Contains("Dbms.CheckTimeout", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ClusterResolver_StartsOnBackgroundThread()
    {
        var wpf = ReadProjectFile("Workspace1C", "Views", "DbmsBackupWindow.cs");
        var avalonia = ReadProjectFile("Workspace1C", "Views", "DbmsBackupWindow.Avalonia.cs");

        Assert.Contains("Task.Run(() => _vm.ResolveDbmsFromClusterAsync())", wpf, StringComparison.Ordinal);
        Assert.Contains("Task.Run(() => _vm.ResolveDbmsFromClusterAsync())", avalonia, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgressPanel_IsHiddenWhenIdle()
    {
        var wpf = ReadProjectFile("Workspace1C", "Views", "DbmsBackupWindow.cs");
        var avalonia = ReadProjectFile("Workspace1C", "Views", "DbmsBackupWindow.Avalonia.cs");

        Assert.Contains("BooleanToVisibilityConverter", wpf, StringComparison.Ordinal);
        Assert.Contains("nameof(DbmsBackupViewModel.IsBusy)", wpf, StringComparison.Ordinal);
        Assert.Contains("Visual.IsVisibleProperty", avalonia, StringComparison.Ordinal);
        Assert.Contains("nameof(DbmsBackupViewModel.IsBusy)", avalonia, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessRunner_BoundsOutputDrainAfterExit()
    {
        var code = ReadProjectFile("Workspace1C", "Services", "ProcessRunner.cs");

        Assert.Contains("OutputDrainTimeout", code, StringComparison.Ordinal);
        Assert.Contains("Task.WhenAny(readTask, Task.Delay(OutputDrainTimeout))", code, StringComparison.Ordinal);
        Assert.Contains("readCts?.Cancel()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_SaveButtonUsesAsyncHandler()
    {
        var wpf = ReadProjectFile("Workspace1C", "Views", "DbmsBackupWindow.cs");
        var avalonia = ReadProjectFile("Workspace1C", "Views", "DbmsBackupWindow.Avalonia.cs");

        Assert.Contains("await _vm.SaveAsync()", wpf, StringComparison.Ordinal);
        Assert.Contains("await _vm.SaveAsync()", avalonia, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ru.json")]
    [InlineData("en.json")]
    public void Localization_HasSaveTimeoutKeys(string file)
    {
        var json = ReadProjectFile("Workspace1C", "Localization", "Languages", file);

        Assert.Contains("\"Dbms.Saving\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Dbms.SaveTimeout\"", json, StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var relativePath = Path.Combine(parts);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var fullPath = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(fullPath))
                return File.ReadAllText(fullPath);
            dir = dir.Parent;
        }

        Assert.Fail($"Файл проекта не найден: {relativePath}");
        return string.Empty;
    }
}
