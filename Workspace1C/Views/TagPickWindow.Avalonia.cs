#if LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Workspace1C.Localization;
using Workspace1C.Themes;

namespace Workspace1C
{
    /// <summary>
    /// Диалог назначения тегов для мультивыделения (issue #315, Avalonia/Linux):
    /// список существующих тегов с флажками (мультивыбор) + поле ввода нового тега
    /// с кнопкой «Добавить». Результат — отмеченные теги (включая новые).
    /// Windows-аналог: <see cref="TagPickWindow"/> (WPF).
    /// </summary>
    public sealed class TagPickWindow : ModalWindowBase
    {
        private readonly List<(CheckBox Check, string Name)> _checks = new();
        private readonly StackPanel _tagsPanel = new() { Spacing = 2 };
        private readonly TextBlock _selectedCount = new();
        private readonly TextBox _newTagBox = new TextBox { Width = 180, Padding = new Thickness(8, 6) }
            .Styled(ControlThemes.ModernTextBox);
        private readonly string _selectedFormat = LocalizationManager.T("TagPick.SelectedCount");

        /// <summary>
        /// Создаёт диалог выбора тегов.
        /// </summary>
        /// <param name="availableTags">Существующие теги всех баз (источник списка).</param>
        public TagPickWindow(IEnumerable<string>? availableTags)
        {
            Title = LocalizationManager.T("TagPick.Title");
            Width = 460;
            Height = 440;
            MinWidth = 400;
            MinHeight = 380;
            FontSize = 13;
            CanResize = true;

            Content = BuildRoot();

            var tags = (availableTags ?? Enumerable.Empty<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (tags.Count == 0)
            {
                var empty = new TextBlock
                {
                    Text = LocalizationManager.T("TagPick.NoTags"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(6, 8, 6, 8)
                };
                ThemeBrushes.Bind(empty, TextBlock.ForegroundProperty, "TextSecondaryBrush");
                _tagsPanel.Children.Add(empty);
            }

            foreach (var tag in tags)
                AddCheckbox(tag, isChecked: false);

            UpdateSelectedCount();

            Opened += (_, _) =>
            {
                // Фокус ставим отложенно (после полного показа и активации окна):
                // синхронная установка в Opened слетает до отрисовки модального диалога.
                Dispatcher.UIThread.Post(() =>
                {
                    if (_checks.Count > 0)
                        _checks[0].Check.Focus();
                    else
                        _newTagBox.Focus();
                });
            };
        }

        /// <summary>
        /// Отмеченные теги (в порядке списка); пусто, если пользователь ничего
        /// не выбрал и не добавил. Вызывающий код трактует пустой результат
        /// как отмену (ничего не меняет).
        /// </summary>
        public IReadOnlyList<string> Result { get; private set; } = Array.Empty<string>();

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            // Заголовок списка + счётчик «Выбрано: N»
            var header = new DockPanel();
            var headerText = new TextBlock
            {
                Text = LocalizationManager.T("TagPick.ExistingTags"),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            ThemeBrushes.Bind(headerText, TextBlock.ForegroundProperty, "TextPrimaryBrush");

            _selectedCount.FontSize = 12;
            _selectedCount.VerticalAlignment = VerticalAlignment.Center;
            _selectedCount.Margin = new Thickness(8, 0, 0, 0);
            ThemeBrushes.Bind(_selectedCount, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            DockPanel.SetDock(_selectedCount, Dock.Right);
            header.Children.Add(_selectedCount);
            header.Children.Add(headerText);
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            // Список существующих тегов с флажками
            var listBorder = new Border
            {
                Margin = new Thickness(0, 10, 0, 0),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8)
            };
            ThemeBrushes.Bind(listBorder, Border.BackgroundProperty, "CardBackgroundColorBrush");
            ThemeBrushes.Bind(listBorder, Border.BorderBrushProperty, "BorderColorBrush");

            var scroll = new ScrollViewer
            {
                Content = _tagsPanel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(4)
            };
            listBorder.Child = scroll;
            Grid.SetRow(listBorder, 1);
            grid.Children.Add(listBorder);

            // Поле нового тега + «Добавить»
            var newRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 12, 0, 0),
                Spacing = 8
            };
            var newLabel = new TextBlock
            {
                Text = LocalizationManager.T("TagPick.NewTagLabel"),
                VerticalAlignment = VerticalAlignment.Center
            };
            ThemeBrushes.Bind(newLabel, TextBlock.ForegroundProperty, "TextPrimaryBrush");
            newRow.Children.Add(newLabel);

            _newTagBox.KeyDown += OnNewTagBox_KeyDown;
            ToolTip.SetTip(_newTagBox, LocalizationManager.T("TagPick.NewTagHint"));
            newRow.Children.Add(_newTagBox);

            var addButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = "＋", VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = LocalizationManager.T("TagPick.AddNewTag"), VerticalAlignment = VerticalAlignment.Center }
                    }
                },
                Padding = new Thickness(14, 6)
            };
            addButton.Styled(ControlThemes.ModernButton);
            addButton.Click += (_, _) => AddNewTag();
            newRow.Children.Add(addButton);
            Grid.SetRow(newRow, 2);
            grid.Children.Add(newRow);

            // Панель кнопок «Отмена»/«ОК»
            var buttons = BuildButtons(onOk: SaveResult);
            buttons.Margin = new Thickness(0, 16, 0, 0);
            Grid.SetRow(buttons, 3);
            grid.Children.Add(buttons);

            return grid;
        }

        private void SaveResult()
        {
            Result = _checks
                .Where(c => c.Check.IsChecked == true)
                .Select(c => c.Name)
                .ToList();
        }

        private CheckBox AddCheckbox(string tag, bool isChecked)
        {
            var check = new CheckBox
            {
                Content = tag,
                IsChecked = isChecked,
                Margin = new Thickness(2, 3, 2, 3),
                Cursor = new Cursor(StandardCursorType.Hand),
                VerticalContentAlignment = VerticalAlignment.Center
            }.Styled(ControlThemes.CacheCleanCheckBox);
            check.IsCheckedChanged += OnSelectionChanged;
            _checks.Add((check, tag));
            _tagsPanel.Children.Add(check);
            return check;
        }

        private void OnSelectionChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
            => UpdateSelectedCount();

        private void UpdateSelectedCount()
        {
            _selectedCount.Text = string.Format(
                _selectedFormat,
                _checks.Count(c => c.Check.IsChecked == true));
        }

        private void OnNewTagBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddNewTag();
                e.Handled = true;
            }
        }

        /// <summary>Добавляет введённый тег в список и сразу отмечает его.</summary>
        private void AddNewTag()
        {
            var text = _newTagBox.Text?.Trim() ?? string.Empty;
            if (text.Length == 0)
                return;

            // Тег уже есть в списке (регистронезависимо) — просто отмечаем его.
            var existing = _checks.FirstOrDefault(c =>
                string.Equals(c.Name, text, StringComparison.OrdinalIgnoreCase));
            if (existing.Check is not null)
            {
                existing.Check.IsChecked = true;
                existing.Check.BringIntoView();
                _newTagBox.Clear();
                return;
            }

            AddCheckbox(text, isChecked: true);
            _newTagBox.Clear();
            UpdateSelectedCount();
        }
    }
}
#endif