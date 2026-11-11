#if LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Колонки списка баз главного окна (Avalonia/Linux): состав, ширины, заголовки,
    /// перетаскивание разделителей и выравнивание заголовка со строками.
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>Описание колонки списка баз: ключ, заголовок, ширина.</summary>
        private readonly record struct ListColumn(string Key, string Header, double Width);

        /// <summary>Минимум под имя базы: колонка звёздная, но схлопываться ей нельзя.</summary>
        private const double NameColumnMinWidth = 220;

        /// <summary>
        /// Сдвиг пустой группы: у неё нет кнопки разворота, и без сдвига её
        /// заголовок начинался бы левее соседних. Считается как у автора
        /// (Converters/GroupOffsetConverter.cs:37): отступ уровня плюс ширина
        /// кнопки разворота, без масштабирования компактным режимом.
        /// </summary>
        private static double EmptyGroupOffsetFor(int level)
            => level * Converters.LevelToThicknessConverter.IndentStep
                + Converters.LevelToThicknessConverter.ExpanderWidth;

        /// <summary>
        /// Ширина колонки звезды «избранное» в заголовке и в строке базы.
        /// Компактным режимом ведущие колонки не сжимаются: у автора их ширины
        /// заданы числом, а компактный режим меняет только отступы и шрифты.
        /// 40, а не 28: содержимое кнопки звезды — иконка (14), отступ (2) и
        /// плашка слота Alt+N (минимум 13 + отступы 6), то есть ~35–39 точек.
        /// При ширине 28 плашка вылезала в колонку булавки и накладывалась
        /// на её значок (issue #300).
        /// </summary>
        private static double FavoriteColumnWidth => 40;

        /// <summary>
        /// Ведущая колонка на месте прежних кнопок групп во всех трёх сетках
        /// списка. Сами кнопки живут в панели команд, а колонка осталась
        /// небольшим отступом выравнивания и обнуляется вместе с ними
        /// (MainWindow.xaml:651 и 1026).
        /// </summary>
        private double GroupButtonsColumnWidth
            => (_vm?.ShowExpandCollapseButtons ?? true) ? 24 : 0;

        /// <summary>Ширина колонки булавки «закреплено» в заголовке и в строке базы.</summary>
        private static double PinColumnWidth => 26;

        /// <summary>
        /// Ширина фиксированной колонки «Действия» в заголовке и в строке базы.
        /// Компактным режимом не сжимается: остальные колонки списка тоже берут
        /// ширину из настроек как есть (MainWindow.xaml:529). Issue #121 (v1.0.1.54):
        /// сохранённая ширина клампится к минимуму колонки, по умолчанию 120 вместо
        /// прежних 170 — колонку можно сузить под три круглые кнопки-чипа.
        /// </summary>
        private double ActionsColumnWidth
            => _vm is { ActionsColumnWidth: > 0 } vm
                ? Math.Max(Models.AppSettings.ActionsColumnMinWidth, vm.ActionsColumnWidth)
                : 120;

        /// <summary>
        /// Номер колонки с именем: место кнопок групп, компенсатор, звезда, булавка.
        /// Одинаков у заголовка и у строк, потому что набор ведущих колонок общий.
        /// </summary>
        private const int NameHeaderColumn = 4;

        /// <summary>Номер колонки строки с именем базы, он же номер колонки заголовка.</summary>
        private const int NameRowColumn = NameHeaderColumn;

        /// <summary>
        /// Имя ведущего блока строки базы: по нему контейнер дерева находит панель,
        /// чтобы поставить ей отступ вложенности. Сдвигается только она, а сама
        /// строка стоит от левого края, иначе значения уехали бы от заголовков.
        /// </summary>
        internal const string LeadBlockName = "ВедущийБлокСтроки";

        /// <summary>Имя сетки заголовка группы: по нему она находится при перетаскивании разделителя колонок.</summary>
        private const string GroupRowGridName = "СеткаСтрокиГруппы";

        /// <summary>Минимальная ширина колонки при перетаскивании разделителя.</summary>
        private const double MinColumnWidth = 40;

        /// <summary>
        /// Минимальная ширина колонки «Действия» при перетаскивании разделителя (issue #121,
        /// v1.0.1.54): под три круглые кнопки-чипа — общий минимум
        /// <see cref="Models.AppSettings.ActionsColumnMinWidth"/>, тот же, что в WPF и при
        /// клампе сохранённой ширины. Раньше 120 не давали колонке сузиться. Под общий
        /// предел в 40 точек колонка не опускается: в него три кнопки не помещаются, и
        /// часть действий стала бы недоступна.
        /// </summary>
        private static double ActionsColumnMinWidth => Models.AppSettings.ActionsColumnMinWidth;

        /// <summary>Ширина зоны захвата разделителя колонок.</summary>
        private const double ResizeGripWidth = 8;

        /// <summary>
        /// Ширина колонки имени: пока её не тянули за разделитель, колонка
        /// звёздная и занимает остаток, после перетаскивания становится заданной.
        /// </summary>
        private GridLength NameColumnLength()
        {
            var width = _vm?.NameColumnWidth ?? 0;
            return width > 0 ? new GridLength(width) : new GridLength(1, GridUnitType.Star);
        }

        /// <summary>
        /// Колонки списка в порядке отображения, кроме первой (имя базы),
        /// которая занимает оставшееся место. Состав и ширины берутся
        /// из настроек, поэтому заголовок и строки всегда согласованы.
        /// </summary>
        private List<ListColumn> ListColumns()
        {
            var columns = new List<ListColumn>();
            if (_vm is null)
                return columns;

            // Ширина из настроек, а при нуле (настройка ещё не трогалась) запасная
            // из разметки: там она задана параметром конвертера (MainWindow.xaml:512-569).
            void Add(bool visible, string key, string header, double width, double fallback)
            {
                if (visible)
                    columns.Add(new ListColumn(key, LocalizationManager.T(header), width > 0 ? width : fallback));
            }

            // Порядок колонок берётся из настроек; неизвестные ключи пропускаются,
            // поэтому пользовательский список не ломает сборку при изменении состава.
            foreach (var key in _vm.ColumnOrderKeys)
            {
                switch (key)
                {
                    case "Version":
                        Add(_vm.ShowVersionColumn, "Version", "Column.Version", _vm.VersionColumnWidth, 120);
                        break;
                    case "Configuration":
                        Add(_vm.ShowConfigurationColumn, "Configuration", "Column.Configuration", _vm.ConfigurationColumnWidth, 160);
                        break;
                    case "ConfigurationVersion":
                        Add(_vm.ShowConfigurationVersionColumn, "ConfigurationVersion", "Column.ConfigurationVersion", _vm.ConfigurationVersionColumnWidth, 80);
                        break;
                    case "LaunchMode":
                        Add(_vm.ShowLaunchModeColumn, "LaunchMode", "Column.LaunchMode", _vm.LaunchModeColumnWidth, 120);
                        break;
                    case "ServerBase":
                        Add(_vm.ShowServerColumn, "ServerBase", "Column.ServerBase", _vm.ServerColumnWidth, 200);
                        break;
                    case "LastLaunch":
                        Add(_vm.ShowLastLaunchColumn, "LastLaunch", "Column.LastLaunch", _vm.LastLaunchColumnWidth, 140);
                        break;
                    case "Size":
                        Add(_vm.ShowSizeColumn, "Size", "Column.Size", _vm.SizeColumnWidth, 90);
                        break;
                    case "Modified":
                        Add(_vm.ShowModifiedColumn, "Modified", "Column.Modified", _vm.ModifiedColumnWidth, 130);
                        break;
                    case "LastBackup":
                        Add(_vm.ShowLastBackupColumn, "LastBackup", "Column.LastBackup", _vm.LastBackupColumnWidth, 130);
                        break;
                }
            }
            return columns;
        }

        /// <summary>
        /// Сколько колонок данных стоит до колонки «Действия». Колонка участвует
        /// в пользовательском порядке наравне с остальными, как в разметке после
        /// правки автора (MainWindow.Columns.cs, задача апстрима 103: перенос
        /// колонки в настройках не давал никакого эффекта). Если ключа
        /// «Действия» в сохранённом порядке нет, она встаёт сразу после режима
        /// запуска, как было раньше.
        /// </summary>
        private int ActionsOffsetInColumns(List<ListColumn> columns)
        {
            var order = _vm?.ColumnOrderKeys;
            if (order is not null)
            {
                var actionsAt = -1;
                for (var i = 0; i < order.Count; i++)
                    if (order[i] == "Actions")
                    {
                        actionsAt = i;
                        break;
                    }

                if (actionsAt >= 0)
                {
                    // Считаем только те колонки порядка, которые сейчас видимы:
                    // скрытая колонка места не занимает.
                    var before = 0;
                    for (var i = 0; i < actionsAt; i++)
                        for (var c = 0; c < columns.Count; c++)
                            if (columns[c].Key == order[i])
                            {
                                before++;
                                break;
                            }
                    return before;
                }
            }

            for (var i = 0; i < columns.Count; i++)
                if (columns[i].Key == "LaunchMode")
                    return i + 1;
            return columns.Count;
        }

        /// <summary>Значение колонки для конкретной базы.</summary>
        private static string ColumnValue(Infobase ib, string key) => key switch
        {
            // (MainWindow.xaml:1249): свойство PlatformVersionDisplay автор
            // добавил в модель, а колонка брала голую версию.
            "Version" => ib.PlatformVersionDisplay ?? string.Empty,
            // Display-свойства учитывают временную индикацию обновления (issue #244).
            "Configuration" => ib.ConfigurationNameDisplay ?? string.Empty,
            "ConfigurationVersion" => ib.ConfigurationVersionDisplay ?? string.Empty,
            // Режим запуска показывается разобранным, а серверная колонка всегда
            // берёт ServerDatabaseDisplay, в том числе у веб-баз: подстановка WebUrl
            // была расхождением с разметкой (MainWindow.xaml:1261 и 1265).
            "LaunchMode" => ib.ParsedLaunchMode ?? string.Empty,
            "ServerBase" => ib.ServerDatabaseDisplay ?? string.Empty,
            "LastLaunch" => ib.LastLaunchDisplay ?? string.Empty,
            "Size" => ib.FileSizeDisplay ?? string.Empty,
            "Modified" => ib.LastModifiedDisplay ?? string.Empty,
            "LastBackup" => ib.LastBackupDisplay ?? string.Empty,
            _ => string.Empty
        };

        /// <summary>
        /// Строка заголовков колонок над списком. Пересобирается вместе
        /// со списком, чтобы состав колонок совпадал со строками.
        /// </summary>
        private Control BuildColumnHeader()
        {
            _columnHeaderRow = new Grid();
            _columnHeader = new Border
            {
                // Имя как у шапки списка в разметке WPF: по нему ThemeManager
                // применяет шрифт области «Шапка списка».
                Name = "HeaderGrid",
                // Отступ 0,2 из разметки (MainWindow.xaml:484). Горизонтальный
                // обязан совпадать с отступом строки, иначе заголовки разъедутся
                // со значениями; в разметке он нулевой в обоих местах.
                Padding = new Thickness(0, 2),
                // Высоту шапке задавали кнопки блока групп; после их переноса
                // в панель команд её держит минимум из разметки
                // (MainWindow.xaml:639: MinHeight 36 и прозрачность 0.95).
                MinHeight = 36,
                Opacity = 0.95,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = _columnHeaderRow
            };
            ThemeBrushes.Bind(_columnHeader, Border.BorderBrushProperty, "BorderColorBrush");
            return _columnHeader;
        }

        /// <summary>
        /// Подсказка, описывающая колонку списка. Есть не у всех: набор взят
        /// из разметки WPF, где такие подсказки стоят только у части заголовков.
        /// </summary>
        private static string? ColumnHeaderTooltipKey(string columnKey) => columnKey switch
        {
            "Size" => "Main.ColumnSizeTooltip",
            "Configuration" => "Main.ColumnNameTooltip",
            _ => null
        };

        /// <summary>
        /// Ставит пересборку заголовка в очередь диспетчера. Настройки колонок
        /// уведомляют о шестнадцати свойствах подряд, и без склейки заголовок
        /// пересобирался бы на каждое из них.
        /// </summary>
        private void QueueColumnHeaderRefresh()
        {
            if (_columnHeaderRefreshQueued)
                return;
            _columnHeaderRefreshQueued = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _columnHeaderRefreshQueued = false;
                RefreshColumnHeader();
            });
        }

        /// <summary>
        /// Пересобирает панель инструментов и заголовки колонок по текущим настройкам.
        /// Слева направо: кнопки групп, компенсатор отступа дерева, звезда, булавка,
        /// имя базы, дальше колонки значений. Первые колонки повторяются в строке
        /// базы теми же ширинами, поэтому заголовок и значения стоят друг под другом.
        /// </summary>
        private void RefreshColumnHeader()
        {
            if (_vm is null || _columnHeaderRow is null || _columnHeader is null)
                return;

            _columnHeaderRow.Children.Clear();
            _columnHeaderRow.ColumnDefinitions.Clear();

            var columns = ListColumns();

            // Колонки заголовка строятся тем же построителем, что и колонки строк:
            // набор ведущих колонок обязан совпадать, иначе значения разъезжаются
            // с заголовками (MainWindow.xaml:1071-1076).
            _headerOffsetColumn = new ColumnDefinition { Width = new GridLength(0) };
            var actionsOffset = ActionsOffsetInColumns(columns);
            AddListColumns(_columnHeaderRow, _vm.ShowFavoritesButton, _vm.ShowPinnedButton, _headerOffsetColumn);


            var nameHeader = ColumnHeader(LocalizationManager.T("Column.Name"), IconHelper.ColumnIconKey("Name"));
            // У «Названия» отступ слева нулевой: заголовок равняется по тексту строк
            // списка, а не по границе колонки. В разметке WPF так же (MainWindow.xaml:739).
            nameHeader.Margin = new Thickness(0, 0, 8, 4);
            MakeSortableHeader(nameHeader, "Name", LocalizationManager.T("Main.ColumnNameSortTooltip"));
            _columnHeaderRow.Children.Add(nameHeader);
            // Подпись охватывает ведущие колонки и колонку имени и прижата влево,
            // как в разметке после переноса кнопок в панель команд
            // (MainWindow.xaml:739, Grid.Column=0 и ColumnSpan=5): ведущие колонки
            // стоят пустыми ради выравнивания значений, а подпись начинается
            // у левого края шапки, а не за ними.
            Grid.SetColumn(nameHeader, 0);
            Grid.SetColumnSpan(nameHeader, NameHeaderColumn + 1);
            nameHeader.HorizontalAlignment = HorizontalAlignment.Left;

            _headerColumnIndex.Clear();
            _headerColumnIndex["Name"] = NameHeaderColumn;

            var nameGrip = BuildResizeGrip("Name", NameHeaderColumn);
            _columnHeaderRow.Children.Add(nameGrip);

            // Заголовки идут по порядку колонок данных, перескакивая колонку
            // «Действия», встроенную после «Режима запуска».
            var dataColumn = NameHeaderColumn + 1;
            for (var i = 0; i < columns.Count; i++)
            {
                if (i == actionsOffset)
                    dataColumn++;
                _headerColumnIndex[columns[i].Key] = dataColumn;

                var text = ColumnHeader(columns[i].Header, IconHelper.ColumnIconKey(columns[i].Key));
                if (columns[i].Key == "LastLaunch")
                    MakeSortableHeader(text, "LastLaunchDate", LocalizationManager.T("Main.ColumnLastLaunchSortTooltip"));
                // Подсказка, описывающая саму колонку. В разметке WPF она есть
                // не у всех заголовков, набор взят оттуда (MainWindow.xaml:687, 697).
                if (ColumnHeaderTooltipKey(columns[i].Key) is { } tooltipKey)
                    ToolTip.SetTip(text, LocalizationManager.T(tooltipKey));
                _columnHeaderRow.Children.Add(text);
                Grid.SetColumn(text, dataColumn);
                AttachColumnContextMenu(text, columns[i].Key);

                var grip = BuildResizeGrip(columns[i].Key, dataColumn);
                _columnHeaderRow.Children.Add(grip);
                dataColumn++;
            }

            // Подпись колонки «Действия» — сразу после колонки «Режим запуска».
            // Разделитель у неё есть и в разметке (ActionsSplitter,
            // MainWindow.xaml:745), поэтому ширина тянется и сохраняется.
            // Скрытая колонка остаётся нулевой ширины (AddListColumns), поэтому
            // заголовок с разделителем не строится вовсе (issue #158).
            if (_vm.ShowActionsColumn)
            {
                var actionsColumn = NameHeaderColumn + 1 + actionsOffset;
                _headerColumnIndex["Actions"] = actionsColumn;
                var actionsHeader = ColumnHeader(LocalizationManager.T("Column.Actions"), IconHelper.ColumnIconKey("Actions"));
                ToolTip.SetTip(actionsHeader, LocalizationManager.T("Main.Actions"));
                _columnHeaderRow.Children.Add(actionsHeader);
                Grid.SetColumn(actionsHeader, actionsColumn);
                AttachColumnContextMenu(actionsHeader, "Actions");
                _columnHeaderRow.Children.Add(BuildResizeGrip("Actions", actionsColumn));
            }

            UpdateListMinWidth();

            QueueHeaderAlign();
        }

        /// <summary>
        /// Прикрепляет к заголовку колонки контекстное меню (issue #173): пункт
        /// «Скрыть колонку» скрывает колонку по её ключу, пункт «Открыть настройки
        /// колонок» открывает окно настроек сразу на подвкладке «Колонки».
        /// </summary>
        private void AttachColumnContextMenu(Control header, string key)
        {
            var hide = new MenuItem { Header = LocalizationManager.T("Column.HideColumn") };
            hide.Click += (_, _) => _vm?.SetColumnVisible(key, false);
            var open = new MenuItem { Header = LocalizationManager.T("Settings.Columns.OpenSettings") };
            open.Click += (_, _) => OpenSettingsOnColumnsTab();
            var menu = new ContextMenu();
            menu.Items.Add(hide);
            menu.Items.Add(open);
            header.ContextMenu = menu;
        }

        /// <summary>Открывает окно настроек сразу на подвкладке «Колонки» (issue #173).</summary>
        private void OpenSettingsOnColumnsTab()
        {
            if (_vm is null)
                return;
            var settings = new Workspace1C.SettingsWindow(_vm);
            settings.SelectColumnsTab();
            settings.ShowDialog(this);
        }

        /// <summary>Открывает окно настроек сразу на вкладке «Меню „Утилиты“» (issue #374).</summary>
        private void OpenSettingsOnUtilitiesMenuTab()
        {
            if (_vm is null)
                return;
            var settings = new Workspace1C.SettingsWindow(_vm);
            settings.SelectUtilitiesMenuTab();
            settings.ShowDialog(this);
        }

        /// <summary>Открывает редактор структуры меню «Утилиты» напрямую (issue #374):
        /// по ПКМ на кнопке «Утилиты» — минуя окно настроек и контекстное меню.</summary>
        private void OpenUtilitiesMenuEditorDirectly()
        {
            try
            {
                var store = AppServices.TryGetService<IUtilitiesMenuLayoutStore>();
                var win = new UtilitiesMenuEditorWindow(store);
                win.ShowDialog(this);
            }
            catch
            {
                // Не критично: редактор не открылся — настройка останется прежней.
            }
        }

        /// <summary>
        /// Блок кнопок над списком: развернуть и свернуть все группы и две
        /// сортировки групп (только при группировке), а также переключатель
        /// тегов в строках, который нужен всегда.
        /// </summary>
        private Control BuildGroupToolbar()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            if (_vm?.ShowExpandCollapseButtons == true)
            {
                // Здесь у автора значки из пакета (PackIcon Kind="ExpandAll"
                // и "CollapseAll", MainWindow.xaml:586 и 593), а не одноимённые
                // ключи его словаря: те шевроны и живут в других местах.
                panel.Children.Add(HeaderIconButton("IconExpandAllBox",
                    LocalizationManager.T("Main.ExpandAllGroups"), "ExpandAllGroupsCommand"));
                panel.Children.Add(HeaderIconButton("IconCollapseAllBox",
                    LocalizationManager.T("Main.CollapseAllGroups"), "CollapseAllGroupsCommand"));
                panel.Children.Add(HeaderIconButton("IconSortAscending",
                    LocalizationManager.T("Main.SortGroupsAscending"), "SortGroupsAscendingCommand"));
                panel.Children.Add(HeaderIconButton("IconSortDescending",
                    LocalizationManager.T("Main.SortGroupsDescending"), "SortGroupsDescendingCommand"));
            }

            var tagsToggle = BuildTagsInListToggle();
            panel.Children.Add(tagsToggle);

            return panel;
        }

        /// <summary>
        /// Переключатель показа тегов в строках списка. Сделан тем же
        /// сегментным контролом, что и переключатели верхней панели: у Fluent
        /// в нажатом состоянии свой синий фон, чужой для этой темы.
        /// </summary>
        private Control BuildTagsInListToggle()
        {
            var toggle = MakeSegmentToggle("IconTag", LocalizationManager.T("Main.ToggleListTags"), iconSize: 14);
            toggle.IsChecked = _vm?.ShowTags ?? false;
            toggle.VerticalAlignment = VerticalAlignment.Center;
            // Отступ у него слева, а не справа, как у остальных сегментов
            // (MainWindow.xaml:526).
            toggle.Margin = new Thickness(2, 0, 0, 0);
            toggle.Click += (_, _) =>
            {
                if (_vm is not null)
                    _vm.ShowTags = toggle.IsChecked == true;
            };
            return toggle;
        }

        /// <summary>Компактная иконко-кнопка панели инструментов над списком.</summary>
        private Button HeaderIconButton(string iconKey, string tooltip, string commandPath)
        {
            // Оформление берёт тема IconButton разметки (LightTheme.xaml:561):
            // прозрачный фон, скругление 8, подсветка при наведении, отступ 8.
            var button = new Button
            {
                // Значок 18, как у этих же кнопок в панели команд разметки
                // (MainWindow.xaml:499, 506, 513, 520).
                Content = IconHelper.MakeIcon(iconKey, UiMetrics.Scaled(18), "TextSecondaryBrush"),
                Padding = new Thickness(UiMetrics.Scaled(8)),
                MinWidth = 0,
                MinHeight = 0,
                VerticalAlignment = VerticalAlignment.Center
            };
            button.Styled(Themes.ControlThemes.IconButton);
            ToolTip.SetTip(button, tooltip);
            button.Bind(Button.CommandProperty, new Binding(commandPath));
            return button;
        }

        /// <summary>
        /// Зона захвата у правого края колонки заголовка: тонкая линия по центру
        /// и широкая невидимая полоса вокруг неё, иначе в разделитель трудно попасть.
        /// </summary>
        private Border BuildResizeGrip(string key, int column)
        {
            var line = new Border
            {
                Width = 1,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 4),
                Opacity = 0.55
            };
            ThemeBrushes.Bind(line, Border.BackgroundProperty, "BorderColorBrush");

            var grip = new Border
            {
                Width = ResizeGripWidth,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = Brushes.Transparent,
                ZIndex = 2,
                Cursor = new Cursor(StandardCursorType.SizeWestEast),
                Tag = key,
                Child = line
            };
            ToolTip.SetTip(grip, LocalizationManager.T("Main.ResizeColumnTooltip"));
            Grid.SetColumn(grip, column);
            grip.PointerPressed += OnColumnResizePressed;
            grip.PointerMoved += OnColumnResizeMoved;
            grip.PointerReleased += OnColumnResizeReleased;
            // Захват теряется не только отпусканием кнопки: его снимает и оконная
            // система, и пересборка окна в компактном режиме. Без этого обработчика
            // перетаскивание осталось бы незавершённым, а ширина несохранённой.
            grip.PointerCaptureLost += OnColumnResizeCaptureLost;
            return grip;
        }

        private void OnColumnResizePressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Border grip || grip.Tag is not string key || _columnHeaderRow is null)
                return;
            if (_resizeKey is not null)
                return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;

            var column = Grid.GetColumn(grip);
            if (column < 0 || column >= _columnHeaderRow.ColumnDefinitions.Count)
                return;

            _resizeKey = key;
            _resizePointerId = e.Pointer.Id;
            _resizeStartWidth = _columnHeaderRow.ColumnDefinitions[column].ActualWidth;
            _resizeStartX = e.GetPosition(this).X;

            // Сетки строк собираются один раз на перетаскивание: во время него
            // дерево не пересобирается, а обход визуального дерева на каждое
            // движение указателя стоил бы дорого на списке в сотни баз.
            _resizeRowGrids.Clear();
            if (_tree is not null)
            {
                foreach (var card in _tree.GetVisualDescendants().OfType<InfobaseRowCard>())
                {
                    if (card.Child is Grid grid)
                        _resizeRowGrids.Add(grid);
                }
                // Заголовки групп ведут те же колонки и обязаны ехать вместе
                // со строками баз, иначе кнопки группы расходятся с кнопками
                // строк на всё время перетаскивания.
                foreach (var group in _tree.GetVisualDescendants().OfType<Grid>())
                {
                    if (group.Name == GroupRowGridName)
                        _resizeRowGrids.Add(group);
                }
            }

            e.Pointer.Capture(grip);
            e.Handled = true;
        }

        private void OnColumnResizeMoved(object? sender, PointerEventArgs e)
        {
            if (_resizeKey is null || e.Pointer.Id != _resizePointerId)
                return;
            if (sender is not Border grip || !ReferenceEquals(e.Pointer.Captured, grip))
                return;

            var minWidth = _resizeKey == "Actions" ? ActionsColumnMinWidth : MinColumnWidth;
            var width = Math.Max(minWidth, _resizeStartWidth + e.GetPosition(this).X - _resizeStartX);
            ApplyColumnWidth(_resizeKey, width);
            _vm?.UpdateColumnWidth(_resizeKey, width, save: false);
        }

        private void OnColumnResizeReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_resizeKey is null || e.Pointer.Id != _resizePointerId)
                return;

            e.Pointer.Capture(null);
            FinishColumnResize();
            e.Handled = true;
        }

        private void OnColumnResizeCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (_resizeKey is null || e.Pointer.Id != _resizePointerId)
                return;

            FinishColumnResize();
        }

        /// <summary>
        /// Завершает перетаскивание: пишет ширину в настройки один раз, а не
        /// на каждое движение указателя, и отпускает собранные сетки строк.
        /// </summary>
        private void FinishColumnResize()
        {
            if (_resizeKey is not null)
                _vm?.UpdateColumnWidth(_resizeKey, ColumnWidthOf(_resizeKey), save: true);

            _resizeKey = null;
            _resizeRowGrids.Clear();
        }

        /// <summary>Текущая ширина колонки заголовка по её ключу.</summary>
        private double ColumnWidthOf(string key)
        {
            var index = HeaderColumnIndex(key);
            return index >= 0 && _columnHeaderRow is not null && index < _columnHeaderRow.ColumnDefinitions.Count
                ? _columnHeaderRow.ColumnDefinitions[index].ActualWidth
                : 0;
        }

        /// <summary>Номер колонки заголовка по ключу колонки списка.</summary>
        private int HeaderColumnIndex(string key) =>
            _headerColumnIndex.TryGetValue(key, out var index) ? index : -1;

        /// <summary>
        /// Ведёт ширину колонки в двух сетках сразу: в заголовке и в каждой
        /// построенной строке. Пересборки дерева при этом не происходит, поэтому
        /// перетаскивание не мигает списком.
        /// </summary>
        private void ApplyColumnWidth(string key, double width)
        {
            var header = HeaderColumnIndex(key);
            if (header < 0 || _columnHeaderRow is null || header >= _columnHeaderRow.ColumnDefinitions.Count)
                return;

            _columnHeaderRow.ColumnDefinitions[header].Width = new GridLength(width);

            var row = header - (NameHeaderColumn - NameRowColumn);
            foreach (var grid in _resizeRowGrids)
            {
                if (row < 0 || row >= grid.ColumnDefinitions.Count)
                    continue;
                grid.ColumnDefinitions[row].Width = new GridLength(width);
            }

            // Минимум области пересчитывается отложенно (дебаунс): во время
            // перетаскивания PointerMoved приходит часто, а полный пересчёт на каждый
            // шаг дёргал бы раскладку. Полоса прокрутки обновляется живьём — один
            // вызов в очереди диспетчера собирает все движения кадра (issue #309).
            QueueListMinWidthRefresh();
            // И заново выравнивается шапка: новая ширина колонки меняет и общую
            // ширину сеток, от равенства которой зависит совпадение колонок.
            QueueHeaderAlign();
        }

        /// <summary>
        /// Ставит пересчёт минимальной ширины списка в очередь диспетчера (дебаунс,
        /// issue #309): собирает частые движения указателя при перетаскивании
        /// разделителя колонок в один пересчёт на кадр (Background). Полоса
        /// горизонтальной прокрутки расширяется/сужается ЖИВЬЁМ, пока колонку тянут
        /// за край видимой области, а не только после отпускания кнопки.
        /// </summary>
        private void QueueListMinWidthRefresh()
        {
            if (_listMinWidthQueued)
                return;
            _listMinWidthQueued = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _listMinWidthQueued = false;
                UpdateListMinWidth();
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// Минимальная ширина области списка: сумма колонок заголовка плюс отступы.
        /// При более узком окне включается горизонтальная прокрутка, и заголовок
        /// едет вместе со строками, а не разъезжается с ними.
        /// </summary>
        private void UpdateListMinWidth()
        {
            if (_listContent is null || _columnHeaderRow is null
                || _columnHeaderRow.ColumnDefinitions.Count <= NameHeaderColumn)
                return;

            var definitions = _columnHeaderRow.ColumnDefinitions;

            // Ведущие колонки, включая нулевую с местом под кнопки групп: без неё
            // минимум занижался, и правые колонки подрезались раньше, чем включалась
            // горизонтальная прокрутка.
            var leading = new List<ListMinWidthCalculator.Column>(NameHeaderColumn);
            for (var i = 0; i < NameHeaderColumn; i++)
                leading.Add(ColumnSpec(definitions[i]));

            var nameDef = definitions[NameHeaderColumn];
            var nameWidth = nameDef.Width.IsAbsolute ? nameDef.Width.Value : 0;

            // Колонки значений вместе с «Действиями»: они лежат в тех же определениях,
            // что и заголовок, поэтому состав суммы всегда совпадает с фактическим
            // набором колонок (включая добавленные позже — issue #309).
            var values = new List<ListMinWidthCalculator.Column>(definitions.Count - NameHeaderColumn - 1);
            for (var i = NameHeaderColumn + 1; i < definitions.Count; i++)
                values.Add(ColumnSpec(definitions[i]));

            _listMinWidth = ListMinWidthCalculator.Compute(
                nameWidth, NameColumnMinWidth, leading, UiMetrics.PaddingControl, values);

            // MinWidth держит жёсткую границу при конечном измерении (сужение окна),
            // а явная ширина — при измерении контента от ScrollViewer с бесконечной
            // шириной, где MinWidth в Avalonia не увеличивает DesiredSize, и extent
            // внешней полосы не дотягивал бы до последней колонки (issue #309).
            _listContent.MinWidth = _listMinWidth;
            SyncListWidthToViewport();
        }

        /// <summary>
        /// Спецификация колонки для расчёта минимума: скрытые и звёздные колонки
        /// места не занимают (звёздная «Название» отдельно берётся по минимуму).
        /// </summary>
        private static ListMinWidthCalculator.Column ColumnSpec(ColumnDefinition d)
            => new(
                d.Width.IsAbsolute ? d.Width.Value : 0,
                d.Width.IsAbsolute && d.Width.Value > 0);

        /// <summary>
        /// Приравнивает ширину контента списка максимуму из суммы видимых колонок
        /// и вьюпорта внешней прокрутки. Внешний ScrollViewer меряет контент
        /// с бесконечной шириной, где MinWidth не влияет на DesiredSize, поэтому
        /// без явной ширины горизонтальный extent был бы меньше суммы колонок
        /// (issue #309). Тот же приём, что в <see cref="SyncHeaderWidthWithList"/>
        /// для заголовка.
        /// <para>
        /// ВАЖНО: ширина контента считается СТРОГО по сумме видимых колонок — без
        /// «фактической желаемой ширины строк» (Extent внутренней прокрутки дерева
        /// и <see cref="_rowContentWidth"/>). Именно эти слагаемые давали «пустой
        /// хвост» справа от колонок (extent > суммы колонок, регресс issue #309):
        /// звёздная колонка «Название» мерялась по содержимому (длинные имена баз)
        /// и раздвигала контент за последнюю колонку. Последняя колонка при этом
        /// остаётся достижимой: она входит в сумму, а прокрутка ведётся до границы
        /// суммы.
        /// </para>
        /// </summary>
        private void SyncListWidthToViewport(ScrollViewer? inner = null)
        {
            if (_listContent is null || _listScroll is null)
                return;
            var viewport = _listScroll.Viewport.Width;
            if (viewport <= 0)
                return;

            // Явная видимость горизонтальной полосы (issue #21, 1.0.0.62, паритет с WPF):
            // решение только по сумме колонок и вьюпорту, не по числу строк вкладки —
            // пока колонки помещаются, полоса отключена и мигнуть не может.
            var wantedHBar = ListMinWidthCalculator.NeedsHorizontalScroll(_listMinWidth, viewport)
                ? global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                : global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
            if (ScrollViewer.GetHorizontalScrollBarVisibility(_listScroll) != wantedHBar)
                ScrollViewer.SetHorizontalScrollBarVisibility(_listScroll, wantedHBar);

            // Guard «полоса не нужна» (issue #21, паритет с WPF): если фактическая полоса
            // меньше 1px (доли пикселя от округления последней колонки) и расчётная сумма
            // колонок помещается во вьюпорт (с допуском 1px) — схлопываем контент до
            // вьюпорта, полоса исчезает. Условие по _listMinWidth защищает анти-регресс
            // #255: при колонках, реально не помещающихся, полоса нужна и сохраняется.
            var scrollable = _listScroll.Extent.Width - _listScroll.Viewport.Width;
            var target = Math.Max(_listMinWidth, viewport);
            if (scrollable < 1 && _listMinWidth <= viewport + 1)
                target = viewport;

            // При схлопывании разница может быть меньше обычного порога 0.5 (например,
            // 1000.3 → 1000) — записываем принудительно, иначе «зависшая» полоса
            // не исчезла бы (паритет с WPF, issue #21).
            var collapseToViewport = scrollable < 1 && _listMinWidth <= viewport + 1
                && target < _listContent.Width - 0.01;
            if (collapseToViewport || Math.Abs(_listContent.Width - target) > 0.5)
                _listContent.Width = target;
        }

        /// <summary>Делает заголовок колонки кликабельным: клик меняет поле сортировки.</summary>
        private void MakeSortableHeader(Control header, string field, string tooltip)
        {
            header.Cursor = new Cursor(StandardCursorType.Hand);
            ToolTip.SetTip(header, tooltip);
            header.Tapped += (_, _) => _vm?.SetSortField(field);
        }

        /// <summary>
        /// Ставит выравнивание заголовка со строками в очередь диспетчера:
        /// положение строки известно только после раскладки.
        /// </summary>
        private void QueueHeaderAlign()
        {
            if (_headerAlignQueued)
                return;
            _headerAlignQueued = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _headerAlignQueued = false;
                AlignHeaderToRows();
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Подгоняет ширину колонки-компенсатора так, чтобы звезда и булавка
        /// заголовка встали над теми же значками первой строки базы. Дерево
        /// сдвигает строки на отступ уровня, и без компенсации заголовок
        /// разошёлся бы со списком.
        /// </summary>
        private void AlignHeaderToRows()
        {
            if (_headerOffsetColumn is null || _columnHeaderRow is null || _tree is null
                || _columnHeaderRow.ColumnDefinitions.Count <= NameHeaderColumn)
                return;

            // Арифметика авторская (MainWindow.Columns.cs:265): компенсатор равен
            // разнице между началом первой колонки значений строки и началом той же
            // колонки заголовка, посчитанным без самого компенсатора. Звёздная колонка
            // имени исключена из обеих сумм ведущих колонок (issue #191): иначе компенсатор
            // зависит от собственного прошлого значения и ширина списка растёт до десятков
            // тысяч точек, из-за чего колонки, кроме «Названия», уезжают за правый край.
            // Ведущие колонки у заголовка и строки одинаковы, а общая ширина общая, поэтому
            // после совмещения имя занимает одинаковое место в обеих сетках, и разницу даёт
            // только сдвиг строки деревом, после чего значения стоят ровно под заголовками.
            Grid? rowGrid = null;
            double rowOrigin = 0;
            // Первая реализованная карточка строки базы (аналог FindFirstInfobaseItem
            // в WPF): обход останавливается на первом валидном контейнере, а не проходит
            // всё визуальное дерево (десятки тысяч узлов на списке в сотни баз — источник
            // «паузы» при каждом выравнивании после переключения вкладки, issue #21, Этап B).
            foreach (var card in _tree.GetVisualDescendants().OfType<InfobaseRowCard>())
            {
                if (card.Child is not Grid content)
                    continue;
                var origin = content.TranslatePoint(new Point(0, 0), _columnHeaderRow);
                if (origin is null)
                    continue;
                rowGrid = content;
                rowOrigin = origin.Value.X;
                break;
            }

            double offset = 0;
            if (rowGrid is not null && rowGrid.ColumnDefinitions.Count > NameRowColumn)
            {
                // Замер фактической желаемой ширины первой строки больше не выполняется:
                // ширина контента списка считается СТРОГО по сумме видимых колонок
                // (SyncListWidthToViewport, issue #309), и дорогой Measure(Infinity) давал
                // лишь «пустой хвост» справа от колонок.
                double rowLead = 0;
                for (var i = 0; i < NameRowColumn; i++)
                    rowLead += rowGrid.ColumnDefinitions[i].ActualWidth;

                double headerLead = 0;
                for (var i = 0; i < NameHeaderColumn; i++)
                {
                    if (!ReferenceEquals(_columnHeaderRow.ColumnDefinitions[i], _headerOffsetColumn))
                        headerLead += _columnHeaderRow.ColumnDefinitions[i].ActualWidth;
                }

                offset = Math.Max(0, (rowOrigin + rowLead) - headerLead);
            }

            if (Math.Abs(offset - _headerOffsetColumn.Width.Value) > 0.5)
                _headerOffsetColumn.Width = new GridLength(offset);

            SyncHeaderWidthWithList();
        }

        /// <summary>
        /// Приравнивает ширину сетки заголовка ширине содержимого списка
        /// (MainWindow.Columns.cs:299). Колонка «Название» звёздная, и лишний
        /// пиксель общей ширины целиком уходит в неё: если заголовок шире строк
        /// на полосу прокрутки, все колонки значений строк оказываются левее
        /// своих заголовков ровно на эту разницу.
        /// </summary>
        private void SyncHeaderWidthWithList()
        {
            if (_columnHeaderRow is null || _tree is null)
                return;

            // Как в WPF (MainWindow.Columns.cs, SyncHeaderWidthWithList): синхронизация ширины
            // заголовка держит и минимум контента в актуальном состоянии. Выравнивание шапки
            // пересчитывается на появление строк/изменение размеров области, и если к этому
            // моменту колонки изменились без события модели (загрузка настроек прямым
            // присвоением), без явного пересчёта полоса осталась бы прежней ширины и не
            // доехала бы до последней колонки (issue #309).
            UpdateListMinWidth();

            double extent = _tree.Bounds.Width;
            double viewport = _tree.Bounds.Width;
            if (TreeScroll is { } scroll)
            {
                extent = Math.Max(scroll.Extent.Width, scroll.Viewport.Width);
                viewport = scroll.Viewport.Width;
            }

            var target = Math.Max(extent, viewport);
            if (target > 0 && Math.Abs(_columnHeaderRow.Width - target) > 0.5)
                _columnHeaderRow.Width = target;
        }

        /// <summary>
        /// Заголовок колонки: иконка колонки и подпись. Иконки заголовков совпадают
        /// с иконками списка колонок на вкладке «Отображение» — оба берут ключ из
        /// <see cref="IconHelper.ColumnIconKey"/>.
        /// </summary>
        private static Control ColumnHeader(string text, string iconKey)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                VerticalAlignment = VerticalAlignment.Center,
                // Отступы как в разметке WPF (Margin="6,0,6,4"): без них подпись
                // встаёт вплотную к соседней, а горизонтальный StackPanel меряет
                // детей без ограничения по ширине, поэтому сам текст не подрезается.
                Margin = new Thickness(6, 0, 6, 4),
                ClipToBounds = true
            };
            panel.Children.Add(IconHelper.MakeIcon(iconKey, UiMetrics.Scaled(13), "TextSecondaryBrush"));

            var block = new TextBlock
            {
                Text = text,
                FontSize = UiMetrics.ScaledFont(12),
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            panel.Children.Add(block);
            return panel;
        }

        /// <summary>
        /// Колонки строки списка. Ведущих ровно пять и они одинаковы у заголовка,
        /// строки группы и строки базы: место под кнопки групп, компенсатор отступа
        /// дерева, звезда, булавка, название (MainWindow.xaml:495-512, 893-905
        /// и 1077-1088). Одинаковый набор ведущих колонок и есть причина, по которой
        /// значения строк стоят ровно под своими заголовками: дальше идут колонки
        /// значений с «Действиями» на своём месте.
        /// </summary>
        /// <param name="compensator">
        /// Колонка-компенсатор заголовка: её ширину подбирает <see cref="AlignHeaderToRows"/>.
        /// У строк компенсатор всегда нулевой, поэтому там передаётся null.
        /// </param>
        /// <returns>Индекс колонки «Действия».</returns>
        private int AddListColumns(Grid grid, bool showFavorite, bool showPin,
            ColumnDefinition? compensator = null)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(GroupButtonsColumnWidth) });
            grid.ColumnDefinitions.Add(compensator ?? new ColumnDefinition { Width = new GridLength(0) });
            // Резерв под переключатель тегов снят вместе с переносом кнопок
            // в панель команд: колонка пустая и лишнего отступа слева от
            // «Названия» давать не должна (MainWindow.xaml:656 и 1030).
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(showFavorite ? FavoriteColumnWidth : 0)
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(showPin ? PinColumnWidth : 0)
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = NameColumnLength(),
                MinWidth = MinColumnWidth
            });

            var columns = ListColumns();
            var actionsOffset = ActionsOffsetInColumns(columns);
            // Скрытая колонка «Действия» остаётся в сетке нулевой ширины (issue #158):
            // индексы и выравнивание остальных колонок и заголовка не меняются, а на
            // экране колонка просто не занимает места.
            var actionsWidth = _vm?.ShowActionsColumn != false ? ActionsColumnWidth : 0;
            var actionsIndex = -1;
            for (var i = 0; i < columns.Count; i++)
            {
                if (i == actionsOffset)
                {
                    actionsIndex = grid.ColumnDefinitions.Count;
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(actionsWidth) });
                }
                grid.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = new GridLength(columns[i].Width), MinWidth = MinColumnWidth });
            }
            if (actionsOffset >= columns.Count)
            {
                actionsIndex = grid.ColumnDefinitions.Count;
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(actionsWidth) });
            }
            return actionsIndex;
        }
    }
}
#endif