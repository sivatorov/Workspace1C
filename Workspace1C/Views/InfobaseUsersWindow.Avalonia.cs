#if LINUX
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Пользователи информационных баз» (выпуск 1.0.0.83, Avalonia/Linux) — пара
    /// WPF-окна. COM-коннектора 1С на Linux нет, поэтому операции возвращают понятный
    /// отказ, а подсказка вверху окна объясняет ограничение.
    /// </summary>
    public sealed class InfobaseUsersWindow : ModalWindowBase
    {
        private static readonly double[] UserColumns = { -1.2, -1.6, -0.9, -0.8, -1 };

        private readonly InfobaseUsersViewModel _vm;

        public InfobaseUsersWindow(InfobaseUsersViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            Title = LocalizationManager.T("IbUsers.Title");
            // Issue #119 (T3): целевой стартовый размер из таблицы 1.4 (паритет с WPF).
            Width = 1100;
            Height = 720;
            MinWidth = 900;
            MinHeight = 560;
            FontSize = 13;
            CanResize = true;

            var root = new Grid
            {
                Margin = new Thickness(16),
                ColumnDefinitions = AvaloniaUi.Columns(-0.8, 16, -1.4),
                RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) }
            };

            // ---- Слева: базы ----
            var left = AvaloniaUi.Rows(0, 0, -1, 0);
            AvaloniaUi.Place(left, AvaloniaUi.Section("IbUsers.Bases"), 0);
            AvaloniaUi.Place(left, AvaloniaUi.Wrap(
                AvaloniaUi.Label("Extensions.Search"),
                AvaloniaUi.BoundTextBox(nameof(InfobaseUsersViewModel.SearchText), 180),
                AvaloniaUi.Button("Extensions.CheckAll", (_, _) => _vm.SetAllChecked(true), 90),
                AvaloniaUi.Button("Extensions.UncheckAll", (_, _) => _vm.SetAllChecked(false), 90)), 1);
            var targets = new ListBox { ItemsSource = _vm.Targets };
            targets.ItemTemplate = new FuncDataTemplate<ExtensionTargetRow>((_, _) => BuildTargetRow());
            targets.Bind(SelectingItemsControl.SelectedItemProperty,
                new Binding(nameof(InfobaseUsersViewModel.SelectedTarget)) { Mode = BindingMode.TwoWay });
            AvaloniaUi.Place(left, targets, 2);
            AvaloniaUi.Place(left, AvaloniaUi.BoundText(nameof(InfobaseUsersViewModel.CheckedSummary)), 3);
            AvaloniaUi.Place(root, left, 0, 0);

            // ---- Справа ----
            var right = AvaloniaUi.Rows(0, -1.2, 0, -0.6);
            var hint = AvaloniaUi.BoundText(nameof(InfobaseUsersViewModel.PlatformHint), 11);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, 0, 0, 8);
            AvaloniaUi.Place(right, hint, 0);

            var usersPanel = AvaloniaUi.Rows(0, 0, 0, -1, 0);
            AvaloniaUi.Place(usersPanel, AvaloniaUi.Section("IbUsers.UsersSection"), 0);
            AvaloniaUi.Place(usersPanel, AvaloniaUi.BoundText(nameof(InfobaseUsersViewModel.SelectedBaseText)), 1);
            AvaloniaUi.Place(usersPanel, AvaloniaUi.Header(new[]
            {
                "IbUsers.ColName", "IbUsers.ColFullName", "IbUsers.ColAuth", "IbUsers.ColPassword", "IbUsers.ColOsUser"
            }, UserColumns), 2);
            var users = new ListBox { ItemsSource = _vm.Users };
            users.ItemTemplate = new FuncDataTemplate<InfobaseUserRow>((_, _) => BuildUserRow());
            users.Bind(SelectingItemsControl.SelectedItemProperty,
                new Binding(nameof(InfobaseUsersViewModel.SelectedUser)) { Mode = BindingMode.TwoWay });
            AvaloniaUi.Place(usersPanel, users, 3);
            var refresh = AvaloniaUi.Button("IbUsers.Refresh", async (_, _) => await _vm.RefreshUsersAsync(), 150);
            var loadRoles = AvaloniaUi.Button("IbUsers.LoadRoles", async (_, _) => await _vm.LoadSelectedUserRolesAsync(), 200);
            var newUser = AvaloniaUi.Button("IbUsers.NewUser", (_, _) => _vm.NewUser(), 130);
            foreach (var b in new[] { refresh, loadRoles })
                b.Bind(IsEnabledProperty, new Binding(nameof(InfobaseUsersViewModel.IsIdle)));
            AvaloniaUi.Place(usersPanel, AvaloniaUi.ButtonRow(refresh, loadRoles, newUser), 4);
            AvaloniaUi.Place(right, AvaloniaUi.Card(usersPanel), 1);

            var form = new StackPanel();
            form.Children.Add(AvaloniaUi.Section("IbUsers.FormSection"));
            form.Children.Add(AvaloniaUi.Wrap(
                AvaloniaUi.Label("IbUsers.Name"), AvaloniaUi.BoundTextBox(nameof(InfobaseUsersViewModel.UserName), 200),
                AvaloniaUi.Label("IbUsers.FullName"), AvaloniaUi.BoundTextBox(nameof(InfobaseUsersViewModel.FullName), 260)));
            form.Children.Add(AvaloniaUi.Wrap(
                AvaloniaUi.BoundCheckBox("IbUsers.SetPassword", nameof(InfobaseUsersViewModel.SetPasswordFlag)),
                AvaloniaUi.Label("IbUsers.Password"),
                AvaloniaUi.BoundPassword(nameof(InfobaseUsersViewModel.Password), 200)));
            form.Children.Add(AvaloniaUi.Wrap(
                AvaloniaUi.BoundCheckBox("IbUsers.StandardAuth", nameof(InfobaseUsersViewModel.StandardAuthentication)),
                AvaloniaUi.BoundCheckBox("IbUsers.ShowInList", nameof(InfobaseUsersViewModel.ShowInList)),
                AvaloniaUi.BoundCheckBox("IbUsers.OsAuth", nameof(InfobaseUsersViewModel.OSAuthentication)),
                AvaloniaUi.Label("IbUsers.OsUser"), AvaloniaUi.BoundTextBox(nameof(InfobaseUsersViewModel.OSUser), 200)));
            form.Children.Add(AvaloniaUi.Wrap(
                AvaloniaUi.Label("IbUsers.Roles"), AvaloniaUi.BoundTextBox(nameof(InfobaseUsersViewModel.RolesText), 520),
                AvaloniaUi.BoundCheckBox("IbUsers.ReplaceRoles", nameof(InfobaseUsersViewModel.ReplaceRoles))));
            var available = AvaloniaUi.BoundText(nameof(InfobaseUsersViewModel.AvailableRolesText), 11);
            available.TextWrapping = TextWrapping.Wrap;
            available.MaxHeight = 48;
            form.Children.Add(available);

            var upsert = AvaloniaUi.Button("IbUsers.Upsert", async (_, _) => await OnBatchAsync(InfobaseUserBatchOperation.Upsert), 220, accent: true);
            var password = AvaloniaUi.Button("IbUsers.ChangePassword", async (_, _) => await OnBatchAsync(InfobaseUserBatchOperation.SetPassword), 160);
            var disable = AvaloniaUi.Button("IbUsers.Disable", async (_, _) => await OnBatchAsync(InfobaseUserBatchOperation.DisableLogin), 150);
            var delete = AvaloniaUi.Button("IbUsers.Delete", async (_, _) => await OnBatchAsync(InfobaseUserBatchOperation.Delete), 120);
            foreach (var b in new[] { upsert, password, disable, delete })
                b.Bind(IsEnabledProperty, new Binding(nameof(InfobaseUsersViewModel.IsIdle)));
            form.Children.Add(AvaloniaUi.ButtonRow(upsert, password, disable, delete));
            form.Children.Add(AvaloniaUi.Label("IbUsers.BatchHint", 11));
            AvaloniaUi.Place(right, AvaloniaUi.Card(form), 2);

            AvaloniaUi.Place(right, AvaloniaUi.ReadOnlyText(nameof(InfobaseUsersViewModel.LogText)), 3);
            AvaloniaUi.Place(root, right, 0, 2);

            var cancel = AvaloniaUi.Button("Common.Cancel", (_, _) => _vm.Cancel(), 110);
            cancel.Bind(IsEnabledProperty, new Binding(nameof(InfobaseUsersViewModel.IsBusy)));
            var close = AvaloniaUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var bottom = AvaloniaUi.ButtonRow(cancel, close);
            bottom.HorizontalAlignment = HorizontalAlignment.Right;
            bottom.Margin = new Thickness(0, 12, 0, 0);
            AvaloniaUi.Place(root, bottom, 1, 0, 3);

            Content = root;
            Closed += (_, _) => _vm.Cancel();
        }

        private static Control BuildTargetRow()
        {
            var check = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
            check.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(ExtensionTargetRow.IsChecked)) { Mode = BindingMode.TwoWay });
            var name = AvaloniaUi.CellBound(nameof(ExtensionTargetRow.Name), 1, bold: true);
            var status = AvaloniaUi.CellBound(nameof(ExtensionTargetRow.Status), 2);
            status.Opacity = 0.75;
            var grid = new Grid { ColumnDefinitions = AvaloniaUi.Columns(32, -2, -1), Height = 28 };
            grid.Children.Add(check);
            grid.Children.Add(name);
            grid.Children.Add(status);
            return grid;
        }

        private static Control BuildUserRow()
        {
            var grid = new Grid { ColumnDefinitions = AvaloniaUi.Columns(UserColumns), Height = 26 };
            grid.Children.Add(AvaloniaUi.CellBound(nameof(InfobaseUserRow.Name), 0, bold: true));
            grid.Children.Add(AvaloniaUi.CellBound(nameof(InfobaseUserRow.FullName), 1));
            grid.Children.Add(AvaloniaUi.CellBound(nameof(InfobaseUserRow.AuthText), 2));
            grid.Children.Add(AvaloniaUi.CellBound(nameof(InfobaseUserRow.PasswordText), 3));
            grid.Children.Add(AvaloniaUi.CellBound(nameof(InfobaseUserRow.OSUser), 4));
            return grid;
        }

        private static IDialogService Dialogs => AppServices.GetRequiredService<IDialogService>();

        private async Task OnBatchAsync(InfobaseUserBatchOperation operation)
        {
            var title = Title ?? string.Empty;
            var count = _vm.CheckedTargets.Count;
            if (count == 0)
            {
                Dialogs.ShowInfo(LocalizationManager.T("IbUsers.NoChecked"), title);
                return;
            }
            var error = _vm.ValidateFor(operation);
            if (error is not null)
            {
                Dialogs.ShowInfo(error, title);
                return;
            }
            if (!Dialogs.Confirm(InfobaseUsersDialogs.ConfirmText(operation, _vm.UserName, count), title))
                return;
            await _vm.RunBatchAsync(operation);
        }
    }
}
#endif
