#if LINUX
using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Workspace1C.Services;

namespace Workspace1C.Controls
{
    /// <summary>
    /// Единые метрики UI и вспомогательные методы полировки (Avalonia/Linux).
    /// Элементы, собираемые в коде, используют эти константы, чтобы отступы и скругления
    /// выглядели цельно, а тени и плавные переходы брались из ресурсов темы (без жёстких цветов).
    /// Всё ограничено #if LINUX и не влияет на Windows (WPF) сборку.
    /// </summary>
    public static class UiMetrics
    {
        // ---- Скругления (аудит UI P1c: единая шкала 4 / 8 / 12) ----
        // 4 — чипы и мелкие элементы, 8 — кнопки/поля/карточки, 12 — диалоги и
        // крупные карточки. Пилюли (круглые кнопки, дорожки тумблеров) задаются
        // явным 999 по месту: это не радиус, а признак «капсулы».
        /// <summary>Крупные карточки-секции и диалоги.</summary>
        public const double RadiusXl = 12;
        /// <summary>Управляющие элементы (кнопки, поля, карточки, поле поиска).</summary>
        public const double RadiusLg = 8;
        /// <summary>Карточки строк и иконки-«аватары» (тот же шаг шкалы, что и RadiusLg).</summary>
        public const double RadiusMd = 8;
        /// <summary>Чипы и мелкие элементы (заголовки групп, сегменты, пункты меню).</summary>
        public const double RadiusSm = 4;

        // ---- Размеры шрифтов (аудит UI P2a) ----
        // Единая шкала: подпись 11, вторичный текст 12, основной 13,
        // подзаголовок 15, заголовок 18, заголовок страницы 24.
        // Значения — из общего менеджера темы, чтобы обе платформы жили по одной шкале.
        /// <summary>Мелкие подписи, бейджи, сноски.</summary>
        public const double FontSizeCaption = Themes.ThemeManager.FontSizeCaption;
        /// <summary>Вторичный текст: подсказки, пояснения, статусы.</summary>
        public const double FontSizeSecondary = Themes.ThemeManager.FontSizeSecondary;
        /// <summary>Основной текст интерфейса.</summary>
        public const double FontSizeBody = Themes.ThemeManager.FontSizeBody;
        /// <summary>Заголовок секции/подзаголовок окна.</summary>
        public const double FontSizeSubtitle = Themes.ThemeManager.FontSizeSubtitle;
        /// <summary>Заголовок окна.</summary>
        public const double FontSizeTitle = Themes.ThemeManager.FontSizeTitle;
        /// <summary>Крупный заголовок страницы/пустого состояния.</summary>
        public const double FontSizePage = Themes.ThemeManager.FontSizePage;

        // ---- Размеры иконок (аудит UI, P1b) ----
        // Единая шкала вместо разрозненных 13/14/15/18/22/28/34/44:
        // 13→12, 14/15→16, 18→20, 22→24, 28→24/32, 34→32, 44→48.
        /// <summary>Иконка внутри строки текста, чипа или мелкой метки.</summary>
        public const double IconInline = 12;
        /// <summary>Иконка управляющего элемента: кнопки, поля, пункты меню.</summary>
        public const double IconControl = 16;
        /// <summary>Иконка панели инструментов и компактных кнопок-иконок.</summary>
        public const double IconToolbar = 20;
        /// <summary>Иконка заголовка окна/секции.</summary>
        public const double IconHeader = 24;
        /// <summary>Иконка пустого состояния.</summary>
        public const double IconEmpty = 32;
        /// <summary>Крупная иконка пустого состояния/оверлея.</summary>
        public const double IconEmptyLarge = 48;

        // ---- Отступы (аудит UI P2b: единая шкала 4 / 8 / 12 / 16 / 24 / 32) ----
        /// <summary>Внутренний отступ секций-карточек правой панели (обычный режим).</summary>
        public const double PaddingSection = 12;
        /// <summary>Внутренний отступ управляющих элементов (обычный режим).</summary>
        public const double PaddingControl = 8;

        /// <summary>
        /// Ширина вертикального скроллбара (DIP). Нужна, чтобы содержимое вкладок
        /// настроек не уходило под полосу прокрутки (issue #175 п.7).
        /// </summary>
        public const double ScrollBarWidth = 12;

