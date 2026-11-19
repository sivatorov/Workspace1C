#if LINUX
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.Themes;

namespace Workspace1C
{
    /// <summary>
    /// Диалог удаления информационной базы кластера (issue #3, комментарий 7OH от
    /// 2026-10-17) — Avalonia/Linux-версия WPF-окна <see cref="RacInfobaseDropWindow"/>:
    /// выбор «Просто удалить из кластера» или «Удалить с удалением на сервере СУБД».
    /// В кластере бывают только клиент-серверные базы (7OH 18.10 20:24 и 19.10 19:12),
    /// поэтому вариант «с удалением на сервере СУБД» доступен всегда — независимо от
    /// распознанного типа СУБД (1.0.0.21). Команда rac: «infobase drop [--drop-database]».
    /// Рядом с именем базы выводится распознанный из summary тип и сервер СУБД
    /// (issue #3, часть 1) — диагностическая подпись.
    /// </summary>
    public sealed class RacInfobaseDropWindow : ModalWindowBase
    {
        /// <summary>Удалять ли заодно базу данных на сервере СУБД (флаг rac «--drop-database»).</summary>
        public bool DropDatabase { get; private set; }

        private RadioButton? _withDatabaseRadio;

        public RacInfobaseDropWindow(
            string infobaseName, bool canDropDatabase, string? dbmsText = null, string? dbServer = null)
        {
            Title = LocalizationManager.T("ServerMonitor.Infobase.Drop.Title");
            Width = 560;
            Height = 300;
            MinWidth = 520;
            MinHeight = 260;
            FontSize = 13;
            CanResize = false;
            Content = BuildRoot(infobaseName, canDropDatabase, dbmsText, dbServer);
        }

        /// <summary>Показывает окно модально (синхронно).</summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private Control BuildRoot(string infobaseName, bool canDropDatabase, string? dbmsText, string? dbServer)
        {
            var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };

            // Кнопки внизу.
            // Удаление базы кластера — опасное действие: DangerButton (issue #26).
            var drop = new Button { Content = LocalizationManager.T("ServerMonitor.Infobase.Drop.Confirm"), Width = 140, Height = 36, IsDefault = true };
            drop.Styled(ControlThemes.DangerButton);
            drop.Click += (_, _) => OnDrop();
            var cancel = new Button { Content = LocalizationManager.T("Common.Cancel"), Width = 120, Height = 36 };
            cancel.Styled(ControlThemes.SecondaryButton);
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
                Children = { drop, cancel }
            };
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            // Вопрос.
            var question = new TextBlock
            {
                FontSize = ThemeManager.FontSizeSubtitle,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4),
                Text = string.Format(
                    LocalizationManager.T("ServerMonitor.Infobase.Drop.QuestionFormat"),
                    string.IsNullOrWhiteSpace(infobaseName) ? "—" : infobaseName)
            };

            // Варианты удаления.
            RadioButton Radio(string textKey, string group, bool isChecked = false)
            {
                var radio = new RadioButton
                {
                    Content = LocalizationManager.T(textKey),
                    GroupName = group,
                    IsChecked = isChecked,
                    Margin = new Thickness(0, 2, 0, 8)
                };
                return radio;
            }

            var fromCluster = Radio("ServerMonitor.Infobase.Drop.OnlyFromCluster", "DropOption", isChecked: true);
            _withDatabaseRadio = Radio("ServerMonitor.Infobase.Drop.WithDatabase", "DropOption");
            var withDatabase = _withDatabaseRadio;
            // Вариант «с удалением на сервере СУБД» показываем всем базам списка кластера
            // (в кластере бывают только клиент-серверные базы, 7OH 19.10 19:12); флаг
            // canDropDatabase приходит от VM и для баз кластера всегда true (1.0.0.21).
            withDatabase.IsVisible = canDropDatabase;

            var options = new StackPanel
            {
                Spacing = 2,
                Children = { fromCluster, withDatabase }
            };
            var group = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10),
                Margin = new Thickness(0, 8, 0, 0),
                Child = options
            };
            ThemeBrushes.Bind(group, Border.BorderBrushProperty, "BorderColorBrush");

            var form = new StackPanel();
            form.Children.Add(question);
            // Диагностика (issue #3, часть 1): распознанный тип СУБД и сервер СУБД.
            // Показывается, только если тип СУБД непустой — если вариант «с удалением
            // на сервере СУБД» не появился, сразу видно, что распарсилось из summary.
            if (!string.IsNullOrWhiteSpace(dbmsText))
            {
                form.Children.Add(new TextBlock
                {
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4),
                    Text = string.Format(
                        LocalizationManager.T("ServerMonitor.Infobase.Drop.DbmsFormat"),
                        dbmsText, string.IsNullOrWhiteSpace(dbServer) ? "—" : dbServer)
                });
            }
            form.Children.Add(group);
            root.Children.Add(form);
            return root;
        }

        private void OnDrop()
        {
            DropDatabase = _withDatabaseRadio?.IsChecked == true;
            DialogResult = true;
            Close();
        }
    }
}
#endif