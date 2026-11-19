#if WINDOWS
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Workspace1C
{
    /// <summary>
    /// Аудит UI P3a: доступные имена для иконочных кнопок WPF.
    /// Имя берётся из уже заданной подсказки, чтобы не дублировать локализованные
    /// строки в коде. Вызывается рядом с установкой <c>ToolTip</c>.
    /// </summary>
    internal static class UiAutomation
    {
        /// <summary>Задаёт AutomationProperties.Name/HelpText по тексту подсказки контрола.</summary>
        public static void SetNameFromTip(FrameworkElement control)
        {
            if (control is null)
                return;

            SetName(control, ExtractText(control.ToolTip));
        }

        /// <summary>Задаёт AutomationProperties.Name/HelpText явным текстом.</summary>
        public static void SetName(DependencyObject control, string? text)
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