        /// <summary>
        /// Боковой отступ содержимого вкладок настроек (issue #174, 1.0.1.129):
        /// от него начинается левый край карточек и полей, он же задаёт внешнее
        /// поле правого края.
        /// </summary>
        public const double SettingsSideInset = 12;

        /// <summary>
        /// Базовый правый запас содержимого вкладок настроек под вертикальный
        /// скроллбар (issue #175 п.7): 12 — уже существующий боковой отступ, плюс
        /// ширина скроллбара. Применяется как часть <c>Padding</c> правого края
        /// <c>ScrollViewer</c>, поэтому полоса прокрутки не наезжает на правую
        /// границу карточек-групп, а «Резервное копирование» и ширина вкладок
        /// (260 DIP) не меняются.
        /// </summary>
        public const double SettingsContentRightInset = 12 + ScrollBarWidth;

        /// <summary>
        /// Итоговый правый padding содержимого вкладок настроек (issue #174, 1.0.1.129):
        /// базовый запас <see cref="SettingsContentRightInset"/> плюс дополнительный
        /// боковой отступ <see cref="SettingsSideInset"/>. До этой правки часть
        /// вкладок использовала 0/4/12 DIP, из-за чего правые края галочек и полей
        /// не совпадали. 36 DIP гарантированно больше прежних 12 DIP и оставляет
        /// воздух между содержимым и полосой прокрутки.
        /// </summary>
        public const double SettingsContentRightPadding = SettingsContentRightInset + SettingsSideInset;

        /// <summary>
        /// Полный правый отступ содержимого от края вкладки: внешнее поле
        /// <see cref="SettingsSideInset"/> плюс <see cref="SettingsContentRightPadding"/>.
        /// Нужен вложенным подвкладкам, у которых нет собственного внешнего поля,
        /// чтобы их правый край совпадал с правым краем обычных вкладок.
        /// </summary>
        public const double SettingsContentTotalRightInset = SettingsSideInset + SettingsContentRightPadding;

        // ---- Компактный режим ----
        private static bool _compact;
        /// <summary>
        /// Компактный режим интерфейса: уменьшает размеры иконок, кнопок, шрифтов,
        /// отступов и расстояний между элементами. Устанавливается из настроек при
        /// запуске и из окна настроек; применяется через пересчёт UI.
        /// </summary>
        public static bool Compact
        {
            get => _compact;
            set { if (_compact != value) { _compact = value; CompactChanged?.Invoke(); } }
        }

        /// <summary>Событие изменения компактного режима (для перестроения UI главного окна).</summary>
        public static event Action? CompactChanged;

        /// <summary>Коэффициент масштабирования отступов/размеров при компактном режиме.</summary>
        public static double Scale => Compact ? 0.8 : 1.0;

        /// <summary>Масштабирует значение на коэффициент компактного режима.</summary>
        public static double Scaled(double value) => value * Scale;

        /// <summary>
        /// Коэффициент масштабирования шрифтов при компактном режиме. Задаётся мягче,
        /// чем <see cref="Scale"/> для геометрии: отступы и иконки сжимаются сильнее,
        /// а текст остаётся читаемым (при общем Scale=0.8 мелкие надписи становились
        /// практически неразличимыми).
        /// </summary>
        public static double FontScale => Compact ? 0.9 : 1.0;

        /// <summary>Масштабирует размер шрифта на коэффициент компактного режима.</summary>
        public static double ScaledFont(double value) => value * FontScale;

        // ---- Масштаб строк списка (issue #303) ----

        private static double _userRowScale = 1.0;

        /// <summary>
        /// Пользовательский масштаб строк списка (issue #303): произведение базы
        /// области шрифта («Список баз»/«По умолчанию», эталон 13) и масштаба
        /// Ctrl+колеса/хоткеев. Задаётся вью-моделью; строки списка строятся кодом
        /// с явными размерами, поэтому после изменения дерево перестраивается.
        /// </summary>
        public static double UserRowScale
        {
            get => _userRowScale;
            set
            {
                var clamped = value < 0.5 ? 0.5 : value > 3.0 ? 3.0 : value;
                if (Math.Abs(_userRowScale - clamped) > 0.001)
                {
                    _userRowScale = clamped;
                    RowScaleChanged?.Invoke();
                }
            }
        }

