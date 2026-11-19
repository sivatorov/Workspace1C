#if LINUX
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Миграция между кластерами 1С» (issue #41, переработка по комментарию 7OH,
    /// Avalonia/Linux): перенос информационных баз между кластерами серверов 1С.
    /// Двухпанельная раскладка «Источник»/«Приёмник» (сервер + кластер + статусы),
    /// чеклист баз источника со статусами источника/приёмника, опции «Создавать группу
    /// по имени кластера» и «Не удалять в старом кластере», учётные данные СУБД и кнопка
    /// «Мигрировать». Тонкая обёртка над <see cref="ClusterMigrationViewModel"/>
    /// (аналогично <see cref="ClusterImportWindow"/>).
    /// После <c>ShowDialogSync()==true</c> результат доступен через <see cref="Result"/>.
    /// </summary>
    public partial class ClusterMigrationWindow : ModalWindowBase
    {
        private readonly ClusterMigrationViewModel _vm;
        private readonly ComboBox _sourceServerCombo;
        private readonly ComboBox _sourceClusterCombo;
        private readonly ComboBox _targetServerCombo;
        private readonly ComboBox _targetClusterCombo;
        private readonly TextBlock _progressText;
        private readonly ProgressBar _progressBar;
        private readonly Button _migrateButton;
        private readonly Button _sourceConnectButton;
        private readonly Button _sourceLoadBasesButton;
        private readonly Button _targetConnectButton;
        private readonly Button _targetGetStatusesButton;
        private readonly Button _selectAllButton;
        private readonly Button _selectNoneButton;
        private readonly TextBox _dbPasswordBox;
        private readonly ContentControl _sourceConnectHost = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly ContentControl _sourceLoadHost = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly ContentControl _targetConnectHost = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly ContentControl _targetStatusesHost = new() { VerticalAlignment = VerticalAlignment.Center };

        /// <param name="rac">Клиент rac (кластеры, «infobase summary list/create/drop»).</param>
        /// <param name="existingInfobases">Базы, уже присутствующие в списке приложения — для статуса источника.</param>
        /// <param name="availableServers">Справочник «Серверы 1С» (полный список для обеих панелей).</param>
        /// <param name="initialSourceServer">Сервер, открытый из окна «Серверы 1С» (issue #41); null — обычное открытие.</param>
        /// <param name="initialSourceClusterId">Кластер, выбранный в окне «Серверы 1С» (issue #41).</param>
        public ClusterMigrationWindow(
            IRacClient rac,
            IReadOnlyList<Infobase> existingInfobases,
            IReadOnlyList<ManagementServer> availableServers,
            ManagementServer? initialSourceServer = null,
            Guid? initialSourceClusterId = null)
        {
            AvaloniaXamlLoader.Load(this);

            _vm = new ClusterMigrationViewModel(
                rac,
                existingInfobases,
                availableServers,
                action => Dispatcher.UIThread.Post(action));

            Title = LocalizationManager.T("ClusterMigration.Title");
            DataContext = _vm;
            _vm.PropertyChanged += OnVmPropertyChanged;
            Closed += (_, _) => _vm.PropertyChanged -= OnVmPropertyChanged;

            this.FindControl<ListBox>("BasesList")!.ItemsSource = _vm.SourceRows;

            // ---- Панель «Источник»: сервер + кластер + статус (строится кодом). ----
            _sourceServerCombo = new ComboBox { Width = 300, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 2) };
            _sourceServerCombo.ItemsSource = _vm.AvailableSourceServers;
            _sourceServerCombo.ItemTemplate = new FuncDataTemplate<ManagementServer>((server, _) =>
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = server.Description, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = $"{server.Address}:{server.Port}", Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center }
                    }
                });
            // Двусторонняя привязка (issue #41): сервер, подставленный из окна «Серверы 1С»
            // (SelectedSourceServer), отображается в поле, а выбор пользователем пишется в VM.
            _sourceServerCombo.Bind(ComboBox.SelectedItemProperty,
                new Binding("SelectedSourceServer", BindingMode.TwoWay));

            _sourceClusterCombo = new ComboBox { Width = 300, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 2) };
            _sourceClusterCombo.Bind(ItemsControl.ItemsSourceProperty, new Binding("SourceClusters"));
            _sourceClusterCombo.ItemTemplate = new FuncDataTemplate<RacCluster>((cluster, _) =>
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = cluster.Name, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = $"[{cluster.Port}]", Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center }
                    }
                });
            _sourceClusterCombo.SelectionChanged += (_, _) =>
            {
                if (_sourceClusterCombo.SelectedItem is RacCluster cluster)
                    _vm.SelectedSourceClusterId = cluster.Id;
            };

            var sourceStatusText = new TextBlock { FontSize = 12, Opacity = 0.65 };
            sourceStatusText.Bind(TextBlock.TextProperty, new Binding("SourceStatusText"));

            var sourcePanel = new Grid();
            sourcePanel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            sourcePanel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            sourcePanel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var sourceServerRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(0, 0, 0, 6),
                Children = { FieldLabel("ClusterMigration.SourceServer"), _sourceServerCombo }
            };
            sourceServerRow.Children.Add(_sourceConnectHost);
            Grid.SetRow(sourceServerRow, 0);
            sourcePanel.Children.Add(sourceServerRow);

            var sourceClusterRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(0, 0, 0, 6),
                Children = { FieldLabel("ClusterMigration.SourceCluster"), _sourceClusterCombo }
            };
            sourceClusterRow.Children.Add(_sourceLoadHost);
            Grid.SetRow(sourceClusterRow, 1);
            sourcePanel.Children.Add(sourceClusterRow);

            Grid.SetRow(sourceStatusText, 2);
            sourcePanel.Children.Add(sourceStatusText);
            this.FindControl<ContentControl>("SourcePanelHost")!.Content =
                Controls.GroupBoxPanel.Build("ClusterMigration.SourceGroup", sourcePanel, padding: new Thickness(12, 8));

            // ---- Панель «Приёмник»: сервер + кластер + статус (строится кодом). ----
            _targetServerCombo = new ComboBox { Width = 300, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 2) };
            _targetServerCombo.ItemsSource = _vm.AvailableTargetServers;
            _targetServerCombo.ItemTemplate = new FuncDataTemplate<ManagementServer>((server, _) =>
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = server.Description, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = $"{server.Address}:{server.Port}", Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center }
                    }
                });
            // Двусторонняя привязка (issue #41): как у сервера-источника.
            _targetServerCombo.Bind(ComboBox.SelectedItemProperty,
                new Binding("SelectedTargetServer", BindingMode.TwoWay));

            _targetClusterCombo = new ComboBox { Width = 300, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 2) };
            _targetClusterCombo.Bind(ItemsControl.ItemsSourceProperty, new Binding("TargetClusters"));
            _targetClusterCombo.ItemTemplate = new FuncDataTemplate<RacCluster>((cluster, _) =>
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = cluster.Name, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = $"[{cluster.Port}]", Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center }
                    }
                });
            _targetClusterCombo.SelectionChanged += (_, _) =>
            {
                if (_targetClusterCombo.SelectedItem is RacCluster cluster)
                    _vm.SelectedTargetClusterId = cluster.Id;
            };

            var targetStatusText = new TextBlock { FontSize = 12, Opacity = 0.65 };
            targetStatusText.Bind(TextBlock.TextProperty, new Binding("TargetStatusText"));

            var targetPanel = new Grid();
            targetPanel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            targetPanel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            targetPanel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var targetServerRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(0, 0, 0, 6),
                Children = { FieldLabel("ClusterMigration.TargetServer"), _targetServerCombo }
            };
            targetServerRow.Children.Add(_targetConnectHost);
            Grid.SetRow(targetServerRow, 0);
            targetPanel.Children.Add(targetServerRow);

            var targetClusterRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(0, 0, 0, 6),
                Children = { FieldLabel("ClusterMigration.TargetCluster"), _targetClusterCombo }
            };
            targetClusterRow.Children.Add(_targetStatusesHost);
            Grid.SetRow(targetClusterRow, 1);
            targetPanel.Children.Add(targetClusterRow);

            Grid.SetRow(targetStatusText, 2);
            targetPanel.Children.Add(targetStatusText);
            this.FindControl<ContentControl>("TargetPanelHost")!.Content =
                Controls.GroupBoxPanel.Build("ClusterMigration.TargetGroup", targetPanel, padding: new Thickness(12, 8));

            // ---- Статусные строки и прогресс. ----
            this.FindControl<TextBlock>("ErrorText")!
                .Bind(TextBlock.TextProperty, new Binding("ErrorMessage"));
            _progressText = this.FindControl<TextBlock>("ProgressText")!;
            _progressBar = this.FindControl<ProgressBar>("BusyProgress")!;

            // ---- Опции: «Создавать группу по имени кластера» + «Не удалять в старом». ----
            var groupCheck = this.FindControl<CheckBox>("GroupByClusterCheck")!;
            groupCheck.Bind(CheckBox.IsCheckedProperty, new Binding("UseClusterGrouping", BindingMode.TwoWay));
            var keepCheck = this.FindControl<CheckBox>("KeepInSourceCheck")!;
            keepCheck.Bind(CheckBox.IsCheckedProperty, new Binding("KeepInSource", BindingMode.TwoWay));

            // ---- Тип СУБД + Сервер СУБД (issue #41, комментарий 7OH). ----
            // Поле «Имя БД» удалено (фикс 1.0.0.71): имя БД в СУБД переносится автоматически
            // из источника (BuildCreateRequest: DbName из rac, иначе — имя ИБ).
            var dbmsBox = this.FindControl<ComboBox>("DbmsBox")!;
            dbmsBox.Bind(ComboBox.SelectedItemProperty, new Binding("Dbms", BindingMode.TwoWay));
            var dbServerBox = this.FindControl<TextBox>("DbServerBox")!;
            dbServerBox.Bind(TextBox.TextProperty, new Binding("DbServer", BindingMode.TwoWay));

            // ---- Учётные данные СУБД и локаль (issue #41, комментарий 7OH). ----
            var dbUserBox = this.FindControl<TextBox>("DbUserBox")!;
            dbUserBox.Bind(TextBox.TextProperty, new Binding("DbUser", BindingMode.TwoWay));
            _dbPasswordBox = this.FindControl<TextBox>("DbPasswordBox")!;
            _dbPasswordBox.TextChanged += (_, _) => _vm.DbPassword = _dbPasswordBox.Text ?? string.Empty;
            var localeBox = this.FindControl<ComboBox>("LocaleBox")!;
            localeBox.Bind(ComboBox.SelectedItemProperty, new Binding("Locale", BindingMode.TwoWay));

            // ---- Кнопки (паттерн BaseSelectionWindow: строятся в коде, хост — в панели). ----
            _sourceConnectButton = BuildActionButton(LocalizationManager.T("ClusterMigration.Connect"), "IconLink",
                () => _vm.ConnectSourceCommand.Execute(null));
            _sourceConnectHost.Content = _sourceConnectButton;

            _sourceLoadBasesButton = BuildActionButton(LocalizationManager.T("ClusterMigration.LoadBases"), "IconRefresh",
                () => _vm.LoadSourceBasesCommand.Execute(null));
            _sourceLoadHost.Content = _sourceLoadBasesButton;

            _targetConnectButton = BuildActionButton(LocalizationManager.T("ClusterMigration.Connect"), "IconLink",
                () => _vm.ConnectTargetCommand.Execute(null));
            _targetConnectHost.Content = _targetConnectButton;

            _targetGetStatusesButton = BuildActionButton(LocalizationManager.T("ClusterMigration.GetStatuses"), "IconRefresh",
                () => _vm.GetTargetStatusesCommand.Execute(null));
            _targetStatusesHost.Content = _targetGetStatusesButton;

            var bulk = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            _selectAllButton = BuildSecondaryButton(LocalizationManager.T("Selection.SelectAll"),
                () => _vm.SelectAllCommand.Execute(null));
            _selectNoneButton = BuildSecondaryButton(LocalizationManager.T("Selection.None"),
                () => _vm.SelectNoneCommand.Execute(null));
            // Сводка выделения «Выделено: N из M · будет перенесено: N» (issue #41,
            // комментарий 7OH): слева от кнопок, привязана к SelectionSummaryText.
            var selectionSummary = new TextBlock
            {
                FontSize = 12,
                Opacity = 0.75,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
                MaxWidth = 380,
                TextWrapping = TextWrapping.Wrap
            };
            selectionSummary.Bind(TextBlock.TextProperty, new Binding("SelectionSummaryText"));
            bulk.Children.Insert(0, selectionSummary);
            bulk.Children.Add(_selectAllButton);
            bulk.Children.Add(_selectNoneButton);
            this.FindControl<ContentControl>("BulkHost")!.Content = bulk;

            _migrateButton = BuildPrimaryButton(LocalizationManager.T("ClusterMigration.MigrateButton"), "IconArrowRight",
                OnMigrateClick);
            this.FindControl<ContentControl>("MigrateHost")!.Content = _migrateButton;

            var cancelButton = BuildSecondaryButton(LocalizationManager.T("Common.Cancel"),
                () => { DialogResult = false; Close(); });
            this.FindControl<ContentControl>("CancelHost")!.Content = cancelButton;

            UpdateCommandStates();

            // Открытие из окна «Серверы 1С»: выбранный сервер-источник + автоподключение
            // (issue #41): сопоставление с записями справочника по Id — в VM, чтобы ComboBox
            // подсвечивал текущий сервер (у ManagementServer нет переопределённого Equals).
            _vm.SetInitialSource(initialSourceServer, initialSourceClusterId);
        }

        /// <summary>
        /// Результат миграции (при <c>ShowDialogSync()==true</c>): перенесённые базы, базы
        /// для добавления в список приложения, новый сервер/порт и ответ на вопрос о замене
        /// строки подключения.
        /// </summary>
        public ClusterMigrationResult? Result => _vm.Result;

        /// <summary>Показывает окно модально (синхронно) — обёртка над <see cref="ModalWindowBase.ShowDialogSync(Window?)"/>.</summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ClusterMigrationViewModel.IsBusy):
                case nameof(ClusterMigrationViewModel.HasAvailableServers):
                case nameof(ClusterMigrationViewModel.SelectedSourceServer):
                case nameof(ClusterMigrationViewModel.SelectedTargetServer):
                case nameof(ClusterMigrationViewModel.SelectedSourceClusterId):
                case nameof(ClusterMigrationViewModel.SelectedTargetClusterId):
                case nameof(ClusterMigrationViewModel.CheckedCount):
                case nameof(ClusterMigrationViewModel.HasCheckedInTarget):
                case nameof(ClusterMigrationViewModel.ServersOverlap):
                case nameof(ClusterMigrationViewModel.ProgressText):
                    UpdateCommandStates();
                    break;
                case nameof(ClusterMigrationViewModel.MigrationCompleted):
                    OnMigrationCompleted();
                    break;
            }
        }

        /// <summary>
        /// Единый пересчёт кнопок и индикатора занятости: «Подключиться» доступна при
        /// выбранном сервере и неактивном запросе; «Получить базы»/«Получить статусы» —
        /// при выбранном кластере; «Мигрировать» — по <see cref="ClusterMigrationViewModel.CanMigrate"/>.
        /// </summary>
        private void UpdateCommandStates()
        {
            var busy = _vm.IsBusy;
            _sourceConnectButton.IsEnabled = !busy && _vm.HasAvailableServers;
            _sourceLoadBasesButton.IsEnabled = !busy && _vm.SelectedSourceClusterId is not null;
            _targetConnectButton.IsEnabled = !busy && _vm.HasAvailableServers;
            _targetGetStatusesButton.IsEnabled = !busy && _vm.SelectedTargetClusterId is not null && _vm.SourceRows.Count > 0;
            _selectAllButton.IsEnabled = !busy;
            _selectNoneButton.IsEnabled = !busy;
            _migrateButton.IsEnabled = _vm.CanMigrate;
            _sourceServerCombo.IsEnabled = !busy;
            _sourceClusterCombo.IsEnabled = !busy;
            _targetServerCombo.IsEnabled = !busy;
            _targetClusterCombo.IsEnabled = !busy;
            _progressBar.IsVisible = busy;
            _progressText.Text = _vm.ProgressText;
        }

        /// <summary>
        /// Перенос завершён (issue #41, комментарий 7OH от 2026-10-26 «закрывать окно
        /// не надо»): вопрос «Хотите заменить строку подключения?» → если ДА — результат
        /// помечается (MainViewModel заменит сервер/порт в списке приложения). Окно НЕ
        /// закрывается: строки со статусами и сводка «Перенесено: N · Ошибок: M» видны,
        /// можно повторить перенос (например, на другой кластер-приёмник) — перенесённые
        /// базы накапливаются в <see cref="ClusterMigrationViewModel.Result"/>. Закрытие —
        /// только кнопкой «Отмена»; MainViewModel применяет результат после закрытия окна.
        /// </summary>
        private void OnMigrationCompleted()
        {
            if (_vm.Result is null)
                return;

            // issue #41 (комментарий 7OH от 2026-10-28): при «Не удалять в старом кластере»
            // (копирование вместо переноса) вопрос «Хотите заменить строку подключения?»
            // НЕ задаётся: новые базы уже созданы с корректным подключением, а старые
            // строки остались на месте — менять их нечем. Вопрос — только при переносе.
            if (_vm.KeepInSource)
                return;

            if (AppServices.GetRequiredService<IDialogService>().Confirm(
                    LocalizationManager.T("ClusterMigration.ConfirmReplaceConnection"),
                    LocalizationManager.T("ClusterMigration.ReplaceConnectionTitle")))
            {
                _vm.SetReplaceConnectionStrings(true);
            }
        }

        private void OnMigrateClick() => _vm.MigrateCommand.Execute(null);

        /// <summary>Подпись поля формы по ключу локализации (issue #61): единая кисть
        /// FormLabelBrush — читаема в обеих темах и пользовательских схемах.</summary>
        private static TextBlock FieldLabel(string textKey)
        {
            var block = new TextBlock
            {
                Text = LocalizationManager.T(textKey),
                VerticalAlignment = VerticalAlignment.Center
            };
            ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "FormLabelBrush");
            return block;
        }

        private Button BuildPrimaryButton(string text, string icon, Action onClick)
        {
            var button = new Button
            {
                // Текст и иконка берут контрастный цвет ModernButton (TextOnAccentBrush);
                // жёсткий зелёный фон и белый текст убраны (issue #26).
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        BuildIconOrGlyph(icon, 14, "TextOnAccentBrush"),
                        new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }
                    }
                },
                Padding = new Thickness(16, 5)
            };
            button.Styled(ControlThemes.DialogConfirmButton);
            button.Click += (_, _) => onClick();
            return button;
        }

        private Button BuildActionButton(string text, string icon, Action onClick)
        {
            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        BuildIconOrGlyph(icon, UiMetrics.IconControl, "TextPrimaryBrush"),
                        new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }
                    }
                },
                Padding = new Thickness(10, 5)
            };
            button.Styled(ControlThemes.ModernButton);
            ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            button.Click += (_, _) => onClick();
            return button;
        }

        /// <summary>
        /// Содержимое кнопки: ключ <c>Icon*</c> — векторная иконка набора темы (P1a),
        /// иначе — текстовый глиф для обратной совместимости с остальными кнопками окна.
        /// </summary>
        private static Control BuildIconOrGlyph(string icon, double size, string brushKey)
            => icon.StartsWith("Icon", StringComparison.Ordinal)
                ? IconHelper.MakeIcon(icon, size, brushKey)
                : new TextBlock { Text = icon, VerticalAlignment = VerticalAlignment.Center };

        private Button BuildSecondaryButton(string text, Action onClick)
        {
            var button = new Button { Content = text, Padding = new Thickness(12, 6) };
            button.Styled(ControlThemes.SecondaryButton);
            button.Click += (_, _) => onClick();
            return button;
        }
    }
}
#endif
