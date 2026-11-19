#if LINUX
using System;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
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
    /// Окно «Хранилище конфигурации…» (цикл 0.3.9.127–0.3.9.130, Avalonia/Linux): панель
    /// подключения (адрес и пользователь readonly из свойств базы, пароль — в памяти окна),
    /// список версий хранилища (№/дата/автор/комментарий) и состав выбранной версии
    /// (тип/имя/владелец), действия этапа 3 (сравнение с базой / между версиями, выгрузка .cf),
    /// статус-строка и подсказка. Вся логика — в чистой ViewModel
    /// <see cref="RepositoryBrowserViewModel"/>; окно прогресса операций —
    /// <see cref="RepositoryProgressWindow"/>; результат сравнения — существующее
    /// <see cref="ConfigDiffResultWindow"/> с экспортом CSV/TXT. Пароль не биндится —
    /// передаётся в VM из кода при нажатии «Подключить». Образец построения —
    /// ProcessInspectorWindow.Avalonia.cs.
    /// </summary>
    public sealed class RepositoryBrowserWindow : ModalWindowBase
    {
        private readonly RepositoryBrowserViewModel _vm;
        private readonly TextBox _passwordBox;
        private readonly Button _compareWithBaseButton;
        private readonly Button _compareVersionsButton;
        private readonly Button _dumpCfButton;
        private readonly Button _lockAllButton;
        private readonly Button _unlockAllButton;
        private RepositoryProgressWindow? _progress;

        /// <param name="infobase">Выбранная база с заполненным адресом хранилища.</param>
        /// <param name="persistChanges">
        /// Сохранение списка баз после записи в историю запусков (передаёт главное окно);
        /// null — без сохранения.
        /// </param>
        public RepositoryBrowserWindow(Infobase infobase, Action? persistChanges = null)
        {
            Title = LocalizationManager.T("RepositoryBrowser.Title");
            Width = 1180;
            Height = 640;
            MinWidth = 900;
            MinHeight = 460;
            FontSize = 13;
            CanResize = true;

            var service = AppServices.GetRequiredService<IRepositoryStorageService>();
            var dialogs = AppServices.GetRequiredService<IDialogService>();

            _vm = new RepositoryBrowserViewModel(
                infobase,
                service,
                dialogs,
                action => Dispatcher.UIThread.Post(action),
                showDiffResult: result =>
                {
                    var window = new ConfigDiffResultWindow(result);
                    window.ShowDialogSync(this);
                },
                persistChanges: persistChanges);
            DataContext = _vm;

            // Окно прогресса: показывается, пока идёт подключение/загрузка состава версии.
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.StageChanged += OnStageChanged;
            Closed += (_, _) =>
            {
                _vm.PropertyChanged -= OnVmPropertyChanged;
                _vm.StageChanged -= OnStageChanged;
                HideProgress();
            };

            // ---- Заголовок ----
            var title = new TextBlock
            {
                Text = LocalizationManager.T("RepositoryBrowser.Title"),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };

            // ---- Панель подключения: адрес/пользователь readonly, пароль в памяти окна ----
            var addressBox = ReadOnlyText("RepositoryAddress");
            var userBox = ReadOnlyText("UserName");
            _passwordBox = new TextBox
            {
                PasswordChar = '●',
                MinWidth = 180,
                Padding = new Thickness(6, 3)
            }.Styled(ControlThemes.ModernTextBox);

            var connectPanel = new Grid
            {
                Margin = new Thickness(0, 14, 0, 0),
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(new GridLength(2, GridUnitType.Star)),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(new GridLength(1.5, GridUnitType.Star)),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(new GridLength(1.5, GridUnitType.Star))
                }
            };
            PlaceColumn(connectPanel, FieldLabel(LocalizationManager.T("RepositoryBrowser.Address")), 0);
            PlaceColumn(connectPanel, addressBox, 1);
            PlaceColumn(connectPanel, FieldLabel(LocalizationManager.T("RepositoryBrowser.User")), 2);
            PlaceColumn(connectPanel, userBox, 3);
            PlaceColumn(connectPanel, FieldLabel(LocalizationManager.T("RepositoryBrowser.Password")), 4);
            PlaceColumn(connectPanel, _passwordBox, 5);

            // ---- Кнопки: Подключить / Обновить / действия этапа 3 / Закрыть ----
            var connectButton = BuildActionButton(LocalizationManager.T("RepositoryBrowser.Connect"), OnConnectClick, "IconLink");
            var refreshButton = BuildActionButton(LocalizationManager.T("RepositoryBrowser.Refresh"), OnRefreshClick, "IconRefresh");
            _compareWithBaseButton = BuildActionButton(LocalizationManager.T("RepositoryBrowser.CompareWithBase"),
                () => _vm.CompareWithBaseCommand.Execute(null), "IconSwap");
            _compareVersionsButton = BuildActionButton(LocalizationManager.T("RepositoryBrowser.CompareVersions"),
                () => _vm.CompareVersionsCommand.Execute(null), "IconSwapVertical");
            _dumpCfButton = BuildActionButton(LocalizationManager.T("RepositoryBrowser.DumpToCf"),
                () => _vm.DumpVersionToCfCommand.Execute(null), "IconUpload");
            var closeButton = BuildCloseButton();
            RefreshActionButtons();

            // ---- Панель захвата (этап 4): комментарий операции + «Захватить все» / «Отменить захват».
            // Комментарий хранится локально (журнал окна + история запусков) и платформе
            // при /ConfigurationRepositoryLock|Unlock не передаётся (ограничение плана §3.3). ----
            var commentBox = new TextBox
            {
                Width = 300,
                Padding = new Thickness(4, 3)
            }.Styled(ControlThemes.ModernTextBox);
            commentBox.Bind(TextBox.TextProperty, new Binding("LockComment"));
            ToolTip.SetTip(commentBox, LocalizationManager.T("RepositoryBrowser.LockCommentHint"));

            _lockAllButton = BuildActionButton(LocalizationManager.T("RepositoryBrowser.LockAll"),
                () => _vm.LockAllCommand.Execute(null), "IconLock");
            _unlockAllButton = BuildActionButton(LocalizationManager.T("RepositoryBrowser.UnlockAll"),
                () => _vm.UnlockAllCommand.Execute(null), "IconUnlock");

            var lockPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 12, 0, 0),
                Children =
                {
                    FieldLabel(LocalizationManager.T("RepositoryBrowser.LockComment")),
                    commentBox,
                    _lockAllButton,
                    _unlockAllButton
                }
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Spacing = 8,
                Margin = new Thickness(0, 14, 0, 0),
                Children = { connectButton, refreshButton, _compareWithBaseButton, _compareVersionsButton, _dumpCfButton, closeButton }
            };

            // ---- Версии (левая колонка) ----
            var versionsHeader = ColumnHeader(new[] { "58", "150", "170", "*" },
                new[] { LocalizationManager.T("RepositoryBrowser.Columns.Number"), LocalizationManager.T("RepositoryBrowser.Columns.Date"), LocalizationManager.T("RepositoryBrowser.Columns.Author"), LocalizationManager.T("RepositoryBrowser.Columns.Comment") });
            var versionsList = new ListBox
            {
                ItemsSource = _vm.Versions,
                ItemTemplate = new FuncDataTemplate<RepositoryVersionRow>((_, _) => BuildVersionRow())
            };
            versionsList.Bind(ListBox.SelectedItemProperty, new Binding("SelectedVersion"));

            var versionsPanel = new DockPanel { Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(versionsHeader, Dock.Top);
            versionsPanel.Children.Add(versionsHeader);
            versionsPanel.Children.Add(versionsList);

            // ---- Состав версии (правая колонка) ----
            var objectsHeader = ColumnHeader(new[] { "1.2*", "2*", "1.6*" },
                new[] { LocalizationManager.T("RepositoryBrowser.Columns.Type"), LocalizationManager.T("RepositoryBrowser.Columns.Name"), LocalizationManager.T("RepositoryBrowser.Columns.Owner") });
            var objectsList = new ListBox
            {
                ItemsSource = _vm.Objects,
                ItemTemplate = new FuncDataTemplate<RepositoryObjectRow>((_, _) => BuildObjectRow())
            };

            var objectsPanel = new DockPanel { Margin = new Thickness(8, 0, 0, 0) };
            DockPanel.SetDock(objectsHeader, Dock.Top);
            objectsPanel.Children.Add(objectsHeader);
            objectsPanel.Children.Add(objectsList);

            var versionsTitle = new TextBlock
            {
                Text = LocalizationManager.T("RepositoryBrowser.Versions"),
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };
            var objectsTitle = new TextBlock
            {
                Text = LocalizationManager.T("RepositoryBrowser.Objects"),
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };

            var tables = new Grid
            {
                Margin = new Thickness(0, 14, 0, 0),
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(5, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(6, GridUnitType.Star))
                }
            };
            PlaceColumn(tables, WrapTitle(versionsTitle, versionsPanel), 0);
            PlaceColumn(tables, WrapTitle(objectsTitle, objectsPanel), 1);

            // ---- Статус-строка ----
            var status = new TextBlock
            {
                FontSize = 12,
                Opacity = 0.65,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 10, 0, 0)
            };
            status.Bind(TextBlock.TextProperty, new Binding("StatusText"));
            ToolTip.SetTip(status, new Binding("ErrorMessage"));

            // ---- Подсказка: при ограниченной истории — пояснение, иначе — общий hint ----
            var hint = new TextBlock
            {
                Text = LocalizationManager.T("RepositoryBrowser.Hint"),
                FontSize = 11,
                Opacity = 0.65,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            };
            var historyHint = new TextBlock
            {
                Text = LocalizationManager.T("RepositoryBrowser.HistoryUnavailable"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#D97706")),
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
                IsVisible = false
            };
            historyHint.Bind(IsVisibleProperty, new Binding("IsHistoryLimited"));
            hint.Bind(IsVisibleProperty, InvertBool("IsHistoryLimited"));

            var root = new Grid
            {
                Margin = new Thickness(16),
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                }
            };
            Place(root, title, 0);
            Place(root, connectPanel, 1);
            Place(root, buttons, 2);
            Place(root, lockPanel, 3);
            Place(root, tables, 4);
            Place(root, status, 5);
            Place(root, StackHints(hint, historyHint), 6);
            Content = root;
        }

        // ===================== Обработчики =====================

        private void OnConnectClick()
        {
            // Пароль не биндится — передаём его в VM перед командой.
            _vm.Password = _passwordBox.Text ?? string.Empty;
            _vm.ConnectCommand.Execute(null);
        }

        private void OnRefreshClick() => _vm.RefreshCommand.Execute(null);

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Доступность действий зависит от SelectedVersion/HasConnected/IsHistoryLimited/IsBusy.
            RefreshActionButtons();

            if (e.PropertyName != nameof(RepositoryBrowserViewModel.IsBusy) &&
                e.PropertyName != nameof(RepositoryBrowserViewModel.IsObjectsLoading))
                return;

            if (_vm.IsBusy || _vm.IsObjectsLoading)
            {
                // Текст этапа берём из статуса VM: подключение, сравнение, выгрузка…
                ShowProgress(_vm.IsObjectsLoading
                    ? LocalizationManager.T("RepositoryBrowser.Status.ObjectsLoading")
                    : string.IsNullOrWhiteSpace(_vm.StatusText)
                        ? LocalizationManager.T("RepositoryBrowser.Status.Connecting")
                        : _vm.StatusText);
            }
            else
            {
                HideProgress();
            }
        }

        /// <summary>
        /// Доступность кнопок действий по CanExecute команд VM: «Сравнить с базой»/«Выгрузить в .cf»
        /// — при выбранной версии после подключения; «Сравнить версии» — дополнительно только при
        /// полной истории (<see cref="RepositoryBrowserViewModel.IsHistoryLimited"/> = false).
        /// </summary>
        private void RefreshActionButtons()
        {
            _compareWithBaseButton.IsEnabled = _vm.CompareWithBaseCommand.CanExecute(null);
            _compareVersionsButton.IsEnabled = _vm.CompareVersionsCommand.CanExecute(null);
            _dumpCfButton.IsEnabled = _vm.DumpVersionToCfCommand.CanExecute(null);
            _lockAllButton.IsEnabled = _vm.LockAllCommand.CanExecute(null);
            _unlockAllButton.IsEnabled = _vm.UnlockAllCommand.CanExecute(null);
        }

        private void OnStageChanged(string stage) => _progress?.SetStage(stage);

        private void ShowProgress(string stage)
        {
            if (_progress is null)
            {
                _progress = new RepositoryProgressWindow { WindowStartupLocation = WindowStartupLocation.CenterOwner };
                _progress.Show(this);
                IsEnabled = false;
            }
            _progress.SetStage(stage);
        }

        private void HideProgress()
        {
            _progress?.Close();
            _progress = null;
            IsEnabled = true;
        }

        // ===================== Построение строк =====================

        /// <summary>Строка списка версий: №/дата/автор/комментарий с пометкой актуальной версии.</summary>
        private static Control BuildVersionRow()
        {
            var number = CellText("NumberText");
            var date = CellText("DateText");
            var author = CellText("Author");
            var comment = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            comment.Bind(TextBlock.TextProperty, new Binding("Comment"));
            // P1b: пометка актуальной версии — векторный кружок, не текстовый «●».
            var currentMark = IconHelper.MakeIcon("IconBullet", UiMetrics.IconInline,
                new SolidColorBrush(Color.Parse("#16A34A")));
            currentMark.Margin = new Thickness(8, 0, 0, 0);
            currentMark.Bind(IsVisibleProperty, new Binding("IsCurrent"));
            var commentCell = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { currentMark, comment }
            };

            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(58)),
                    new ColumnDefinition(new GridLength(150)),
                    new ColumnDefinition(new GridLength(170)),
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star))
                },
                Children = { number, date, author, commentCell }
            };
            Grid.SetColumn(number, 0);
            Grid.SetColumn(date, 1);
            Grid.SetColumn(author, 2);
            Grid.SetColumn(commentCell, 3);
            return row;
        }

        /// <summary>Строка состава версии: тип/имя (с отступом вложенных)/владелец.</summary>
        private static Control BuildObjectRow()
        {
            var type = CellText("TypeName");
            var name = CellText("DisplayName");
            var owner = CellText("Owner");

            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(1.2, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(2, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(1.6, GridUnitType.Star))
                },
                Children = { type, name, owner }
            };
            Grid.SetColumn(type, 0);
            Grid.SetColumn(name, 1);
            Grid.SetColumn(owner, 2);
            return row;
        }

        /// <summary>Шапка списка: одна строка с колонками-заголовками поверх строк.</summary>
        private static Grid ColumnHeader(string[] widths, string[] headers)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 4)
            };
            for (var i = 0; i < headers.Length; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(ParseGridLength(widths[i])));
                var label = new TextBlock
                {
                    Text = headers[i],
                    FontSize = 12,
                    Opacity = 0.6,
                    Margin = new Thickness(8, 0, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                grid.Children.Add(label);
                Grid.SetColumn(label, i);
            }
            return grid;
        }

        private static GridLength ParseGridLength(string value)
        {
            if (value.EndsWith("*", StringComparison.Ordinal) && value.Length > 1)
            {
                var ratioText = value[..^1];
                return double.TryParse(ratioText, out var ratio)
                    ? new GridLength(ratio, GridUnitType.Star)
                    : new GridLength(1, GridUnitType.Star);
            }
            return double.TryParse(value, out var px)
                ? new GridLength(px)
                : GridLength.Auto;
        }

        private static Control WrapTitle(TextBlock title, Control content)
        {
            var panel = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star)
                }
            };
            Place(panel, title, 0);
            Place(panel, content, 1);
            return panel;
        }

        private static Control StackHints(Control hint, Control historyHint)
        {
            var panel = new StackPanel { Children = { hint, historyHint } };
            return panel;
        }

        private static IBinding InvertBool(string path) =>
            new Binding(path) { Converter = new InvertBoolConverter() };

        // ===================== Вспомогательные элементы =====================

        private static TextBlock FieldLabel(string text) => new()
        {
            Text = text,
            FontSize = 12,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center
        };

        private static TextBox ReadOnlyText(string property)
        {
            var box = new TextBox
            {
                IsReadOnly = true,
                Padding = new Thickness(6, 3)
            };
            box.Bind(TextBox.TextProperty, new Binding(property) { Mode = BindingMode.OneWay });
            return box;
        }

        private static Button BuildActionButton(string text, Action onClick, string icon)
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
                Padding = new Thickness(12, 5)
            };
            button.Styled(ControlThemes.ModernButton);
            Themes.ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
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

        private Button BuildCloseButton()
        {
            var button = new Button
            {
                Content = IconHelper.IconAndText("IconClose", LocalizationManager.T("Common.Close"), UiMetrics.IconControl, "TextPrimaryBrush"),
                Width = 110,
                Margin = new Thickness(16, 0, 0, 0),
                IsCancel = true
            };
            button.Styled(ControlThemes.ModernButton);
            Themes.ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            Themes.ThemeBrushes.Bind(button, Button.ForegroundProperty, "TextPrimaryBrush");
            button.Click += (_, _) => Close();
            return button;
        }

        private static Control CellText(string property)
        {
            var text = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            text.Bind(TextBlock.TextProperty, new Binding(property));
            return text;
        }

        private static void Place(Grid grid, Control control, int row)
        {
            Grid.SetRow(control, row);
            grid.Children.Add(control);
        }

        private static void PlaceColumn(Grid grid, Control control, int column)
        {
            Grid.SetColumn(control, column);
            grid.Children.Add(control);
        }

        /// <summary>Инверсия bool для привязки (общий hint скрывается при ограниченной истории).</summary>
        private sealed class InvertBoolConverter : IValueConverter
        {
            public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
                value is not true;

            public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
                value is not true;
        }
    }
}
#endif