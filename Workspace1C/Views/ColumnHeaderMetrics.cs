using System;
using System.Collections.Generic;

namespace Workspace1C
{
    /// <summary>
    /// Метрики заголовков колонок списка баз (аудит UI, пункт P0-3): минимальная ширина
    /// колонки, при которой подпись заголовка читается полностью, и высота шапки под две
    /// строки. Общие для WPF и Avalonia, поэтому ширины колонок на Windows и Linux
    /// симметричны; покрыты юнит-тестами.
    /// <para>
    /// Почему понадобилось: колонка «Последняя копия» имела ширину 130, а подпись
    /// с иконкой занимает ≈ 140 (110.6 px текст Segoe UI/Inter 12 px SemiBold + 29 px
    /// иконка и отступы), поэтому в шапке оставалось «Послед…». Теперь подпись
    /// переносится на две строки, а колонка не может стать уже самого длинного слова
    /// заголовка — обрезки нет ни при ширине по умолчанию, ни после перетаскивания
    /// разделителя.
    /// </para>
    /// </summary>
    public static class ColumnHeaderMetrics
    {
        /// <summary>Размер иконки заголовка (px), как в разметке WPF и в построителе Avalonia.</summary>
        public const double IconSize = 13;

        /// <summary>Зазор между иконкой и текстом заголовка (px).</summary>
        public const double IconTextGap = 5;

        /// <summary>Горизонтальные отступы заголовка: 6 слева + 6 справа (px).</summary>
        public const double HorizontalMargins = 12;

        /// <summary>Запас на округление метрик шрифта (px).</summary>
        public const double TextSafety = 2;

        /// <summary>Неизменяемая часть заголовка: иконка, зазор и отступы (30 px).</summary>
        public const double HeaderChrome = IconSize + IconTextGap + HorizontalMargins;

        /// <summary>
        /// Высота шапки списка (px): две строки текста 12 px (≈ 16 px каждая)
        /// плюс нижний отступ 4 px и запас на межстрочный интервал. Фиксированная
        /// высота не даёт шапке «прыгать» при переносе подписи.
        /// </summary>
        public const double HeaderHeight = 40;

        /// <summary>Абсолютный минимум ширины колонки значений (px).</summary>
        public const double MinWidthFloor = 80;

        /// <summary>
        /// Предел длины подписи заголовка (символов): при двух строках и минимальной
        /// ширине колонки более длинный текст потребовал бы третьей строки и снова
        /// обрезался бы. Проверяется тестом по ru.json/en.json.
        /// </summary>
        public const int MaxHeaderChars = 16;

        /// <summary>Минимум колонки «Название»: имя базы должно читаться (px).</summary>
        public const double NameColumnMinWidth = 220;

        /// <summary>
        /// Ширина самого длинного слова подписи заголовка (px) при шрифте 12 px SemiBold.
        /// Значения замерены для Segoe UI (Windows) и Inter (Linux/Avalonia); берётся
        /// максимум из двух, чтобы перенос на две строки одинаково работал на обеих
        /// платформах. Перенос идёт по пробелам и «/», поэтому подпись не обрезается,
        /// если колонка не уже самого длинного слова.
        /// </summary>
        private static readonly Dictionary<string, double> LongestWordWidths = new(StringComparer.Ordinal)
        {
            ["Name"] = 61.9,                 // «Название»
            ["Version"] = 74.4,              // «Платформа»
            ["Configuration"] = 93.5,        // «Конфигурация»
            ["ConfigurationVersion"] = 46.2, // «релиза»
            ["LaunchMode"] = 51.1,           // «запуска»
            ["ServerBase"] = 81.1,           // «Сервер/База»
            ["LastLaunch"] = 71.8,           // «Последний»
            ["Size"] = 49.3,                 // «Размер»
            ["Modified"] = 71.3,             // «изменений»
            ["LastBackup"] = 70.4,           // «Последняя»
            ["Actions"] = 61.5,              // «Действия»
        };

        /// <summary>
        /// Минимальная ширина колонки по ключу: длиннейшее слово заголовка плюс иконка,
        /// зазор, отступы и запас. Для «Названия» — отдельный минимум под имя базы.
        /// Неизвестный ключ получает <see cref="MinWidthFloor"/>, чтобы новые колонки
        /// не могли стать уже 80 px.
        /// </summary>
        public static double MinWidthFor(string? columnKey)
        {
            if (string.IsNullOrEmpty(columnKey))
                return MinWidthFloor;
            if (string.Equals(columnKey, "Name", StringComparison.Ordinal))
                return NameColumnMinWidth;
            if (!LongestWordWidths.TryGetValue(columnKey, out var wordWidth))
                return MinWidthFloor;

            return Math.Ceiling(Math.Max(MinWidthFloor, wordWidth + HeaderChrome + TextSafety));
        }

        /// <summary>Таблица замеров самого длинного слова заголовка (px) — для тестов.</summary>
        public static IReadOnlyDictionary<string, double> LongestWordWidthTable => LongestWordWidths;
    }
}
