using System;
using System.Collections.Generic;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Хранилище и расчёт ширины колонок вкладок окна «Серверы 1С» (issue #75).
/// Чистая логика без платформенных зависимостей — используется обеими платформами
/// (WPF/Windows и Avalonia/Linux) и покрыта юнит-тестами.
/// <para>
/// Задачи, решаемые классом:
/// <list type="bullet">
/// <item>минимальная ширина колонки по содержимому ЗАГОЛОВКА: берётся худший случай
/// русской/английской локали (длина строки × коэффициент на символ) плюс запас
/// на стрелку сортировки ▲/▼ и горизонтальные отступы заголовка — чтобы заголовок
/// не обрезался по умолчанию;</item>
/// <item>восстановление сохранённых ширин по ключу вкладки
/// (<see cref="SessionsKey"/>, <see cref="ConnectionsKey"/>, <see cref="LocksKey"/>,
/// <see cref="ProcessesKey"/>) с изоляцией вкладок друг от друга;</item>
/// <item>нормализация при применении: сохранённое значение, меньшее минимального
/// или некорректное (NaN/бесконечность/≤ 0), отбрасывается в пользу дефолта.</item>
/// </list>
/// </para>
/// </summary>
public static class ServerMonitorColumnWidthsStore
{
    /// <summary>Ключ вкладки «Сеансы».</summary>
    public const string SessionsKey = "Sessions";

    /// <summary>Ключ вкладки «Соединения».</summary>
    public const string ConnectionsKey = "Connections";

    /// <summary>Ключ вкладки «Блокировки».</summary>
    public const string LocksKey = "Locks";

    /// <summary>Ключ вкладки «Рабочие процессы».</summary>
    public const string ProcessesKey = "Processes";

    /// <summary>
    /// Ключи вкладок в порядке их следования в окне (используется тестами для аудита).
    /// </summary>
    public static readonly IReadOnlyList<string> TabKeys = new[]
    {
        SessionsKey, ConnectionsKey, LocksKey, ProcessesKey
    };

    /// <summary>Средняя ширина одного символа заголовка, px (для базового шрифта 13).</summary>
    public const double CharWidthFactor = 7.5;

    /// <summary>
    /// Запас на отображение пиктограммы направления сортировки (▲/▼, 9 px + отступы
    /// 4/6 px) и горизонтальные отступы заголовка (ColumnHeaderStyle Padding="8,0"),
    /// px.
    /// </summary>
    public const double HeaderReserve = 40.0;

    /// <summary>Абсолютный минимум ширины колонки вкладки, px (служебные колонки-глифы).</summary>
    public const double AbsoluteMinWidth = 60.0;

    /// <summary>
    /// Минимальная ширина колонки, достаточная для заголовка и стрелки сортировки
    /// (issue #75): худший случай русской/английской локали. Для пустых заголовков
    /// (колонки-глифы «●») возвращается <see cref="AbsoluteMinWidth"/>.
    /// </summary>
    public static double HeaderMinWidth(string ru, string en)
    {
        var ruLen = string.IsNullOrEmpty(ru) ? 0 : ru.Length;
        var enLen = string.IsNullOrEmpty(en) ? 0 : en.Length;
        var maxLen = Math.Max(ruLen, enLen);
        if (maxLen == 0)
            return AbsoluteMinWidth;
        return Math.Max(AbsoluteMinWidth, Math.Ceiling(maxLen * CharWidthFactor) + HeaderReserve);
    }

    /// <summary>
    /// Нормализует сохранённые ширины перед применением (issue #75): некорректные
    /// значения (NaN/бесконечность/≤ 0) заменяются дефолтом колонки; корректные —
    /// ограничиваются снизу минимальной шириной колонки (заголовок + стрелка).
    /// Дополнительные значения за пределами числа колонок игнорируются.
    /// </summary>
    /// <param name="saved">Сохранённые ширины (null — запись отсутствует).</param>
    /// <param name="defaults">Дефолтные ширины колонок вкладки (для несохранённых позиций).</param>
    /// <param name="minWidths">Минимальные ширины колонок вкладки (issue #75).</param>
    /// <returns>Нормализованный массив ширины длиной <paramref name="defaults"/>.</returns>
    public static double[] ResolveWidths(
        IReadOnlyList<double>? saved,
        IReadOnlyList<double> defaults,
        IReadOnlyList<double> minWidths)
    {
        if (defaults is null)
            throw new ArgumentNullException(nameof(defaults));
        if (minWidths is not null && minWidths.Count != defaults.Count)
            throw new ArgumentException("Количество минимальных ширин должно совпадать с числом колонок.", nameof(minWidths));

        var result = new double[defaults.Count];
        for (var i = 0; i < defaults.Count; i++)
        {
            var min = minWidths is { Count: > 0 } ? minWidths[i] : AbsoluteMinWidth;
            var value = defaults[i];
            if (saved is not null && i < saved.Count && IsValid(saved[i]))
                value = Math.Max(saved[i], min);
            result[i] = Math.Max(value, min);
        }
        return result;
    }

    /// <summary>Ширина корректна для сохранения/применения (не NaN, не бесконечность, не ≤ 0).</summary>
    public static bool IsValid(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0.5;

    /// <summary>
    /// Новая ширина ЛЕВОЙ колонки границы при ОБЫЧНОМ перетаскивании (issue #75,
    /// поведение как в 1С, 1.0.0.96): граница двигается за курсором, при этом ВСЕ
    /// колонки справа сдвигаются как блок — их ширины не меняются, меняются только
    /// позиции; изменение Δ при переполнении области компенсируется появлением
    /// горизонтальной прокрутки. Левой колонке «достаётся» всё изменение Δ, минимум
    /// колонки соблюдён (дальше граница не сдвигается).
    /// </summary>
    /// <param name="startWidth">Ширина левой колонки в начале перетаскивания, px.</param>
    /// <param name="delta">Накопленное смещение границы, px (вправо — положительное).</param>
    /// <param name="minWidth">Минимальная ширина левой колонки (заголовок + стрелка), px.</param>
    public static double MovedLeftColumnWidth(double startWidth, double delta, double minWidth)
        => Math.Max(minWidth, startWidth + delta);

    /// <summary>
    /// Ширины ДВУХ смежных колонок границы при перетаскивании с зажатым Ctrl
    /// (issue #75, 1.0.0.96): меняется только граница между ними — левая растёт/ужимается
    /// на Δ, правая — на обратную величину (суммарная ширина пары не меняется), остальные
    /// колонки НЕ двигаются. Обе колонки не сжимаются ниже своих минимумов — при упоре
    /// в минимум граница останавливается.
    /// </summary>
    /// <param name="startLeft">Ширина левой колонки в начале перетаскивания, px.</param>
    /// <param name="startRight">Ширина правой колонки в начале перетаскивания, px.</param>
    /// <param name="delta">Накопленное смещение границы, px (вправо — положительное).</param>
    /// <param name="minLeft">Минимальная ширина левой колонки, px.</param>
    /// <param name="minRight">Минимальная ширина правой колонки, px.</param>
    /// <returns>Новые ширины пары (левая, правая); сумма равна <paramref name="startLeft"/> + <paramref name="startRight"/>.</returns>
    public static (double Left, double Right) ResizedTwoAdjacentWidths(
        double startLeft, double startRight, double delta, double minLeft, double minRight)
    {
        // Вправо левая может вырасти не больше, чем на свободный ход правой до минимума;
        // влево — ужаться не больше, чем на свободный ход самой себя до минимума.
        var maxGrow = Math.Max(0, startRight - minRight);
        var maxShrink = Math.Max(0, startLeft - minLeft);
        var applied = Math.Clamp(delta, -maxShrink, maxGrow);
        var left = startLeft + applied;
        var right = startLeft + startRight - left;
        return (left, right);
    }

    /// <summary>
    /// Применяет ОБЫЧНОЕ перетаскивание границы колонки <paramref name="index"/>
    /// (issue #75, комментарий 6444211969): ширина ЛЕВОЙ колонки становится
    /// стартовая + Δ (не меньше минимума — заголовок и стрелка сортировки), ВСЕ
    /// остальные колонки НЕ меняют ширину — они сдвигаются как блок (меняется
    /// только позиция); при переполнении области появляется горизонтальная
    /// прокрутка и границу можно двигать дальше. Возвращает новый массив ширин —
    /// чистый аналог <see cref="MovedLeftColumnWidth"/> для всей строки таблицы.
    /// </summary>
    /// <param name="startWidths">Ширины колонок слева направо в начале перетаскивания.</param>
    /// <param name="index">Индекс левой колонки границы.</param>
    /// <param name="delta">Накопленное смещение границы, px (вправо — положительное).</param>
    /// <param name="minWidths">Минимальные ширины колонок (issue #75).</param>
    public static double[] ApplyDragMoveBlock(
        double[] startWidths, int index, double delta, IReadOnlyList<double>? minWidths = null)
    {
        if (startWidths is null)
            throw new ArgumentNullException(nameof(startWidths));
        if (index < 0 || index >= startWidths.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        var result = (double[])startWidths.Clone();
        result[index] = MovedLeftColumnWidth(
            startWidths[index], delta, MinOf(minWidths, index, startWidths[index]));
        return result;
    }

    /// <summary>
    /// Применяет перетаскивание границы колонки <paramref name="index"/> с зажатым
    /// Ctrl (issue #75): меняются ширины ТОЛЬКО двух смежных колонок — левой (растёт/
    /// ужимается на Δ) и правой (зеркально), остальные колонки НЕ двигаются; сумма
    /// ширины пары постоянна, обе колонки не сжимаются ниже минимумов. Возвращает
    /// новый массив ширин — чистый аналог <see cref="ResizedTwoAdjacentWidths"/>.
    /// </summary>
    /// <param name="startWidths">Ширины колонок слева направо в начале перетаскивания.</param>
    /// <param name="index">Индекс левой колонки границы (нужна и правая, индекс+1).</param>
    /// <param name="delta">Накопленное смещение границы, px (вправо — положительное).</param>
    /// <param name="minWidths">Минимальные ширины колонок (issue #75).</param>
    public static double[] ApplyDragResizePair(
        double[] startWidths, int index, double delta, IReadOnlyList<double>? minWidths = null)
    {
        if (startWidths is null)
            throw new ArgumentNullException(nameof(startWidths));
        if (index < 0 || index + 1 >= startWidths.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        var result = (double[])startWidths.Clone();
        var (left, right) = ResizedTwoAdjacentWidths(
            startWidths[index], startWidths[index + 1], delta,
            MinOf(minWidths, index, startWidths[index]),
            MinOf(minWidths, index + 1, startWidths[index + 1]));
        result[index] = left;
        result[index + 1] = right;
        return result;
    }

    private static double MinOf(IReadOnlyList<double>? minWidths, int index, double fallback)
        => minWidths is { Count: > 0 } && minWidths.Count > index && IsValid(minWidths[index])
            ? minWidths[index]
            : Math.Max(AbsoluteMinWidth, fallback);

    /// <summary>
    /// Определяет границу колонок по координате X клика относительно грида (issue #75,
    /// репорт 7OH от 2026-10-30: зона захвата была смещена — клик по выступу ручки/левой
    /// кромке заголовка следующей колонки распознавался как граница СЛЕДУЮЩЕЙ пары,
    /// off-by-one). Граница между колонками <c>i</c> и <c>i+1</c> — правый край колонки
    /// <c>i</c> (накопленная ширина колонок 0..i); из всех границ выбирается БЛИЖАЙШАЯ
    /// к <paramref name="x"/>, если она не дальше <paramref name="tolerance"/> px.
    /// Возвращает индекс ЛЕВОЙ колонки границы (для <see cref="ApplyDragMoveBlock"/> /
    /// <see cref="ApplyDragResizePair"/>) или −1, если клик не у границы (центр заголовка —
    /// сортировка/без действия, драг не стартует). Чистая функция — обе платформы, тесты.
    /// </summary>
    /// <param name="widths">Фактические ширины колонок слева направо, px.</param>
    /// <param name="x">Координата X клика относительно левого края грида, px.</param>
    /// <param name="tolerance">Допуск расстояния до границы, px (±).</param>
    public static int ResolveDragBoundary(IReadOnlyList<double> widths, double x, double tolerance)
    {
        if (widths is null || widths.Count == 0)
            return -1;
        if (double.IsNaN(x) || double.IsInfinity(x))
            return -1;
        if (double.IsNaN(tolerance) || tolerance < 0)
            tolerance = 0;

        var bestIndex = -1;
        var bestDistance = double.MaxValue;
        var cumulative = 0.0;
        for (var i = 0; i < widths.Count; i++)
        {
            var width = widths[i];
            if (!IsValid(width))
                return -1; // ширины ещё не измерены — определение по координате невозможно.
            cumulative += width;
            var distance = Math.Abs(x - cumulative);
            if (distance <= tolerance && distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }
        return bestIndex;
    }

    /// <summary>
    /// Сохраняет ширины колонок вкладки в настройки приложения (issue #75):
    /// запись по ключу вкладки заменяется целиком, остальные вкладки не затрагиваются.
    /// Некорректные значения (0 для гибких колонок, NaN) записываются как 0 — значит
    /// «не сохранять эту колонку» (при применении используется дефолт/минимум).
    /// </summary>
    /// <param name="settings">Настройки приложения (не null; словарь создаётся при необходимости).</param>
    /// <param name="tabKey">Ключ вкладки (Sessions/Connections/Locks/Processes).</param>
    /// <param name="widths">Фактические ширины колонок слева направо.</param>
    public static void Save(AppSettings settings, string tabKey, IReadOnlyList<double> widths)
    {
        if (settings is null)
            throw new ArgumentNullException(nameof(settings));
        if (string.IsNullOrWhiteSpace(tabKey))
            throw new ArgumentNullException(nameof(tabKey));
        if (widths is null)
            throw new ArgumentNullException(nameof(widths));

        settings.ServerMonitorColumnWidths ??= new Dictionary<string, double[]>();
        var stored = new double[widths.Count];
        for (var i = 0; i < widths.Count; i++)
            stored[i] = IsValid(widths[i]) ? Math.Round(widths[i], 1) : 0.0;
        settings.ServerMonitorColumnWidths[tabKey] = stored;
    }

    /// <summary>
    /// Возвращает сохранённые ширины вкладки (issue #75). Отсутствие записи —
    /// возвращается <c>null</c> (вызывающий использует дефолтные ширины по умолчанию).
    /// </summary>
    /// <param name="settings">Настройки приложения (null или без записи — null).</param>
    /// <param name="tabKey">Ключ вкладки (Sessions/Connections/Locks/Processes).</param>
    public static double[]? TryLoad(AppSettings? settings, string tabKey)
    {
        if (settings is null || string.IsNullOrWhiteSpace(tabKey))
            return null;
        if (settings.ServerMonitorColumnWidths is null)
            return null;
        return settings.ServerMonitorColumnWidths.TryGetValue(tabKey, out var widths) ? widths : null;
    }
}
