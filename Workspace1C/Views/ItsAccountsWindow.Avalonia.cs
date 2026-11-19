#if LINUX
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно справочника «Учетные данные ИТС» (issue #333): список учётных записей с кнопками
    /// «Добавить», «Изменить», «Удалить», «Задать основным». Флажок «Основная» в списке —
    /// только для чтения; смена основной — ТОЛЬКО кнопкой «Задать основным». Правка/добавление
    /// выполняются в отдельном модальном окне <see cref="ItsAccountEditWindow"/> с фокусом на
    /// первом поле. Изменения сохраняются сразу в файл <c>its_accounts.json</c> через
    /// <see cref="IItsAccountsStore"/>. Avalonia/Linux-версия WPF-окна
    /// <see cref="ItsAccountsWindow"/>.
    /// </summary>
    public sealed class ItsAccountsWindow : ModalWindowBase
    {
        private readonly Services.IItsAccountsStore _store = AppServices.GetRequiredService<Services.IItsAccountsStore>();
        private readonly Services.IAppLogger _logger = AppServices.GetRequiredService<Services.IAppLogger>();
        private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

        // Панель строк списка (пересобирается после каждого изменения).
        private readonly StackPanel _rowsPanel = new();
        private readonly DockPanel _listView = new() { LastChildFill = true };

        // «Текущая» запись для кнопки «Задать основным» на нижней панели (issue #333):
        // выбор строки отслеживается кликом; кнопка активна только для неосновной записи.
        private ItsAccount? _selectedAccount;
        private Button? _primaryButton;

        /// <summary>
        /// Открывает окно редактирования списка учётных записей ИТС.
        /// </summary>
        public ItsAccountsWindow()
        {
            Title = T("ItsAccounts.Title");
            Width = 760;
            Height = 520;
            MinWidth = 640;
            MinHeight = 420;
            FontSize = 13;
            CanResize = true;

            ReloadRows();
            Content = BuildRoot();
        }

        /// <summary>
        /// Показывает окно модально (синхронно). Открытая публичная обёртка над
        /// <see cref="ModalWindowBase.ShowDialogSync(Window?)"/>, чтобы диалог можно было
        /// вызывать из ViewModel (не наследника окна).
        /// </summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private static string T(string key) => LocalizationManager.T(key);

        private void ReloadRows()
        {
            _rowsPanel.Children.Clear();
            _selectedAccount = null;
            UpdatePrimaryButtonState();
            foreach (var account in _store.Load())
                _rowsPanel.Children.Add(BuildRow(account));
        }

        /// <summary>Строит визуальную строку таблицы учётных записей.</summary>
        private Grid BuildRow(ItsAccount account)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };

            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(80, GridUnitType.Pixel)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var name = new TextBlock
            {
                Text = account.Name,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(name, 0);
            grid.Children.Add(name);

            var login = new TextBlock
            {
                Text = account.Login,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(login, 1);
            grid.Children.Add(login);

            // Флажок «Основная» — только для чтения (смена основной — кнопкой «Задать основным»).
            var primary = new CheckBox
            {
                IsChecked = account.IsPrimary,
                IsHitTestVisible = false,
                IsEnabled = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(primary, new TextBlock
            {
                Text = T("ItsAccounts.PrimaryTooltip"),
                MaxWidth = 320,
                TextWrapping = TextWrapping.Wrap
            });
            Grid.SetColumn(primary, 2);
            grid.Children.Add(primary);

            var edit = new Button
            {
                Content = T("ItsAccounts.Edit"),
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(10, 4),
                Margin = new Thickness(4, 0, 4, 0)
            };
            edit.Styled(ControlThemes.SelectAllButton);
            edit.Click += (_, _) => OnEditRow(account);
            Grid.SetColumn(edit, 3);
            grid.Children.Add(edit);

            var delete = new Button
            {
                Content = T("ItsAccounts.Delete"),
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(10, 4),
                Margin = new Thickness(4, 0, 4, 0)
            };
            delete.Styled(ControlThemes.SelectAllButton);
            delete.Click += (_, _) => OnDeleteRow(account);
            Grid.SetColumn(delete, 4);
            grid.Children.Add(delete);

            // Двойной клик по строке = правка записи (issue #333), как в WPF-версии.
            grid.DoubleTapped += (_, _) => OnEditRow(account);

            // Клик по строке запоминает «текущую» запись для кнопки «Задать основным».
            grid.PointerPressed += (_, _) => SelectRow(account);

            return grid;
        }

        /// <summary>Запоминает выбранную строку и обновляет доступность кнопки «Задать основным».</summary>
        private void SelectRow(ItsAccount account)
        {
            _selectedAccount = account;
            UpdatePrimaryButtonState();
        }

        /// <summary>«Задать основным» активна только при выбранной неосновной записи (issue #333).</summary>
        private void UpdatePrimaryButtonState()
        {
            if (_primaryButton is not null)
                _primaryButton.IsEnabled = _selectedAccount is { } account && !account.IsPrimary;
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
                Text = T("ItsAccounts.Title"),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            var hint = new TextBlock
            {
                Text = T("ItsAccounts.Hint"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            };
            Themes.ThemeBrushes.Bind(hint, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
            Grid.SetRow(hint, 1);
            grid.Children.Add(hint);

            // Карточка списка.
            var listBorder = new Border
            {
                Margin = new Thickness(0, 12, 0, 0),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8)
            };
            Themes.ThemeBrushes.Bind(listBorder, Border.BackgroundProperty, "CardBackgroundColorBrush");
            Themes.ThemeBrushes.Bind(listBorder, Border.BorderBrushProperty, "BorderColorBrush");

            _rowsPanel.Margin = new Thickness(4, 2);
            var header = BuildHeaderGrid();

            var scroll = new ScrollViewer
            {
                Content = _rowsPanel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(4)
            };

            _listView.Children.Clear();
            var toolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(8, 8, 8, 4)
            };
            var addButton = new Button { Content = T("ItsAccounts.Add"), Height = 32 };
            addButton.Styled(ControlThemes.ModernButton);
            addButton.Click += (_, _) => OnAddClick();
            toolbar.Children.Add(addButton);

            // «Задать основным» — на панели рядом с «Добавить» (issue #333): активна
            // при выбранной неосновной записи (клик по строке запоминает выбор).
            _primaryButton = new Button
            {
                Content = T("ItsAccounts.SetPrimary"),
                Height = 32,
                IsEnabled = false
            };
            _primaryButton.Styled(ControlThemes.SelectAllButton);
            _primaryButton.Click += (_, _) =>
            {
                if (_selectedAccount is { } account && !account.IsPrimary)
                    OnSetPrimary(account);
            };
            toolbar.Children.Add(_primaryButton);
            UpdatePrimaryButtonState();

            var toolbarBorder = new Border
            {
                Padding = new Thickness(8, 4),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = Brushes.Transparent,
                Child = toolbar
            };
            Themes.ThemeBrushes.Bind(toolbarBorder, Border.BorderBrushProperty, "BorderColorBrush");
            DockPanel.SetDock(toolbarBorder, Dock.Top);
            _listView.Children.Add(toolbarBorder);

            DockPanel.SetDock(header, Dock.Top);
            _listView.Children.Add(header);
            _listView.Children.Add(scroll);
            listBorder.Child = _listView;

            Grid.SetRow(listBorder, 2);
            grid.Children.Add(listBorder);

            // Нижняя панель: «Закрыть».
            var bottom = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            bottom.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var close = BuildCancelActionButton(140);
            close.Click += (_, _) => Close();
            Grid.SetColumn(close, 1);
            bottom.Children.Add(close);

            Grid.SetRow(bottom, 3);
            grid.Children.Add(bottom);

            return grid;
        }

        /// <summary>Заголовок таблицы учётных записей.</summary>
        private Grid BuildHeaderGrid()
        {
            var grid = new Grid { Margin = new Thickness(8, 0, 8, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(80, GridUnitType.Pixel)));

            grid.Children.Add(MakeHeaderText(T("ItsAccounts.Name"), 0));
            grid.Children.Add(MakeHeaderText(T("ItsAccounts.Login"), 1));
            grid.Children.Add(MakeHeaderText(T("ItsAccounts.Primary"), 2));
            return grid;
        }

        private static TextBlock MakeHeaderText(string text, int column)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Themes.ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetColumn(block, column);
            return block;
        }

        /// <summary>Открывает модальный редактор новой учётной записи.</summary>
        private void OnAddClick()
        {
            var edit = new ItsAccountEditWindow();
            if (!edit.ShowSync(this) || edit.Result is not { } created)
                return;

            try
            {
                _store.Upsert(created);
                _logger.Info($"[ITS] Добавлена учётная запись «{Mask(created)}».");
                ReloadRows();
            }
            catch (Exception ex)
            {
                _logger.Error("Ошибка добавления учётной записи ИТС", ex);
            }
        }

        /// <summary>Открывает модальный редактор выбранной записи (правка на копии).</summary>
        private void OnEditRow(ItsAccount account)
        {
            var edit = new ItsAccountEditWindow(CloneAccount(account));
            if (!edit.ShowSync(this) || edit.Result is not { } updated)
                return; // Отмена — модель не изменялась.

            try
            {
                _store.Upsert(updated);
                _logger.Info($"[ITS] Изменена учётная запись «{Mask(updated)}».");
                ReloadRows();
            }
            catch (Exception ex)
            {
                _logger.Error("Ошибка изменения учётной записи ИТС", ex);
            }
        }

        /// <summary>Удаляет запись с подтверждением; основная после удаления — первая запись списка.</summary>
        private void OnDeleteRow(ItsAccount account)
        {
            if (!_dialogs.Confirm(T("ItsAccounts.ConfirmDelete"), T("ItsAccounts.Title")))
                return;

            try
            {
                var wasPrimary = account.IsPrimary;
                _store.Delete(account.Id);
                _logger.Info(wasPrimary
                    ? "[ITS] Удалена основная учётная запись — основной стала первая запись списка."
                    : $"[ITS] Удалена учётная запись «{Mask(account)}».");
                ReloadRows();
            }
            catch (Exception ex)
            {
                _logger.Error("Ошибка удаления учётной записи ИТС", ex);
            }
        }

        /// <summary>Делает запись основной (снимает флаг с остальных) и пересобирает список.</summary>
        private void OnSetPrimary(ItsAccount account)
        {
            try
            {
                _store.SetPrimary(account.Id);
                ReloadRows();
            }
            catch (Exception ex)
            {
                _logger.Error("Ошибка смены основной учётной записи ИТС", ex);
            }
        }

        /// <summary>Глубокая копия записи (для правки без мутации исходного экземпляра).</summary>
        private static ItsAccount CloneAccount(ItsAccount source) => new()
        {
            Id = source.Id,
            Name = source.Name,
            Login = source.Login,
            Password = source.Password,
            IsPrimary = source.IsPrimary,
        };

        /// <summary>Маскирует пароль при журналировании (issue #333): пароль не попадает в лог.</summary>
        private static string Mask(ItsAccount account)
            => Services.ItsAccountsStore.MaskPasswordForLog(account.Name ?? string.Empty, account.Password);
    }
}
#endif