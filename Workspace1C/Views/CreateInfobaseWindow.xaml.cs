#if WINDOWS
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace Workspace1C
{
    /// <summary>
    /// Диалог создания ИБ через CREATEINFOBASE (пустая или из шаблона .cf/.dt).
    /// </summary>
    public partial class CreateInfobaseWindow : Window
    {
        private readonly bool _fromTemplate;
        private readonly IReadOnlyList<string> _platformVersions;
        private readonly IReadOnlyList<Group> _groups;
        private readonly IReadOnlyList<string> _availableServers;
        private string _selectedGroupPath;
        private readonly IInfobaseRepository _repository =
            AppServices.GetRequiredService<IInfobaseRepository>();
        private readonly ICreateInfobaseService _createService =
            AppServices.GetRequiredService<ICreateInfobaseService>();
        private readonly IDialogService _dialogs =
            AppServices.GetRequiredService<IDialogService>();

        public Infobase? Result { get; private set; }

        public CreateInfobaseWindow(
            bool fromTemplate,
            IEnumerable<string> platformVersions,
            string defaultGroupPath = "",
            IEnumerable<Group>? groups = null,
            IEnumerable<string>? availableServers = null)
        {
            _fromTemplate = fromTemplate;
            _platformVersions = platformVersions?.ToList() ?? new List<string>();
            _groups = groups?.ToList() ?? new List<Group>();
            _availableServers = availableServers?.ToList() ?? new List<string>();
            _selectedGroupPath = defaultGroupPath ?? string.Empty;
            InitializeComponent();

            // Кнопка «развернуть» в собственной шапке не нужна (issue #3): ресайз
            // (CanResizeWithGrip) сохраняется, максимизация подавляется.
            WindowChromeHelper.SetSuppressMaximizeButton(this, true);

            // Список известных серверов 1С из зарегистрированных клиент-серверных
            // баз — выпадающий список поля «Сервер 1С» (issue #305).
            ServerBox.ItemsSource = _availableServers;

            // ESC сначала закрывает открытые всплывающие подсказки, а только потом окно (issue #270).
            ToolTipCloser.Register();
            PreviewKeyDown += OnToolTipEscPreviewKeyDown;

            // Живая подсказка формата DBSrvr (issue #305): у редактируемого ComboBox нет
            // собственного TextChanged, но внутренний TextBox поднимает всплывающее событие
            // TextBox.TextChangedEvent — ловим его, чтобы подсказка менялась и при ручном вводе СУБД.
            DbmsBox.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(OnDbms_TextChanged));

            // Последний тип базы, сервер СУБД и порт из настроек подставляются по умолчанию (issue #305).
            RestoreLastCreateState();
            UpdateDbServerHint();

            // Подсказки скрываются при потере фокуса окна (issue #275).
            Deactivated += (_, _) => ToolTipCloser.CloseAll();

            // «Сервер СУБД» запоминается и при закрытии окна (issue #305): пользователь мог
            // ввести значение и закрыть окно без создания — сохранение только после успешного
            // создания такой сценарий не покрывало.
            Closed += OnWindowClosed;

            Title = fromTemplate
                ? LocalizationManager.T("CreateInfobase.TitleFromTemplate")
                : LocalizationManager.T("CreateInfobase.TitleEmpty");

            TemplatePanel.Visibility = fromTemplate ? Visibility.Visible : Visibility.Collapsed;
            GroupPathBox.Text = string.IsNullOrWhiteSpace(_selectedGroupPath)
                ? LocalizationManager.T("Connection.NoGroup")
                : _selectedGroupPath;

            HintText.Text = fromTemplate
                ? LocalizationManager.T("CreateInfobase.HintTemplate")
                : LocalizationManager.T("CreateInfobase.HintEmpty");

            RefreshPlatformList();
            RememberPlatformSelection(true, PlatformBox.Text);

            if (fromTemplate)
                LoadInstalledTemplates();

            // Высота окна — явная (не SizeToContent=Height): после первой отрисовки один раз
            // подстраиваемся под контент и держим окно в рабочей области. Регресс «странного/
            // пустого окна» (issues #305/#308) был связан с хрупким авторазмером при модальном показе.
            ContentRendered += (_, _) => FitHeightToContent();
        }

        /// <summary>
        /// Подгоняет высоту окна под содержимое (явно, без <c>SizeToContent=Height</c>) и не даёт
        /// окну уйти за нижний край рабочей области при изменении типа базы (issue #305).
        /// Логика клампинга — чистый <see cref="WindowSizeMath"/> (общий с Avalonia, покрыт тестами).
        /// </summary>
        private void FitHeightToContent()
        {
            if (RootGrid is null || !IsLoaded || !IsVisible)
                return;

            // Измеряем контент при бесконечной высоте: сколько места нужно, чтобы внутренний
            // ScrollViewer не прокручивался (он вернёт полную высоту содержимого).
            var availableWidth = RootGrid.ActualWidth > 0 ? RootGrid.ActualWidth : Math.Max(400, Width);
            RootGrid.Measure(new Size(availableWidth, double.PositiveInfinity));
            var desired = RootGrid.DesiredSize.Height;
            if (desired <= 0)
                return;

            // Хром (заголовок окна + рамки) — разница между полной высотой и клиентской областью.
            var chrome = Math.Max(0, ActualHeight - (RootGrid.ActualHeight > 0 ? RootGrid.ActualHeight : desired));
            var target = WindowSizeMath.ClampHeight(desired + chrome, MinHeight, MaxHeight);
            if (Math.Abs(target - Height) > 1)
                Height = target;

            // Окно стояло у нижнего края экрана и выросло при смене типа — поднимаем его,
            // чтобы нижняя часть не уходила за экран.
            var wa = SystemParameters.WorkArea;
            var newTop = WindowSizeMath.FitTop(Top, Height, wa.Top, wa.Bottom);
            if (Math.Abs(newTop - Top) > 1)
                Top = newTop;
        }

        /// <summary>
        /// Preview-обработчик ESC (issue #270): первый ESC закрывает открытые всплывающие
        /// подсказки и помечает событие обработанным, повторный ESC закрывает окно как обычно.
        /// </summary>
        private void OnToolTipEscPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape &&
                Keyboard.Modifiers == ModifierKeys.None &&
                ToolTipCloser.CloseAll(suppressReopen: true))
            {
                e.Handled = true;
            }
        }

        private List<string> _platforms = new();
        private bool _platformSelectionIsFile = true;
        private string? _filePlatformSelection;
        private string? _clientServerPlatformSelection;

        
        private void OnPickGroup_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new GroupPickerWindow(
                _groups,
                currentGroupId: null,
                allowNone: true,
                noneLabel: LocalizationManager.T("Connection.NoGroup"),
                kind: GroupPickerObjectKind.Infobase)
            {
                Owner = this
            };
            if (dialog.ShowDialog() == true)
            {
                _selectedGroupPath = string.IsNullOrWhiteSpace(dialog.ResultFullPath)
                    ? string.Empty
                    : dialog.ResultFullPath;
                GroupPathBox.Text = string.IsNullOrWhiteSpace(_selectedGroupPath)
                    ? LocalizationManager.T("Connection.NoGroup")
                    : _selectedGroupPath;
            }
        }

        private void RefreshPlatformList(bool replaceSelection = false, string? preferredSelection = null)
        {
            var extras = PlatformVersionService.GetAdditionalSearchPaths();
            _platforms = PlatformVersionService.FindInstalledVersions(extras);
            if (_platforms.Count == 0)
                _platforms = _platformVersions.ToList();

            if (_platforms.Count == 0)
            {
                if (replaceSelection)
                    PlatformBox.Text = string.Empty;
                return;
            }

            if (string.IsNullOrWhiteSpace(preferredSelection))
                preferredSelection = GetPlatformSelection(TypeBox.SelectedIndex != 1);

            // По умолчанию подставляем последнюю успешно использованную версию для текущего
            // типа базы (файловая/клиент-серверная), если она всё ещё установлена. Иначе — самую новую.
            string selected = _platforms[0];
            var preferred = _platforms.FirstOrDefault(p =>
                string.Equals(p, preferredSelection, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(preferred))
            {
                selected = preferred;
            }
            else
            {
                var saved = GetSavedPlatformVersion();
                if (!string.IsNullOrWhiteSpace(saved))
                {
                    foreach (var p in _platforms)
                    {
                        PlatformVersionService.ParseVariant(p, out var clean, out _);
                        var candidate = string.IsNullOrWhiteSpace(clean) ? p : clean;
                        if (string.Equals(candidate, saved, StringComparison.OrdinalIgnoreCase))
                        {
                            selected = p;
                            break;
                        }
                    }
                }
            }

            if (replaceSelection || string.IsNullOrWhiteSpace(PlatformBox.Text))
                PlatformBox.Text = selected;
        }

        private string? GetPlatformSelection(bool isFile) =>
            isFile ? _filePlatformSelection : _clientServerPlatformSelection;

        private void RememberPlatformSelection(bool isFile, string? platform)
        {
            if (string.IsNullOrWhiteSpace(platform))
                return;

            if (isFile)
                _filePlatformSelection = platform;
            else
                _clientServerPlatformSelection = platform;
        }

        /// <summary>
        /// Возвращает последнюю успешно использованную версию платформы для текущего типа базы.
        /// Тип базы определяется по TypeBox; по умолчанию (до инициализации) — файловая.
        /// </summary>
        private string GetSavedPlatformVersion()
        {
            var settings = _repository.LoadSettings();
            var isFile = TypeBox.SelectedIndex != 1;
            return isFile
                ? settings.LastFileCreatePlatformVersion ?? ""
                : settings.LastClientServerCreatePlatformVersion ?? "";
        }

        private void OnTypeChanged(object sender, SelectionChangedEventArgs e)
        {
            var isFile = TypeBox.SelectedIndex != 1;
            // Обработчик может сработать в ходе InitializeComponent(), когда элементы,
            // объявленные ниже ComboBox в XAML, ещё не созданы, поэтому защищаемся от null.
            if (FilePanel != null)
                FilePanel.Visibility = isFile ? Visibility.Visible : Visibility.Collapsed;
            if (ServerPanel != null)
                ServerPanel.Visibility = isFile ? Visibility.Collapsed : Visibility.Visible;
            // В пределах открытого окна ручной выбор хранится отдельно для каждого типа.
            // При первом переходе используется последняя успешная версия из настроек.
            if (PlatformBox != null)
            {
                RememberPlatformSelection(_platformSelectionIsFile, PlatformBox.Text);
                _platformSelectionIsFile = isFile;
                RefreshPlatformList(
                    replaceSelection: true,
                    preferredSelection: GetPlatformSelection(isFile));
                RememberPlatformSelection(isFile, PlatformBox.Text);
            }

            // После смены типа высота пересчитывается под новый набор полей и окно
            // удерживается в рабочей области (issue #305). Отложенный вызов — панели
            // должны сначала перестроиться по новому типу.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(FitHeightToContent));
        }

        private void OnPickPlatform_Click(object sender, RoutedEventArgs e)
        {
            RefreshPlatformList();
            var dlg = new PlatformVersionPickerWindow(_platforms, PlatformBox.Text ?? "")
            {
                Owner = this
            };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Result))
            {
                PlatformBox.Text = dlg.Result;
                RememberPlatformSelection(TypeBox.SelectedIndex != 1, dlg.Result);
            }
        }

        private void OnEditPlatformPaths_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow?.DataContext is ViewModels.MainViewModel vm)
            {
                vm.OpenSettingsCommand.Execute(null);
                RefreshPlatformList();
            }
            else
            {
                _dialogs.ShowInfo(
                    LocalizationManager.T("CreateInfobase.NoPlatformsPathsMsg"),
                    LocalizationManager.T("CreateInfobase.PlatformPathsTitle"));
            }
        }

        private void LoadInstalledTemplates()
        {
            _templateLoadingCts?.Cancel();
            var cts = new CancellationTokenSource();
            _templateLoadingCts = cts;

            // Показываем окно сразу, а тяжёлое сканирование каталогов шаблонов
            // выполняем в фоне; список дособерётся, когда будет готов.
            ShowTemplateLoading(true);
            TemplateTree.ItemsSource = null;
            _flatTemplates = new List<OneCTemplateService.TemplateInfo>();

            Task.Run(() =>
            {
                // Основной источник — первый фактически существующий корень.
                // GetTemplateRootFolders() ставит настроенные пользователем каталоги
                // первыми, поэтому подсказка отражает реально используемый каталог,
                // а не дефолтный tmplts.
                var roots = OneCTemplateService.GetTemplateRootFolders().ToList();
                var primary = roots.Count > 0
                    ? roots[0]
                    : OneCTemplateService.GetConfiguredOrDefaultTemplatePath();
                var primaryExists = Directory.Exists(primary);

                var templates = OneCTemplateService.FindInstalledTemplates().ToList();
                var tree = OneCTemplateService.BuildTemplateTree(templates);
                return new TemplateLoadResult(primary, roots, primaryExists, templates, tree);
            }).ContinueWith(t =>
            {
                if (cts.IsCancellationRequested)
                    return;

                ShowTemplateLoading(false);

                if (t.IsFaulted)
                {
                    _flatTemplates = new List<OneCTemplateService.TemplateInfo>();
                    TemplateTree.ItemsSource = null;
                    TemplateRootsHint.Text +=
                        LocalizationManager.T("CreateInfobase.LoadingFailed");
                    return;
                }

                var r = t.Result;
                _flatTemplates = r.Templates;
                _templateTree = r.Tree;
                ApplyTemplateFilter();
                UpdateTemplateRootsHint(r.Primary, r.Roots, r.PrimaryExists);

                if (r.Templates.Count == 0)
                    TemplateRootsHint.Text +=
                        LocalizationManager.T("CreateInfobase.NoTemplates");
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void UpdateTemplateRootsHint(string primary, IReadOnlyList<string> roots, bool primaryExists)
        {
            TemplateRootsHint.Text =
                string.Format(LocalizationManager.T("CreateInfobase.TemplateRootsDefault"), primary) +
                (primaryExists ? "" : LocalizationManager.T("CreateInfobase.FolderNotCreated")) +
                (roots.Count > 1
                    ? string.Format(LocalizationManager.T("CreateInfobase.AlsoChecked"),
                        string.Join("; ", roots.Where(r =>
                            !r.Equals(primary, StringComparison.OrdinalIgnoreCase))))
                    : "");
        }

        private void ShowTemplateLoading(bool loading)
        {
            if (TemplateLoadingPanel != null)
                TemplateLoadingPanel.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            if (TemplateTree != null)
                TemplateTree.IsEnabled = !loading;
        }

        // ============ Поиск, свёртка/развёртка и доп. информация по шаблонам (issue #138) ============

        private void OnTplSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyTemplateFilter();
        }

        private void OnTplCollapseAll_Click(object sender, RoutedEventArgs e)
        {
            SetAllExpanded(false);
        }

        private void OnTplExpandAll_Click(object sender, RoutedEventArgs e)
        {
            SetAllExpanded(true);
        }

        /// <summary>
        /// Применяет фильтр поиска к дереву шаблонов. Пустой запрос показывает всё дерево.
        /// </summary>
        private void ApplyTemplateFilter()
        {
            if (TemplateTree is null)
                return;
            var query = (TplSearchBox?.Text ?? string.Empty).Trim();
            TemplateTree.ItemsSource = string.IsNullOrEmpty(query)
                ? _templateTree
                : FilterTemplateNodes(_templateTree, query);
        }

        /// <summary>Рекурсивно оставляет только ветви, ведущие к узлам, совпадающим с запросом.</summary>
        private static List<OneCTemplateService.TemplateTreeNode> FilterTemplateNodes(
            IReadOnlyList<OneCTemplateService.TemplateTreeNode> nodes, string query)
        {
            var result = new List<OneCTemplateService.TemplateTreeNode>();
            foreach (var n in nodes)
            {
                if (n.Children.Count > 0)
                {
                    var kids = FilterTemplateNodes(n.Children, query);
                    if (kids.Count > 0)
                    {
                        var clone = new OneCTemplateService.TemplateTreeNode
                        {
                            Title = n.Title,
                            Subtitle = n.Subtitle,
                            Template = n.Template
                        };
                        foreach (var k in kids)
                            clone.Children.Add(k);
                        result.Add(clone);
                    }
                    else if (NodeMatchesQuery(n, query))
                    {
                        result.Add(n);
                    }
                }
                else if (NodeMatchesQuery(n, query))
                {
                    result.Add(n);
                }
            }
            return result;
        }

        /// <summary>Проверяет, что узел (название, подпись или поля шаблона) содержит запрос.</summary>
        private static bool NodeMatchesQuery(OneCTemplateService.TemplateTreeNode n, string query)
        {
            if (n.Title?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.Subtitle?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            var t = n.Template;
            if (t is null)
                return false;
            if (t.DisplayName?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (t.Vendor?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return t.ConfigurationName?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Разворачивает или сворачивает все группы дерева шаблонов.
        /// Повторяет проходы до стабилизации, чтобы учесть ленивую генерацию контейнеров TreeViewItem.
        /// </summary>
        private void SetAllExpanded(bool expanded)
        {
            for (var pass = 0; pass < 6; pass++)
            {
                var changed = WalkAndToggle(TemplateTree, expanded);
                if (changed == 0)
                    break;
                TemplateTree.UpdateLayout();
            }
        }

        private static int WalkAndToggle(ItemsControl parent, bool expanded)
        {
            var changed = 0;
            foreach (var item in parent.Items)
            {
                if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem tvi)
                    continue;
                if (tvi.IsExpanded != expanded)
                {
                    tvi.IsExpanded = expanded;
                    changed++;
                }
                if (expanded && tvi.HasItems)
                    changed += WalkAndToggle(tvi, expanded);
            }
            return changed;
        }

        private sealed class TemplateLoadResult
        {
            public TemplateLoadResult(
                string primary,
                IReadOnlyList<string> roots,
                bool primaryExists,
                List<OneCTemplateService.TemplateInfo> templates,
                IReadOnlyList<OneCTemplateService.TemplateTreeNode> tree)
            {
                Primary = primary;
                Roots = roots;
                PrimaryExists = primaryExists;
                Templates = templates;
                Tree = tree;
            }

            public string Primary { get; }
            public IReadOnlyList<string> Roots { get; }
            public bool PrimaryExists { get; }
            public List<OneCTemplateService.TemplateInfo> Templates { get; }
            public IReadOnlyList<OneCTemplateService.TemplateTreeNode> Tree { get; }
        }

        private List<OneCTemplateService.TemplateInfo> _flatTemplates = new();
        /// <summary>Полное дерево шаблонов (до фильтрации по поиску), issue #138.</summary>
        private IReadOnlyList<OneCTemplateService.TemplateTreeNode> _templateTree =
            new List<OneCTemplateService.TemplateTreeNode>();
        private CancellationTokenSource? _templateLoadingCts;

        private static string SuggestNameFromTemplate(OneCTemplateService.TemplateInfo t)
        {
            // Как в стартере 1С: последний сегмент Catalog (без суффиксов демо/пустая)
            var segs = t.CatalogSegments;
            if (segs.Length > 0)
            {
                var leaf = segs[^1]
                    .Replace(LocalizationManager.T("Template.SuffixDemo"), "", StringComparison.OrdinalIgnoreCase)
                    .Replace(LocalizationManager.T("Template.SuffixEmpty"), "", StringComparison.OrdinalIgnoreCase)
                    .Trim();
                if (!string.IsNullOrWhiteSpace(leaf))
                    return leaf;
            }
            if (!string.IsNullOrWhiteSpace(t.ConfigurationName) && t.ConfigurationName != "—")
                return t.ConfigurationName;
            var parts = t.RelativePath.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return parts[^2];
            if (parts.Length == 1)
                return Path.GetFileNameWithoutExtension(parts[0]);
            return Path.GetFileNameWithoutExtension(t.FilePath);
        }

        private void OnTemplateTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is OneCTemplateService.TemplateTreeNode { Template: { } t })
            {
                TemplateBox.Text = t.FilePath;
                if (string.IsNullOrWhiteSpace(NameBox.Text) || NameWasSuggested())
                    NameBox.Text = SuggestNameFromTemplate(t);
            }
        }

        private bool NameWasSuggested()
        {
            var name = NameBox.Text?.Trim() ?? "";
            return _flatTemplates.Any(t =>
                SuggestNameFromTemplate(t).Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        private void OnRefreshTemplates_Click(object sender, RoutedEventArgs e)
        {
            LoadInstalledTemplates();
        }

        private void OnBrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new WinForms.FolderBrowserDialog
            {
                Description = LocalizationManager.T("CreateInfobase.ChooseFolderDescription"),
                UseDescriptionForTitle = true
            };
            if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                FilePathBox.Text = dlg.SelectedPath;
        }

        private void OnBrowseTemplate_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = LocalizationManager.T("CreateInfobase.TemplateDialogTitle"),
                Filter = $"{LocalizationManager.T("CreateInfobase.FilterTemplates")}|*.cf;*.dt|{LocalizationManager.T("CreateInfobase.FilterConfig")}|*.cf|{LocalizationManager.T("CreateInfobase.FilterDump")}|*.dt|{LocalizationManager.T("Common.AllFiles")}|*.*"
            };
            if (dlg.ShowDialog() == true)
                TemplateBox.Text = dlg.FileName;
        }

        /// <summary>
        /// Кнопка копирования у поля «Имя базы данных» (issue #306): переносит
        /// значение «Имя базы на сервере» (RefBox) в имя базы данных (DbNameBox).
        /// Пустое значение Ref имя базы данных не затирает.
        /// </summary>
        private void OnCopyRefToDbName_Click(object sender, RoutedEventArgs e)
        {
            var refName = RefBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(refName))
                DbNameBox.Text = refName;
        }

        // ================= Живая подсказка формата DBSrvr (issue #305) =================

        /// <summary>
        /// Восстанавливает последний тип базы, сервер СУБД и порт из настроек (issue #305).
        /// Тип выставляется первым (чтобы клиент-серверные поля были видны), затем —
        /// сервер/порт. Вызывается после <c>InitializeComponent()</c>, поэтому <see cref="TypeBox"/>
        /// уже существует; переключение типа на этом шаге не затирает подставляемые поля.
        /// </summary>
        private void RestoreLastCreateState()
        {
            var settings = _repository.LoadSettings();
            TypeBox.SelectedIndex =
                string.Equals(settings.LastCreateDbType, "ClientServer", StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : 0;

            if (!string.IsNullOrWhiteSpace(settings.LastCreateDbServer))
                DbServerBox.Text = settings.LastCreateDbServer;
            if (!string.IsNullOrWhiteSpace(settings.LastCreateDbPort))
                DbPortBox.Text = settings.LastCreateDbPort;
        }

        /// <summary>
        /// Закрытие окна: запоминает «Сервер СУБД», порт и тип базы (issue #305). Сохранение
        /// только после успешного создания не покрывало сценарий «ввёл значение и закрыл окно
        /// без создания». Сервер/порт сохраняются только в клиент-серверном режиме; тип —
        /// всегда (файловый режим не затирает ранее сохранённый сервер СУБД).
        /// </summary>
        private void OnWindowClosed(object? sender, EventArgs e)
        {
            try
            {
                var isClientServer = TypeBox.SelectedIndex == 1;
                _createService.SaveLastDbServer(
                    isClientServer ? DbServerBox.Text?.Trim() ?? "" : "",
                    isClientServer ? DbPortBox.Text?.Trim() ?? "" : "",
                    isClientServer);
            }
            catch
            {
                // Несохранение последнего сервера СУБД не должно ломать закрытие окна.
            }
        }

        /// <summary>
        /// Выбор сервера 1С из списка (issue #305): строка «server:port» остаётся в поле
        /// целиком, как в окне правки свойств базы; при создании она разнесётся на сервер
        /// и порт. Свободный ввод не затрагивается: при IsTextSearchEnabled=false событие
        /// приходит только от явного выбора элемента списка, а не от автоподбора по вводимому
        /// тексту (0.3.9.262).
        /// </summary>
        private void OnServerBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServerBox.SelectedItem is not string item)
                return;

            ServerBox.Text = item;
            // Сброс выделения откладываем: немедленный SelectedItem=null в редактируемом
            // ComboBox синхронизирует Text обратно и затирает только что подставленную
            // строку («поле оставалось пустым», регресс после 0.3.9.150, issue #305).
            var box = ServerBox;
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    if (box.SelectedItem is not null)
                    {
                        // Страховка (issue #305, 0.3.9.295): если сброс выделения всё же
                        // перезаписал поле пустой/старой строкой — восстанавливаем подставленную.
                        var keep = box.Text;
                        box.SelectedItem = null;
                        if (!string.Equals(box.Text, item, StringComparison.Ordinal))
                            box.Text = string.IsNullOrWhiteSpace(box.Text) ? keep : box.Text;
                    }
                }));
        }

        /// <summary>
        /// Страховка подстановки выбранного сервера 1С (issue #305): при закрытии
        /// выпадающего списка ещё раз применяем выбранный элемент к полю — покрывает
        /// случай, когда SelectionChanged пришёл до готовности редактируемого текста.
        /// </summary>
        private void OnServerBox_DropDownClosed(object sender, EventArgs e)
        {
            if (ServerBox.SelectedItem is string item &&
                !string.Equals(ServerBox.Text, item, StringComparison.Ordinal))
                ServerBox.Text = item;
        }

        /// <summary>
        /// Клик по подсказке под полем «Сервер СУБД» (issue #305): если поле пустое,
        /// подставляет пример «localhost».
        /// </summary>
        private void OnDbServerHint_Click(object sender, MouseButtonEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(DbServerBox.Text))
                DbServerBox.Text = "localhost";
        }

        /// <summary>Изменение выбранной СУБД из списка (редактируемый ComboBox).</summary>
        private void OnDbms_Changed(object sender, SelectionChangedEventArgs e)
        {
            UpdateDbServerHint();
        }

        /// <summary>Ручной ввод типа СУБД в редактируемый ComboBox.</summary>
        private void OnDbms_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateDbServerHint();
        }

        /// <summary>Изменение адреса сервера СУБД — обновляем пример передаваемой строки.</summary>
        private void OnDbServer_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateDbServerHint();
        }

        /// <summary>Изменение порта СУБД — обновляем пример передаваемой строки.</summary>
        private void OnDbPort_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateDbServerHint();
        }

        /// <summary>
        /// Живая подсказка под полем «Сервер СУБД» (issue #305): формат значения
        /// DBSrvr зависит от выбранной СУБД (PostgreSQL — «host port=NNNN» через пробел,
        /// MSSQL Server — «host,NNNN»), а при заполненном сервере показываем,
        /// что именно будет передано в CREATEINFOBASE.
        /// </summary>
        private void UpdateDbServerHint()
        {
            var dbms = DbmsBox.Text?.Trim() ?? "";
            var hintKey = string.Equals(dbms, "PostgreSQL", StringComparison.OrdinalIgnoreCase)
                ? "CreateInfobase.DbServerHintPostgres"
                : string.Equals(dbms, "MSSQLServer", StringComparison.OrdinalIgnoreCase)
                    ? "CreateInfobase.DbServerHintMssql"
                    : "CreateInfobase.DbServerHintOther";
            var hint = LocalizationManager.T(hintKey);

            var assembled = CreateInfobaseService.BuildDbServerString(
                dbms, DbServerBox.Text, DbPortBox.Text);
            if (!string.IsNullOrWhiteSpace(assembled))
                hint += "\n" + string.Format(
                    LocalizationManager.T("CreateInfobase.DbServerPreview"), assembled);

            DbServerHint.Text = hint;
        }

        /// <summary>
        /// Сообщение об ошибке создания (issue #305): к тексту платформы добавляется понятная
        /// подсказка про порт кластера/агента 1С, если сервер указан с портом — это самая
        /// частая причина «база не создаётся» на сервере с нестандартным портом.
        /// </summary>
        private static string BuildCreateFailedMessage(CreateInfobaseResult result, string serverPort)
        {
            var message = string.Format(
                LocalizationManager.T("CreateInfobase.CreateFailed"), result.ErrorMessage ?? "");
            return string.IsNullOrWhiteSpace(serverPort)
                ? message
                : message + LocalizationManager.T("CreateInfobase.CreateFailedPortHint");
        }

        private void OnCreate_Click(object sender, RoutedEventArgs e)
        {
            // Сервер 1С выбирается одним полем «server:port» — разносим на сервер и порт (issue #305).
            CreateInfobaseService.ParseServerPort(ServerBox.Text, out var serverName, out var serverPortFromName);
            var serverPort = serverPortFromName > 0
                ? serverPortFromName.ToString()
                : "";

            var request = new CreateInfobaseRequest
            {
                Name = NameBox.Text?.Trim() ?? "",
                FromTemplate = _fromTemplate,
                TemplatePath = TemplateBox.Text?.Trim(),
                PlatformVersion = PlatformBox.Text?.Trim() ?? "",
                IsFile = TypeBox.SelectedIndex != 1,
                FilePath = FilePathBox.Text?.Trim(),
                Server = serverName,
                ServerPort = serverPort,
                DatabaseName = RefBox.Text?.Trim(),
                Dbms = DbmsBox.Text?.Trim(),
                DbServer = DbServerBox.Text?.Trim(),
                DbPort = DbPortBox.Text?.Trim(),
                DbName = DbNameBox.Text?.Trim(),
                DbUser = DbUserBox.Text?.Trim(),
                DbPassword = DbPwdBox.Password ?? "",
                CreateSqlDatabase = CreateDbCheck.IsChecked == true,
                BlockScheduledJobs = BlockJobsCheck.IsChecked == true,
                ForbidSpeechRecognition = ForbidSpeechCheck.IsChecked == true,
                GroupPath = _selectedGroupPath
            };

            var result = _createService.TryCreate(request, confirmVersionMismatch: false);
            switch (result.Kind)
            {
                case CreateInfobaseResultKind.EnterName:
                    _dialogs.ShowWarning(LocalizationManager.T("CreateInfobase.EnterName"), LocalizationManager.T("CreateInfobase.CreateTitle"));
                    return;
                case CreateInfobaseResultKind.EnterTemplateFile:
                    _dialogs.ShowWarning(LocalizationManager.T("CreateInfobase.EnterTemplateFile"), LocalizationManager.T("CreateInfobase.CreateTitle"));
                    return;
                case CreateInfobaseResultKind.NoPlatform:
                    _dialogs.ShowWarning(
                        LocalizationManager.T("CreateInfobase.NoPlatform"),
                        LocalizationManager.T("CreateInfobase.CreateTitle"));
                    return;
                case CreateInfobaseResultKind.EnterFilePath:
                    _dialogs.ShowWarning(LocalizationManager.T("CreateInfobase.EnterFilePath"), LocalizationManager.T("CreateInfobase.CreateTitle"));
                    return;
                case CreateInfobaseResultKind.EnterServerAndDb:
                    _dialogs.ShowWarning(LocalizationManager.T("CreateInfobase.EnterServerAndDb"), LocalizationManager.T("CreateInfobase.CreateTitle"));
                    return;
                case CreateInfobaseResultKind.VersionMismatch:
                {
                    // Вариант 2 (#91): заранее предупреждаем, если выбранная версия платформы
                    // отличается (по major.minor) от версий, которыми уже работают
                    // клиент-серверные базы на этом же сервере. «Нет» — прерывает создание.
                    // В предупреждении показываются ОБА адреса (issue #305): адрес из ввода
                    // пользователя и полный адрес найденной базы из списка (с портом) —
                    // чтобы было понятно, откуда взялся порт, которого пользователь не вводил.
                    var mismatchServerAddress = string.IsNullOrWhiteSpace(result.IncompatibleExistingServerAddress)
                        ? request.Server
                        : result.IncompatibleExistingServerAddress;
                    var enteredServerAddress = CreateInfobaseService.Format1CServer(serverName, serverPortFromName);
                    if (!_dialogs.Confirm(
                            string.Format(
                                LocalizationManager.T("CreateInfobase.VersionMismatchMsg"),
                                request.PlatformVersion, result.IncompatibleExistingVersion,
                                mismatchServerAddress, enteredServerAddress),
                            LocalizationManager.T("CreateInfobase.VersionMismatchTitle")))
                        return;
                    result = _createService.TryCreate(request, confirmVersionMismatch: true);
                    if (result.Kind == CreateInfobaseResultKind.CreateFailed)
                    {
                        _dialogs.ShowError(
                            BuildCreateFailedMessage(result, serverPort),
                            LocalizationManager.T("CreateInfobase.CreateTitle"));
                        return;
                    }
                    break;
                }
                case CreateInfobaseResultKind.CreateFailed:
                    _dialogs.ShowError(
                        BuildCreateFailedMessage(result, serverPort),
                        LocalizationManager.T("CreateInfobase.CreateTitle"));
                    return;
                case CreateInfobaseResultKind.Success:
                    break;
            }

            Result = result.CreatedInfobase;
            DialogResult = true;
        }

        private sealed class PlatformItem
        {
            public PlatformItem(string version) => Version = version;
            public string Version { get; }
            public string DisplayName => Version;
        }
    }
}
#endif
