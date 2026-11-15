using System;

namespace Workspace1C.Services
{
    /// <summary>
    /// Чистая (без WPF/Avalonia) политика подавления повторного открытия всплывающих
    /// подсказок (issue #131). Отделяет «закрыть подсказку» от «запретить её повторное
    /// открытие»: запрет нужен только в сценариях принудительного закрытия (ESC), и он
    /// всегда ограничен окном времени — «вечного» подавления быть не может.
    /// </summary>
    /// <remarks>
    /// Причина появления (issue #131, комментарий 7OH: «подсказка появляется и почти сразу
    /// исчезает, и после этого больше не появляется до перезапуска»):
    /// <list type="bullet">
    /// <item>прежняя проверка вето в <c>ToolTipCloser.OnToolTipOpening</c> содержала
    /// <c>|| IsPointerOver(owner)</c>, а при показе подсказки по наведению курсор ВСЕГДА
    /// над владельцем — запись о подавлении никогда не снималась, и владелец навсегда
    /// терял подсказку;</item>
    /// <item>подавление ставилось при ЛЮБОМ программном закрытии (потеря фокуса,
    /// деактивация, прокрутка, fallback-таймер), поэтому подсказка «мигала».</item>
    /// </list>
    /// Теперь подавление включается только ESC-путями (<c>CloseAll(suppressReopen: true)</c>)
    /// и живёт не дольше <see cref="WindowMs"/>, а уход курсора с владельца снимает его
    /// немедленно — повторное наведение снова показывает подсказку без перезапуска.
    /// </remarks>
    public sealed class ToolTipSuppressionPolicy
    {
        /// <summary>Значение «подавления нет» для <c>suppressedTick</c>.</summary>
        public const long NoSuppression = long.MinValue;

        /// <summary>Окно подавления по умолчанию (мс) — как было в прежнем механизме.</summary>
        public const long DefaultWindowMs = 800;

        /// <summary>
        /// Создаёт политику с заданным окном подавления. Отрицательное значение
        /// трактуется как 0 (подавление выключено).
        /// </summary>
        public ToolTipSuppressionPolicy(long windowMs = DefaultWindowMs)
        {
            WindowMs = windowMs < 0 ? 0 : windowMs;
        }

        /// <summary>Окно подавления повторного открытия (мс).</summary>
        public long WindowMs { get; }

        /// <summary>
        /// Нужно ли подавить повторное открытие подсказки в момент <paramref name="nowTick"/>.
        /// Подавление действует только внутри окна <see cref="WindowMs"/> от момента закрытия.
        /// <paramref name="pointerOver"/> намеренно НЕ продлевает запрет: иначе наведённый
        /// курсор «замораживал» бы владельца навсегда (дефект issue #131).
        /// </summary>
        public bool ShouldSuppress(long nowTick, long suppressedTick, bool pointerOver)
        {
            if (suppressedTick == NoSuppression || WindowMs <= 0)
                return false;

            var elapsed = nowTick - suppressedTick;
            return elapsed >= 0 && elapsed < WindowMs;
        }

        /// <summary>
        /// Можно ли снять запись о подавлении. Снимаем, если записи нет, окно истекло
        /// или курсор уже не над владельцем — в последнем случае цикл закрытия завершён
        /// и повторное наведение должно показывать подсказку сразу.
        /// </summary>
        public bool ShouldRelease(long nowTick, long suppressedTick, bool pointerOver)
        {
            if (suppressedTick == NoSuppression)
                return true;
            if (!ShouldSuppress(nowTick, suppressedTick, pointerOver))
                return true;
            return !pointerOver;
        }

        /// <summary>
        /// Разрешено ли fallback-таймеру закрывать всплывающие элементы (issue #131).
        /// Закрываем только когда foreground-окно принадлежит другому процессу И курсор
        /// не находится над нашим окном: иначе кратковременный «чужой» foreground или
        /// работа пользователя с приложением гасили бы только что показанную подсказку.
        /// </summary>
        public static bool IsFallbackCloseAllowed(bool ourProcessForeground, bool pointerOverOurWindow)
        {
            return !ourProcessForeground && !pointerOverOurWindow;
        }
    }
}
