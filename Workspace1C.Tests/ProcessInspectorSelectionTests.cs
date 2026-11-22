using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты сохранения выделения в инспекторе процессов (issue #342): при автообновлении
/// списка (каждые ~5 с) выбранная строка восстанавливается по PID, а не сбрасывается;
/// если процесс завершился — выделение снимается; «Завершить процесс» без выделения
/// показывает подсказку вместо молчаливого возврата.
/// </summary>
public sealed class ProcessInspectorSelectionTests
{
    [Fact]
    public void Refresh_PreservesSelectionByPid()
    {
        var service = new FakeRunningService(Proc(1000, "Первый"), Proc(2000, "Второй"));
        using var vm = CreateViewModel(service, out _, out _);

        WaitFor(() => vm.Processes.Count == 2);
        var target = vm.Processes.First(r => r.Pid == 2000);
        vm.SelectedRow = target;

        // Автообновление (issue #134, 1.0.1.102): при том же наборе процессов строки
        // переиспользуются — выделение остаётся на том же экземпляре, а не только по PID.
        service.Set(Proc(1000, "Первый"), Proc(2000, "Второй"));
        vm.Refresh();

        WaitFor(() => vm.Processes.Count == 2 && ReferenceEquals(vm.Processes.First(r => r.Pid == 2000), target));
        Assert.Equal(2000, vm.SelectedRow!.Pid);
        Assert.Same(target, vm.SelectedRow);
    }

    [Fact]
    public void Refresh_ClearsSelectionWhenProcessGone()
    {
        var service = new FakeRunningService(Proc(1000, "Первый"));
        using var vm = CreateViewModel(service, out _, out _);

        WaitFor(() => vm.Processes.Count == 1);
        vm.SelectedRow = vm.Processes.First();

        // Процесс 1000 завершился, появился другой — выделение должно сняться.
        service.Set(Proc(2000, "Другой"));
        vm.Refresh();

        WaitFor(() => vm.Processes.Count == 1 && vm.SelectedRow is null);
        Assert.Null(vm.SelectedRow);
    }

    [Fact]
    public void KillSelected_NoSelection_ShowsHint()
    {
        var service = new FakeRunningService(Proc(1000, "Первый"));
        using var vm = CreateViewModel(service, out var killer, out var dialogs);

        WaitFor(() => vm.Processes.Count == 1);
        vm.SelectedRow = null;

        vm.KillSelected();

        // Киллер не вызывается, пользователю показывается подсказка о выборе строки.
        Assert.Empty(killer.Calls);
        Assert.Single(dialogs.Warnings);
    }

    [Fact]
    public void KillSelected_WithSelection_CallsKiller()
    {
        var service = new FakeRunningService(Proc(1000, "Первый"));
        using var vm = CreateViewModel(service, out var killer, out var dialogs);

        WaitFor(() => vm.Processes.Count == 1);
        vm.SelectedRow = vm.Processes.First(r => r.Pid == 1000);

        vm.KillSelected();

        var call = Assert.Single(killer.Calls);
        Assert.Equal(1000, call.Pid);
        Assert.Empty(dialogs.Warnings);
    }

    [Fact]
    public void Refresh_ClearsSelection_WhenPidReusedWithDifferentCommandLine()
    {
        // Переиспользование PID ОС (issue #342): старый процесс завершился, тот же PID
        // получил другой процесс с иной командной строкой — выделение НЕ должно
        // перескакивать на чужой процесс.
        var service = new FakeRunningService(Proc(1000, "1cv8.exe", "/S srv\\БазаA"));
        using var vm = CreateViewModel(service, out _, out _);

        WaitFor(() => vm.Processes.Count == 1);
        vm.SelectedRow = vm.Processes.First(r => r.Pid == 1000);

        service.Set(Proc(1000, "1cv8.exe", "/S srv\\БазаB"));
        vm.Refresh();

        WaitFor(() => vm.Processes.Count == 1 && vm.SelectedRow is null);
        Assert.Null(vm.SelectedRow);
    }

    [Fact]
    public void Refresh_RestoresSelection_WhenPidAndCommandLineMatch()
    {
        // Тот же PID и та же командная строка — тот же логический процесс (issue #342):
        // выделение восстанавливается по составному ключу.
        var service = new FakeRunningService(Proc(1000, "1cv8.exe", "/S srv\\БазаA"));
        using var vm = CreateViewModel(service, out _, out _);

        WaitFor(() => vm.Processes.Count == 1);
        vm.SelectedRow = vm.Processes.First(r => r.Pid == 1000);

        service.Set(Proc(1000, "1cv8.exe", "/S srv\\БазаA"));
        vm.Refresh();

        WaitFor(() => vm.Processes.Count == 1 && vm.SelectedRow is not null);
        Assert.Equal(1000, vm.SelectedRow!.Pid);
    }

    [Fact]
    public void Refresh_DoesNotOverrideUserSelectionMadeSinceRestore()
    {
        // Двухфазное восстановление не должно «возвращать» старую строку, если
        // пользователь уже выбрал другую (issue #342: страховка от гонки «клик ↔
        // повторная фаза»).
        var service = new FakeRunningService(Proc(1000, "Первый"), Proc(2000, "Второй"));
        using var vm = CreateViewModel(service, out _, out _);

        WaitFor(() => vm.Processes.Count == 2);
        vm.SelectedRow = vm.Processes.First(r => r.Pid == 1000);

        // Автообновление восстанавливает PID 1000.
        service.Set(Proc(1000, "Первый"), Proc(2000, "Второй"));
        vm.Refresh();
        WaitFor(() => vm.SelectedRow is { } r && r.Pid == 1000);

        // Пользователь кликнул строку 2000, затем пришло ещё одно обновление —
        // выделение должно остаться на 2000 (выбор пользователя не перебивается).
        vm.SelectedRow = vm.Processes.First(r => r.Pid == 2000);
        service.Set(Proc(1000, "Первый"), Proc(2000, "Второй"));
        vm.Refresh();

        WaitFor(() => vm.SelectedRow is { } sel && sel.Pid == 2000);
        Assert.Equal(2000, vm.SelectedRow!.Pid);
    }

    private static ProcessInspectorViewModel CreateViewModel(
        FakeRunningService service,
        out FakeKiller killer,
        out RecordingDialogs dialogs)
    {
        killer = new FakeKiller();
        dialogs = new RecordingDialogs();
        return new ProcessInspectorViewModel(
            service, killer, dialogs, Enumerable.Empty<Infobase>(), _ => { }, null);
    }

    /// <summary>Ожидание состояния ViewModel (опрос выполняется в фоновом потоке).</summary>
    private static void WaitFor(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount > deadline)
                throw new TimeoutException("Состояние ViewModel не достигнуто за отведённое время.");
            Thread.Sleep(10);
        }
    }

    private static RunningOneCProcessDetails Proc(int pid, string processName) =>
        Proc(pid, processName, string.Empty);

    private static RunningOneCProcessDetails Proc(int pid, string processName, string commandLine) =>
        new(pid, processName, commandLine, null, null, null);

    /// <summary>Настраиваемый источник процессов (список меняется между опросами).</summary>
    private sealed class FakeRunningService : IRunningInfobasesService
    {
        private IReadOnlyList<RunningOneCProcessDetails> _details;

        public FakeRunningService(params RunningOneCProcessDetails[] details)
        {
            _details = details;
        }

        public void Set(params RunningOneCProcessDetails[] details) => _details = details;

        public IReadOnlyList<RunningOneCProcess> GetRunning() => Array.Empty<RunningOneCProcess>();

        public IReadOnlyList<RunningOneCProcessDetails> GetRunningDetails() => _details;
    }

    /// <summary>Записывает вызовы Kill и результат.</summary>
    private sealed class FakeKiller : IOneCProcessKiller
    {
        public List<(int Pid, string? Token)> Calls { get; } = new();

        public bool Result { get; set; } = true;

        public string? LastError { get; set; }

        public bool Kill(int pid, string? startTimeToken)
        {
            Calls.Add((pid, startTimeToken));
            return Result;
        }
    }

    /// <summary>Записывает предупреждения; подтверждение — настраиваемое.</summary>
    private sealed class RecordingDialogs : IDialogService
    {
        public List<string> Warnings { get; } = new();

        public bool ConfirmResult { get; set; } = true;

        public void ShowInfo(string message, string title = "")
        {
        }

        public void ShowWarning(string message, string title = "") => Warnings.Add(message);

        public void ShowError(string message, string title = "")
        {
        }

        public bool Confirm(string message, string title = "") => ConfirmResult;

        public string? OpenFileDialog(string title = "", string filter = "", string? initialDirectory = null) => null;

        public string? SaveFileDialog(string title = "", string defaultFileName = "", string filter = "", string? initialDirectory = null) => null;

        public string? OpenFolderDialog(string title = "", string? initialDirectory = null) => null;
    }
}