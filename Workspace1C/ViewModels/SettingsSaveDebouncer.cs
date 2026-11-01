using System.Threading;

namespace Workspace1C.ViewModels;

/// <summary>
/// Debounce-хранилище <see cref="CancellationTokenSource"/> отложенного сохранения
/// настроек (issue #96). Выделено в чистый хелпер сознательно: полный
/// <see cref="MainViewModel"/> для прямого теста слишком нагружен (конструктор,
/// диалоги, диспетчер — конвенция проекта, см. GoToRowTests), а жизненный цикл CTS
/// обязан быть покрыт юнит-тестами — ObjectDisposedException здесь ронял сеттер
/// <c>SelectedInfobase</c> (стек: <c>ScheduleSaveSettings → CancellationTokenSource.Cancel</c>).
///
/// Первопричина issue #96: CTS освобождался (<c>Dispose</c>) в <c>finally</c>
/// отложенной задачи <c>RunDelayedSaveSettingsAsync</c>, пока приватное поле
/// <c>_saveSettingsDebounceCts</c> всё ещё ссылалось на него. Следующий вызов
/// <c>ScheduleSaveSettings</c> (смена <c>SelectedInfobase</c>, <c>AddInfobase</c>,
/// клавиатурная навигация) делал <c>Cancel()</c> на уже освобождённом инстансе.
///
/// Контракт (recreate-on-demand):
/// - <see cref="Schedule"/> всегда создаёт НОВЫЙ CTS и безопасно отменяет предыдущий
///   (завершённый — отменять нечего, освобождённый — <c>ObjectDisposedException</c>
///   гасится как страховка);
/// - <see cref="Complete"/> не освобождает CTS, а лишь убирает ссылку из поля, если
///   не запланирован более новый вызов (Dispose в <c>finally</c> был первопричиной);
///   завершённый, но не освобождённый CTS безопасно переживает повторный Cancel.
/// </summary>
internal sealed class SettingsSaveDebouncer
{
    /// <summary>Текущий (последний запланированный) CTS debounce-сохранения.</summary>
    private CancellationTokenSource? _cts;

    /// <summary>Текущий CTS — точка проверки состояния в юнит-тестах.</summary>
    internal CancellationTokenSource? CurrentCts => _cts;

    /// <summary>
    /// Планирует новое отложенное сохранение: создаёт новый CTS, отменяет предыдущий
    /// отложенный запуск (штатный debounce) и возвращает новый CTS.
    /// </summary>
    /// <returns>Живой (не освобождённый) CTS нового отложенного запуска.</returns>
    public CancellationTokenSource Schedule()
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _cts, cts);
        if (previous is not null)
        {
            try
            {
                // Отменяем ещё не выполненное отложенное сохранение. Завершённый CTS
                // отменять безопасно (коллбэков уже нет). Освобождённый бросает
                // ObjectDisposedException — гасим, его запуск уже завершён (issue #96).
                previous.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Предыдущее поколение CTS уже освобождено — отменять нечего.
            }
        }

        return cts;
    }

    /// <summary>
    /// Отмечает CTS как завершённый (debounce отработал или отменён): убирает ссылку
    /// из поля, если не запланирован более новый вызов. CTS намеренно НЕ освобождается:
    /// поле могло всё ещё ссылаться на этот инстанс, и Dispose здесь был
    /// первопричиной issue #96.
    /// </summary>
    /// <param name="cts">CTS завершённой отложенной задачи.</param>
    public void Complete(CancellationTokenSource cts) =>
        Interlocked.CompareExchange(ref _cts, null, cts);
}
