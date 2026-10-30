using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Серверы 1С» (0.3.9.124, Windows/WPF): встроенный монитор серверов 1С
    /// через утилиту rac. Слева — справочник «Сервера управления» (issue #3): список
    /// серверов (только Описание) с кнопками «Добавить/Изменить/Удалить»; при активации
    /// строки выполняется подключение к кластеру. Справа — выбор кластера, вкладки
    /// «Рабочие процессы / Сеансы / Соединения / Блокировки / Регламентные задания /
    /// Информация о кластере» и статус-строка. Вся логика — в чистой ViewModel
    /// <see cref="ServerMonitorViewModel"/>. Адрес/порт/логин/пароль хранятся в
    /// справочнике (server_list.json) и на форме не показываются.
    /// </summary>
    public partial class ServerMonitorWindow : Window
    {
        private readonly ServerMonitorViewModel _vm;

        /// <summary>
        /// Открытие «Импорта из кластера 1С» (issue #27): делегат MainViewModel —
        /// окно монитора к репозиторию напрямую не обращается.
        /// </summary>
        private readonly Action<ManagementServer?, Guid?>? _openClusterImport;

        /// <summary>
        /// Открытие «Миграции между кластерами 1С» (issue #41): делегат MainViewModel —
        /// окно монитора к репозиторию напрямую не обращается.
        /// </summary>
        private readonly Action<ManagementServer?, Guid?>? _openClusterMigration;

        /// <summary>
        /// Настройки окна «Серверы 1С» (issue #75): сюда пишутся сохранённые ширины
        /// колонок вкладок при закрытии окна. Загружаются один раз в конструкторе.
        /// </summary>
        private AppSettings _windowSettings = new();

        /// <summary>
        /// Текущие фактические ширины колонок по вкладкам (issue #75): ключ — вкладка
        /// (<c>Sessions</c>/<c>Connections</c>/<c>Locks</c>/<c>Processes</c>). Обновляется
        /// при изменении размеров заголовков колонок и сохраняется в настройки при закрытии.
        /// </summary>
        private readonly Dictionary<string, double[]> _columnWidths = new();

        /// <summary>
        /// Заголовки, к которым уже привязано отслеживание ширины (issue #75): заголовки
        /// пересоздаются при переключении вкладок, повторные подписки на один экземпляр не нужны.
        /// </summary>
        private readonly HashSet<System.Windows.Controls.Primitives.DataGridColumnHeader> _trackedColumnHeaders = new();

        /// <summary>
        /// Гриды, на которых уже установлен ОБЩИЙ перехват перетаскивания границ колонок
        /// (issue #75, 1.0.1.4): перехват висит на гриде (PreviewMouseLeftButtonDown), а не на
        /// заголовках — заголовки пересоздаются при переключении вкладок, и их перехват через
        /// генератор контейнеров в прежних версиях не срабатывал у пользователя (комментарий
        /// 7OH: «вообще ничего не меняется»), грид же стабилен на весь срок жизни окна.
        /// </summary>
        private readonly HashSet<DataGrid> _dragInterceptedGrids = new();

        /// <summary>Состояние перетаскивания границы колонки (issue #75; 1.0.0.96+).</summary>
        private DataGrid? _resizingGrid;
        /// <summary>
        /// Заголовок, границу которого перетаскивают (issue #75, 1.0.1.4): нужен для пересчёта
        /// координат мыши в локальную систему заголовка во время захвата гридом.
        /// </summary>
        private System.Windows.Controls.Primitives.DataGridColumnHeader? _resizingHeader;
        private int _resizingColumnIndex = -1;
        private bool _resizingWithCtrl;
        private double[] _resizeStartWidths = Array.Empty<double>();
        private double _resizeTotalDelta;
        /// <summary>Начальная горизонтальная позиция мыши в заголовке (перетаскивание, issue #75).</summary>
        private double _resizeStartX;
        /// <summary>Идёт ли собственный цикл перетаскивания (захват мыши гридом, issue #75).</summary>
        private bool _resizingActive;
        /// <summary>
        /// Ширина «зоны границы» заголовка колонки, px (issue #75): перетаскивание
        /// начинается, когда курсор в правой зоне ширины Thumb
        /// PART_RightHeaderGripper (7 px) + запас — клик в эту зону перехватывается
        /// ДО внутреннего обработчика DataGridColumnHeader. Расширена с 16 до 24 px:
        /// пользователи целятся в границу и по краю заголовка, узкая зона (16 px)
        /// пропускала часть кликов в штатный resize/reorder DataGrid (комментарий 7OH
        /// «Нет — не работает. Всё по-старому», issue #75).
        /// </summary>
        private const double HeaderResizeHotZone = 24.0;

        /// <summary>Фоновое обновление кэша статусов служб (5 с; issue #3, часть 2).</summary>
        private System.Threading.Timer? _servicesStatusTimer;

        private static readonly TimeSpan ServicesStatusRefreshInterval = TimeSpan.FromSeconds(5);

        /// <param name="openClusterImport">
        /// Открытие окна импорта с сервером и выбранным кластером, текущими на момент
        /// нажатия «Импорт» (issue #27; кластер — issue #25); null — кнопка «Импорт»
        /// под списком баз скрыта/не активна.
        /// </param>
        /// <param name="openClusterMigration">
        /// Открытие окна миграции с сервером и выбранным кластером, текущими на момент
        /// нажатия «Миграция» (issue #41); null — кнопка «Миграция» под списком баз
        /// скрыта/не активна.
        /// </param>
        public ServerMonitorWindow(
            Action<ManagementServer?, Guid?>? openClusterImport = null,
            Action<ManagementServer?, Guid?>? openClusterMigration = null)
        {
            InitializeComponent();
            _openClusterImport = openClusterImport;
            _openClusterMigration = openClusterMigration;

            var rac = AppServices.GetRequiredService<IRacClient>();
            var dialogs = AppServices.GetRequiredService<IDialogService>();
            var serverListStore = AppServices.GetRequiredService<IServerListStore>();
            // п.5 «Мониторинг лицензий» (1.0.0.87): установленные программные лицензии
            // (ring/licensetool) и порог «свободно меньше N» из настроек уведомлений.
            var ringLicenses = AppServices.GetRequiredService<IRingLicenseService>();
            var licenseAlerts = AppServices.GetRequiredService<LicenseManager>();
            var licenseSettings = AppServices.GetRequiredService<INotificationSettingsStore>().Load();

            // issue #69: диалог повтора удаления базы открывается из VM (после неудачного
            // rac «infobase drop») — колбэк реализует окно, чтобы VM оставалась тестируемой.
            _vm = new ServerMonitorViewModel(
                rac,
                dialogs,
                action => Application.Current?.Dispatcher.BeginInvoke(action),
                serverListStore,
                (errorText, login, password) =>
                {
                    // issue #69: диалог повтора открывается поверх активного окна
                    // приложения — сама «Серверы 1С», если она активна (обычный случай),
                    // иначе запасной вариант MainWindow (WindowOwnerResolver, issue #41).
                    var retry = new RacInfobaseDropRetryWindow(errorText, login, password)
                    {
                        Owner = WindowOwnerResolver.ActiveOrMain() ?? this
                    };
                    return retry.ShowDialog() == true
                        ? new ViewModels.DropRetryResult(
                            retry.Retry, retry.User, retry.Password,
                            retry.TerminateSessions, retry.BlockScheduledJobs)
                        : ViewModels.DropRetryResult.Cancel;
                },
                ringLicenses,
                licenseAlerts,
                licenseSettings.LicenseFreeThreshold,
                licenseSettings.LicenseAlertRepeatHours);

            DataContext = _vm;
            Title = LocalizationManager.T("ServerMonitor.Title");
            // Кнопка «Закрыть» на панели подключения убрана (issue #324) — есть кнопка ✕
            // в заголовке окна. Esc сохранён: раньше его давала IsCancel у панельной
            // кнопки, теперь обрабатываем вручную (фокус может быть в текстовом поле).
            PreviewKeyDown += OnWindowPreviewKeyDown;
            // issue #3: таблицы сеансов/соединений/блокировок биндятся на ОТФИЛЬТРОВАННЫЕ
            // коллекции — выбор базы фильтрует их на клиенте без перезагрузки кластера.
            // Список баз и заголовки вкладок привязаны в XAML (Infobases, *TabText).
            SessionsGrid.ItemsSource = _vm.FilteredSessions;
            ConnectionsGrid.ItemsSource = _vm.FilteredConnections;
            LocksGrid.ItemsSource = _vm.FilteredLocks;
            ProcessesGrid.ItemsSource = _vm.Processes;
            // п.5 «Мониторинг лицензий» (1.0.0.87): вкладка «Лицензии».
            LicensedSessionsGrid.ItemsSource = _vm.LicensedSessions;
            InstalledLicensesGrid.ItemsSource = _vm.InstalledLicenses;

            // Состояние окна запоминается (issue #3, комментарий 7OH): максимизировано или нет.
            // Ширины колонок вкладок (issue #75): применяем сохранённые (если есть), «минимальные»
            // ширины по содержимому заголовка заложены в XAML (MinWidth), отслеживаем изменение
            // фактических ширин через заголовки колонок и сохраняем при закрытии окна.
            try
            {
                var repository = AppServices.GetRequiredService<IInfobaseRepository>();
                var settings = repository.LoadSettings();
                _windowSettings = settings;
                if (settings.ServerMonitorMaximized)
                    WindowState = WindowState.Maximized;
                ApplySavedColumnWidths();
                AttachColumnWidthTracking();
            }
            catch
            {
                // Запоминание состояния — вспомогательная возможность, сбой не важен.
            }

            // Таймер автообновления останавливается при закрытии окна (без утечки);
            // состояние «максимизировано» и ширины колонок сохраняются в настройки.
            Closed += (_, _) =>
            {
                // Фоновый таймер статусов служб останавливается при закрытии окна (без утечки).
                _servicesStatusTimer?.Dispose();
                _servicesStatusTimer = null;
                _vm.Dispose();
                try
                {
                    // issue #75: фиксируем фактические ширины всех вкладок и сохраняем
                    // их в настройки — сохранение переживает закрытие программы.
                    CaptureAllColumnWidths();
                    _windowSettings.ServerMonitorMaximized = WindowState == WindowState.Maximized;
                    foreach (var (tabKey, widths) in _columnWidths)
                        ServerMonitorColumnWidthsStore.Save(_windowSettings, tabKey, widths);
                    AppServices.GetRequiredService<IInfobaseRepository>().SaveSettings(_windowSettings);
                }
                catch
                {
                    // Запоминание состояния — вспомогательная возможность, сбой не важен.
                }
            };
        }

        /// <summary>
        /// Двойной клик по строке справочника «Сервера управления» (issue #3): подключение
        /// к кластеру сервера (адрес/порт/логин/пароль из записи). Простое выделение строки
        /// подключение НЕ запускает — строку можно спокойно выделять для правки.
        /// </summary>
        private async void OnServersList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ServersList.SelectedItem is ManagementServer server)
                await _vm.ConnectToServerAsync(server);
        }

        /// <summary>
        /// «Подключиться» (issue #3): подключение к кластеру выбранного сервера
        /// справочника (если строка выделена); без выделения — по текущим полям.
        /// </summary>
        private void OnConnect_Click(object sender, RoutedEventArgs e) =>
            _vm.ConnectCommand.Execute(null);

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private void OnRefresh_Click(object sender, RoutedEventArgs e) => _vm.Refresh();

        private void OnTerminateSession_Click(object sender, RoutedEventArgs e) =>
            _vm.TerminateSessionCommand.Execute(null);

        private void OnDisconnectConnection_Click(object sender, RoutedEventArgs e) =>
            _vm.DisconnectConnectionCommand.Execute(null);

        /// <summary>
        /// Мультивыделение на вкладке «Сеансы» (issue #72): при изменении выбора DataGrid
        /// (простой клик, Ctrl — инверсия, Shift — диапазон) весь набор строк
        /// синхронизируется в ViewModel — кнопка «Завершить сеансы» действует на все.
        /// </summary>
        private void OnSessionsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            _vm.SelectedSessions = SessionsGrid.SelectedItems.Cast<RacSessionRow>().ToList();

        /// <summary>
        /// Мультивыделение на вкладке «Соединения» (issue #72): набор выбранных строк
        /// синхронизируется в ViewModel для кнопки «Разорвать соединения».
        /// </summary>
        private void OnConnectionsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            _vm.SelectedConnections = ConnectionsGrid.SelectedItems.Cast<RacConnectionRow>().ToList();

        /// <summary>
        /// «Сбросить» фильтр по базе (issue #3): клиентская операция — показываются
        /// сеансы/соединения/блокировки всех баз кластера, без rac-вызовов.
        /// </summary>
        private void OnClearInfobaseFilter_Click(object sender, RoutedEventArgs e) =>
            _vm.ClearInfobaseFilter();

        /// <summary>
        /// Сортировка колонок DataGrid (issue #3): явно управляем индикаторами — стрелка
        /// показывается у активной колонки, у остальных сбрасывается. Саму сортировку
        /// выполняет DataGrid (e.Handled = false), направление он переключает сам.
        /// </summary>
        /// <summary>Список баз по алфавиту (issue #3, кнопка «А↓»).</summary>
        private void OnSortInfobasesAsc_Click(object sender, RoutedEventArgs e) => _vm.SortInfobases(false);

        /// <summary>Список баз в обратном порядке (issue #3, кнопка «Я↓»).</summary>
        private void OnSortInfobasesDesc_Click(object sender, RoutedEventArgs e) => _vm.SortInfobases(true);

        private void OnGridSorting(object sender, DataGridSortingEventArgs e)
        {
            var grid = (DataGrid)sender;
            foreach (var column in grid.Columns)
            {
                if (column != e.Column)
                    column.SortDirection = null;
            }
            e.Handled = false;
        }

        // ===================== Ширины колонок вкладок (issue #75) =====================
        // Колонки вкладок «Сеансы/Соединения/Блокировки/Рабочие процессы» по умолчанию
        // имеют размер, достаточный для заголовка и стрелки сортировки (MinWidth задан
        // в XAML по расчёту ServerMonitorColumnWidthsStore, «звёздные» колонки не сжимают
        // заголовок). Фактические ширины отслеживаются через заголовки гридов и сохраняются
        // в настройки приложения при закрытии окна — восстановление переживает закрытие
        // программы.

        /// <summary>
        /// Применяет сохранённые ширины колонок всех вкладок (issue #75): для каждой
        /// колонки берётся сохранённая ширина, если она записана и валидна; значение
        /// ограничивается снизу MinWidth колонки (заголовок + стрелка). При отсутствии
        /// записи колонка остаётся с шириной из XAML (дефолтной, уже ≥ MinWidth).
        /// </summary>
        private void ApplySavedColumnWidths()
        {
            ApplySavedWidths(SessionsGrid, ServerMonitorColumnWidthsStore.SessionsKey);
            ApplySavedWidths(ConnectionsGrid, ServerMonitorColumnWidthsStore.ConnectionsKey);
            ApplySavedWidths(LocksGrid, ServerMonitorColumnWidthsStore.LocksKey);
            ApplySavedWidths(ProcessesGrid, ServerMonitorColumnWidthsStore.ProcessesKey);
        }

        private void ApplySavedWidths(DataGrid grid, string tabKey)
        {
            var widths = ServerMonitorColumnWidthsStore.TryLoad(_windowSettings, tabKey);
            if (widths is null)
                return;
            _columnWidths[tabKey] = widths;
            for (var i = 0; i < grid.Columns.Count && i < widths.Length; i++)
            {
                var width = widths[i];
                if (!ServerMonitorColumnWidthsStore.IsValid(width))
                    continue;
                var min = grid.Columns[i].MinWidth;
                if (min > 0 && width < min)
                    width = min;
                grid.Columns[i].Width = new DataGridLength(width);
            }
        }

        /// <summary>
        /// Отслеживает изменение ширины колонок гридов (issue #75): на каждый загруженный
        /// (видимый) заголовок колонки вешается SizeChanged — при перетаскивании границы
        /// фиксируются фактические ширины вкладки. Заголовки пересоздаются при переключении
        /// вкладок, поэтому подписка ставится на каждый Loaded грида.
        /// </summary>
        private void AttachColumnWidthTracking()
        {
            AttachColumnWidthTracking(SessionsGrid, ServerMonitorColumnWidthsStore.SessionsKey);
            AttachColumnWidthTracking(ConnectionsGrid, ServerMonitorColumnWidthsStore.ConnectionsKey);
            AttachColumnWidthTracking(LocksGrid, ServerMonitorColumnWidthsStore.LocksKey);
            AttachColumnWidthTracking(ProcessesGrid, ServerMonitorColumnWidthsStore.ProcessesKey);
        }

        private void AttachColumnWidthTracking(DataGrid grid, string tabKey)
        {
            // issue #75 (1.0.1.4): общий перехват перетаскивания границ колонок вешается
            // на ГРИД сразу (грид существует и стабилен с момента создания окна), тогда
            // как сами заголовки материализуются позже и пересоздаются при переключении
            // вкладок — прежний перехват на заголовках через генератор контейнеров
            // в реальном приложении не срабатывал (см. AttachGridColumnDrag).
            AttachGridColumnDrag(grid, tabKey);
            grid.Loaded += (_, _) =>
            {
                // Заголовки материализуются после первичной компоновки — обрабатываем
                // по низкому приоритету диспетчера, когда визуальное дерево стабильно.
                Dispatcher.BeginInvoke(
                    new Action(() => AttachHeaderWidthTracking(grid, tabKey)),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                // issue #75 (комментарий 7OH «Нет — не работает. Всё по-старому»):
                // заголовки неактивных вкладок могут материализоваться ПОЗЖЕ первого
                // Loaded (переключение вкладок, первое наполнение данными) — довешиваем
                // подписки по LayoutUpdated, пока все заголовки не будут обслужены
                // (повторные вызовы дедуплицируются HashSet-ом _trackedColumnHeaders).
                void OnGridLayoutUpdated(object? _, EventArgs __)
                {
                    AttachHeaderWidthTracking(grid, tabKey);
                }
                grid.LayoutUpdated -= OnGridLayoutUpdated;
                grid.LayoutUpdated += OnGridLayoutUpdated;
            };
        }

        private void AttachHeaderWidthTracking(DataGrid grid, string tabKey)
        {
            var presenter = FindVisualChild<System.Windows.Controls.Primitives.DataGridColumnHeadersPresenter>(grid);
            if (presenter is null)
                return;
            // issue #75 (1.0.0.96; в 1.0.0.95 — InvalidCastException при открытии окна):
            // коллекция Items презентера заголовков на момент применения (ApplicationIdle)
            // может содержать НЕ только DataGridColumnHeader, но и сырые колонки
            // (DataGridColumn) — Cast<> падал с InvalidCastException. OfType<> безопасно
            // пропускает лишние элементы, и ошибка больше не возникает.
            foreach (var header in presenter.Items.OfType<System.Windows.Controls.Primitives.DataGridColumnHeader>())
                AttachHeaderTracking(grid, tabKey, header);
            // Контейнеры заголовков могут достраиваться/пересоздаваться позже (переключение
            // вкладок, повторная генерация) — «доснимаем» их по статусу генератора.
            presenter.ItemContainerGenerator.StatusChanged += (_, _) =>
            {
                for (var i = 0; i < presenter.Items.Count; i++)
                {
                    if (presenter.ItemContainerGenerator.ContainerFromIndex(i)
                        is System.Windows.Controls.Primitives.DataGridColumnHeader header)
                        AttachHeaderTracking(grid, tabKey, header);
                }
            };
        }

        /// <summary>
        /// Общий перехват перетаскивания границ колонок НА УРОВНЕ ГРИДА (issue #75, 1.0.1.4):
        /// обычное перетаскивание двигает ВСЕ колонки справа как блок (их ширины не меняются,
        /// при переполнении области появляется горизонтальная прокрутка), с зажатым Ctrl
        /// меняется ширина только двух смежных колонок.
        /// <para>
        /// Почему на гриде, а не на заголовках. Штатный ресайз колонок WPF-DataGrid обрабатывает
        /// сам DataGridColumnHeader: при перетаскивании границы «звёздные» колонки справа
        /// перераспределяют ширину на себя и ужимаются до минимума — ровно то, что видел
        /// пользователь в прежних версиях (комментарий 7OH от 2026-10-29: «вообще ничего не
        /// меняется… ужимает 2 справа до определённого предела и всё», «вместо изменения
        /// размера двух соседних»). Прежние фиксы вешали перехват на сами заголовки
        /// (<c>PART_RightHeaderGripper</c> через FindName шаблона в 1.0.0.96, затем
        /// <c>PreviewMouseLeftButtonDown</c> заголовка в 1.0.1.1/1.0.1.3) — в реальном
        /// приложении он не срабатывал: заголовки материализуются и пересоздаются при
        /// переключении вкладок, и перехват через генератор контейнеров либо не успевал
        /// навеситься, либо внутренний обработчик заголовка срабатывал раньше.
        /// </para>
        /// <para>
        /// Здесь перехват висит на самом гриде: <c>PreviewMouseLeftButtonDown</c> грида
        /// срабатывает ДО любого обработчика заголовка (tunnel-фаза идёт от корня вниз),
        /// не зависит от материализации заголовков, а <c>e.Handled</c> останавливает доставку
        /// события штатному ресайзу DataGrid. Заголовок под курсором находится в момент клика
        /// подъёмом от источника события, зона границы — те же 24 px (<see cref="HeaderResizeHotZone"/>).
        /// Мышь на время перетаскивания захватывается гридом (<c>CaptureMouse</c>), движение
        /// применяет единую семантику «как в 1С» из <see cref="ServerMonitorColumnWidthsStore"/>.
        /// </para>
        /// </summary>
        private void AttachGridColumnDrag(DataGrid grid, string tabKey)
        {
            if (!_dragInterceptedGrids.Add(grid))
                return;
            grid.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (_resizingActive)
                    return;
                var source = e.OriginalSource as DependencyObject;
                var header = source is null
                    ? null
                    : FindAncestor<System.Windows.Controls.Primitives.DataGridColumnHeader>(source);
                if (header is null || header.Column is null)
                    return; // клик не по заголовку (ячейка, скроллбар и т.п.) — не трогаем
                var columnIndex = grid.Columns.IndexOf(header.Column);
                if (columnIndex < 0 || columnIndex >= grid.Columns.Count)
                    return;
                // Зона границы: правые ~24 px заголовка (ширина «ручки» + запас).
                var x = e.GetPosition(header).X;
                if (x < header.ActualWidth - HeaderResizeHotZone)
                    return;
                StartColumnDrag(grid, header, columnIndex, x);
                e.Handled = true;
            };
            grid.MouseMove += (_, e) =>
            {
                if (_resizingGrid == grid && _resizingActive && _resizingHeader is not null)
                    UpdateColumnDrag(grid, e.GetPosition(_resizingHeader).X);
            };
            grid.MouseLeftButtonUp += (_, _) =>
            {
                if (_resizingGrid == grid && _resizingActive)
                {
                    FinishColumnDrag(grid, tabKey);
                    // Освобождаем захват после завершения перетаскивания (LostMouseCapture
                    // при этом вызовет FinishColumnDrag повторно — защищено сбросом состояния).
                    grid.ReleaseMouseCapture();
                }
            };
            grid.LostMouseCapture += (_, _) =>
            {
                if (_resizingGrid == grid && _resizingActive)
                    FinishColumnDrag(grid, tabKey);
            };
        }

        /// <summary>
        /// Привязывает к заголовку отслеживание ширины (issue #75): при изменении ширины
        /// колонки (в т.ч. нашим перетаскиванием) фиксируются фактические ширины вкладки.
        /// Заголовки пересоздаются при переключении вкладок, поэтому подписка ставится на
        /// каждый материализованный экземпляр (дедупликация — <see cref="_trackedColumnHeaders"/>).
        /// Само перетаскивание перехватывается на гриде (<see cref="AttachGridColumnDrag"/>),
        /// здесь оно не дублируется.
        /// </summary>
        private void AttachHeaderTracking(DataGrid grid, string tabKey, System.Windows.Controls.Primitives.DataGridColumnHeader header)
        {
            if (!_trackedColumnHeaders.Add(header))
                return;
            header.SizeChanged += (_, _) => CaptureColumnWidths(grid, tabKey);
        }

        /// <summary>
        /// Начало перетаскивания границы колонки (issue #75): фиксируем стартовые
        /// фактические ширины всех колонок (для Ctrl-режима нужна вся пара) и «замораживаем»
        /// гибкие (звёздные) колонки в пиксели: иначе при обычном перетаскивании звёздные
        /// колонки справа перераспределяли бы ширину на себя («двигает одну-две,
        /// упираются в третью» — и граница дальше не идёт).
        /// <para>
        /// Мышь захватывается ГРИДОМ (а не заголовком): грид стабилен, а заголовки
        /// пересоздаются при переключении вкладок — захват заголовка мог обрываться.
        /// Движение и отпускание обрабатываются на гриде (см. <see cref="AttachGridColumnDrag"/>).
        /// </para>
        /// </summary>
        private void StartColumnDrag(DataGrid grid, System.Windows.Controls.Primitives.DataGridColumnHeader header, int columnIndex, double startX)
        {
            _resizeStartWidths = grid.Columns.Select(c => c.ActualWidth).ToArray();
            for (var j = 0; j < grid.Columns.Count; j++)
            {
                if (grid.Columns[j].Width.IsStar)
                    grid.Columns[j].Width = new DataGridLength(grid.Columns[j].ActualWidth);
            }
            _resizingGrid = grid;
            _resizingHeader = header;
            _resizingColumnIndex = columnIndex;
            _resizingWithCtrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            _resizeTotalDelta = 0;
            _resizeStartX = startX;
            _resizingActive = true;
            // Захват мыши гридом: MouseMove продолжает приходить, даже когда курсор
            // выходит за пределы заголовка/грида (drag границы за пределы окна не обрывается).
            grid.CaptureMouse();
        }

        /// <summary>
        /// Применяет ширины колонок по правилам «как в 1С» (issue #75): обычное
        /// перетаскивание — <see cref="ServerMonitorColumnWidthsStore.ApplyDragMoveBlock"/>
        /// (все колонки справа сдвигаются как блок, ширины не меняются; появляется
        /// горизонтальная прокрутка), с Ctrl —
        /// <see cref="ServerMonitorColumnWidthsStore.ApplyDragResizePair"/> (меняются
        /// только две смежные колонки).
        /// </summary>
        private void UpdateColumnDrag(DataGrid grid, double currentX)
        {
            if (!_resizingActive || _resizingGrid != grid || _resizingColumnIndex < 0)
                return;
            var i = _resizingColumnIndex;
            var columns = grid.Columns;
            if (i >= columns.Count)
                return;
            _resizeTotalDelta = currentX - _resizeStartX;
            // Минимальные ширины колонок (заголовок + стрелка сортировки, issue #75):
            // колонка не сжимается ниже минимума ни в одном режиме.
            var minWidths = columns.Select(c => c.MinWidth > 0 ? c.MinWidth : ServerMonitorColumnWidthsStore.AbsoluteMinWidth).ToArray();
            var newWidths = _resizingWithCtrl && i + 1 < columns.Count
                ? ServerMonitorColumnWidthsStore.ApplyDragResizePair(_resizeStartWidths, i, _resizeTotalDelta, minWidths)
                : ServerMonitorColumnWidthsStore.ApplyDragMoveBlock(_resizeStartWidths, i, _resizeTotalDelta, minWidths);
            for (var j = 0; j < columns.Count; j++)
                columns[j].Width = new DataGridLength(newWidths[j]);
        }

        private void FinishColumnDrag(DataGrid grid, string tabKey)
        {
            if (!_resizingActive || _resizingGrid != grid)
                return;
            // Фиксируем фактические ширины вкладки — при закрытии окна они сохранятся
            // в настройки (переживают закрытие программы).
            CaptureColumnWidths(grid, tabKey);
            _resizingGrid = null;
            _resizingHeader = null;
            _resizingColumnIndex = -1;
            _resizeStartWidths = Array.Empty<double>();
            _resizeTotalDelta = 0;
            _resizingActive = false;
        }

        /// <summary>
        /// Фиксирует фактические ширины колонок грида по ключу вкладки (issue #75).
        /// Гибкие колонки (*/Auto) записываются нулём — «не сохранять»: восстановление
        /// оставит такой колонке дефолтную (звёздную) ширину из разметки, и она
        /// продолжит занимать свободное место окна.
        /// </summary>
        private void CaptureColumnWidths(DataGrid grid, string tabKey)
        {
            _columnWidths[tabKey] = grid.Columns
                .Select(c => c.Width.IsStar || c.Width.IsAuto ? 0.0 : c.ActualWidth)
                .ToArray();
        }

        /// <summary>Фиксирует фактические ширины всех четырёх вкладок перед сохранением (issue #75).</summary>
        private void CaptureAllColumnWidths()
        {
            CaptureColumnWidths(SessionsGrid, ServerMonitorColumnWidthsStore.SessionsKey);
            CaptureColumnWidths(ConnectionsGrid, ServerMonitorColumnWidthsStore.ConnectionsKey);
            CaptureColumnWidths(LocksGrid, ServerMonitorColumnWidthsStore.LocksKey);
            CaptureColumnWidths(ProcessesGrid, ServerMonitorColumnWidthsStore.ProcessesKey);
        }

        /// <summary>
        /// Ищет в визуальном дереве элемент заданного типа (для доступа к контейнерам
        /// заголовков DataGrid, issue #75).
        /// </summary>
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed)
                    return typed;
                var result = FindVisualChild<T>(child);
                if (result is not null)
                    return result;
            }
            return null;
        }

        /// <summary>
        /// Ищет предка заданного типа в визуальном дереве (включая сам узел): по источнику
        /// события клика поднимается до <see cref="System.Windows.Controls.Primitives.DataGridColumnHeader"/>
        /// (клик может прийти в Thumb «ручки» PART_RightHeaderGripper или внутренний элемент
        /// шаблона заголовка). Та же схема, что в MainWindow (issue #75, 1.0.1.4).
        /// </summary>
        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current is not null)
            {
                if (current is T typed)
                    return typed;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        /// <summary>
        /// «Сохранить изменения» на вкладке «Информация о кластере» (issue #324, C3):
        /// rac «cluster update» по изменённым параметрам, перечитывание данных.
        /// </summary>
        private void OnSaveClusterInfo_Click(object sender, RoutedEventArgs e) =>
            _vm.SaveClusterPropertiesCommand.Execute(null);

        /// <summary>
        /// «Диагностика сети…» (0.3.9.232, функция 12): открывает окно диагностики
        /// с адресом/портом выбранного сервера справочника. Не требует успешного
        /// подключения rac — диагностика нужна именно до подключения.
        /// </summary>
        private void OnDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            var target = NetworkDiagnosticsTargets.FromServerMonitor(_vm.ServerAddress, _vm.ServerPort);
            var vm = new NetworkDiagnosticsViewModel(
                AppServices.GetRequiredService<INetworkDiagnosticsService>(),
                target,
                action => Application.Current?.Dispatcher.BeginInvoke(action));
            new NetworkDiagnosticsWindow(vm) { Owner = this }.ShowDialog();
        }

        // ===================== Базы кластера (issue #3) =====================

        /// <summary>
        /// «Добавить базу»: модальный диалог создания информационной базы (rac
        /// «infobase create») → создание в выбранном кластере → обновление списков.
        /// </summary>
        private async void OnAddInfobase_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new RacInfobaseCreateWindow() { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result is not { } request)
                return;
            await _vm.CreateInfobaseAsync(request);
        }

        /// <summary>
        /// «Изменить базу» (issue #3, комментарий 7OH): модальный диалог правки свойств
        /// (поля предзаполнены, rac «infobase update») → изменение → обновление списков.
        /// </summary>
        private async void OnEditInfobase_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedInfobase is not { } row || row.InfobaseId == Guid.Empty)
                return;
            // Полный диалог свойств (issue #3, 7OH 17.10): «infobase info» с учётными
            // данными администратора ИБ из списка баз приложения либо введёнными вручную.
            System.Collections.Generic.IEnumerable<Infobase>? bases = null;
            try
            {
                bases = AppServices.GetRequiredService<IInfobaseRepository>().Load();
            }
            catch
            {
                // Список баз недоступен — учётные данные пользователь введёт сам.
            }
            var propertiesVm = _vm.CreateInfobasePropertiesViewModel(SummaryFromRow(row), bases);
            var dialog = new RacInfobasePropertiesWindow(propertiesVm) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result is not { } update)
                return;
            await _vm.UpdateSelectedInfobaseAsync(update);
        }

        /// <summary>Строит «infobase summary» из строки списка для предзаполнения диалога правки.</summary>
        private static RacInfobaseSummary SummaryFromRow(RacInfobaseRow row) => new()
        {
            InfobaseId = row.InfobaseId,
            Name = row.Name,
            // RacInfobaseRow подставляет «—» для пустых строк — в диалог возвращаем пустоту.
            Descr = Unplacehold(row.Descr),
            Dbms = row.Dbms,
            DbServer = row.DbServer,
            DbName = row.DbName,
            DbUser = Unplacehold(row.DbUser),
            Locale = Unplacehold(row.Locale),
            SecurityLevel = row.SecurityLevel
        };

        private static string Unplacehold(string value) =>
            string.Equals(value, "—", StringComparison.Ordinal) ? string.Empty : value;

        /// <summary>
        /// «Удалить базу» (issue #3, комментарий 7OH от 2026-10-17): диалог удаления
        /// с выбором «Просто удалить из кластера» / «Удалить с удалением на сервере
        /// СУБД» (в кластере бывают только клиент-серверные базы, 7OH 18.10 20:24) →
        /// rac «infobase drop [--drop-database]» → обновление.
        /// </summary>
        private async void OnDeleteInfobase_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedInfobase is not { } row)
                return;
            var dialog = new RacInfobaseDropWindow(
                row.Name, ServerMonitorViewModel.CanDropDatabaseOnServer(row), row.Dbms, row.DbServer)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true)
                return;
            await _vm.DropSelectedInfobaseAsync(dialog.DropDatabase);
        }

        /// <summary>
        /// «Импорт» (issue #27): открывает «Импорт из кластера 1С» с текущим сервером —
        /// выбранный в списке, иначе подключённый, иначе первый из справочника. При
        /// пустом справочнике — подсказка и выход. Сам импорт выполняет делегат
        /// MainViewModel (логика добавления баз/групп — там, окно к репозиторию не ходит).
        /// </summary>
        private void OnImport_Click(object sender, RoutedEventArgs e)
        {
            var server = _vm.SelectedServer ?? _vm.ConnectedServer ?? _vm.Servers.FirstOrDefault();
            if (server is null)
            {
                MessageBox.Show(
                    LocalizationManager.T("ClusterImport.ServersEmpty"),
                    LocalizationManager.T("ServerMonitor.Title"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _openClusterImport?.Invoke(server, _vm.SelectedClusterId);
        }

        /// <summary>
        /// «Миграция» (issue #41): открывает «Миграцию между кластерами 1С» с текущим
        /// сервером — выбранный в списке, иначе подключённый, иначе первый из справочника.
        /// При пустом справочнике — подсказка и выход. Сам перенос выполняет делегат
        /// MainViewModel (логика добавления баз/групп — там, окно к репозиторию не ходит).
        /// </summary>
        private void OnMigrate_Click(object sender, RoutedEventArgs e)
        {
            var server = _vm.SelectedServer ?? _vm.ConnectedServer ?? _vm.Servers.FirstOrDefault();
            if (server is null)
            {
                MessageBox.Show(
                    LocalizationManager.T("ClusterImport.ServersEmpty"),
                    LocalizationManager.T("ServerMonitor.Title"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _openClusterMigration?.Invoke(server, _vm.SelectedClusterId);
        }

        // ===================== Справочник «Сервера управления» (issue #3) =====================

        /// <summary>«Добавить»: модальный редактор новой записи → добавление в справочник.</summary>
        private void OnAddServer_Click(object sender, RoutedEventArgs e)
        {
            var edit = new ServerEditWindow() { Owner = this };
            if (edit.ShowDialog() != true || edit.Result is not { } created)
                return;
            _vm.AddServer(created);
        }

        /// <summary>«Изменить»: модальный редактор выбранной записи (правка на копии).</summary>
        private void OnEditServer_Click(object sender, RoutedEventArgs e)
        {
            if (ServersList.SelectedItem is not ManagementServer server)
                return;
            var edit = new ServerEditWindow(CloneServer(server)) { Owner = this };
            if (edit.ShowDialog() != true || edit.Result is not { } updated)
                return; // Отмена — модель не изменялась.
            _vm.UpdateServer(updated);
        }

        /// <summary>«Удалить»: подтверждение → удаление записи из справочника.</summary>
        private void OnDeleteServer_Click(object sender, RoutedEventArgs e)
        {
            if (ServersList.SelectedItem is not ManagementServer server)
                return;
            if (!AppServices.GetRequiredService<IDialogService>().Confirm(
                    string.Format(LocalizationManager.T("ServerMonitor.Servers.ConfirmDelete"), server.Description),
                    LocalizationManager.T("ServerMonitor.Servers.Title")))
                return;
            _vm.RemoveServer(server);
        }

        /// <summary>«Вверх»: переместить выбранный сервер на позицию выше (issue #3).</summary>
        private void OnMoveServerUp_Click(object sender, RoutedEventArgs e) => _vm.MoveServerUp();

        /// <summary>«Вниз»: переместить выбранный сервер на позицию ниже (issue #3).</summary>
        private void OnMoveServerDown_Click(object sender, RoutedEventArgs e) => _vm.MoveServerDown();

        /// <summary>Глубокая копия записи (для правки без мутации исходного экземпляра).</summary>
        private static ManagementServer CloneServer(ManagementServer source) => new()
        {
            Id = source.Id,
            Description = source.Description,
            Address = source.Address,
            Port = source.Port,
            User = source.User,
            Password = source.Password,
        };

        // ===================== Кнопка «СЛУЖБЫ» (issue #3, 18.10 08:57) =====================
        // Кнопка внизу левой колонки справочника серверов показывает подменю служб —
        // то же, что в трее по Ctrl+ПКМ. Данные строит переиспользуемый
        // ServiceTrayMenuBuilder, результат адаптируется к WPF ContextMenu.

        /// <summary>«СЛУЖБЫ»: показывает подменю служб у кнопки (переиспользование построителя трей-меню).</summary>
        private void OnServicesMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;
            var menu = new ContextMenu
            {
                PlacementTarget = button,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                // Скин (issue #3, часть 2): тот же стиль, что у меню служб главного окна.
                Style = TryFindResource("ModernContextMenu") as Style,
            };
            foreach (var item in BuildServicesMenuItems())
                menu.Items.Add(ToWpfServiceMenuItem(item));
            menu.IsOpen = true;
        }

        /// <summary>
        /// Пункты подменю «Службы» по данным services.json и статусам из кэша
        /// (issue #3, часть 2): построение не блокирует UI — статусы читаются
        /// из <see cref="ServiceStatusCache"/> и обновляются фоном (как в трее).
        /// </summary>
        private List<ServiceMenuItem> BuildServicesMenuItems()
        {
            var services = AppServices.TryGetService<IServiceControllerService>();
            var store = AppServices.TryGetService<IServicesStore>();
            var data = store?.Load() ?? new ServiceStoreData();
            var builder = new ServiceTrayMenuBuilder();
            if (services is null)
            {
                // Контроллер недоступен — команды неактивны; остаются «Настроить…»
                // и «Все сервисы» с пустым набором.
                return builder.Build(data, _ => new ServiceInfo { Status = ServiceStatus.Unknown }, ServicePlatform.Windows);
            }
            // Мгновенный показ: статусы из кэша (фоновый таймер держит их свежими).
            EnsureServicesStatusTimer();
            return builder.Build(data,
                name => ServicesStatusCache()?.GetStatus(name) ?? new ServiceInfo { ServiceName = name, Status = ServiceStatus.Unknown },
                ServicePlatform.Windows);
        }

        /// <summary>Единый кэш статусов из DI (issue #3, часть 2): тот же экземпляр,
        /// что у трея и главного окна.</summary>
        private Services.ServiceStatusCache? ServicesStatusCache()
            => AppServices.TryGetService<Services.ServiceStatusCache>();

        /// <summary>Запускает фоновый таймер обновления кэша статусов (5 с).
        /// Опросы — в рабочих потоках, UI не блокируется.</summary>
        private void EnsureServicesStatusTimer()
        {
            if (_servicesStatusTimer is not null)
                return;
            _servicesStatusTimer = new System.Threading.Timer(
                _ => RefreshServicesStatusesInBackground(),
                null,
                TimeSpan.Zero, // первый опрос сразу — прогрев кэша
                ServicesStatusRefreshInterval);
        }

        /// <summary>Фоновое обновление кэша статусов по списку из services.json.</summary>
        private void RefreshServicesStatusesInBackground()
        {
            var cache = ServicesStatusCache();
            var store = AppServices.TryGetService<IServicesStore>();
            if (cache is null || store is null)
                return;
            var data = store.Load();
            var names = ServiceStoreData.NormalizeOrder(data)
                .Where(k => !ServiceStoreData.IsGroupKey(k))
                .Concat(data.Groups.SelectMany(g => g.Services))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (names.Count == 0)
                return;
            _ = cache.RefreshAsync(names);
        }

        /// <summary>
        /// Адаптер пункта модели меню служб к WPF-элементу ContextMenu: те же иконки,
        /// что в контекстном меню основного окна — кружок статуса для службы, папка
        /// для группы, шестерёнка у «Настроить…», цветные глифы команд (1.0.0.14).
        /// Стиль скина ModernMenuItem — как у остальных контекстных меню (issue #3, часть 2).
        /// </summary>
        private object ToWpfServiceMenuItem(ServiceMenuItem item)
        {
            switch (item.Kind)
            {
                case ServiceMenuItemKind.Separator:
                    return new Separator();

                case ServiceMenuItemKind.Settings:
                {
                    // Экранирование '_' (WPF AccessKey; issue #11, 7OH 19.10).
                    var settings = new MenuItem
                    {
                        Header = ServiceTrayMenuBuilder.EscapeMenuText(item.Text),
                        Style = TryFindResource("ModernMenuItem") as Style,
                    };
                    settings.Icon = MenuIconText("⚙", "#6B7280");
                    settings.Click += (_, _) => ShowServicesSettings();
                    return settings;
                }

                case ServiceMenuItemKind.ServiceSubmenu:
                {
                    var submenu = new MenuItem
                    {
                        Header = ServiceTrayMenuBuilder.EscapeMenuText(item.Text),
                        IsEnabled = item.IsEnabled,
                        Style = TryFindResource("ModernMenuItem") as Style,
                    };
                    if (!string.IsNullOrWhiteSpace(item.StatusColorHex))
                        submenu.Icon = MenuStatusCircle(item.StatusColorHex);
                    foreach (var child in item.Children)
                        submenu.Items.Add(ToWpfServiceMenuItem(child));
                    return submenu;
                }

                case ServiceMenuItemKind.GroupSubmenu:
                case ServiceMenuItemKind.AllServicesSubmenu:
                {
                    var submenu = new MenuItem
                    {
                        Header = ServiceTrayMenuBuilder.EscapeMenuText(item.Text),
                        IsEnabled = item.IsEnabled,
                        Style = TryFindResource("ModernMenuItem") as Style,
                    };
                    // Группа — «микс» папка + кружок статуса (замечание 7OH 19.10),
                    // как в трее и в контекстном меню служб главного окна; «Все сервисы» — ☰.
                    submenu.Icon = item.Kind == ServiceMenuItemKind.GroupSubmenu
                        ? MenuGroupIcon(item.StatusColorHex)
                        : MenuIconText("☰", "#7C3AED");
                    foreach (var child in item.Children)
                        submenu.Items.Add(ToWpfServiceMenuItem(child));
                    return submenu;
                }

                case ServiceMenuItemKind.ServiceCommand:
                case ServiceMenuItemKind.GroupCommand:
                case ServiceMenuItemKind.AllServicesCommand:
                {
                    var command = new MenuItem
                    {
                        Header = ServiceTrayMenuBuilder.EscapeMenuText(item.Text),
                        IsEnabled = item.IsEnabled,
                        Style = TryFindResource("ModernMenuItem") as Style,
                    };
                    var action = item.Action ?? ServiceAction.Start;
                    command.Icon = MenuIconText(ServiceCommandGlyphs.Glyph(action), ServiceCommandGlyphs.ColorHex(action));
                    var target = item.Target;
                    var targetName = item.TargetName;
                    var names = item.ServiceNames;
                    command.Click += (_, _) => ExecuteServicesOperation(action, target, targetName, names);
                    return command;
                }

                default:
                    return new MenuItem
                    {
                        Header = ServiceTrayMenuBuilder.EscapeMenuText(item.Text),
                        IsEnabled = item.IsEnabled,
                        Style = TryFindResource("ModernMenuItem") as Style,
                    };
            }
        }

        /// <summary>Цветная иконка пункта меню (глиф-текст, как в контекстном меню основного окна).</summary>
        private static TextBlock MenuIconText(string text, string colorHex) => new()
        {
            Text = text,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        /// <summary>Кружок статуса службы в пункте меню (цвет StatusColorHex).</summary>
        private static Border MenuStatusCircle(string colorHex) => new()
        {
            Width = 9,
            Height = 9,
            CornerRadius = new CornerRadius(4.5),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)),
            VerticalAlignment = VerticalAlignment.Center
        };

        /// <summary>
        /// Иконка группы служб в пункте меню — «микс» (замечание 7OH 19.10): папка 📦 +
        /// кружок статуса группы справа-снизу (цвет StatusColorHex — зелёный/жёлтый/серый),
        /// как в трее и в контекстном меню служб главного окна. При отсутствии статуса
        /// (кэш пуст, локальная машина) — обычная папка без кружка.
        /// </summary>
        private static FrameworkElement MenuGroupIcon(string statusColorHex)
        {
            var grid = new Grid { Width = 16, Height = 16 };
            grid.Children.Add(new TextBlock
            {
                Text = "📦",
                FontSize = 13,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C3AED")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            if (!string.IsNullOrWhiteSpace(statusColorHex))
            {
                grid.Children.Add(new Border
                {
                    Width = 6,
                    Height = 6,
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(statusColorHex)),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 1, 1),
                    BorderBrush = new SolidColorBrush(Colors.White),
                    BorderThickness = new Thickness(1)
                });
            }
            return grid;
        }

        /// <summary>«Настроить…»: окно «Управление службами» (модально к монитору).</summary>
        private void ShowServicesSettings()
        {
            var window = new ServicesWindow { Owner = this };
            window.ShowDialog();
        }

        /// <summary>
        /// Выполняет операцию над целевыми службами (отдельная служба / члены группы /
        /// все службы списка): ServiceController блокирующий — работа в фоновом потоке.
        /// После завершения кэш статусов сбрасывается и опрашивается заново (как в трее).
        /// </summary>
        private void ExecuteServicesOperation(
            ServiceAction action,
            ServiceMenuItemTarget target,
            string targetName,
            IReadOnlyList<string> serviceNames)
        {
            var services = AppServices.TryGetService<IServiceControllerService>();
            if (services is null)
                return;

            var names = target switch
            {
                ServiceMenuItemTarget.Group or ServiceMenuItemTarget.All => serviceNames.ToList(),
                _ => new List<string> { targetName }
            };
            if (names.Count == 0)
                return;

            Task.Run(() =>
            {
                foreach (var name in names)
                {
                    try
                    {
                        // Статусы актуализируем на момент операции: доступность могла измениться.
                        var info = services.GetStatus(name);
                        if (!ServiceActionAvailability.CanExecute(
                                action, info.Status, info.CanPauseAndContinue, info.CanStop, ServicePlatform.Windows))
                            continue;
                        switch (action)
                        {
                            case ServiceAction.Start: services.Start(name); break;
                            case ServiceAction.Stop: services.Stop(name); break;
                            case ServiceAction.Pause: services.Pause(name); break;
                            case ServiceAction.Continue: services.Continue(name); break;
                            case ServiceAction.Restart: services.Restart(name); break;
                        }
                    }
                    catch
                    {
                        // Отдельная служба не должна останавливать остальные.
                    }
                }
            }).ContinueWith(_ =>
            {
                // Статусы изменились — сбрасываем кэш и опрашиваем заново в фоне.
                AppServices.TryGetService<ServiceStatusCache>()?.Invalidate();
                RefreshServicesStatusesInBackground();
            }, TaskScheduler.Default);
        }
    }
}
