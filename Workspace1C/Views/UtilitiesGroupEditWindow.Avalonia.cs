#if LINUX
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Диалог создания/правки группы меню «Утилиты» (issue #374): имя группы и иконка
    /// из каталога (с превью). Avalonia/Linux-версия WPF-окна <see cref="UtilitiesGroupEditWindow"/>.
    /// Результат — <see cref="UtilitiesGroupEditParams"/> через <see cref="Result"/> (null — отмена).
    /// </summary>
    public sealed class UtilitiesGroupEditWindow : ModalWindowBase
    {
        private readonly TextBox _nameBox = new();
        private readonly ComboBox _iconCombo = new ComboBox().Styled(ControlThemes.ModernComboBox);
        private readonly ComboBox _displayCombo = new ComboBox().Styled(ControlThemes.ModernComboBox);
        private readonly TextBlock _displayModeLabel = new();
        private readonly Button _okButton = new();

        /// <param name="icons">Доступные иконки каталога (из ViewModel редактора).</param>
        /// <param name="initialTitle">Текущее имя группы (для правки) или пусто.</param>
        /// <param name="initialIconKey">Текущий ключ иконки (для правки) или пусто.</param>
        /// <param name="initialIconColor">Текущий цвет иконки (#RRGGBB) для синтетического
        /// варианта, если ключа нет в каталоге (issue #146).</param>
        /// <param name="initialDisplayMode">Текущий режим отображения группы (issue #146).</param>
        /// <param name="showDisplayMode">Показывать ли выбор «Иконка» / «С надписью».
        /// Только для групп верхнего уровня; у вложенных всегда подпись.</param>
        public UtilitiesGroupEditWindow(
            IReadOnlyList<UtilitiesIconOption> icons,
            string? initialTitle = null,
            string? initialIconKey = null,
            string? initialIconColor = null,
            UtilitiesMenuGroupDisplay initialDisplayMode = UtilitiesMenuGroupDisplay.IconWithLabel,
            bool showDisplayMode = false)
        {
            Title = LocalizationManager.T("Utilities.Editor.GroupDialogTitle");
            Width = 440;
            // Минимальные размеры (issue #55): при уменьшении окна поля не обрезаются.
            MinWidth = 400;
            MinHeight = 200;
            // Высота по содержимому: фиксированная 240 обрезала нижние кнопки (issue #374).
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _nameBox.Text = initialTitle ?? string.Empty;
            _nameBox.TextChanged += (_, _) => UpdateOkEnabled();

            // issue #146: эффективной иконки группы может не быть в каталоге (новая
            // верхнеуровневая папка) — добавляем её синтетическим вариантом, иначе
            // поле показывало бы первый пункт каталога, а не иконку из дерева.
            var options = new List<UtilitiesIconOption>(icons);
            if (!string.IsNullOrWhiteSpace(initialIconKey) &&
                !options.Any(o => string.Equals(o.Key, initialIconKey, StringComparison.Ordinal)))
            {
                options.Add(new UtilitiesIconOption(
                    initialIconKey!, string.IsNullOrWhiteSpace(initialIconColor) ? "#6B7280" : initialIconColor!));
            }
            _iconCombo.ItemsSource = options;
            _iconCombo.ItemTemplate = new FuncDataTemplate<UtilitiesIconOption>(
                (o, _) => BuildIconRow(o));
            if (!string.IsNullOrWhiteSpace(initialIconKey))
            {
                var match = options.FirstOrDefault(o => string.Equals(o.Key, initialIconKey, StringComparison.Ordinal));
                if (match is not null)
                    _iconCombo.SelectedItem = match;
            }
            if (_iconCombo.SelectedItem is null && options.Count > 0)
                _iconCombo.SelectedItem = options[0];

            // issue #146: режим отображения доступен только группам верхнего уровня.
            _displayCombo.ItemsSource = UtilitiesMenuEditorViewModel.GroupDisplayOptions;
            var mode = showDisplayMode
                ? initialDisplayMode
                : UtilitiesMenuGroupDisplay.IconWithLabel;
            _displayCombo.SelectedItem = UtilitiesMenuEditorViewModel.GroupDisplayOptions
                .FirstOrDefault(o => o.Mode == mode)
                ?? UtilitiesMenuEditorViewModel.GroupDisplayOptions[0];
            _displayCombo.IsVisible = showDisplayMode;
            _displayModeLabel.IsVisible = showDisplayMode;

            Content = BuildLayout();
            UpdateOkEnabled();
            Opened += (_, _) =>
            {
                // Активное поле «Имя» с выделением текста (issue #374). Фокус откладываем:
                // в Opened окно может быть ещё не активировано — Focus() не встаёт.
                Dispatcher.UIThread.Post(() =>
                {
                    Activate();
                    _nameBox.Focus();
                    _nameBox.SelectAll();
                });
                // Диалог чуть выше центра владельца (issue #374).
                Position = new PixelPoint(Position.X, Math.Max(0, Position.Y - 120));
            };
        }

        /// <summary>Результат диалога: имя и ключ иконки (null — отмена).</summary>
        public UtilitiesGroupEditParams? Result { get; private set; }

        /// <summary>Показывает окно модально (синхронно). Открытая публичная обёртка над
        /// <see cref="ModalWindowBase.ShowDialogSync(Window?)"/>.</summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private Control BuildLayout()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            // Имя группы.
            var nameLabel = new TextBlock
            {
                Text = LocalizationManager.T("Utilities.Editor.GroupNameLabel"),
                Margin = new Thickness(0, 0, 0, 6)
            };
            Grid.SetRow(nameLabel, 0);
            grid.Children.Add(nameLabel);

            _nameBox.Padding = new Thickness(8, 6);
            Grid.SetRow(_nameBox, 1);
            grid.Children.Add(_nameBox);

            // Иконка.
            var iconLabel = new TextBlock
            {
                Text = LocalizationManager.T("Utilities.Editor.IconHeader"),
                Margin = new Thickness(0, 12, 0, 6)
            };
            Grid.SetRow(iconLabel, 2);
            grid.Children.Add(iconLabel);

            _iconCombo.Width = 220;
            Grid.SetRow(_iconCombo, 3);
            Grid.SetColumn(_iconCombo, 0);
            grid.Children.Add(_iconCombo);

            // Отображение группы (issue #146): только для верхнего уровня.
            _displayModeLabel.Text = LocalizationManager.T("Utilities.Editor.DisplayHeader");
            _displayModeLabel.Margin = new Thickness(0, 12, 0, 6);
            Grid.SetRow(_displayModeLabel, 4);
            grid.Children.Add(_displayModeLabel);

            _displayCombo.Width = 220;
            _displayCombo.ItemTemplate = new FuncDataTemplate<UtilitiesGroupDisplayOption>(
                (o, _) => new TextBlock { Text = o?.Label ?? string.Empty, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetRow(_displayCombo, 5);
            grid.Children.Add(_displayCombo);

            // Кнопки.
            var buttons = BuildButtons(
                okText: LocalizationManager.T("Common.Ok"), onOk: OnOkClick);
            Grid.SetRow(buttons, 6);
            buttons.Margin = new Thickness(0, 16, 0, 0);
            grid.Children.Add(buttons);

            return grid;
        }

        /// <summary>Строка иконки: превью слева + подпись. Превью = та же геометрия,
        /// что применяют меню «Утилиты» и трей (issue #62): PackIcon-ключ каталога —
        /// через UtilitiesMenuCatalog.WpfIconGeometryKey на парный ключ Icon*, иначе
        /// резолв уходил бы в запасную «папку». Цвет — выбранного варианта каталога.</summary>
        private Control BuildIconRow(UtilitiesIconOption option)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var geometryKey = Services.UtilitiesMenuCatalog.WpfIconGeometryKey(option.Key);
            if (string.IsNullOrEmpty(geometryKey))
                geometryKey = option.Key;
            Control icon;
            if (TryParseColor(option.Color) is { } brush)
                icon = IconHelper.MakeIcon(geometryKey, 16, brush);
            else
                icon = IconHelper.MakeIcon(geometryKey, 16, "TextPrimaryBrush");
            row.Children.Add(icon);
            row.Children.Add(new TextBlock { Text = option.Label, VerticalAlignment = VerticalAlignment.Center });
            return row;
        }

        /// <summary>Разбирает цвет #RRGGBB в кисть (невалидный — null, рисуем кистью темы).</summary>
        private static IBrush? TryParseColor(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return null;
            try
            {
                if (!Avalonia.Media.Color.TryParse(hex!.Trim(), out var color))
                    return null;
                return new SolidColorBrush(color);
            }
            catch
            {
                return null;
            }
        }

        private void UpdateOkEnabled()
        {
            _okButton.IsEnabled = !string.IsNullOrWhiteSpace(_nameBox.Text);
        }

        private void OnOkClick()
        {
            var icon = _iconCombo.SelectedItem as UtilitiesIconOption;
            // issue #146: для вложенной группы поле скрыто — всегда «С надписью».
            var mode = _displayCombo.IsVisible
                ? (_displayCombo.SelectedItem as UtilitiesGroupDisplayOption)?.Mode
                    ?? UtilitiesMenuGroupDisplay.IconWithLabel
                : UtilitiesMenuGroupDisplay.IconWithLabel;
            Result = new UtilitiesGroupEditParams(
                _nameBox.Text?.Trim() ?? string.Empty,
                icon is null ? null : icon.Key,
                mode);
            DialogResult = true;
            Close();
        }
    }
}
#endif