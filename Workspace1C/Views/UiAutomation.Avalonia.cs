#if LINUX
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace Workspace1C
{
    /// <summary>
    /// Аудит UI P3a: доступные имена для иконочных кнопок Avalonia/Linux.
    /// Имя берётся из уже заданной подсказки, чтобы не дублировать локализованные
    /// строки в коде. Вызывается рядом с <c>ToolTip.SetTip</c>.
    /// </summary>
    internal static class UiAutomation
    {
        /// <summary>Задаёт AutomationProperties.Name/HelpText по тексту подсказки контрола.</summary>
        public static void SetNameFromTip(StyledElement control)
        {
            if (control is not Control target)
                return;

            var text = ExtractText(ToolTip.GetTip(target));
            SetName(target, text);
        }

        /// <summary>Задаёт AutomationProperties.Name/HelpText явным текстом.</summary>
        public static void SetName(StyledElement control, string? text)
        {
            if (control is null || string.IsNullOrWhiteSpace(text))
                return;

            AutomationProperties.SetName(control, text);
            AutomationProperties.SetHelpText(control, text);
        }

        private static string? ExtractText(object? tip)
        {
            return tip switch
            {
                string text => text,
                TextBlock block => block.Text,
                _ => null
            };
        }
    }
}
#endif
