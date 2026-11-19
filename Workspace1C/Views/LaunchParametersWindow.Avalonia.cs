#if LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;

namespace Workspace1C
{
    /// <summary>
    /// Диалог-конфигуратор параметров запуска платформы 1С. Состоит из поля ввода
    /// параметров и справочника ключей командной строки, из которого параметр
    /// подставляется в поле двойным кликом. Avalonia/Linux-версия WPF-окна
    /// <see cref="LaunchParametersWindow"/>. Пользовательские параметры справочника
    /// (issue #141) поддерживаются с 1.0.0.72 (#68): добавление, правка в отдельном
    /// диалоге, порядок (вверх/вниз), поиск над списком.
    /// </summary>
    public class LaunchParametersWindow : ModalWindowBase
    {
        private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();
        private readonly TextBox _txtCustom;
        private readonly List<string> _customParams;
        private readonly Action<IReadOnlyList<string>>? _onCustomParametersChanged;

        private readonly TextBox _txtSearch;
        private readonly ListBox _list;
        private readonly Button _btnEdit;
        private readonly Button _btnMoveUp;
        private readonly Button _btnMoveDown;
        private readonly Button _btnRemove;
        private readonly TextBox _txtNewParam;
        private readonly TextBox _txtNewComment;

        /// <summary>
        /// Создаёт диалог конфигуратора параметров запуска.
        /// </summary>
        /// <param name="currentParameters">Текущая строка параметров для предзаполнения.</param>
        /// <param name="customParameters">Пользовательские параметры, дополняющие справочник (необязательно).</param>
        /// <param name="onCustomParametersChanged">Обратный вызов сохранения изменённого списка пользовательских параметров (необязательно).</param>
        public LaunchParametersWindow(
            string currentParameters,
            IReadOnlyList<string>? customParameters = null,
            Action<IReadOnlyList<string>>? onCustomParametersChanged = null)
        {
            Title = LocalizationManager.T("LaunchParams.Title");
            FontSize = 13;
            Width = 800;
            Height = 640;
            MinWidth = 720;
            MinHeight = 480;

            _onCustomParametersChanged = onCustomParametersChanged;
            _customParams = (customParameters ?? Array.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            _txtCustom = new TextBox
            {
                Text = currentParameters ?? string.Empty,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 110,
                Padding = new Thickness(6, 6),
                VerticalContentAlignment = VerticalAlignment.Top
            };
            _txtCustom.Styled(Themes.ControlThemes.ModernTextBox);
            ToolTip.SetTip(_txtCustom, LocalizationManager.T("LaunchParams.InputTooltip"));

            // Поиск над справочником (issue #68).
            _txtSearch = new TextBox
            {
                MinHeight = 30,
                Padding = new Thickness(6, 2),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _txtSearch.Styled(Themes.ControlThemes.ModernTextBox);
            ToolTip.SetTip(_txtSearch, LocalizationManager.T("LaunchParams.SearchPlaceholder"));
            _txtSearch.TextChanged += (_, _) => RefreshList();

            _list = new ListBox();
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            _list.SelectionChanged += (_, _) => UpdateCustomButtonsState();
            _list.DoubleTapped += (_, e) =>
            {
                if (_list.SelectedItem is OneCLaunchParameterReference item)
                {
                    InsertCustomText(item.Key);
                    e.Handled = true;
                }
            };

            _btnEdit = BuildToolButton("LaunchParams.CustomEdit", "IconEdit", OnEditParam_Click);
            _btnMoveUp = BuildToolButton("LaunchParams.MoveUp", "IconArrowUp", () => MoveSelectedCustomParameter(-1));
            _btnMoveDown = BuildToolButton("LaunchParams.MoveDown", "IconArrowDown", () => MoveSelectedCustomParameter(1));
            // Удаление параметра — опасное действие: DangerButton (P2-1).
            _btnRemove = BuildToolButton("Common.Delete", "IconDelete", RemoveSelectedCustomParameter, danger: true);

            _txtNewParam = new TextBox
            {
                MinHeight = 30,
                Padding = new Thickness(6, 2),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _txtNewParam.Styled(Themes.ControlThemes.ModernTextBox);
            ToolTip.SetTip(_txtNewParam, LocalizationManager.T("LaunchParams.CustomInputTooltip"));
            _txtNewParam.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    AddCustomParameter();
                    e.Handled = true;
                }
            };
            _txtNewComment = new TextBox
            {
                MinHeight = 30,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(6, 2),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _txtNewComment.Styled(Themes.ControlThemes.ModernTextBox);
            ToolTip.SetTip(_txtNewComment, LocalizationManager.T("LaunchParams.CustomCommentTooltip"));

            Content = BuildRoot();
            RefreshList();
        }

        /// <summary>Кружок справки тем же контролом, что и в остальных окнах.</summary>
        private static Control BuildHelpLink(string helpKey) => new Controls.HelpLink
        {
            HelpText = LocalizationManager.T(helpKey),
            VerticalAlignment = VerticalAlignment.Center
        };

        /// <summary>Итоговая строка параметров запуска.</summary>
        public string Result { get; private set; } = string.Empty;

        /// <summary>Обновлённый список пользовательских параметров справочника (issue #141).</summary>
        public IReadOnlyList<string> CustomParameters => _customParams;

        private Button BuildToolButton(string labelKey, string iconKey, Action onClick, bool danger = false)
        {
            // У опасной кнопки цвет задаёт стиль: значок 16 px и белая кисть
            // DangerTextBrush, без локального красного Foreground (P2-1).
            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        danger
                            ? IconHelper.MakeIcon(iconKey, 16, "DangerTextBrush")
                            : IconHelper.MakeIcon(iconKey, 15),
                        new TextBlock { Text = T(labelKey) }
                    }
                },
                MinHeight = 32,
                Padding = new Thickness(10, 4),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            button.Styled(danger ? ControlThemes.DangerButton : ControlThemes.SecondaryButton);
            button.Click += (_, _) => onClick();
            return button;
        }

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var inputBox = Controls.GroupBoxPanel.Build(
                "Main.Parameters", _txtCustom,
                margin: new Thickness(0, 0, 0, 12),
                padding: new Thickness(12),
                headerExtra: BuildHelpLink("LaunchParams.InputHelp"));
            Grid.SetRow(inputBox, 0);
            grid.Children.Add(inputBox);

