using System.Threading;

namespace Workspace1C.Services;

/// <summary>
/// Issue #158 (1.0.1.123): не более одного одновременно открытого окна редактора
/// «Мои программы». У 7OH после ошибки удаления/сохранения открывались два окна
/// редактора (второй клик по шестерёнке создавал новый экземпляр, пока первый
/// оставался на экране).
/// <para>
/// Логика вынесена в чистый статический класс без зависимостей от UI, чтобы её можно
/// было покрыть обычными юнит-тестами (WPF/Avalonia-окна в тестах не создаются).
/// Вход в редактор — <see cref="TryEnter"/>; флаг снимается в <c>finally</c> открытия
/// и в <c>Closed</c> окна, поэтому ошибка сохранения/удаления не «залипает» навсегда.
/// </para>
/// </summary>
public static class CustomProgramsEditorGuard
{
    private static int _open;

    /// <summary>Открыт ли редактор «Мои программы» прямо сейчас.</summary>
    public static bool IsOpen => Volatile.Read(ref _open) == 1;

    /// <summary>
    /// Пытается занять «слот» редактора. <c>true</c> — окно можно открывать;
    /// <c>false</c> — редактор уже открыт, второе окно создавать нельзя
    /// (вызывающий код активирует существующее).
    /// </summary>
    public static bool TryEnter()
        => Interlocked.CompareExchange(ref _open, 1, 0) == 0;

    /// <summary>Освобождает «слот» редактора (вызывается при любом исходе открытия).</summary>
    public static void Exit()
        => Interlocked.Exchange(ref _open, 0);

    /// <summary>
    /// Сброс состояния между тестами. В рабочем коде не используется: снятие флага
    /// выполняют <c>finally</c> открытия и <c>Closed</c> окна.
    /// </summary>
    internal static void ResetForTests()
        => Interlocked.Exchange(ref _open, 0);
}
