using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты безопасного выполнения асинхронных команд (A3, v100112):
/// <see cref="AsyncRelayCommand.Execute"/> — async void, исключение задачи должно
/// быть поймано внутри команды (лог + диалог через глобальные службы), а не уходить
/// необработанным в глобальный обработчик приложения; семантика IsExecuting
/// сохраняется. Если catch отсутствует, xunit-синхронизационный контекст ловит
/// исключение async void и падает тест — проверка регрессии автоматически.
/// </summary>
public sealed class RelayCommandAsyncSafetyTests
{
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Ждёт, пока предикат станет true (команда выполняется через async void,
    /// детерминированного await нет) — возвращает false по таймауту.</summary>
    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow + CompletionTimeout;
        while (!predicate() && DateTime.UtcNow < deadline)
            await Task.Delay(10).ConfigureAwait(false);
    }

    [Fact]
    public async Task Execute_Completes_IsExecutingResetsAndCanExecuteRestores()
    {
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new AsyncRelayCommand(async _ =>
        {
            await finish.Task.ConfigureAwait(true);
        });

        command.Execute(null);
        Assert.True(command.IsExecuting);
        Assert.False(command.CanExecute(null)); // повторный вход запрещён на время выполнения

        finish.SetResult();
        await WaitUntilAsync(() => !command.IsExecuting).ConfigureAwait(true);

        Assert.False(command.IsExecuting);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void Execute_NullDelegate_CanExecute()
    {
        var command = new AsyncRelayCommand(async () => await Task.Yield());
        Assert.True(command.CanExecute(null));
        Assert.False(command.IsExecuting);
    }

    [Fact]
    public void CanExecute_CustomPredicate_Respected()
    {
        var command = new AsyncRelayCommand(() => Task.CompletedTask, () => false);
        Assert.False(command.CanExecute(null));
    }

    [Fact]
    public async Task Execute_ThrownException_IsSwallowedAndIsExecutingResets()
    {
        // Регрессия A3 (v100112): исключение из задачи НЕ должно покидать Execute
        // (async void). Раньше try/finally без catch — исключение уходило в
        // глобальный обработчик; xunit-контекст фиксирует необработанное исключение
        // async void и падает тест, так что этот тест ловит возврат регрессии.
        var command = new AsyncRelayCommand(_ =>
            Task.FromException(new InvalidOperationException("ошибка асинхронной команды")));

        command.Execute(null);

        await WaitUntilAsync(() => !command.IsExecuting).ConfigureAwait(true);

        Assert.False(command.IsExecuting);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task Execute_ThrownAfterAwait_IsSwallowedAndIsExecutingResets()
    {
        // То же, но исключение брошено ПОСЛЕ await (продолжение не в точке синхронного
        // старта) — покрывает путь catch в асинхронной части Execute.
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new AsyncRelayCommand(async _ =>
        {
            await release.Task.ConfigureAwait(true);
            throw new InvalidOperationException("ошибка после await");
        });

        command.Execute(null);
        Assert.True(command.IsExecuting);

        release.SetResult();
        await WaitUntilAsync(() => !command.IsExecuting).ConfigureAwait(true);

        Assert.False(command.IsExecuting);
        Assert.True(command.CanExecute(null));
    }
}
