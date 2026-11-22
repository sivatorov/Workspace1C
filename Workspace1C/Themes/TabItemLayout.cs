namespace Workspace1C.Themes
{
    /// <summary>
    /// Геометрия заголовка вертикальной вкладки настроек (issue #149).
    /// Акцентная полоса выбранной вкладки рисуется левой рамкой <c>Border</c>,
    /// поэтому её ширина должна быть зарезервирована у вкладки в любом
    /// состоянии. Если в покое рамки нет, а при <c>IsSelected</c> появляется
    /// полоса 3 px, рамка «отъедает» ширину и надпись с иконкой смещается
    /// вправо относительно остальных вкладок. Расчёт вынесен сюда, чтобы
    /// инвариант «отступ не зависит от выделения» покрывался юнит-тестами.
    /// </summary>
    public static class TabItemLayout
    {
        /// <summary>Ширина акцентной полосы выбранной вкладки, DIP.</summary>
        public const double AccentBarThickness = 3;

        /// <summary>
        /// Горизонтальный отступ контента от левого края вкладки, DIP:
        /// левая рамка плюс левый внутренний отступ.
        /// </summary>
        public static double ContentLeftOffset(double borderLeftThickness, double paddingLeft)
            => borderLeftThickness + paddingLeft;

        /// <summary>
        /// Сдвиг контента при смене состояния выделения, DIP. Должен быть равен
        /// нулю: толщина левой рамки в покое и при выделении совпадает.
        /// </summary>
        public static double SelectionShift(double normalBorderLeftThickness, double selectedBorderLeftThickness)
            => selectedBorderLeftThickness - normalBorderLeftThickness;
    }
}