            _list.ItemTemplate = new FuncDataTemplate<OneCLaunchParameterReference>((item, _) =>
            {
                if (item is null)
                    return new Control();

                var panel = new Grid { Margin = new Thickness(2, 3) };
                panel.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(220)));
                panel.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

                var key = new TextBlock
                {
                    Text = item.Key,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 12, 0)
                };
                Grid.SetColumn(key, 0);
                panel.Children.Add(key);

                var desc = new TextBlock
                {
                    Text = item.Description,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(desc, 1);
                panel.Children.Add(desc);
                return panel;
            });

            var listHost = new ScrollViewer
            {
                Content = _list,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var columnsHeader = new Grid { Margin = new Thickness(2, 0, 2, 4) };
            columnsHeader.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(220)));
            columnsHeader.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            var paramHead = new TextBlock
            {
                Text = LocalizationManager.T("LaunchParams.Parameter"),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold
            };
            var descHead = new TextBlock
            {
                Text = LocalizationManager.T("LaunchParams.Description"),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold
            };
            Themes.ThemeBrushes.Bind(paramHead, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Themes.ThemeBrushes.Bind(descHead, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetColumn(paramHead, 0);
            Grid.SetColumn(descHead, 1);
            columnsHeader.Children.Add(paramHead);
            columnsHeader.Children.Add(descHead);

            var refContent = new Grid();
            refContent.RowDefinitions.Add(new RowDefinition(GridLength.Auto));   // поиск
            refContent.RowDefinitions.Add(new RowDefinition(GridLength.Auto));   // шапка колонок
            refContent.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star))); // список
            refContent.RowDefinitions.Add(new RowDefinition(GridLength.Auto));   // нижняя панель
            var bottomPanel = BuildBottomPanel();
            Grid.SetRow(_txtSearch, 0);
            refContent.Children.Add(_txtSearch);
            Grid.SetRow(columnsHeader, 1);
            refContent.Children.Add(columnsHeader);
            Grid.SetRow(listHost, 2);
            refContent.Children.Add(listHost);
            Grid.SetRow(bottomPanel, 3);
            refContent.Children.Add(bottomPanel);

            var referenceBox = Controls.GroupBoxPanel.Build(
                "LaunchParams.Reference", refContent,
                margin: new Thickness(0, 0, 0, 12),
                padding: new Thickness(12),
                headerExtra: BuildHelpLink("LaunchParams.ReferenceHelp"));
            Grid.SetRow(referenceBox, 1);
            grid.Children.Add(referenceBox);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Margin = new Thickness(0, 8, 0, 0),
                Children =
                {
                    BuildConfirmActionButton("Common.Ok", "IconCheck", 140, OnOk_Click),
                    BuildCancelActionButton(140)
                }
            };
            Grid.SetRow(buttons, 2);
            grid.Children.Add(buttons);

            return grid;
        }

        /// <summary>Нижняя панель: поля добавления + кнопки «Добавить/Изменить/Вверх/Вниз/Удалить».</summary>
        private Control BuildBottomPanel()
        {
            var panel = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            panel.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
            panel.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(3, GridUnitType.Star)));
            panel.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            panel.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            panel.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            panel.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            panel.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            Grid.SetColumn(_txtNewParam, 0);
            panel.Children.Add(_txtNewParam);
            Grid.SetColumn(_txtNewComment, 1);
            panel.Children.Add(_txtNewComment);

            var addButton = BuildToolButton("LaunchParams.CustomAdd", "IconPlus", AddCustomParameter);
            Grid.SetColumn(addButton, 2);
            addButton.Margin = new Thickness(8, 0, 0, 0);
            panel.Children.Add(addButton);

            Grid.SetColumn(_btnEdit, 3);
            _btnEdit.Margin = new Thickness(6, 0, 0, 0);
            panel.Children.Add(_btnEdit);

            Grid.SetColumn(_btnMoveUp, 4);
            _btnMoveUp.Margin = new Thickness(6, 0, 0, 0);
            panel.Children.Add(_btnMoveUp);

            Grid.SetColumn(_btnMoveDown, 5);
            _btnMoveDown.Margin = new Thickness(6, 0, 0, 0);
            panel.Children.Add(_btnMoveDown);

            Grid.SetColumn(_btnRemove, 6);
            _btnRemove.Margin = new Thickness(6, 0, 0, 0);
            panel.Children.Add(_btnRemove);

            return panel;
        }

        private static string T(string key) => LocalizationManager.T(key);

        /// <summary>Добавляет текст в поле «Параметры», разделяя пробелом.</summary>
        private void InsertCustomText(string text)
        {
            var updated = OneCLaunchArgumentParser.AppendParameter(_txtCustom.Text, text);
            if (updated == _txtCustom.Text)
                return;

            _txtCustom.Text = updated;
            _txtCustom.CaretIndex = _txtCustom.Text.Length;
            _txtCustom.Focus();
        }

        private void OnOk_Click()
        {
            Result = (_txtCustom.Text ?? string.Empty).Trim();
        }

        // ============ Пользовательские параметры (issue #141) ============

        private void RefreshList()
        {
            var catalog = OneCLaunchArgumentParser.BuildReferenceCatalog(_customParams);
            _list.ItemsSource = OneCLaunchArgumentParser.FilterReferenceCatalog(catalog, _txtSearch.Text);
            UpdateCustomButtonsState();
        }

        private OneCLaunchParameterReference? SelectedCustomParameter =>
            _list.SelectedItem as OneCLaunchParameterReference;

        private void UpdateCustomButtonsState()
        {
            var selected = SelectedCustomParameter;
            var isCustom = selected is { IsCustom: true };
            var index = isCustom ? IndexOfCustom(selected!.Key) : -1;

            _btnEdit.IsEnabled = isCustom;
            _btnRemove.IsEnabled = isCustom;
            _btnMoveUp.IsEnabled = isCustom && index > 0;
            _btnMoveDown.IsEnabled = isCustom && index >= 0 && index < _customParams.Count - 1;
        }

        private int IndexOfCustom(string key)
        {
            for (var i = 0; i < _customParams.Count; i++)
            {
                var (candidate, _) = OneCLaunchArgumentParser.SplitCustomEntry(_customParams[i]);
                if (string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private void AddCustomParameter()
        {
            var key = (_txtNewParam.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key))
                return;
            var comment = (_txtNewComment.Text ?? string.Empty).Trim();
            var entry = string.IsNullOrWhiteSpace(comment) ? key : key + "\t" + comment;

            if (_customParams.Any(p =>
                string.Equals(OneCLaunchArgumentParser.SplitCustomEntry(p).Key, key,
                    StringComparison.OrdinalIgnoreCase)))
                return;

            _customParams.Add(entry);
            _txtNewParam.Clear();
            _txtNewComment.Clear();
            PersistCustomParameters();
            RefreshList();
            _txtNewParam.Focus();
        }

        private void EditSelectedCustomParameter()
        {
            var selected = SelectedCustomParameter;
            if (selected is not { IsCustom: true })
                return;

            var index = IndexOfCustom(selected.Key);
            if (index < 0)
                return;

            var (oldKey, oldComment) = OneCLaunchArgumentParser.SplitCustomEntry(_customParams[index]);

            var dialog = new LaunchParameterEditWindow(oldKey, oldComment);
            if (!dialog.ShowDialogSync(this))
                return;

            var newKey = (dialog.Key ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(newKey))
                return;
            var newComment = (dialog.Comment ?? string.Empty).Trim();

            for (var i = 0; i < _customParams.Count; i++)
            {
                if (i == index)
                    continue;
                var (candidate, _) = OneCLaunchArgumentParser.SplitCustomEntry(_customParams[i]);
                if (string.Equals(candidate, newKey, StringComparison.OrdinalIgnoreCase))
                {
                    _dialogs.ShowWarning(T("LaunchParams.CustomDuplicate"), T("LaunchParams.EditTitle"));
                    return;
                }
            }

            _customParams[index] = string.IsNullOrWhiteSpace(newComment) ? newKey : newKey + "\t" + newComment;
            PersistCustomParameters();
            RefreshList();
        }

        private void RemoveSelectedCustomParameter()
        {
            var selected = SelectedCustomParameter;
            if (selected is not { IsCustom: true })
                return;

            _customParams.RemoveAll(p =>
                string.Equals(OneCLaunchArgumentParser.SplitCustomEntry(p).Key, selected.Key,
                    StringComparison.OrdinalIgnoreCase));
            PersistCustomParameters();
            RefreshList();
        }

        private void MoveSelectedCustomParameter(int delta)
        {
            var selected = SelectedCustomParameter;
            if (selected is not { IsCustom: true })
                return;

            if (!OneCLaunchArgumentParser.MoveCustomParameter(_customParams, selected.Key, delta))
                return;
            PersistCustomParameters();
            RefreshList();
            _list.SelectedItem = (OneCLaunchArgumentParser.BuildReferenceCatalog(_customParams))
                .FirstOrDefault(r => r is { IsCustom: true } &&
                    string.Equals(r.Key, selected.Key, StringComparison.OrdinalIgnoreCase));
        }

        private void OnEditParam_Click() => EditSelectedCustomParameter();

        private void PersistCustomParameters()
        {
            _onCustomParametersChanged?.Invoke(_customParams);
        }
    }
}
#endif