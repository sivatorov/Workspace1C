#if LINUX
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Themes;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C
{
    /// <summary>
    /// Управление учётными записями (профилями): создание, переименование, удаление,
    /// установка и снятие пароля. Каждый профиль хранит собственные настройки и список баз.
    /// </summary>
    public class ProfilesWindow : ModalWindowBase
    {
        private readonly IProfileService _profileService;
        private readonly TextBlock _currentAccountLabel;
        private readonly ListBox _profilesList;
        private readonly TextBox _nameBox = new TextBox { Width = 260 }.Styled(Themes.ControlThemes.ModernTextBox);
        private readonly PasswordBox _passwordInput =
            new PasswordBox { Width = 260, Padding = new Thickness(8, 6) }
                .Styled(Themes.ControlThemes.ModernPasswordBox);
        private readonly CheckBox _protectCheck = new() { Content = LocalizationManager.T("Profiles.ProtectWithPassword"), Margin = new Thickness(108, 0, 0, 0) };
        private readonly TextBlock _errorLabel;
        private readonly TextBlock _editingTitle;
        private readonly TextBlock _selectPrompt;
        private readonly StackPanel _fieldsPanel;
        private readonly ComboBox _activeAccountBox = new();
        private bool _suppressSelection;
        private bool _suppressActiveAccount;

        public ProfilesWindow(IProfileService profileService)
        {
            _profileService = profileService;
            Title = LocalizationManager.T("Profiles.Title");
            // Кегль окна из разметки: подписи без явного размера берут его по наследству.
            FontSize = 13;
            // Размеры и возможность растягивания из разметки
            // (ProfilesWindow.xaml:10-13).
            Width = 760;
            Height = 560;
            MinWidth = 640;
            MinHeight = 500;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            // Подсказка и подпись текущей записи (ProfilesWindow.xaml:43-45).
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            header.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Profiles.Hint"),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.85
            });
            _currentAccountLabel = new TextBlock
            {
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            header.Children.Add(_currentAccountLabel);
            Grid.SetRow(header, 0);

            // Выбор активной учётной записи: смена применяется сразу, кнопкой
            // «Сохранить» она не подтверждается (ProfilesWindow.xaml:49-60).
            var activeRow = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var activeLabel = new TextBlock
            {
                Text = LocalizationManager.T("Profiles.ActiveAccount"),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 10, 0)
            };
            DockPanel.SetDock(activeLabel, Dock.Left);
            activeRow.Children.Add(activeLabel);
            _activeAccountBox.DisplayMemberBinding = new Avalonia.Data.Binding("Name");
            _activeAccountBox.SelectionChanged += (_, _) => OnActiveAccountChanged();
            activeRow.Children.Add(_activeAccountBox);
            Grid.SetRow(activeRow, 1);

            // Список слева шириной 240 и карточка редактора справа
            // (ProfilesWindow.xaml:63-67).
            var body = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _profilesList = new ListBox { Margin = new Thickness(0, 0, 12, 0) };
            _profilesList.ItemTemplate = new FuncDataTemplate<UserProfile>((profile, _) => BuildProfileRow(profile), true);
            _profilesList.SelectionChanged += (_, _) => OnSelectionChanged();
            Grid.SetColumn(_profilesList, 0);
            body.Children.Add(_profilesList);

            _editingTitle = new TextBlock
            {
                FontWeight = FontWeight.SemiBold,
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            _selectPrompt = new TextBlock
            {
                Text = LocalizationManager.T("Profiles.SelectToEdit"),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
                IsVisible = false
            };
            _fieldsPanel = BuildEditor();

            var editorContent = new StackPanel();
            editorContent.Children.Add(_editingTitle);
            editorContent.Children.Add(_selectPrompt);
            editorContent.Children.Add(_fieldsPanel);

            var editorCard = new Border
            {
                Padding = new Thickness(14, 12),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Child = editorContent
            };
            Themes.ThemeBrushes.Bind(editorCard, Border.BackgroundProperty, "ItemHoverBrush");
            // Рамка не красится намеренно: в разметке она привязана к ресурсу
            // DividerBrush (ProfilesWindow.xaml:99), которого в словарях нет,
            // поэтому в версии для Windows рамка карточки не рисуется вовсе.
            Grid.SetColumn(editorCard, 1);
            body.Children.Add(editorCard);
            Grid.SetRow(body, 2);

            _errorLabel = new TextBlock { Foreground = Themes.ThemeBrushes.Get("ErrorBrush", "#DC2626"), TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(0, 0, 0, 12) };
            Grid.SetRow(_errorLabel, 3);

            var buttons = BuildButtons();
            Grid.SetRow(buttons, 4);

            grid.Children.Add(header);
            grid.Children.Add(activeRow);
            grid.Children.Add(body);
            grid.Children.Add(_errorLabel);
            grid.Children.Add(buttons);

            Content = grid;

            RefreshList();
        }

        /// <summary>
        /// Строка списка: имя и зелёный бейдж «активная» у активной записи
        /// (ProfilesWindow.xaml:74-90).
        /// </summary>
        private Control BuildProfileRow(UserProfile? profile)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text = profile?.Name ?? string.Empty,
                VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Profiles.Active"),
                Margin = new Thickness(8, 0, 0, 0),
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                Foreground = Themes.ThemeBrushes.Get("SuccessBrush", "#16A34A"),
                VerticalAlignment = VerticalAlignment.Center,
                // Признака «активная» у модели профиля нет: активной считается та,
                // что сейчас выбрана службой профилей, как и в вьюмодели WPF.
                IsVisible = profile is not null && profile.Id == _profileService.CurrentProfile?.Id
            });
            return row;
        }

        private StackPanel BuildEditor()
        {
            var panel = new StackPanel { Spacing = 8 };

            var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var nameLabel = new TextBlock { Text = LocalizationManager.T("Profiles.Name"), VerticalAlignment = VerticalAlignment.Center, Width = 100 };
            ThemeBrushes.Bind(nameLabel, TextBlock.ForegroundProperty, "FormLabelBrush");
            nameRow.Children.Add(nameLabel);
            nameRow.Children.Add(_nameBox);
            panel.Children.Add(nameRow);

            var passRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var passLabel = new TextBlock { Text = LocalizationManager.T("Profiles.Password"), VerticalAlignment = VerticalAlignment.Center, Width = 100 };
            ThemeBrushes.Bind(passLabel, TextBlock.ForegroundProperty, "FormLabelBrush");
            passRow.Children.Add(passLabel);
            passRow.Children.Add(_passwordInput);
            panel.Children.Add(passRow);

            panel.Children.Add(_protectCheck);

            return panel;
        }

        private Control BuildButtons()
        {
            // Раскладка по разметке (ProfilesWindow.xaml:161): отмена прижата
            // влево, остальные кнопки вправо. Удаление, как и отмена, идёт мягкой
            // заливкой, три остальные акцентом.
            var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };

            var delete = SoftButton("Profiles.Delete", "IconDelete", 120);
            delete.Click += (_, _) => OnDelete();
            panel.Children.Add(delete);

            // Кнопка активации стоит между удалением и сохранением, как в разметке
            // (ProfilesWindow.xaml:183). Без неё сменить активный профиль
            // из интерфейса было нельзя вовсе: команда живёт в ProfilesViewModel,
            // а тот в Linux-сборку не входит.
            var activate = AccentButton("Profiles.Activate", "IconStar", 150);
            activate.Click += (_, _) => OnActivate();
            panel.Children.Add(activate);

            var save = AccentButton("Profiles.Save", "IconOk", 130);
            save.Click += (_, _) => OnSave();
            panel.Children.Add(save);

            var create = AccentButton("Profiles.Create", "IconAdd", 130);
            create.Click += (_, _) => OnCreate();
            panel.Children.Add(create);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            var cancel = BuildCancelButton(110);
            // Отступ кнопки отмены из разметки (ProfilesWindow.xaml:162),
            // общая тема кнопки даёт 14,9.
            cancel.Padding = new Thickness(12, 6);
            Grid.SetColumn(cancel, 0);
            row.Children.Add(cancel);

            Grid.SetColumn(panel, 1);
            row.Children.Add(panel);
            return row;
        }

        /// <summary>Кнопка акцентной заливки со значком и подписью.</summary>
        private static Button AccentButton(string textKey, string iconKey, double width)
        {
            var button = new Button
            {
                Content = IconHelper.IconAndText(iconKey, LocalizationManager.T(textKey), 14, "ButtonTextBrush"),
                Width = width
            };
            button.Styled(Themes.ControlThemes.ModernButton);
            return button;
        }

        /// <summary>Кнопка мягкой заливки: в разметке ей задан фон ItemHoverBrush.</summary>
        private static Button SoftButton(string textKey, string iconKey, double width)
        {
            var button = new Button
            {
                Content = IconHelper.IconAndText(iconKey, LocalizationManager.T(textKey), 14, "TextPrimaryBrush"),
                Width = width
            };
            button.Styled(Themes.ControlThemes.ModernButton);
            Themes.ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            Themes.ThemeBrushes.Bind(button, Button.ForegroundProperty, "TextPrimaryBrush");
            return button;
        }

        private UserProfile? SelectedProfile => _profilesList.SelectedItem as UserProfile;

        /// <summary>
        /// Смена активной записи выпадающим списком: применяется сразу, как
        /// в вьюмодели WPF (ProfilesViewModel.cs, сеттер CurrentProfile).
        /// </summary>
        private void OnActiveAccountChanged()
        {
            if (_suppressActiveAccount)
                return;
            if (_activeAccountBox.SelectedItem is not UserProfile profile)
                return;
            if (profile.Id == _profileService.CurrentProfile?.Id)
                return;

            try
            {
                _profileService.SetCurrentProfile(profile.Id);
                RefreshList(profile.Id);
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void ApplyCurrentAccountLabel()
        {
            var name = _profileService.CurrentProfile?.Name ?? "-";
            _currentAccountLabel.Text = string.Format(LocalizationManager.T("Profiles.CurrentAccount"), name);
        }

        private void RefreshList(string? selectId = null)
        {
            var profiles = _profileService.Profiles.ToList();

            _suppressActiveAccount = true;
            _activeAccountBox.ItemsSource = profiles;
            _activeAccountBox.SelectedItem = profiles.FirstOrDefault(p => p.Id == _profileService.CurrentProfile?.Id);
            _suppressActiveAccount = false;

            _suppressSelection = true;
            _profilesList.ItemsSource = profiles;

            selectId ??= _profileService.CurrentProfile?.Id;
            var target = _profileService.Profiles.FirstOrDefault(p => p.Id == selectId);
            _profilesList.SelectedItem = target;
            _suppressSelection = false;

            if (target == null)
                ClearEditor();
            else
                LoadEditor(target);
            ApplyCurrentAccountLabel();
        }

        private void OnSelectionChanged()
        {
            if (_suppressSelection)
                return;
            _errorLabel.IsVisible = false;
            var profile = SelectedProfile;
            if (profile == null)
                ClearEditor();
            else
                LoadEditor(profile);
        }

        private void LoadEditor(UserProfile profile)
        {
            // Заголовок карточки и приглашение переключаются вместе с выбором,
            // как триггеры разметки (ProfilesWindow.xaml:101-125).
            _editingTitle.Text = string.Format(LocalizationManager.T("Profiles.Editing"), profile.Name);
            _selectPrompt.IsVisible = false;
            _fieldsPanel.IsVisible = true;
            _nameBox.Text = profile.Name;
            _protectCheck.IsChecked = profile.HasPassword;
            _passwordInput.Clear();
        }

        private void ClearEditor()
        {
            _editingTitle.Text = LocalizationManager.T("Profiles.EditingNone");
            _selectPrompt.IsVisible = true;
            _fieldsPanel.IsVisible = false;
            _nameBox.Text = string.Empty;
            _protectCheck.IsChecked = false;
            _passwordInput.Clear();
        }

        private void ShowError(string message)
        {
            _errorLabel.Text = message;
            _errorLabel.IsVisible = true;
        }

        private void OnCreate()
        {
            _errorLabel.IsVisible = false;
            var name = _nameBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowError(LocalizationManager.T("Profiles.EmptyName"));
                return;
            }

            try
            {
                var password = _protectCheck.IsChecked == true ? _passwordInput.Password : null;
                var created = _profileService.CreateProfile(name, password);
                RefreshList(created.Id);
            }
            catch (ArgumentException ex)
            {
                ShowError(ex.Message);
            }
        }

        private void OnSave()
        {
            _errorLabel.IsVisible = false;
            var profile = SelectedProfile;
            if (profile == null)
            {
                ShowError(LocalizationManager.T("Profiles.EmptySelection"));
                return;
            }

            var name = _nameBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowError(LocalizationManager.T("Profiles.EmptyName"));
                return;
            }

            try
            {
                if (!string.Equals(profile.Name, name, StringComparison.Ordinal))
                    _profileService.RenameProfile(profile.Id, name);

                var wantPassword = _protectCheck.IsChecked == true;
                if (wantPassword && !string.IsNullOrEmpty(_passwordInput.Password))
                    _profileService.SetPassword(profile.Id, _passwordInput.Password);
                else if (!wantPassword && profile.HasPassword)
                    _profileService.SetPassword(profile.Id, null);

                RefreshList(profile.Id);
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        /// <summary>Делает выбранный профиль активным и обновляет список с подписью.</summary>
        private void OnActivate()
        {
            _errorLabel.IsVisible = false;
            var profile = SelectedProfile;
            if (profile == null)
            {
                ShowError(LocalizationManager.T("Profiles.NoSelectionToActivate"));
                return;
            }

            try
            {
                _profileService.SetCurrentProfile(profile.Id);
                RefreshList(profile.Id);
                ApplyCurrentAccountLabel();
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void OnDelete()
        {
            _errorLabel.IsVisible = false;
            var profile = SelectedProfile;
            if (profile == null)
                return;

            var confirm = string.Format(LocalizationManager.T("Profiles.DeleteConfirm"), profile.Name);
            var dialog = AppServices.GetRequiredService<IDialogService>();
            if (!dialog.Confirm(confirm, LocalizationManager.T("Profiles.Title")))
                return;

            try
            {
                if (!_profileService.DeleteProfile(profile.Id))
                {
                    ShowError(LocalizationManager.T("Profiles.CantDeleteLast"));
                    return;
                }

                RefreshList();
            }
            catch (Exception ex)
            {
                // Например, не удалось сохранить реестр profiles.json — удаление откачено
                // сервисом, показываем причину пользователю.
                ShowError(ex.Message);
            }
        }
    }
}
#endif