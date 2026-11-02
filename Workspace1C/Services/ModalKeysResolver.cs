namespace Workspace1C.Services;

/// <summary>
/// Чистая логика клавиш модальных диалогов (issue #100, Linux: пробел в диалоге
/// с одной кнопкой «ОК» не нажимал её, а пробрасывался в перекрытое окно).
/// Вынесена из UI-кода отдельно, чтобы покрыть юнит-тестами: Space и Enter без
/// модификаторов активируют кнопку по умолчанию модального окна сообщения.
/// WPF-версия в этом правиле не нуждается — фокус по умолчанию устанавливается
/// на кнопку IsDefault, и Space/Enter обрабатывает сама сфокусированная кнопка.
/// </summary>
public static class ModalKeysResolver
{
    /// <summary>
    /// Является ли клавиша клавишей активации кнопки по умолчанию диалога:
    /// <c>Space</c> или <c>Enter</c> без модификаторов. Клавиши сравниваются по
    /// имени (<c>Key.ToString()</c> в WPF и Avalonia даёт «Space»/«Enter»), поэтому
    /// логика одинакова для обеих платформ.
    /// </summary>
    /// <param name="keyName">Имя клавиши (<c>Key.Space</c>/<c>Key.Enter</c> → «Space»/«Enter»).</param>
    /// <param name="hasModifiers">Нажаты ли модификаторы (Ctrl/Alt/Shift/Meta).</param>
    public static bool IsActivationKey(string? keyName, bool hasModifiers)
    {
        if (hasModifiers || string.IsNullOrWhiteSpace(keyName))
            return false;
        return keyName is "Space" or "Enter" or "Return";
    }

    /// <summary>
    /// Должен ли обработчик окна перехватить клавишу и нажать кнопку по умолчанию:
    /// клавиша активации И фокус не на кнопке (если фокус на кнопке — Space/Enter
    /// обрабатывает сама кнопка, перехват задваивал бы нажатие).
    /// </summary>
    /// <param name="focusedElementIsButton">Фокус находится на кнопке.</param>
    /// <param name="keyName">Имя клавиши.</param>
    /// <param name="hasModifiers">Нажаты ли модификаторы.</param>
    public static bool ShouldActivateDefaultButton(bool focusedElementIsButton, string? keyName, bool hasModifiers)
        => !focusedElementIsButton && IsActivationKey(keyName, hasModifiers);
}
