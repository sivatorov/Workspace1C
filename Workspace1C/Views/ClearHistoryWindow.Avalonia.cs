#if LINUX
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Диалог «Очистка истории запусков» (issue #246), Avalonia/Linux-версия
    /// WPF-окна <see cref="ClearHistoryWindow"/>. Таблица баз с флажками, колонками
    /// имени базы и количества записей истории запусков. Кнопка «Очистить историю»
    /// обнуляет <see cref="Infobase.LaunchHistory"/> отмеченных баз. Правки вносятся
    /// прямо в объекты <see cref="Infobase"/>, поэтому сохранение выполняет вызывающий
    /// код (окно настроек) через персист-метод, если <see cref="DataChanged"/> == true.
    /// </summary>
    public sealed class ClearHistoryWindow : ModalWindowBase
    {
        private readonly List<ClearHistoryRowViewModel> _rows = new();
        private readonly Dictionary<ClearHistoryRowViewModel, CheckBox> _checks = new();
        private readonly Dictionary<ClearHistoryRowViewModel, TextBlock> _countTexts = new();

        private readonly StackPanel _rowsPanel = new() { Margin = new Thickness(4, 2) };
        private readonly TextBlock _checkedHeaderText = new();
        private readonly TextBlock _summaryText = new();
        private Button _clearButton = new();

        /// <param name="infobases">Все информационные базы для очистки истории.</param>
        public ClearHistoryWindow(IReadOnlyList<Infobase> infobases)
        {
            Title = LocalizationManager.T("ClearHistory.Title");
            Width = 620;
            Height = 520;
            MinWidth = 520;
            MinHeight = 400;
            FontSize = 13;
            CanResize = true;

            foreach (var ib in infobases)
                _rows.Add(new ClearHistoryRowViewModel(ib));

            Content = BuildRoot();
            foreach (var row in _rows)
                AddRow(row);
            UpdateCheckedHeader();
            UpdateClearButtonEnabled();
        }

        /// <summary>Признак того, что хотя бы у одной базы история была очищена (для персиста).</summary>
        public bool DataChanged { get; private set; }

        private static string T(string key) => LocalizationManager.T(key);

        private static TextBlock MakeHeaderText(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
        }

        private static void BindSecondary(TextBlock block)
            => Themes.ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "TextSecondaryBrush");

        /// <summary>Обновляет заголовок колонки-флажка счётчиком отмеченных элементов.</summary>
        private void UpdateCheckedHeader()
        {
            _checkedHeaderText.Text = string.Format(
                LocalizationManager.T("ClearHistory.Column.CheckedHeaderFormat"),
                _rows.Count(r => r.IsChecked));
        }

        /// <summary>Доступность кнопки «Очистить историю»: активна, если отмечена хотя бы одна база.</summary>
        private void UpdateClearButtonEnabled()
        {
            _clearButton.IsEnabled = _rows.Any(r => r.IsChecked);
        }

        private void OnCheckAll()
        {
            foreach (var row in _rows)
                row.IsChecked = true;
            UpdateCheckedHeader();
            UpdateClearButtonEnabled();
        }

        private void OnUncheckAll()
        {
            foreach (var row in _rows)
                row.IsChecked = false;
            UpdateCheckedHeader();
            UpdateClearButtonEnabled();
        }

        private void OnInvert()
        {
            foreach (var row in _rows)
                row.IsChecked = !row.IsChecked;
            UpdateCheckedHeader();
            UpdateClearButtonEnabled();
        }

        /// <summary>Очищает историю запусков отмеченных баз.</summary>
        private void OnClearClick()
        {
            var targets = _rows.Where(r => r.IsChecked).ToList();
            if (targets.Count == 0)
            {
                _summaryText.Text = LocalizationManager.T("ClearHistory.NoneSelected");
                return;
            }

            var cleared = 0;
            foreach (var row in targets)
            {
                row.Infobase.ClearLaunchHistory();
                DataChanged = true;
                cleared++;
                row.SyncFromInfobase();
                UpdateRow(row);
            }

            _summaryText.Text = string.Format(
                LocalizationManager.T("ClearHistory.DoneFormat"), cleared);
        }

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var title = new TextBlock
            {
                Text = LocalizationManager.T("ClearHistory.TitleHeader"),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            var subtitle = new TextBlock
            {
                Text = LocalizationManager.T("ClearHistory.Subtitle"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            };
            BindSecondary(subtitle);
            Grid.SetRow(subtitle, 1);
            grid.Children.Add(subtitle);

            // Список баз
            var listBorder = new Border
            {
                Margin = new Thickness(0, 12, 0, 0),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8)
            };
            Themes.ThemeBrushes.Bind(listBorder, Border.BackgroundProperty, "CardBackgroundColorBrush");
            Themes.ThemeBrushes.Bind(listBorder, Border.BorderBrushProperty, "BorderColorBrush");

            var dock = new DockPanel { LastChildFill = true };

            // Панель кнопок выбора
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var checkAll = new Button { Content = LocalizationManager.T("ClearHistory.Button.CheckAll") };
            checkAll.Styled(ControlThemes.SelectAllButton);
            ToolTip.SetTip(checkAll, LocalizationManager.T("ClearHistory.Button.CheckAllTooltip"));
            checkAll.Click += (_, _) => OnCheckAll();

            var uncheckAll = new Button { Content = LocalizationManager.T("ClearHistory.Button.UncheckAll") };
            uncheckAll.Styled(ControlThemes.SelectAllButton);
            ToolTip.SetTip(uncheckAll, LocalizationManager.T("ClearHistory.Button.UncheckAllTooltip"));
            uncheckAll.Click += (_, _) => OnUncheckAll();

            var invert = new Button { Content = LocalizationManager.T("ClearHistory.Button.Invert") };
            invert.Styled(ControlThemes.SelectAllButton);
            ToolTip.SetTip(invert, LocalizationManager.T("ClearHistory.Button.InvertTooltip"));
            invert.Click += (_, _) => OnInvert();

            toolbar.Children.Add(checkAll);
            toolbar.Children.Add(uncheckAll);
            toolbar.Children.Add(invert);

            var toolbarBorder = new Border
            {
                Padding = new Thickness(8, 4),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = Brushes.Transparent,
                Child = toolbar
            };
            Themes.ThemeBrushes.Bind(toolbarBorder, Border.BorderBrushProperty, "BorderColorBrush");
            DockPanel.SetDock(toolbarBorder, Dock.Top);
            dock.Children.Add(toolbarBorder);

            // Шапка таблицы
            var headerGrid = BuildHeaderGrid();
            DockPanel.SetDock(headerGrid, Dock.Top);
            dock.Children.Add(headerGrid);

            var scroll = new ScrollViewer
            {
                Content = _rowsPanel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(4)
            };
            dock.Children.Add(scroll);

            listBorder.Child = dock;
            Grid.SetRow(listBorder, 2);
            grid.Children.Add(listBorder);

            // Нижняя панель
            var bottom = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            bottom.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            _summaryText.FontSize = 12;
            _summaryText.TextWrapping = TextWrapping.Wrap;
            _summaryText.TextTrimming = TextTrimming.CharacterEllipsis;
            BindSecondary(_summaryText);
            _summaryText.VerticalAlignment = VerticalAlignment.Center;
            _summaryText.Margin = new Thickness(0, 0, 12, 0);
            Grid.SetColumn(_summaryText, 0);
            bottom.Children.Add(_summaryText);

            _clearButton = new Button
            {
                Content = LocalizationManager.T("ClearHistory.Button.Clear"),
                Width = 180,
                Height = 36,
                IsDefault = true
            };
            // Очистка истории — опасное действие: DangerButton (issue #26).
            _clearButton.Styled(ControlThemes.DangerButton);
            _clearButton.Click += (_, _) => OnClearClick();

            var cancel = BuildCancelActionButton(140);
            var rightPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Children = { _clearButton, cancel }
            };
            Grid.SetColumn(rightPanel, 1);
            bottom.Children.Add(rightPanel);

            Grid.SetRow(bottom, 3);
            grid.Children.Add(bottom);

            return grid;
        }

        /// <summary>Строит закреплённую шапку таблицы.</summary>
        private Grid BuildHeaderGrid()
        {
            var grid = new Grid { Margin = new Thickness(8, 0, 8, 2) };
            ApplyColumns(grid);

            _checkedHeaderText.FontSize = 12;
            _checkedHeaderText.FontWeight = FontWeight.SemiBold;
            BindSecondary(_checkedHeaderText);
            Grid.SetColumn(_checkedHeaderText, 0);
            grid.Children.Add(_checkedHeaderText);

            var nameHeader = MakeHeaderText(LocalizationManager.T("ClearHistory.Column.Name"));
            BindSecondary(nameHeader);
            Grid.SetColumn(nameHeader, 1);
            grid.Children.Add(nameHeader);

            var countHeader = MakeHeaderText(LocalizationManager.T("ClearHistory.Column.Count"));
            BindSecondary(countHeader);
            Grid.SetColumn(countHeader, 2);
            grid.Children.Add(countHeader);

            return grid;
        }

        private void ApplyColumns(Grid grid)
        {
            grid.ColumnDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));                       // 0 — флажок
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star))); // 1 — имя
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));                      // 2 — количество
        }

        /// <summary>Строит строку базы и добавляет её в общий список.</summary>
        private void AddRow(ClearHistoryRowViewModel row)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            ApplyColumns(grid);

            var check = new CheckBox
            {
                IsChecked = row.IsChecked,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };
            check.Styled(ControlThemes.CacheCleanCheckBox);
            check.IsCheckedChanged += (_, _) =>
            {
                row.IsChecked = check.IsChecked == true;
                UpdateCheckedHeader();
                UpdateClearButtonEnabled();
            };
            Grid.SetColumn(check, 0);
            grid.Children.Add(check);

            var name = new TextBlock
            {
                Text = row.Name,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);

            var count = new TextBlock
            {
                Text = row.HistoryCount.ToString(),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(count, 2);
            grid.Children.Add(count);

            _checks[row] = check;
            _countTexts[row] = count;
            _rowsPanel.Children.Add(grid);
        }

        /// <summary>Обновляет текстовые ячейки строки из её модели.</summary>
        private void UpdateRow(ClearHistoryRowViewModel row)
        {
            if (_countTexts.TryGetValue(row, out var count))
                count.Text = row.HistoryCount.ToString();
            if (_checks.TryGetValue(row, out var check))
                check.IsChecked = row.IsChecked;
        }
    }
}
#endif