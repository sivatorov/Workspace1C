using System.Windows;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Реализация ICommand для привязки команд в XAML.
/// Поддерживает ручной вызов RaiseCanExecuteChanged для мгновенного обновления UI.
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <summary>
    /// Конструктор для команд без параметра.
    /// </summary>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    /// <summary>
    /// Принудительно уведомляет привязки о необходимости пересчитать CanExecute.
    /// </summary>
    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}

/// <summary>
/// Асинхронная команда для операций I/O и длительных действий без блокировки UI.
/// </summary>
public class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Predicate<object?>? _canExecute;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool IsExecuting => _isExecuting;

    public bool CanExecute(object? parameter) =>
        !_isExecuting && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        _isExecuting = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute(parameter).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // A3 (v100112): async void Execute — исключение задачи больше не уходит
            // необработанным в DispatcherUnhandledException без контекста команды:
            // лог + диалог ошибки (как в глобальном обработчике App).
            ReportExecutionFailure(ex);
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Единая обработка исключения асинхронной команды (A3, v100112): запись в журнал
    /// и показ диалога ошибки в UI-потоке. Ошибки логирования/показа диалога глушатся —
    /// обработчик ошибок команды не должен сам ронять приложение.
    /// </summary>
    private static void ReportExecutionFailure(Exception ex)
    {
        try
        {
            AppServices.TryGetService<IAppLogger>()?.Error("Ошибка асинхронной команды", ex);
        }
        catch
        {
            // Журнал недоступен — не критично.
        }

        try
        {
            ShowErrorDialog(ex.Message);
        }
        catch
        {
            // Диалог недоступен (нет приложения/сервиса) — не критично.
        }
    }

    /// <summary>Показ диалога ошибки в UI-потоке; без приложения/сервиса диалогов — тихий выход.</summary>
    private static void ShowErrorDialog(string message)
    {
        var dialogs = AppServices.TryGetService<IDialogService>();
        if (dialogs is null)
            return;
        var title = LocalizationManager.T("Common.Error");
        var application = Application.Current;
        if (application is null)
        {
            dialogs.ShowError(message, title);
            return;
        }

        application.Dispatcher.Invoke(() => dialogs.ShowError(message, title));
    }

    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}