        /// <summary>Событие изменения масштаба строк списка (для пересборки UI).</summary>
        public static event Action? RowScaleChanged;

        /// <summary>Масштабирует размер шрифта элемента строки списка (компакт × масштаб #303).</summary>
        public static double ScaledRowFont(double value) => value * FontScale * _userRowScale;

        /// <summary>Вертикальный отступ верхней панели.</summary>
        /// <remarks>
        /// Значение 8 (в обычном режиме) согласовано с WPF-разметкой, где верхняя панель
        /// поиска имеет Padding="0,8" (MainWindow.xaml:265). Это даёт панели «Теги» под ней
        /// одинаковый вертикальный отступ сверху и снизу (по 8) и выравнивает группу «Теги»
        /// по высоте на обеих платформах (issue #167). Прежнее 10 оставляло сверху 10 против
        /// 8 снизу, из-за чего группа «Теги» на Linux выглядела сдвинутой вниз.
        /// </remarks>
        public static double TopBarV => Compact ? 6 : 8;
        /// <summary>Горизонтальный отступ верхней панели.</summary>
        public static double TopBarH => Compact ? 8 : 12;

        /// <summary>Внутренний отступ секций-карточек правой панели.</summary>
        public static double SectionPad => Compact ? 8 : PaddingSection;
        /// <summary>Нижний отступ между секциями-карточками.</summary>
        public static double SectionMarginBottom => Compact ? 4 : 8;

        /// <summary>Внутренний отступ управляющих элементов (кнопки, поля).</summary>
        public static double ControlPad => Compact ? 6 : PaddingControl;

        /// <summary>Стандартный вертикальный промежуток между строками внутри секции.</summary>
        public static double Gap => Compact ? 4 : 8;

        /// <summary>Вертикальный padding кнопок (primary/secondary).</summary>
        public static double ButtonPadV => Compact ? 4 : 8;
        /// <summary>Горизонтальный padding кнопок (primary/secondary).</summary>
        public static double ButtonPadH => Compact ? 8 : 12;

        /// <summary>Вертикальный padding компактных кнопок действий правой панели.</summary>
        public static double ActionButtonPadV => Compact ? 4 : 8;
        /// <summary>Горизонтальный padding компактных кнопок действий правой панели.</summary>
        public static double ActionButtonPadH => Compact ? 6 : 8;
        /// <summary>Минимальная высота кнопки действия в правой панели.</summary>
        public static double ActionButtonMinHeight => Compact ? 26 : 32;
        /// <summary>
        /// Единая высота кнопок действий правой панели (аудит UI, P1-3, 1.0.1.90):
        /// «1С:Предприятие», «Конфигуратор», «Перейти по ссылке» и «Выход» должны
        /// быть одной высоты, как в WPF-разметке (Height="34").
        /// </summary>
        public static double ActionPanelButtonHeight => Scaled(34);
        /// <summary>Размер иконки на кнопке действия правой панели.</summary>
        public static double ActionIconSize => Compact ? IconInline : IconControl;
        /// <summary>Размер шрифта подписи кнопки действия правой панели.</summary>
        public static double ActionFontSize => Compact ? 11 : 12;
        /// <summary>Промежуток между ячейками сетки действий правой панели.</summary>
        public static double ActionGridGap => Compact ? 4 : 8;

        /// <summary>Размер квадратной подложки под иконку статуса базы в списке.</summary>
        public static double RowIconBox => Compact ? 28 : 38;
        /// <summary>Размер самой иконки статуса внутри подложки.</summary>
        public static double RowIcon => IconControl;
        /// <summary>Размер шрифта имени базы в строке списка.</summary>
        public static double RowNameFont =>
            (Compact ? FontSizeSecondary : FontSizeBody) * _userRowScale;
        /// <summary>Размер шрифта вторичной информации в строке списка.</summary>
        public static double RowSecondaryFont =>
            (Compact ? FontSizeCaption : FontSizeSecondary) * _userRowScale;

        /// <summary>
        /// Размер шрифта имени группы в списке. В обычном режиме имя группы наследует
        /// применяемый к интерфейсу шрифт (без жёсткого размера), поэтому значение имеет
        /// смысл только в компактном режиме, где имя группы задаётся явно и уменьшается.
        /// </summary>
        public static double GroupNameFont => FontSizeBody * _userRowScale;

