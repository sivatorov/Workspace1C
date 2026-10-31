#if LINUX
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Свойства информационной базы» монитора «Серверы 1С» (issue #3, 7OH 17.10) —
    /// Avalonia/Linux-версия WPF-окна. Логика — в <see cref="RacInfobasePropertiesViewModel"/>:
    /// при открытии читаются полные свойства (rac «infobase info»), «Обновить» перечитывает
    /// их с введёнными учётными данными администратора ИБ.
    /// </summary>
    public sealed class RacInfobasePropertiesWindow : ModalWindowBase
    {
        private readonly RacInfobasePropertiesViewModel _vm;
        private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

        /// <summary>Запрос rac «infobase update» при сохранении (null при отмене).</summary>
        public RacInfobaseUpdateRequest? Result { get; private set; }

        public RacInfobasePropertiesWindow(RacInfobasePropertiesViewModel vm)
        {
            _vm = vm;
            DataContext = vm;
            Title = T("ServerMonitor.Infobase.Props.Title");
            Width = 820;
            // 2 вкладки (issue #3, 18.10 08:51); с 1.0.0.14 первой открывается
            // «Настройки», контент вкладок прокручивается (ScrollViewer).
            Height = 660;
            MinWidth = 680;
            MinHeight = 520;
            FontSize = 13;
            // Окно без максимизации (issue #3): в Avalonia кнопка разворота в системном
            // заголовке управляется через CanResize — выставляем false (ресайз некритичен).
            CanResize = false;
            Content = BuildRoot();
            Opened += async (_, _) => await _vm.RefreshAsync();
            // Модальное окно центрируется по владельцу, но не ограничивается рабочей
            // областью (issue #3, 7OH 19.10): на малых экранах 820х660 уходило за экран.
            Opened += (_, _) => ClampToWorkArea();
        }

        /// <summary>
        /// Не даёт окну выйти за рабочую область экрана на малых экранах (issue #3):
        /// размер ужимается с запасом 16 DIP, позиция клампится во все 4 стороны
        /// (проверенный чистый <see cref="WindowSizeMath"/>). Экран — тот, на котором
        /// окно показано (Avalonia центрирует модальное окно по владельцу без клампинга).
        /// Координаты окна — физические пиксели (PixelPoint/WorkingArea), размеры — DIP.
        /// </summary>
        private void ClampToWorkArea()
        {
            try
            {
                var screen = Screens.ScreenFromWindow(this);
                if (screen is null || screen.Scaling <= 0)
                    return;

                var wa = screen.WorkingArea;
                var scaling = screen.Scaling;
                var workLeft = wa.X / scaling;
                var workTop = wa.Y / scaling;
                var workRight = (wa.X + wa.Width) / scaling;
                var workBottom = (wa.Y + wa.Height) / scaling;

                const double spare = 16;
                Width = WindowSizeMath.ClampToWorkArea(
                    Width, Math.Max(MinWidth, workRight - workLeft - spare));
                Height = WindowSizeMath.ClampToWorkArea(
                    Height, Math.Max(MinHeight, workBottom - workTop - spare));

                var position = new PixelPoint(
                    (int)Math.Round(WindowSizeMath.FitLeft(
                        Position.X / scaling, Width, workLeft, workRight) * scaling),
                    (int)Math.Round(WindowSizeMath.FitTop(
                        Position.Y / scaling, Height, workTop, workBottom) * scaling));
                if (position != Position)
                    Position = position;
            }
            catch
            {
                // Клампинг — вспомогательная возможность; сбой не мешает диалогу.
            }
        }

        /// <summary>Показывает окно модально (синхронно).</summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private static string T(string key) => LocalizationManager.T(key);

        private Control BuildRoot()
        {
            var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };

            // Заголовок + «Обновить» справа (как в эталоне).
            var title = new TextBlock
            {
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                [!TextBlock.TextProperty] = new Binding(nameof(RacInfobasePropertiesViewModel.Name))
            };
            var refresh = new Button { Content = "⟳ " + T("ServerMonitor.Refresh"), Height = 32, HorizontalAlignment = HorizontalAlignment.Right };
            refresh.Styled(ControlThemes.ModernButton);
            ToolTip.SetTip(refresh, T("ServerMonitor.Infobase.Props.RefreshHint"));
            refresh.Click += async (_, _) => await _vm.RefreshAsync();
            var header = new Grid { Margin = new Thickness(0, 0, 0, 6), Children = { title, refresh } };
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // Кнопки внизу.
            var save = new Button { Content = T("Updates.Save"), Width = 130, Height = 36, IsDefault = true };
            save.Styled(ControlThemes.DialogConfirmButton);
            save.Click += (_, _) => OnSave();
            var cancel = BuildCancelActionButton(120);
            cancel.Click += (_, _) =>
            {
                DialogResult = false;
                Close();
            };
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Margin = new Thickness(0, 12, 0, 0),
                Children = { save, cancel }
            };
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            // Строка состояния загрузки (ошибка — красным).
            var status = new TextBlock
            {
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
                [!TextBlock.TextProperty] = new Binding(nameof(RacInfobasePropertiesViewModel.StatusText))
            };
            // Ошибка — на семантической кисти темы (аудит UI, п. 2.9).
            void UpdateStatusColor()
            {
                if (!_vm.StatusIsError)
                {
                    status.Foreground = null;
                    return;
                }
                status.Foreground = Avalonia.Application.Current?
                        .TryGetResource("ErrorBrush", Avalonia.Styling.ThemeVariant.Default, out var resource) == true
                    && resource is Avalonia.Media.IBrush errorBrush
                        ? errorBrush
                        : new SolidColorBrush(Color.Parse("#DC2626"));
            }
            _vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(RacInfobasePropertiesViewModel.StatusIsError))
                    UpdateStatusColor();
            };
            DockPanel.SetDock(status, Dock.Bottom);
            root.Children.Add(status);

            // ===================== 2 вкладки (issue #3, 18.10 08:51) =====================
            // «Основная» — общие/подключение: администратор ИБ, имя, защищённое соединение,
            // описание, сервер СУБД и тип, база/пользователь/пароль СУБД, «Разрешить выдачу
            // лицензий». «Настройки» — блокировка сеансов и регламентных, внешнее управление
            // сеансами и его обязательность, профили безопасности.
            var basicPanel = new StackPanel { Spacing = 2 };

            // Администратор информационной базы.
            var adminPassword = new PasswordBox { Height = 32 }.Styled(ControlThemes.ModernPasswordBox);
            adminPassword.Password = _vm.InfobasePassword;
            adminPassword.PasswordChanged += (_, _) => _vm.InfobasePassword = adminPassword.Password ?? string.Empty;
            var adminPanel = new StackPanel
            {
                Children =
                {
                    Label(T("ServerMonitor.Infobase.Props.AdminGroup")),
                    TwoColumns(
                        Field(T("ServerMonitor.Infobase.Props.AdminUser"), Text(nameof(RacInfobasePropertiesViewModel.InfobaseUser))),
                        Field(T("ServerMonitor.Infobase.Props.AdminPassword"), adminPassword)),
                    new TextBlock
                    {
                        FontSize = 11,
                        Opacity = 0.7,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 6, 0, 0),
                        Text = _vm.CredentialsSourceText
                    }
                }
            };
            basicPanel.Children.Add(Group(adminPanel));

            // «Разрешить выдачу лицензий» вынесено выше (issue #3, 1.0.0.14):
            // раньше на малых разрешениях чекбокс обрезался низом формы.
            var license = Check(T("ServerMonitor.Infobase.Props.LicenseDistribution"),
                nameof(RacInfobasePropertiesViewModel.LicenseDistributionAllowed));
            license.Margin = new Thickness(0, 8, 0, 0);
            license.Bind(IsEnabledProperty, new Binding(nameof(RacInfobasePropertiesViewModel.IsFullyLoaded)));
            basicPanel.Children.Add(license);

            basicPanel.Children.Add(TwoColumns(
                Field(T("ServerMonitor.Infobase.Name"), Text(nameof(RacInfobasePropertiesViewModel.Name), readOnly: true)),
                Field(T("ServerMonitor.Infobase.Props.SecureConnection"),
                    Text(nameof(RacInfobasePropertiesViewModel.SecureConnectionText), readOnly: true))));
            basicPanel.Children.Add(Field(T("ServerMonitor.Infobase.Description"), Text(nameof(RacInfobasePropertiesViewModel.Description))));

            var dbms = new ComboBox { Height = 32, HorizontalAlignment = HorizontalAlignment.Stretch };
            dbms.Styled(ControlThemes.ModernComboBox);
            dbms.ItemsSource = RacInfobasePropertiesViewModel.DbmsOptions;
            dbms.Bind(ComboBox.SelectedItemProperty,
                new Binding(nameof(RacInfobasePropertiesViewModel.Dbms), BindingMode.TwoWay));
            basicPanel.Children.Add(TwoColumns(
                Field(T("ServerMonitor.Infobase.DbServer"), Text(nameof(RacInfobasePropertiesViewModel.DbServer))),
                Field(T("ServerMonitor.Infobase.Dbms"), dbms)));

            var dbPassword = new PasswordBox { Height = 32 }.Styled(ControlThemes.ModernPasswordBox);
            ToolTip.SetTip(dbPassword, T("ServerMonitor.Infobase.Props.DbPasswordHint"));
            dbPassword.PasswordChanged += (_, _) => _vm.DbPassword = dbPassword.Password ?? string.Empty;
            basicPanel.Children.Add(ThreeColumns(
                Field(T("ServerMonitor.Infobase.DbName"), Text(nameof(RacInfobasePropertiesViewModel.DbName))),
                Field(T("ServerMonitor.Infobase.DbUser"), Text(nameof(RacInfobasePropertiesViewModel.DbUser))),
                Field(T("ServerMonitor.Infobase.DbPassword"), dbPassword)));

            var settingsPanel = new StackPanel { Spacing = 2 };

            // Блокировка начала сеансов.
            var deniedFrom = Text(nameof(RacInfobasePropertiesViewModel.DeniedFromText));
            var deniedTo = Text(nameof(RacInfobasePropertiesViewModel.DeniedToText));
            deniedFrom.Watermark = T("ServerMonitor.Infobase.Props.DateHint");
            deniedTo.Watermark = T("ServerMonitor.Infobase.Props.DateHint");
            var lockPanel = new StackPanel
            {
                Children =
                {
                    Check(T("ServerMonitor.Infobase.Props.SessionsDeny"), nameof(RacInfobasePropertiesViewModel.SessionsDeny)),
                    TwoColumns(
                        Field(T("ServerMonitor.Infobase.Props.DeniedFrom"), deniedFrom),
                        Field(T("ServerMonitor.Infobase.Props.DeniedTo"), deniedTo)),
                    Field(T("ServerMonitor.Infobase.Props.DeniedMessage"), Text(nameof(RacInfobasePropertiesViewModel.DeniedMessage))),
                    TwoColumns(
                        Field(T("ServerMonitor.Infobase.Props.PermissionCode"), Text(nameof(RacInfobasePropertiesViewModel.PermissionCode))),
                        Field(T("ServerMonitor.Infobase.Props.DeniedParameter"), Text(nameof(RacInfobasePropertiesViewModel.DeniedParameter))))
                }
            };
            var lockGroup = Group(lockPanel);
            lockGroup.Margin = new Thickness(0, 10, 0, 0);
            lockGroup.Bind(IsEnabledProperty, new Binding(nameof(RacInfobasePropertiesViewModel.IsFullyLoaded)));
            settingsPanel.Children.Add(lockGroup);

            var jobs = Check(T("ServerMonitor.Infobase.Props.ScheduledJobsDeny"), nameof(RacInfobasePropertiesViewModel.ScheduledJobsDeny));
            jobs.Margin = new Thickness(0, 10, 0, 0);
            settingsPanel.Children.Add(jobs);

            var extra = new StackPanel
            {
                Children =
                {
                    Field(T("ServerMonitor.Infobase.Props.ExternalSessionManager"),
                        Text(nameof(RacInfobasePropertiesViewModel.ExternalSessionManagerConnectionString))),
                    Check(T("ServerMonitor.Infobase.Props.ExternalSessionManagerRequired"),
                        nameof(RacInfobasePropertiesViewModel.ExternalSessionManagerRequired)),
                    TwoColumns(
                        Field(T("ServerMonitor.Infobase.Props.SecurityProfile"), Text(nameof(RacInfobasePropertiesViewModel.SecurityProfileName))),
                        Field(T("ServerMonitor.Infobase.Props.SafeModeSecurityProfile"),
                            Text(nameof(RacInfobasePropertiesViewModel.SafeModeSecurityProfileName))))
                }
            };
            extra.Bind(IsEnabledProperty, new Binding(nameof(RacInfobasePropertiesViewModel.IsFullyLoaded)));
            settingsPanel.Children.Add(extra);

            // Первой открывается «Настройки» (issue #3, 1.0.0.14): там чаще всего
            // вносят исправления; контент вкладок прокручивается (ScrollViewer) —
            // форма компактнее, но при малом разрешении ничего не теряется.
            var tabs = new TabControl { Margin = new Thickness(0, 6, 0, 0) };
            tabs.Items.Add(new TabItem
            {
                Header = T("ServerMonitor.Infobase.Props.Tab.Settings"),
                Padding = new Thickness(14, 6),
                FontSize = 12.5,
                Content = new ScrollViewer
                {
                    Content = settingsPanel,
                    // Горизонтальная полоса - fallback (issue #55): при узком окне
                    // содержимое вкладки не обрезается правым краем.
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
                }
            });
            tabs.Items.Add(new TabItem
            {
                Header = T("ServerMonitor.Infobase.Props.Tab.Basic"),
                Padding = new Thickness(14, 6),
                FontSize = 12.5,
                Content = new ScrollViewer
                {
                    Content = basicPanel,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
                }
            });
            root.Children.Add(tabs);
            return root;
        }

        private void OnSave()
        {
            var request = _vm.BuildRequest(out var error);
            if (request is null)
            {
                _dialogs.ShowWarning(error ?? string.Empty, Title ?? string.Empty);
                return;
            }
            Result = request;
            DialogResult = true;
            Close();
        }

        // ===================== Построители =====================

        private static TextBlock Label(string text)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                // Компактнее (issue #3, 1.0.0.14): часть полей не помещалась по высоте.
                Margin = new Thickness(0, 4, 0, 2)
            };
            // Issue #61: подпись поля формы — единая кисть FormLabelBrush.
            Themes.ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "FormLabelBrush");
            return block;
        }

        private static TextBox Text(string path, bool readOnly = false)
        {
            var box = new TextBox
            {
                Height = 32,
                VerticalContentAlignment = VerticalAlignment.Center,
                IsReadOnly = readOnly
            };
            box.Bind(TextBox.TextProperty, new Binding(path, readOnly ? BindingMode.OneWay : BindingMode.TwoWay));
            return box;
        }

        private static CheckBox Check(string text, string path)
        {
            var check = new CheckBox { Content = text, Margin = new Thickness(0, 6, 0, 0) };
            check.Bind(Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(path, BindingMode.TwoWay));
            return check;
        }

        private static Control Field(string label, Control editor) =>
            new StackPanel { Children = { Label(label), editor } };

        private static Grid TwoColumns(Control left, Control right)
        {
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(16)),
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star))
                }
            };
            Grid.SetColumn(left, 0);
            Grid.SetColumn(right, 2);
            grid.Children.Add(left);
            grid.Children.Add(right);
            return grid;
        }

        private static Grid ThreeColumns(Control a, Control b, Control c)
        {
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(16)),
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(16)),
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star))
                }
            };
            Grid.SetColumn(a, 0);
            Grid.SetColumn(b, 2);
            Grid.SetColumn(c, 4);
            grid.Children.Add(a);
            grid.Children.Add(b);
            grid.Children.Add(c);
            return grid;
        }

        private static Border Group(Control content)
        {
            var border = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 2, 10, 8),
                Child = content
            };
            ThemeBrushes.Bind(border, Border.BorderBrushProperty, "BorderColorBrush");
            return border;
        }
    }
}
#endif
