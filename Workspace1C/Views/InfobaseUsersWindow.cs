#if WINDOWS
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Пользователи информационных баз» (выпуск 1.0.0.83, Windows/WPF):
    /// список пользователей выбранной базы и пакетные операции над пользователем
    /// во всех отмеченных базах. Логика — в <see cref="InfobaseUsersViewModel"/>.
    /// </summary>
    public sealed class InfobaseUsersWindow : Window
    {
        private readonly InfobaseUsersViewModel _vm;

        public InfobaseUsersWindow(InfobaseUsersViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            // Issue #119 (1.0.1.119): стартовый размер уменьшен по таблице 1.4 плана.
            WpfUi.SetupWindow(this, "IbUsers.Title", 1100, 720);

            var root = new Grid { Margin = new Thickness(16) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ---- Слева: базы ----
            var left = WpfUi.Rows(0, 0, -1, 0);
            left.Children.Add(WpfUi.Section("IbUsers.Bases"));
            var searchRow = WpfUi.Wrap(WpfUi.Label("Extensions.Search"),
                WpfUi.BoundTextBox(nameof(InfobaseUsersViewModel.SearchText), 180),
                WpfUi.Button("Extensions.CheckAll", (_, _) => _vm.SetAllChecked(true), 90),
                WpfUi.Button("Extensions.UncheckAll", (_, _) => _vm.SetAllChecked(false), 90));
            WpfUi.Place(left, searchRow, 1);
            var bases = WpfUi.Grid(_vm.Targets);
            bases.Columns.Add(WpfUi.CheckColumn(nameof(ExtensionTargetRow.IsChecked)));
            bases.Columns.Add(WpfUi.Column("Extensions.ColBase", nameof(ExtensionTargetRow.Name), new DataGridLength(2, DataGridLengthUnitType.Star)));
            bases.Columns.Add(WpfUi.Column("Extensions.ColStatus", nameof(ExtensionTargetRow.Status), new DataGridLength(1, DataGridLengthUnitType.Star)));
            bases.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
                new Binding(nameof(InfobaseUsersViewModel.SelectedTarget)) { Mode = BindingMode.TwoWay });
            WpfUi.Place(left, bases, 2);
            WpfUi.Place(left, WpfUi.BoundText(nameof(InfobaseUsersViewModel.CheckedSummary)), 3);
            WpfUi.Place(root, left, 0, 0);

            // ---- Справа: пользователи базы, форма, журнал ----
            var right = WpfUi.Rows(0, -1.2, 0, -0.6);
            var hint = WpfUi.BoundText(nameof(InfobaseUsersViewModel.PlatformHint), 11);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, 0, 0, 8);
            WpfUi.Place(right, hint, 0);

            var usersPanel = WpfUi.Rows(0, 0, -1, 0);
            usersPanel.Children.Add(WpfUi.Section("IbUsers.UsersSection"));
            WpfUi.Place(usersPanel, WpfUi.BoundText(nameof(InfobaseUsersViewModel.SelectedBaseText)), 1);
            var users = WpfUi.Grid(_vm.Users);
            users.Margin = new Thickness(0, 6, 0, 0);
            users.Columns.Add(WpfUi.Column("IbUsers.ColName", nameof(InfobaseUserRow.Name), new DataGridLength(1.2, DataGridLengthUnitType.Star)));
            users.Columns.Add(WpfUi.Column("IbUsers.ColFullName", nameof(InfobaseUserRow.FullName), new DataGridLength(1.6, DataGridLengthUnitType.Star)));
            users.Columns.Add(WpfUi.Column("IbUsers.ColAuth", nameof(InfobaseUserRow.AuthText), new DataGridLength(0.9, DataGridLengthUnitType.Star)));
            users.Columns.Add(WpfUi.Column("IbUsers.ColPassword", nameof(InfobaseUserRow.PasswordText), new DataGridLength(0.8, DataGridLengthUnitType.Star)));
            users.Columns.Add(WpfUi.Column("IbUsers.ColOsUser", nameof(InfobaseUserRow.OSUser), new DataGridLength(1, DataGridLengthUnitType.Star)));
            users.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
                new Binding(nameof(InfobaseUsersViewModel.SelectedUser)) { Mode = BindingMode.TwoWay });
            WpfUi.Place(usersPanel, users, 2);
            var refresh = WpfUi.Button("IbUsers.Refresh", async (_, _) => await _vm.RefreshUsersAsync(), 150);
            var loadRoles = WpfUi.Button("IbUsers.LoadRoles", async (_, _) => await _vm.LoadSelectedUserRolesAsync(), 200);
            var newUser = WpfUi.Button("IbUsers.NewUser", (_, _) => _vm.NewUser(), 130);
            foreach (var b in new[] { refresh, loadRoles })
                b.SetBinding(IsEnabledProperty, new Binding(nameof(InfobaseUsersViewModel.IsIdle)));
            WpfUi.Place(usersPanel, WpfUi.ButtonRow(refresh, loadRoles, newUser), 3);
            WpfUi.Place(right, WpfUi.Card(usersPanel), 1);

            var form = new StackPanel();
            form.Children.Add(WpfUi.Section("IbUsers.FormSection"));
            form.Children.Add(WpfUi.Wrap(
                WpfUi.Label("IbUsers.Name"), WpfUi.BoundTextBox(nameof(InfobaseUsersViewModel.UserName), 200),
                WpfUi.Label("IbUsers.FullName"), WpfUi.BoundTextBox(nameof(InfobaseUsersViewModel.FullName), 260)));
            form.Children.Add(WpfUi.Wrap(
                WpfUi.BoundCheckBox("IbUsers.SetPassword", nameof(InfobaseUsersViewModel.SetPasswordFlag)),
                WpfUi.Label("IbUsers.Password"),
                WpfUi.BoundPassword(_vm, nameof(InfobaseUsersViewModel.Password), () => _vm.Password, v => _vm.Password = v, 200)));
            form.Children.Add(WpfUi.Wrap(
                WpfUi.BoundCheckBox("IbUsers.StandardAuth", nameof(InfobaseUsersViewModel.StandardAuthentication)),
                WpfUi.BoundCheckBox("IbUsers.ShowInList", nameof(InfobaseUsersViewModel.ShowInList)),
                WpfUi.BoundCheckBox("IbUsers.OsAuth", nameof(InfobaseUsersViewModel.OSAuthentication)),
                WpfUi.Label("IbUsers.OsUser"), WpfUi.BoundTextBox(nameof(InfobaseUsersViewModel.OSUser), 200)));
            var rolesBox = WpfUi.BoundTextBox(nameof(InfobaseUsersViewModel.RolesText), 520);
            form.Children.Add(WpfUi.Wrap(WpfUi.Label("IbUsers.Roles"), rolesBox,
                WpfUi.BoundCheckBox("IbUsers.ReplaceRoles", nameof(InfobaseUsersViewModel.ReplaceRoles))));
            var available = WpfUi.BoundText(nameof(InfobaseUsersViewModel.AvailableRolesText), 11);
            available.TextWrapping = TextWrapping.Wrap;
            available.MaxHeight = 48;
            form.Children.Add(available);

            var upsert = WpfUi.Button("IbUsers.Upsert", async (_, _) => await OnBatchAsync(InfobaseUserBatchOperation.Upsert), 220, accent: true);
            var password = WpfUi.Button("IbUsers.ChangePassword", async (_, _) => await OnBatchAsync(InfobaseUserBatchOperation.SetPassword), 160);
            var disable = WpfUi.Button("IbUsers.Disable", async (_, _) => await OnBatchAsync(InfobaseUserBatchOperation.DisableLogin), 150);
            var delete = WpfUi.Button("IbUsers.Delete", async (_, _) => await OnBatchAsync(InfobaseUserBatchOperation.Delete), 120);
            foreach (var b in new[] { upsert, password, disable, delete })
                b.SetBinding(IsEnabledProperty, new Binding(nameof(InfobaseUsersViewModel.IsIdle)));
            form.Children.Add(WpfUi.ButtonRow(upsert, password, disable, delete));
            form.Children.Add(WpfUi.Label("IbUsers.BatchHint", 11));
            WpfUi.Place(right, WpfUi.Card(form), 2);

            WpfUi.Place(right, WpfUi.ReadOnlyText(nameof(InfobaseUsersViewModel.LogText)), 3);
            WpfUi.Place(root, right, 0, 2);

            // ---- Низ ----
            var cancel = WpfUi.Button("Common.Cancel", (_, _) => _vm.Cancel(), 110);
            cancel.SetBinding(IsEnabledProperty, new Binding(nameof(InfobaseUsersViewModel.IsBusy)));
            var close = WpfUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var bottom = WpfUi.ButtonRow(cancel, close);
            bottom.HorizontalAlignment = HorizontalAlignment.Right;
            bottom.Margin = new Thickness(0, 12, 0, 0);
            WpfUi.Place(root, bottom, 1, 0, 3);

            Content = root;
            Closed += (_, _) => _vm.Cancel();
        }

        private static IDialogService Dialogs => AppServices.GetRequiredService<IDialogService>();

        private async System.Threading.Tasks.Task OnBatchAsync(InfobaseUserBatchOperation operation)
        {
            var count = _vm.CheckedTargets.Count;
            if (count == 0)
            {
                Dialogs.ShowInfo(LocalizationManager.T("IbUsers.NoChecked"), Title);
                return;
            }
            var error = _vm.ValidateFor(operation);
            if (error is not null)
            {
                Dialogs.ShowInfo(error, Title);
                return;
            }
            if (!Dialogs.Confirm(InfobaseUsersDialogs.ConfirmText(operation, _vm.UserName, count), Title))
                return;
            await _vm.RunBatchAsync(operation);
        }
    }
}
#endif