        /// <summary>Вертикальный внутренний отступ заголовка группы (высота оформления группы).</summary>
        public static double GroupHeaderPadV => Compact ? 1 : 3;
        /// <summary>Вертикальный внешний отступ заголовка группы (расстояние между группами).</summary>
        public static double GroupHeaderMarginV => Compact ? 0.5 : 1;

        /// <summary>
        /// Вертикальный внутренний отступ строки базы. В компактном режиме уменьшается
        /// до значения заголовка группы (<see cref="GroupHeaderPadV"/>), чтобы основная
        /// строка сжималась до высоты группы, а не оставалась прежней (issue #296).
        /// </summary>
        public static double RowPadV => Compact ? 1 : 3;
        /// <summary>Вертикальный внешний отступ строки базы (расстояние между строками).</summary>
        public static double RowMarginV => Compact ? 0.5 : 1;

        /// <summary>
        /// Высота полосы заголовка окна (issue #179): та же метрика, что у кнопок окна,
        /// поэтому шапка главного окна и диалогов одинакова по высоте. В компактном
        /// режиме сжимается вместе с кнопками (issue #296).
        /// </summary>
        public static double TitleBarHeight => Scaled(Themes.WindowChromeMetrics.TitleBarHeight);

        /// <summary>
        /// Вертикальный внутренний отступ шапки окна. Ноль: высоту полосы задаёт
        /// <see cref="TitleBarHeight"/>, а не отступы — иначе шапка главного окна
        /// снова стала бы выше диалоговой (issue #179). В компактном режиме
        /// высота уменьшается самой метрикой (issue #296).
        /// </summary>
        public static double TitleBarPadV => 0;

        /// <summary>
        /// Левый внутренний отступ шапки окна (он же левый отступ заголовка).
        /// Та же метрика, что у диалогов (issue #179), поэтому заголовок главного
        /// окна начинается там же, где у дочерних. В компактном режиме берётся
        /// компактный боковой отступ (пропорционально, без «прыжка» на 9.6 DIP).
        /// </summary>
        public static double TitleBarLeftInset => Compact
            ? Themes.WindowChromeMetrics.CompactTitleBarSideInset
            : Themes.WindowChromeMetrics.TitleBarLeftInset;

        /// <summary>Горизонтальный внутренний отступ шапки окна (равен левому отступу).</summary>
        public static double TitleBarPadH => TitleBarLeftInset;

        /// <summary>
        /// Размер шрифта заголовка окна (issue #179): общий для главного и дочерних
        /// окон, поэтому стиль подписи шапки не расходится. В компактном режиме
        /// уменьшается тем же коэффициентом, что и остальной текст.
        /// </summary>
        public static double TitleBarFontSize =>
            ScaledFont(Themes.WindowChromeMetrics.TitleBarFontSize);

        /// <summary>
        /// Ширина кнопки управления окном (issue #179, 1.0.1.130): единая метрика,
        /// не зависящая от темы кнопок. Раньше ширина бралась из <c>ThemeFeatureMap.Size</c>
        /// (34/40/46), а высота — из метрики полосы, из-за чего кнопки обрезались.
        /// В компактном режиме берётся компактное значение.
        /// </summary>
        public static double WindowButtonWidth => Compact
            ? Themes.WindowChromeMetrics.CompactWindowButtonWidth
            : Themes.WindowChromeMetrics.WindowButtonWidth;

        /// <summary>
        /// Диаметр подложки кнопки управления окном (issue #179, 1.0.1.130): общий для
        /// главного окна и диалогов. В компактном режиме — компактное значение, чтобы
        /// круг оставался вписанным в кнопку и центрировался в полосе.
        /// </summary>
        public static double WindowButtonCircleDiameter => Compact
            ? Themes.WindowChromeMetrics.CompactWindowButtonCircleDiameter
            : Themes.WindowChromeMetrics.WindowButtonCircleDiameter;

        /// <summary>Радиус подложки кнопки окна (половина диаметра) для скруглений.</summary>
        public static double WindowButtonCircleRadius => WindowButtonCircleDiameter / 2;

