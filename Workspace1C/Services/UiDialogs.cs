using System;

namespace Workspace1C.Services;

/// <summary>
/// Глобальный переключатель модальных окон и диалогов выбора файлов.
/// <para>
/// Зачем это нужно. В юнит-тестах часть кода приложения выполняется «как в бою»:
/// <see cref="AppServices.Configure"/> регистрирует настоящий
/// <c>WpfDialogService</c>/<c>AvaloniaDialogService</c>, а команды и обработчики
/// ошибок (<c>AsyncRelayCommand</c>, <c>UiExceptionGuard</c>, глобальные обработчики
/// <c>App</c>) при исключении показывают пользователю диалог. В тестовом процессе
/// UI-потока нет (поток xUnit — MTA), поэтому material-окно падает с
/// «Вызывающим потоком должен быть STA…», а резервный <c>MessageBox</c> открывается
/// по-настоящему и блокирует прогон до нажатия кнопки.
/// </para>
/// <para>
/// Флаг выставляется один раз в тестовой сборке
/// (<c>Workspace1C.Tests/TestEnvironment.Init</c>) и в обычном приложении всегда
/// остаётся <c>false</c>. Подавление затрагивает только реальные окна: явно
/// переданные заглушки (<c>IDialogService</c> в конструкторе ViewModel,
/// <see cref="UiExceptionGuard.DialogShower"/>) продолжают работать, поэтому тесты,
/// которые проверяют сам факт показа диалога, остаются валидными.
/// </para>
/// </summary>
public static class UiDialogs
{
    private static volatile bool _suppressModalDialogs;

    /// <summary>
    /// <c>true</c> — модальные окна и файловые диалоги не показываются.
    /// Для <c>Confirm</c>-методов это означает отказ (false), для файловых
    /// диалогов — <c>null</c>, для информационных/ошибочных сообщений — тихий выход
    /// (с записью в <see cref="System.Diagnostics.Debug"/>).
    /// </summary>
    public static bool SuppressModalDialogs
    {
        get => _suppressModalDialogs;
        set => _suppressModalDialogs = value;
    }

    /// <summary>
    /// Пишет факт подавления диалога в отладочный вывод. Специально не используется
    /// <see cref="IAppLogger"/>: тестовый прогон не должен засорять журнал приложения.
    /// </summary>
    internal static void TraceSuppressed(string kind, string title)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine(
                $"[UiDialogs] {kind} suppressed: «{title}»");
        }
        catch
        {
            // Отладочный вывод не должен влиять на работу приложения.
        }
    }
}
