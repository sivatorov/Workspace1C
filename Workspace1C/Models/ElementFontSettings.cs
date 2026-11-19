namespace Workspace1C.Models
{
    /// <summary>
    /// Настройки шрифта для отдельной области интерфейса
    /// (список баз, заголовки, правая панель, строка состояния, вкладки, кнопки, поля ввода).
    /// </summary>
    public class ElementFontSettings
    {
        /// <summary>Семейство шрифта области (по умолчанию — платформенный шрифт).</summary>
        public string FontFamily { get; set; } = Workspace1C.Themes.ThemeManager.DefaultFontFamily;

        /// <summary>Размер шрифта (в логических единицах WPF).</summary>
        public double FontSize { get; set; } = 13;

        /// <summary>Начертание шрифта: «Normal» или «Bold».</summary>
        public string FontWeight { get; set; } = "Normal";

        /// <summary>Стиль шрифта: «Normal» или «Italic».</summary>
        public string FontStyle { get; set; } = "Normal";

        /// <summary>
        /// Миграция при загрузке: пустое семейство заменяется платформенным
        /// шрифтом по умолчанию (не «Segoe UI» на Linux), некорректный размер —
        /// основным 13. Сохранённый пользовательский выбор не сбрасывается.
        /// </summary>
        public void NormalizeForLoad()
        {
            FontFamily = string.IsNullOrWhiteSpace(FontFamily)
                ? Workspace1C.Themes.ThemeManager.DefaultFontFamily
                : FontFamily;
            if (FontSize <= 0)
                FontSize = Workspace1C.Themes.ThemeManager.FontSizeBody;
            FontWeight = string.IsNullOrWhiteSpace(FontWeight) ? "Normal" : FontWeight;
            FontStyle = string.IsNullOrWhiteSpace(FontStyle) ? "Normal" : FontStyle;
        }

        /// <summary>Копия настроек.</summary>
        public ElementFontSettings Clone() => new()
        {
            FontFamily = FontFamily,
            FontSize = FontSize,
            FontWeight = FontWeight,
            FontStyle = FontStyle
        };
    }
}