        /// <summary>
        /// Правый отступ группы кнопок управления окном (issue #179): одинаков у
        /// главного окна и диалогов, чтобы кнопки не выглядели смещёнными вправо.
        /// В компактном режиме — компактное значение (равно боковому отступу полосы).
        /// </summary>
        public static double WindowButtonRightInset => Compact
            ? Themes.WindowChromeMetrics.CompactWindowButtonRightInset
            : Themes.WindowChromeMetrics.WindowButtonRightInset;

        /// <summary>Минимальная ширина правой панели сведений.</summary>
        public static double RightPanelMin => Compact ? 200 : 280;
        /// <summary>Максимальная ширина правой панели сведений.</summary>
        public static double RightPanelMax => Compact ? 255 : 340;

        // ---- Анимации ----
        /// <summary>Длительность плавного перехода цвета/прозрачности.</summary>
        public static readonly TimeSpan TransitionFast = TimeSpan.FromMilliseconds(110);

        /// <summary>
        /// Добавляет мягкую тень (BoxShadow) к элементу. Цвет тени выводится из ресурса
        /// темы «BorderColorBrush» (перекрашивается при смене схемы) — без жёстких цветов.
        /// </summary>
        public static void AddSoftShadow(Border target)
        {
            // Подписка снимается вместе с уходом элемента из дерева: тень
            // добавляется в содержимое главного окна, а оно пересобирается.
            Themes.ThemeBrushes.Observe(target, "BorderColorBrush", brush =>
            {
                if (brush is ISolidColorBrush solid)
                    ApplyShadow(target, solid.Color);
            });
        }

        /// <summary>Добавляет плавный переход цвета фона и/или границы элемента.</summary>
        public static void AddBrushTransition(Border target, bool background = true, bool border = true)
        {
            // На программном рендере/в виртуализации каждый переход держит рендер-цикл
            // занятым и перерисовывает кадры софтом; там отказываемся от плавности,
            // чтобы не жечь CPU (issue #153).
            if (LinuxRendering.DisableAnimations)
                return;
            target.Transitions ??= new Transitions();
            if (background)
                target.Transitions.Add(new BrushTransition
                {
                    Property = Border.BackgroundProperty,
                    Duration = TransitionFast
                });
            if (border)
                target.Transitions.Add(new BrushTransition
                {
                    Property = Border.BorderBrushProperty,
                    Duration = TransitionFast
                });
        }

        /// <summary>Добавляет плавное появление/исчезание по прозрачности.</summary>
        public static void AddOpacityTransition(Visual target, double durationMs = 180)
        {
            // См. AddBrushTransition: на программном рендере/в виртуализации плавность
            // отключаем, чтобы не держать рендер-цикл занятым (issue #153).
            if (LinuxRendering.DisableAnimations)
                return;
            target.Transitions ??= new Transitions();
            target.Transitions.Add(new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(durationMs)
            });
        }

        /// <summary>
        /// Добавляет плавный переход масштаба к ScaleTransform (hover/press-отклик
        /// кнопок управления окном и иконок). См. AddBrushTransition: на программном
        /// рендере/в виртуализации плавность отключается (issue #153).
        /// </summary>
        public static void AddScaleTransition(ScaleTransform target, double durationMs = 100)
        {
            if (LinuxRendering.DisableAnimations)
                return;
            target.Transitions ??= new Transitions();
            target.Transitions.Add(new DoubleTransition
            {
                Property = ScaleTransform.ScaleXProperty,
                Duration = TimeSpan.FromMilliseconds(durationMs)
            });
            target.Transitions.Add(new DoubleTransition
            {
                Property = ScaleTransform.ScaleYProperty,
                Duration = TimeSpan.FromMilliseconds(durationMs)
            });
        }

        /// <summary>
        /// Наблюдатель, который по значению ресурса-кисти строит мягкую полупрозрачную тень
        /// и применяет её к целевому Border.
        /// </summary>
        private static void ApplyShadow(Border target, Color borderColor)
        {
            // Полупрозрачный вариант цвета границы: мягкая тень для обеих тем.
            var shadowColor = new Color((byte)(borderColor.A * 0.26), borderColor.R, borderColor.G, borderColor.B);
            target.BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetY = 3,
                Blur = 14,
                Spread = 0,
                Color = shadowColor
            });
        }
    }
}
#endif