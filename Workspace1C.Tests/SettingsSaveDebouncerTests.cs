using System.Threading;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты debounce-хранилища CTS отложенного сохранения настроек (issue #96).
///
/// Первопричина бага: <c>MainViewModel.RunDelayedSaveSettingsAsync</c> освобождал
/// CTS (<c>Dispose</c> в <c>finally</c>), пока приватное поле MainViewModel всё ещё
/// ссылалось на него; следующий вызов <c>ScheduleSaveSettings</c> (сеттер
/// <c>SelectedInfobase</c>, <c>AddInfobase</c>, клавиатурная навигация) делал
/// <c>Cancel()</c> на освобождённом инстансе — <c>System.ObjectDisposedException</c>
/// (журнал пользователя, v1.0.1.18: guard ловил исключение, но первопричина
/// оставалась).
///
/// Полный <see cref="MainViewModel"/> для прямого теста слишком нагружен
/// (конструктор, диалоги, диспетчер) — конвенция проекта (см. GoToRowTests,
/// CustomActionExecutionPlanTests): жизненный цикл CTS вынесен в чистый хелпер
/// <see cref="SettingsSaveDebouncer"/> и тестируется напрямую, а
/// <c>MainViewModel.ScheduleSaveSettings</c> делегирует ему.
/// </summary>
public class SettingsSaveDebouncerTests
{
    /// <summary>Сценарий issue #96 №1: debounce отработал (Complete), затем — новая
    /// смена SelectedInfobase (Schedule). Раньше: Cancel на «висящем» в поле CTS,
    /// который could уже быть освобождён — ObjectDisposedException.</summary>
    [Fact]
    public void Schedule_AfterPreviousCompleted_ReturnsNewLiveCts()
    {
        var debouncer = new SettingsSaveDebouncer();
        var first = debouncer.Schedule();
        debouncer.Complete(first); // отложенная задача отработала (debounce завершён)

        var second = debouncer.Schedule(); // «смена SelectedInfobase» после завершения

        Assert.NotSame(first, second);
        Assert.False(second.IsCancellationRequested);
        Assert.True(second.Token.CanBeCanceled);
        Assert.Same(second, debouncer.CurrentCts);
    }

    /// <summary>Сценарий issue #96 №2 (защита от регрессии): предыдущий CTS
    /// освобождён (Dispose), поле ссылалось на него — Schedule обязан пережить
    /// Cancel на освобождённом инстансе без ObjectDisposedException и запланировать
    /// новое сохранение.</summary>
    [Fact]
    public void Schedule_WithDisposedPreviousCts_DoesNotThrowObjectDisposedException()
    {
        var debouncer = new SettingsSaveDebouncer();
        var first = debouncer.Schedule();
        first.Dispose(); // симуляция прежнего поведения: CTS освобождён, поле «висит»

        var exception = Record.Exception(() => debouncer.Schedule());

        Assert.Null(exception);
        Assert.NotNull(debouncer.CurrentCts);
        Assert.False(debouncer.CurrentCts!.IsCancellationRequested);
        Assert.NotSame(first, debouncer.CurrentCts);
    }

    /// <summary>Штатный debounce: новый вызов отменяет ещё не выполненное
    /// отложенное сохранение (сохраняется последнее состояние).</summary>
    [Fact]
    public void Schedule_CancelsPreviousPendingDebounce()
    {
        var debouncer = new SettingsSaveDebouncer();
        var first = debouncer.Schedule();

        var second = debouncer.Schedule(); // быстрый повторный вызов (щёлчки по фильтрам)

        Assert.True(first.Token.IsCancellationRequested); // прежнее сохранение отменено
        Assert.False(second.Token.IsCancellationRequested); // новое запланировано
        Assert.Same(second, debouncer.CurrentCts);
    }

    /// <summary>Complete «старого» поколения не должен сбить более новый Schedule:
    /// отложенные задачи могут завершаться позже нового вызова Schedule.</summary>
    [Fact]
    public void Complete_DoesNotRemoveNewerSchedule()
    {
        var debouncer = new SettingsSaveDebouncer();
        var first = debouncer.Schedule();
        var second = debouncer.Schedule(); // новый вызов пришёл раньше завершения старого

        debouncer.Complete(first); // старая задача завершилась позже

        Assert.Same(second, debouncer.CurrentCts);
    }

    /// <summary>Complete текущего CTS убирает ссылку из поля (нет удержания
    /// завершённых инстансов), новый Schedule начнётся с чистого состояния.</summary>
    [Fact]
    public void Complete_RemovesCurrentReference()
    {
        var debouncer = new SettingsSaveDebouncer();
        var cts = debouncer.Schedule();

        debouncer.Complete(cts);

        Assert.Null(debouncer.CurrentCts);
    }

    /// <summary>Стресс реальных последовательностей UI-событий: завершение debounce,
    /// dispose, незавершённый (pending) debounce — вперемешку. После любого из этих
    /// сценариев Schedule обязан вернуть живой, не отменённый CTS (сохранение
    /// настроек по-прежнему планируется).</summary>
    [Fact]
    public void Schedule_MixedDisposeCompleteCycles_NeverThrows_AndKeepsPlanning()
    {
        var debouncer = new SettingsSaveDebouncer();
        for (var i = 0; i < 100; i++)
        {
            var cts = debouncer.Schedule(); // «смена SelectedInfobase»

            // Сразу после Schedule сохранение всегда запланировано: живой,
            // не отменённый CTS в поле (Complete предыдущего поколения мог
            // обнулить поле, но Schedule восстановил его).
            Assert.NotNull(debouncer.CurrentCts);
            Assert.Same(cts, debouncer.CurrentCts);
            Assert.False(cts.IsCancellationRequested);

            switch (i % 3)
            {
                case 0:
                    debouncer.Complete(cts); // debounce отработал — поле закономерно пусто
                    Assert.Null(debouncer.CurrentCts);
                    break;
                case 1:
                    cts.Dispose(); // прежнее поколение было освобождено (issue #96)
                    Assert.NotNull(debouncer.CurrentCts);
                    break;
                case 2:
                    // pending: отложенное сохранение ещё не выполнено
                    Assert.NotNull(debouncer.CurrentCts);
                    break;
            }
        }
    }
}
