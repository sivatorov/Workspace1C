#if WINDOWS
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.IO;
using MaterialDesignThemes.Wpf;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace Workspace1C
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private Point _dragStartPoint;
        private object? _draggedData;
        private bool _isDragging;
        private Forms.NotifyIcon? _trayIcon;
        private bool _forceClose;

        // ---- Диагностика переключения вкладок списка (issue #21, Этап E; флаг CM_TREESWITCH) ----
        // Счётчики накапливаются, а при каждой смене вкладки в лог выводится разница
        // с прошлой сменой: число SizeChanged-событий дерева и итераций стабилизации
        // выравнивания — контроль каскада пересчётов (CM_COLUMNS) и роста задержки.
        private long _treeSizeChangedCount;
        private long _lastTreeSizeChangedCount;
        private long _headerAlignStabilizeTotal;
        private long _lastHeaderAlignStabilizeTotal;
        private long _switchVmStartTicks;
        private long _lastSwitchVmMs;
        // Счётчики событий коллекций дерева (FlatItems/GroupNodes) за смену вкладки
        // (issue #21, 1.0.0.33): «шквал» поштучных Remove/Insert при переходе
        // «Все»↔«Недавние» виден в CM_TREESWITCH как collAdd/collRemove.
        private long _treeCollAddCount;
        private long _lastTreeCollAddCount;
        private long _treeCollRemoveCount;
        private long _lastTreeCollRemoveCount;
        private long _treeCollResetCount;
        private long _lastTreeCollResetCount;
        // Счётчики реализованных/отсоединённых строк дерева за смену вкладки
        // (диагностика v2, issue #21, CM_TREESWITCH): у WPF нет аналога Avalonia-событий
        // ContainerPrepared/ContainerClearing, поэтому считаем Loaded/Unloaded сеток строк
        // в шаблонах (MainWindow.xaml: GroupRowGrid/InfobaseRowGrid). rowsRealized —
        // сколько раз строка (база или группа) подключена к визуальному дереву за смену;
        // rowsCleared — сколько раз отсоединена. Устойчивый разрыв realized >> cleared
        // между сменами указывает на удержание контейнеров (Recycling-пул / подписки).
        private long _treeRowRealizeCount;
        private long _lastTreeRowRealizeCount;
        private long _treeRowUnrealizeCount;
        private long _lastTreeRowUnrealizeCount;
        // Базовый счётчик байт выделения на старте смены вкладки (TreeRebuilding) — из
        // него в CM_TREESWITCH вычисляется allocMb (прирост GC.GetTotalAllocatedBytes
        // за смену): мусор vs живой рост. 0 — старт не зафиксирован (первая запись).
        private long _switchAllocBaseBytes;
        private long _lastSwitchAllocMb;
        // Сброс Recycling-пулов (issue #21, 1.0.0.52): панели дерева переведены на
        // VirtualizationMode.Standard (MainWindow.xaml) — ItemContainerGenerator НЕ
        // рециклирует контейнеры строк, а удаляет их по выходу из видимой области,
        // поэтому пул и связанный с ним рост heapAfterGcMb не накапливаются вовсе.
        // Прежние механизмы динамического переключения VirtualizationMode были
        // неустойчивы: после Measure панели WPF запрещает его менять (InvalidOperationException
        // в логе CM_TREESWITCH_FLUSH 1.0.0.50), а отложенные прогоны (ApplicationIdle
        // 1.0.0.41–1.0.0.44, Background 1.0.0.46) голодали при быстрой серии переключений
        // (flushPanels=0 всю серию, ~4 МБ/смену).
        // Диагностика CM_TREESWITCH: flushPanels/flushMs сохранены для обратной
        // совместимости и всегда 0 — панели сбрасывать не нужно (пула нет).
        private int _lastTreePoolFlushPanels;
        private long _lastTreePoolFlushMs;
        // База счётчика GroupNodeViewModel.TotalDetachCount (диагностика CM_TREESWITCH:
        // detachCount — сколько узлов отписалось за смену вкладки).
        private long _lastDetachCount;

        /// <summary>
        /// Текущее состояние активности окна, по которому окрашивается шапка
        /// (акцент при активном, цвет карточки при неактивном). Хранится отдельным
        /// флагом, чтобы не зависеть от временного значения <see cref="Window.IsActive"/>
        /// в момент перекраски (запуск, восстановление из трея, смена темы).
        /// </summary>
        private bool _isActive;

        /// <summary>Информационная версия сборки для заголовка окна (например «0.3.3.41»).</summary>
        private readonly string? _infoVersion;

        /// <summary>
        /// Состояние нижней кнопки тегов (<see cref="MainViewModel.ShowTags"/>), запомненное
        /// в момент выключения верхней кнопки «теги», чтобы восстановить его при повторном
        /// включении (вместо принудительного включения нижней кнопки).
        /// </summary>
        private bool? _savedTagsStateBeforeTopOff;

        /// <summary>
        /// Grace-таймер деактивации (issue #340, F3, план 0.3.9.306): запускается при
        /// <see cref="Window.Deactivated"/> на ~300 мс; если окно снова стало активным
        /// (<see cref="Window.Activated"/>) — таймер отменяется и состояние menu-close
        /// НЕ сбрасывается. Сброс выполняется только если окно реально осталось
        /// неактивным и контекстные меню закрыты.
        /// </summary>
        private System.Windows.Threading.DispatcherTimer? _menuCloseGraceTimer;

        /// <summary>Grace-период деактивации, мс (issue #340, F3, план 0.3.9.306).</summary>
        private const int MenuCloseGraceMs = 300;

        /// <summary>
        /// Запускает (перезапускает) grace-таймер деактивации (issue #340, F3):
        /// сброс состояния menu-close откладывается, чтобы кратковременная деактивация
        /// окна открытием/закрытием попапа контекстного меню не убивала fallback
        /// до повторной доставки клика.
        /// </summary>
        private void StartMenuCloseGraceTimer()
        {
            if (_menuCloseGraceTimer is null)
            {
                _menuCloseGraceTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(MenuCloseGraceMs)
                };
                _menuCloseGraceTimer.Tick += OnMenuCloseGraceTimerTick;
            }
            _menuCloseGraceTimer.Stop();
            _menuCloseGraceTimer.Start();
        }

        /// <summary>Отменяет grace-таймер деактивации (окно снова активно — состояние НЕ сбрасываем).</summary>
        private void StopMenuCloseGraceTimer()
        {
            _menuCloseGraceTimer?.Stop();
        }

        /// <summary>
        /// Время в мс с последнего закрытия контекстного меню ДЕРЕВА (единые часы
        /// <see cref="Environment.TickCount"/>; -1 — меню дерева в этой сессии не закрывалось).
        /// Используется в записях <c>Activated</c>/<c>Deactivated</c> (issue #340, 0.3.9.311, B-3):
        /// деактивация окна закрытием попапа меню происходит обычно в пределах окна стабилизации.
        /// </summary>
        private long TimeSinceLastMenuCloseMs()
            => _lastMenuCloseTick > 0 ? Environment.TickCount - _lastMenuCloseTick : -1;

        /// <summary>
        /// Срабатывание grace-таймера деактивации (issue #340, F3): сброс снимка клика
        /// и флага pending-применения выполняется ТОЛЬКО если окно действительно осталось
        /// неактивным и все контекстные меню закрыты. Если окно активировалось — состояние
        /// сохраняется (Activated уже отменил таймер); если меню ещё открыты — сброс
        /// пропускается (повторная доставка клика / закрытие меню ещё впереди).
        /// </summary>
        private void OnMenuCloseGraceTimerTick(object? sender, EventArgs e)
        {
            _menuCloseGraceTimer?.Stop();
            if (IsActive)
                return;
            if (_openContextMenus.Count > 0)
                return;
            if (_menuClosePendingApply || _menuCloseClickSnapshot is not null)
                MenuCloseTrace.Log($"Deactivated: grace-сброс, openMenus={_openContextMenus.Count}, isActive={IsActive}");
            _menuCloseClickSnapshot = null;
            _menuClosePendingApply = false;
            _menuCloseTarget = null;
            _menuCloseTargetIsPinnedSection = false;
        }

        public MainWindow(ViewModels.MainViewModel? viewModel = null)
        {
            InitializeComponent();

            // Глифы кнопок управления окном резолвятся по активному набору иконок (Задача 2):
            // стартовый рендер берёт Fluent/XAML-контуры, а активный набор применяется здесь
            // и при каждой смене полноценной темы (см. OnThemeChanged в MainWindow.UtilitiesMenu.cs).
            UpdateWindowControlGlyphs();

            // Подвал строки состояния: версия приложения и ссылки обновления (issue #116).
            InitializeFooterUpdateState();

            // issue #340 (0.3.9.308): файл диагностики trace.json (или legacy
            // menuclose_trace.json при его наличии) создаётся при КАЖДОМ старте — startup-
            // запись пишется вне зависимости от действий пользователя (в 0.3.9.306 файл
            // не появлялся, т.к. startup-запись выполнялась только внутри MenuCloseTrace.Log,
            // а все вызовы Log были условными). Пользователь сразу видит файл рядом с
            // настройками и может убедиться, что диагностика активна.
            MenuCloseTrace.EnsureStarted();

            // Шапка главного окна реагирует на активность: при активном окне заливается
            // акцентным цветом темы, при неактивном — цветом карточки (см. UpdateTitleBarAppearance).
            Activated += (_, _) =>
            {
                _isActive = true;
                // issue #340 (F3): окно снова активно — grace-таймер отменяется,
                // состояние menu-close НЕ сбрасывается: повторная доставка клика,
                // которым закрыли меню, ещё может прийти (попап освободил захват).
                StopMenuCloseGraceTimer();
                UpdateTitleBarAppearance(true);
                // B-3 (0.3.9.311): запись активации окна БЕЗУСЛОВНА (прежде — только при
                // pending-состоянии/снимке): число открытых меню и время с последнего
                // закрытия меню дерева — для проверки гипотезы S4 (деактивация окна
                // закрытием попапа меню и сброс состояния до повторной доставки клика).
                MenuCloseTrace.Log($"Activated: openMenusCount={_openContextMenus.Count}, " +
                                   $"timeSinceMenuCloseMs={TimeSinceLastMenuCloseMs()}");
            };
            Deactivated += (_, _) =>
            {
                _isActive = false;
                // issue #340 (F3): окно потеряло активность. Открытие/закрытие попапа
                // контекстного меню может кратковременно деактивировать окно; НЕМЕДЛЕННЫЙ
                // сброс снимка и флага pending-применения отменял бы fallback, а повторная
                // доставка клика трактовалась бы как новый клик (гипотеза S4). Прежний
                // вариант откладывал сброс на DispatcherPriority.Background при
                // _openContextMenus.Count == 0 — но список меню мог опустеть ДО повторной
                // доставки клика, и сброс убивал fallback. Вместо этого — grace-период
                // ~300 мс с момента Deactivated: если окно снова активировалось (Activated),
                // таймер отменяется и состояние НЕ сбрасывается; сброс — только если окно
                // реально осталось неактивным и меню закрыты (см. OnMenuCloseGraceTimerTick).
                StartMenuCloseGraceTimer();
                UpdateTitleBarAppearance(false);
                // B-3 (0.3.9.311): запись деактивации окна БЕЗУСЛОВНА — см. Activated выше.
                MenuCloseTrace.Log($"Deactivated: openMenusCount={_openContextMenus.Count}, " +
                                   $"timeSinceMenuCloseMs={TimeSinceLastMenuCloseMs()}");
                // Подсказки скрываются при потере фокуса окна (issue #275), как контекстное
                // меню: при клике в другое окно/приложение открытый тултип исчезает,
                // а не «висит» поверх браузера. Общий механизм ToolTipCloser дополнительно
                // вешает Application.Deactivated — покрытие всех окон приложения.
                ToolTipCloser.CloseAll();
            };

            // «Стеклянный» полупрозрачный фон окна: подложка берётся из текущего цвета
            // темы (светлая/тёмная и любые схемы) и пересчитывается при смене темы.
            // ThemeManager.ApplyScheme заменяет словарь темы в Application.Resources,
            // поэтому слушаем коллекцию MergedDictionaries и обновляем подложку заново.
            if (Application.Current?.Resources is { } resources)
            {
                ((System.Collections.Specialized.INotifyCollectionChanged)resources.MergedDictionaries)
                    .CollectionChanged += (_, _) =>
                    {
                        try
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                ApplyGlassBackground();
                                // Акцентная шапка/цвет карточки зависят от темы — перекрашиваем.
                                // Берём фактическое состояние активности окна, а не кэш _isActive:
                                // после смены схемы/темы (возможно с открытым диалогом) кэш мог
                                // устареть, и акцент активного окна терялся бы (issue #135).
                                _isActive = IsActive;
                                UpdateTitleBarAppearance(_isActive);
                            }));
                        }
                        catch { /* не блокируем смену темы */ }
                    };
            }

            // Выводим версию программы в заголовок окна (информационная версия,
            // чтобы показать точное значение «0.3.3.41»).
            // Из InformationalVersion отбрасываем возможный суффикс «+<sha>».
            _infoVersion = VersionInfo.Display();
            // Заголовок собираем через общий метод: это защищает от повторного
            // добавления суффикса версии при повторном применении XAML-привязки.
            UpdateWindowTitle();

            // Смена языка интерфейса: заголовок окна, подсказки и меню трея, которые
            // задаются в code-behind, обновляются вручную (LocExtension-привязки XAML
            // обновляются сами через LocalizationManager.Source.NotifyAll()).
            LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;

            // Меню «Утилиты» (Задача 6): при смене полноценной темы пересобираем
            // контекстное меню кнопки, чтобы цвета/иконки обновились на лету.
            // Меню всё равно пересоздаётся при каждом открытии, подписка гарантирует
            // мгновенное обновление уже построенного меню; отписка — при закрытии окна.
            Themes.ThemeManager.ThemeChanged += OnThemeChanged;
            Closed += (_, _) => Themes.ThemeManager.ThemeChanged -= OnThemeChanged;

            _viewModel = viewModel ?? new ViewModels.MainViewModel();
            DataContext = _viewModel;

            // Контекстное меню строки базы (issue #56): строится кодом (см.
            // MainWindow.ContextMenu.cs) вместо статического XAML-блока и пересобирается
            // при смене темы/языка/настроек (см. RebuildRowContextMenu).
            MainTree.ContextMenu = BuildRowContextMenu();

            // Заголовок с «дополнением» (issue #28/#34): первый вызов UpdateWindowTitle
            // выше выполнялся до присваивания _viewModel и собирал заголовок без суффикса.
            // Пересобираем с VM — и системный Title, и видимая шапка содержат дополнение
            // сразу при открытии окна (в т.ч. если окно создаётся не через App.OnStartup).
            UpdateWindowTitle();

            // Отслеживание открытых ToolTip и перехват ESC перенесены в общий механизм
            // ToolTipCloser (issues #270/#275): он отслеживает сами инстансы ToolTip (в т.ч.
            // внутренние для строковых подсказок ToolTip="..."), закрывает их по первому ESC
            // глобально (класс-обработчик UIElement.PreviewKeyDownEvent) и при потере фокуса
            // приложением (Application.Deactivated). Регистрация идемпотентна.
            ToolTipCloser.Register();

            // Отслеживаем открытые контекстные меню главного окна (issue #261). Те два элемента,
            // которые «не закрываются по ESC» (выпадающие меню запуска/выбора клиента
            // EnterpriseMenuButton/ConfiguratorMenuButton, LaunchWithParams, меню «Утилиты»,
            // контекстные меню строк и заголовков колонок), являются ContextMenu, а не стандартным
            // ToolTip. Класс-обработчики Opened/Closed записывают открытые меню независимо от того,
            // как они показаны — через присоединённый ContextMenu на элементе или программно
            // (IsOpen = true / ShowBookmarksMenu). Первый ESC закрывает открытое меню, а не
            // сворачивает окно в трей; второй ESC уже уводит окно в трей (инвариант #261).
            EventManager.RegisterClassHandler(
                typeof(ContextMenu),
                ContextMenu.OpenedEvent,
                new RoutedEventHandler(OnContextMenuOpened));
            EventManager.RegisterClassHandler(
                typeof(ContextMenu),
                ContextMenu.ClosedEvent,
                new RoutedEventHandler(OnContextMenuClosed));

            // Класс-обработчик Preview (туннелирование) на тип ContextMenu (issue #261): ESC,
            // приходящийся на открытое меню (фокус во внешнем попапе меню, куда
            // Window_PreviewKeyDown главного окна не доходит), закрывает само меню и помечает
            // событие обработанным, чтобы тот же ESC не увёл окно в трей.
            EventManager.RegisterClassHandler(
                typeof(ContextMenu),
                UIElement.PreviewKeyDownEvent,
                new KeyEventHandler(OnContextMenuPreviewKeyDown));

            // Действие «после запуска базы/конфигуратора» согласно глобальной настройке.
            _viewModel.AfterLaunchRequested += OnAfterLaunchRequested;

            // Блокировка приложения (issue #294): при включении блокировки окно
            // сворачивается в трей, чтобы не мешать работать; если окно ввода пароля
            // закрыто без верного пароля — окно сворачивается снова.
            _viewModel.AppLockEngaged += OnViewModelAppLockEngaged;
            _viewModel.AppLockPromptDismissed += OnViewModelAppLockPromptDismissed;

            // Перед пересборкой дерева запоминаем позицию прокрутки списка, чтобы после
            // пересборки (например, сохранения свойств базы) список не «прыгал» в сторону
            // (issue #252). После пересборки возвращаем клавиатурный фокус на выбранную
            // строку — прежний контейнер уничтожен — и пересчитываем выравнивание колонок
            // заголовка с данными: контейнеры строк созданы заново (и заново
            // компактизированы обработчиками реализации), поэтому прежний сдвиг заголовка
            // устарел (issue #214).
            _viewModel.TreeRebuilding += () =>
            {
                RememberTreeScroll();
                // Откладываем запись MinWidth/MaxWidth контента (issue #21): во время
                // перестроения дерева строки ещё не материализованы, и effective по
                // промежуточным headerSumNow/viewportNow «залипал» бы (горизонтальная
                // полоса при переключении на «Недавние»). Финальный проход
                // TreeMinWidthFinalizeStep фиксирует значение по стабильному кадру.
                _treeMinWidthDeferred = true;
                // Замер VM-части чистого переключения вкладки (issue #21, Этап E):
                // флаг IsListModeSwitchInProgress истинен на время RebuildGroupTreeCore.
                if (_viewModel.IsListModeSwitchInProgress)
                {
                    _switchVmStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                    // База выделения на старте смены (диагностика v2, issue #21): из
                    // прироста GC.GetTotalAllocatedBytes(false) к TreeRebuilt получаем
                    // allocMb за смену вкладки.
                    _switchAllocBaseBytes = System.GC.GetTotalAllocatedBytes(precise: false);
                }
            };
            // Перед открытием модального окна свойств базы запоминаем позицию прокрутки,
            // а при закрытии без сохранения («Нет») возвращаем её явно: пересборки не было,
            // иначе список «уезжает» после отмены правки (issue #252).
            _viewModel.TreeModalOpening += RememberTreeScroll;
            _viewModel.TreeModalClosed += RestoreTreeScrollAfterCancel;
            _viewModel.TreeRebuilt += () =>
            {
                // Целевая навигация «переход к строке» (issue #31): НЕ планируем промежуточное
                // восстановление выделения, пока идёт серия пересборок команды (Ctrl+T: смена
                // вкладки + сброс поиска/тегов + повторная пересборка; Ctrl+цифра: пересборка).
                // Иначе несколько отложенных RevealAndSelectAfterRebuild на приоритете Render
                // отработали бы по очереди: первый применил бы запрос (флаг сбрасывается),
                // а последующие выбрали бы первую копию базы в «Закреплённых» и вернули бы
                // старую позицию прокрутки — тот самый «переход в Закреплённые», о котором
                // сообщал пользователь. Единственный reveal планирует OnFindInListRequested
                // по окончании операции (см. MainWindow.Tree.cs).
                if (_viewModel.IsRevealNavigationInProgress)
                {
                    QueueHeaderAlign();
                    // Финальный проход сходимости ширины после пересборки (issue #21):
                    // строки материализуются позже, и минимум фиксируется по стабильному кадру.
                    QueueTreeMinWidthFinalize();
                    return;
                }

                // Чистое переключение вкладки (issue #21): цель выделения из прежнего режима
                // в новом, как правило, недостижима, а обязанное за ней восстановление
                // (RevealAndSelectAfterRebuild -> принудительный UpdateLayout + повторные
                // проходы до MaxRevealAttempts) давало «паузу» на каждое переключение.
                // Восстановление оставляем только для пересборок по изменению данных.
                // Исключение — «Найти в списке»/переход к закладке (issue #31): цель
                // выставлена явно, и строку нужно выделить и прокрутить даже при смене вкладки.
                if (!_viewModel.IsListModeSwitchInProgress || _revealFindInListPending)
                {
                    RestoreTreeKeyboardFocus();
                    QueueHeaderAlign();
                    // Финальный проход сходимости ширины после пересборки (issue #21):
                    // строки материализуются позже, и минимум фиксируется по стабильному кадру.
                    QueueTreeMinWidthFinalize();
                    return;
                }

                // Диагностика переключения вкладки (issue #21, Этап E; флаг CM_TREESWITCH):
                // число SizeChanged-событий дерева и итераций стабилизации выравнивания
                // с прошлой смены вкладки + время VM-части. Пишется ТОЛЬКО при включённом
                // флаге (как CM_COLUMNS, issue #347).
                if (TraceFlags.IsEnabled(TraceFlags.TreeSwitchFlag))
                {
                    var sizeChanged = _treeSizeChangedCount - _lastTreeSizeChangedCount;
                    var stabilize = _headerAlignStabilizeTotal - _lastHeaderAlignStabilizeTotal;
                    var collAdd = _treeCollAddCount - _lastTreeCollAddCount;
                    var collRemove = _treeCollRemoveCount - _lastTreeCollRemoveCount;
                    var collReset = _treeCollResetCount - _lastTreeCollResetCount;
                    // Режим замены коллекции за смену вкладки (эксперимент A, 1.0.0.34):
                    // reset — коллекции дерева заменялись одним Reset (малое пересечение),
                    // itemwise — поштучными событиями. По resetMode сопоставляются замеры
                    // 1.0.0.33 (всегда itemwise при пересечении) и 1.0.0.34.
                    var resetMode = collReset > 0 ? "reset" : "itemwise";
                    // Диагностика v2 (issue #21, итерация B): реализованные/отсоединённые
                    // строки за смену (WPF-аналог Avalonia-сквозных счётчиков контейнеров)
                    // и прирост выделения GC.GetTotalAllocatedBytes (allocMb) за смену.
                    var rowsRealized = _treeRowRealizeCount - _lastTreeRowRealizeCount;
                    var rowsCleared = _treeRowUnrealizeCount - _lastTreeRowUnrealizeCount;
                    var allocNow = System.GC.GetTotalAllocatedBytes(precise: false);
                    var allocMb = _switchAllocBaseBytes != 0
                        ? (allocNow - _switchAllocBaseBytes) / (1024 * 1024)
                        : 0;
                    // Сколько узлов дерева отписалось от коллекций за смену вкладки
                    // (issue #21, 1.0.0.41): детаchCount стабильно ~0 в плоских режимах
                    // (кэш «Все» и постоянный _flatNode переиспользуются), >0 — когда
                    // пересборка выбрасывает старые узлы (изменение данных/фильтров).
                    var detachCount = GroupNodeViewModel.TotalDetachCount - _lastDetachCount;
                    _lastTreeSizeChangedCount = _treeSizeChangedCount;
                    _lastHeaderAlignStabilizeTotal = _headerAlignStabilizeTotal;
                    _lastTreeCollAddCount = _treeCollAddCount;
                    _lastTreeCollRemoveCount = _treeCollRemoveCount;
                    _lastTreeCollResetCount = _treeCollResetCount;
                    _lastTreeRowRealizeCount = _treeRowRealizeCount;
                    _lastTreeRowUnrealizeCount = _treeRowUnrealizeCount;
                    _lastDetachCount = GroupNodeViewModel.TotalDetachCount;
                    _switchAllocBaseBytes = 0;
                    _lastSwitchAllocMb = allocMb;
                    if (_switchVmStartTicks != 0)
                    {
                        _lastSwitchVmMs = (System.Diagnostics.Stopwatch.GetTimestamp() - _switchVmStartTicks)
                            * 1000 / System.Diagnostics.Stopwatch.Frequency;
                        _switchVmStartTicks = 0;
                    }
                    // Замер кучи после смены вкладки (issue #21): рост с каждым переключением
                    // указывает на утечку в UI-слое (контейнеры/подписки), а не в фильтрации.
                    var heapBytes = System.GC.GetTotalMemory(false);
                    // Эксперимент «отложенное освобождение vs утечка» (issue #21, 1.0.0.33):
                    // принудительный GC поколения 2 и повторный замер. Если память вернулась —
                    // рост был следствием отложенного освобождения старых контейнеров дерева
                    // (Recycling-пул ItemContainerGenerator), а не реальной утечки подписок.
                    System.GC.Collect(2, GCCollectionMode.Optimized, blocking: true);
                    var heapAfterGc = System.GC.GetTotalMemory(true);
                    AppServices.GetRequiredService<IAppLogger>().Info(
                        $"CM_TREESWITCH: sizeChanged={sizeChanged}, stabilize={stabilize}, vmMs={_lastSwitchVmMs}, " +
                        $"allocMb={_lastSwitchAllocMb}, rowsRealized={rowsRealized}, rowsCleared={rowsCleared}, " +
                        $"heapMb={heapBytes / (1024 * 1024)}, heapAfterGcMb={heapAfterGc / (1024 * 1024)}, " +
                        $"collAdd={collAdd}, collRemove={collRemove}, collReset={collReset}, " +
                        $"resetMode={resetMode}, detach={detachCount}, flushPanels={_lastTreePoolFlushPanels}, " +
                        $"flushMs={_lastTreePoolFlushMs}, " +
                        $"gc0={System.GC.CollectionCount(0)}, gc1={System.GC.CollectionCount(1)}, " +
                        $"gc2={System.GC.CollectionCount(2)}, " +
                        // issue #21, 1.0.0.62: число живых записей метрик компактного режима —
                        // не должно расти от переключения вкладок (слабые ключи).
                        $"compact={_viewModel.CompactMode}, compactEntries={ThemeManager.CompactMetricEntries}, " +
                        $"rowsAlive={CountAliveProbeRows()}");
                }

                // Виртуализация создаёт новые контейнеры строк в проходе разметки ПОСЛЕ события
                // Loaded, поэтому выравнивание на Loaded-приоритете выполняется до их появления —
                // компактность снова «разъезжается» (issue #214). QueueHeaderAlign ставит
                // стабилизирующий цикл на ApplicationIdle, который добирает строки после полной
                // раскладки (тот же приём, что в RevealAndSelectAfterRebuild).
                // При чистом переключении вкладки — однопроходный режим (issue #21, Этап C):
                // без цикла «до сходимости» (до 8 итераций), финальное положение добирает
                // MainTree.SizeChanged после материализации строк.
                _headerAlignSinglePass = true;
                QueueHeaderAlign();
                // Финальный проход сходимости ширины после смены вкладки (issue #21):
                // строки материализуются виртуализацией в проходе разметки ПОСЛЕ этого
                // события, поэтому минимум фиксируется по стабильному кадру на
                // ApplicationIdle, а не по промежуточным headerSumNow/viewportNow.
                QueueTreeMinWidthFinalize();
                // Освобождение старых контейнеров (issue #21, 1.0.0.52): панели дерева
                // работают в VirtualizationMode.Standard (см. MainWindow.xaml) и не
                // используют Recycling-пул ItemContainerGenerator — контейнеры строк
                // прошлой вкладки удаляются WPF по выходу из видимой области, поэтому
                // heapAfterGcMb не растёт, а динамическое переключение режима (источник
                // InvalidOperationException в 1.0.0.50) больше не выполняется. Вызов лишь
                // обновляет диагностику CM_TREESWITCH (flushPanels/flushMs = 0).
                ScheduleTreePoolFlush();
            };

            // <summary>
            // Диагностический вызов после смены вкладки (issue #21, 1.0.0.52).
            // Панели дерева работают в VirtualizationMode.Standard (см. MainWindow.xaml)
            // и НЕ используют Recycling-пул ItemContainerGenerator: контейнеры строк
            // прошлой вкладки удаляются WPF по выходу из видимой области, поэтому
            // отдельный сброс пула не нужен, а динамическое переключение режима после
            // Measure панели (источник InvalidOperationException в 1.0.0.50) устранено.
            // Поля flushPanels/flushMs оставлены в CM_TREESWITCH для обратной
            // совместимости формата лога — всегда 0 (пул отсутствует).
            // </summary>
            void ScheduleTreePoolFlush()
            {
                if (MainTree is null)
                    return;
                _lastTreePoolFlushPanels = 0;
                _lastTreePoolFlushMs = 0;
            }
            // «Найти в списке» (issue #285): цель выставлена во вьюмодели, и при восстановлении
            // строки прокрутку нужно вести к цели, а не возвращать прежнюю позицию. Флаг читает
            // RevealAndSelectAfterRebuild (RevealFindInListRequested приходит синхронно из
            // ExecuteFindInList ПОСЛЕ планирования RevealAndSelectAfterRebuild на Render, поэтому
            // флаг успевает установиться до исполнения отложенного вызова).
            _viewModel.RevealFindInListRequested += OnFindInListRequested;

            // Пересчитываем выравнивание колонок заголовка после переключения компактного
            // режима: ApplyCompact масштабирует отступы/шрифты/компенсатор заголовка,
            // поэтому старое значение HeaderOffsetColumn становится неактуальным и данные
            // разъезжаются относительно заголовков. Пересчёт откладывается до Loaded-приоритета,
            // чтобы он выполнился уже после того, как ApplyCompact завершит изменения раскладки.
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            // Диагностика переключения вкладок (issue #21, Этап E): подсчёт событий
            // Add/Remove/Reset коллекций дерева за смену вкладки — контроль «шквала»
            // поштучных событий ReplaceItemWise в CM_TREESWITCH.
            // В WPF-версии MainViewModel единственной коллекцией дерева является
            // GroupNodes (плоские режимы реализованы через одиночный плоский узел),
            // поэтому подписка только на неё.
            _viewModel.GroupNodes.CollectionChanged += OnTreeCollectionChanged;

            // После завершения фоновой инициализации (дерево уже построено) восстанавливаем
            // последнее выделение и пересчитываем раскладку — раньше дерево ещё пустое.
            _viewModel.StartupInitializationCompleted += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        RestoreLastSelection();
                        // Настройки колонок модель загружает прямым присвоением полей БЕЗ
                        // PropertyChanged, поэтому событийный пересчёт на старте не сработает:
                        // единственная гарантия применения сохранённых видимостей/ширин к
                        // минимальной ширине списка — явный пересчёт после завершения фоновой
                        // инициализации (дерево построено, binding'ы колонок применены, issue #309).
                        // Строки могли уже материализоваться — обновляем и фактическую ширину
                        // контента (длинные названия), чтобы полоса дотягивала сразу после старта.
                        UpdateTreeMinWidthContent();
                        UpdateTreeMinWidth();
                        QueueHeaderAlign();
                        // Стартовый дамп диагностики колонок (issue #309): одна строка
                        // CM_COLUMNS после завершения компоновки — для сверки фактических
                        // значений; пишется только при включённом флаге CM_COLUMNS
                        // (issue #347: при явном false — безусловных веток нет).
                        LogColumnsDiagnostics(_treeMinWidthTotal);
                    }
                    catch { /* не блокируем запуск из-за восстановления выделения */ }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            };

            // Применяем сохранённую цветовую схему (тему оформления) при запуске.
            _viewModel.ApplyActiveColorSchemeToUi();

            UpdateThemeButton();

            // Компактный режим: синхронизируем состояние кнопки на верхней панели.
            if (CompactModeButton != null)
                CompactModeButton.IsChecked = _viewModel.CompactMode;

            // Применяем сохранённые ширины колонок списка баз.
            ApplySavedColumnWidths();

            // Применяем сохранённые размер, позицию и состояние окна.
            ApplySavedWindowLayout();

            // Трей и хоткеи — после загрузки окна (STA/иконка безопаснее на Loaded).
            Loaded += (_, _) =>
            {
                // Шапка сразу окрашивается по текущему состоянию активности окна.
                try
                {
                    UpdateTitleBarAppearance(_isActive);
                    InitializeTrayIcon();
                }
                catch
                {
                    // не блокируем запуск из‑за трея
                }

                // Хоткеи — в отдельных try/catch (issue #204): неверное сохранённое
                // сочетание (например, значение без модификатора, записанное до правки
                // поля ввода) не должно обрывать регистрацию остальных клавиш и
                // блокировать подписки на разворот/сворачивание узлов и восстановление
                // выделения.
                try { RegisterLaunchHotkeys(); } catch { /* ignore */ }
                try { RegisterFavoriteHotkeys(); } catch { /* ignore */ }

                // Глобальная горячая клавиша «показать главное окно» (issue #82):
                // регистрируется после создания дескриптора окна и перерегистрируется
                // при изменении настройки без перезапуска.
                try { RegisterGlobalShowWindowHotkey(); } catch { /* ignore */ }
                _viewModel.PropertyChanged += OnGlobalShowWindowHotkeyChanged;
                Closed += (_, _) =>
                {
                    _viewModel.PropertyChanged -= OnGlobalShowWindowHotkeyChanged;
                    UnregisterGlobalShowWindowHotkey();
                };

                // Блокировка приложения (issue #294): если приложение было заблокировано
                // в прошлом сеансе (закрыто из трея, выход), при запуске окно открывается
                // с оверлеем и окном ввода пароля. Блокировка снимается только верным
                // паролем; закрытие запроса уводит окно обратно в трей.
                try { _viewModel.RestoreAppLockOnStartup(); } catch { /* не блокируем запуск */ }

                // Раскрытие/сворачивание группы меняет глубину первой видимой базы,
                // из-за чего выравнивание заголовка с данными устаревает и колонки
                // «разъезжаются» отдельно от содержимого (issue #119). Пересчитываем
                // компенсатор сдвига заголовка при каждом изменении состояния узла.
                try
                {
                    MainTree.AddHandler(
                        TreeViewItem.ExpandedEvent,
                        new RoutedEventHandler(OnMainTree_GroupExpansionChanged));
                    MainTree.AddHandler(
                        TreeViewItem.CollapsedEvent,
                        new RoutedEventHandler(OnMainTree_GroupExpansionChanged));
                }
                catch
                {
                    // не блокируем запуск из‑за подписок
                }

                try { RestoreLastSelection(); }
                catch { /* не блокируем запуск из‑за восстановления выделения */ }
            };
            _viewModel.FavoriteHotkeysChanged += (_, _) =>
            {
                try { RegisterFavoriteHotkeys(); }
                catch { /* ignore */ }
            };
            _viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(MainViewModel.ShowTrayIcon))
                {
                    try { UpdateTrayVisibility(); } catch { /* ignore */ }
                    return;
                }

                // Индикатор выгрузки .dt/.cf: запускаем/останавливаем анимацию.
                if (e.PropertyName is nameof(MainViewModel.IsExporting))
                {
                    if (_viewModel.IsExporting) StartExportIndicatorAnimation();
                    else StopExportIndicatorAnimation();
                    return;
                }

                // При изменении видимости колонок/кнопок или их ширин пересчитываем
                // выравнивание заголовка с данными и минимальную ширину области списка,
                // чтобы колонки не разъезжались и горизонтальная полоса дотягивала до
                // последней (новой) колонки (issue #309).
                if (e.PropertyName is nameof(MainViewModel.ShowVersionColumn)
                    or nameof(MainViewModel.ShowLaunchModeColumn)
                    or nameof(MainViewModel.ShowServerColumn)
                    or nameof(MainViewModel.ShowLastLaunchColumn)
                    or nameof(MainViewModel.ShowSizeColumn)
                    or nameof(MainViewModel.ShowModifiedColumn)
                    or nameof(MainViewModel.ShowLastBackupColumn)
                    or nameof(MainViewModel.ShowConfigurationColumn)
                    or nameof(MainViewModel.ShowConfigurationVersionColumn)
                    or nameof(MainViewModel.ShowActionsColumn)
                    or nameof(MainViewModel.ShowFavoritesButton)
                    or nameof(MainViewModel.ShowPinnedButton)
                    or nameof(MainViewModel.NameColumnWidth)
                    or nameof(MainViewModel.VersionColumnWidth)
                    or nameof(MainViewModel.LaunchModeColumnWidth)
                    or nameof(MainViewModel.ServerColumnWidth)
                    or nameof(MainViewModel.LastLaunchColumnWidth)
                    or nameof(MainViewModel.SizeColumnWidth)
                    or nameof(MainViewModel.ModifiedColumnWidth)
                    or nameof(MainViewModel.LastBackupColumnWidth)
                    or nameof(MainViewModel.ConfigurationColumnWidth)
                    or nameof(MainViewModel.ConfigurationVersionColumnWidth)
                    or nameof(MainViewModel.ActionsColumnWidth))
                {
                    // Минимальная ширина области списка пересчитывается сразу, а не только
                    // через цепочку выравнивания заголовка (QueueHeaderAlign -> AlignHeaderToData):
                    // та требует наличия первой строки дерева и выполняется на ApplicationIdle,
                    // поэтому при пустом списке или до материализации строк горизонтальная
                    // полоса оставалась бы прежней ширины и не доезжала до последней колонки
                    // (issue #309). Прямой пересчёт дёшев: при неизменной сумме MinWidth
                    // ставится на то же значение и не инвалидирует раскладку. Вместе с
                    // суммой обновляется и фактическая ширина строк: новая ширина колонки
                    // меняет желаемую ширину строки при замере с бесконечным constraint.
                    UpdateTreeMinWidthContent();
                    UpdateTreeMinWidth();
                    QueueHeaderAlign();
                }

                // Issue #130: линии сетки зависят от состава видимых колонок —
                // перерисовываем их при скрытии/показе колонки и при переключении
                // самих опций «Выводить линии». Ширины колонок линии отслеживают
                // сами (прижаты к правому краю колонки), поэтому на каждое движение
                // разделителя перерисовки нет.
                if (e.PropertyName is nameof(MainViewModel.ShowHorizontalGridLines)
                    or nameof(MainViewModel.ShowVerticalGridLines)
                    or nameof(MainViewModel.ShowVersionColumn)
                    or nameof(MainViewModel.ShowLaunchModeColumn)
                    or nameof(MainViewModel.ShowServerColumn)
                    or nameof(MainViewModel.ShowLastLaunchColumn)
                    or nameof(MainViewModel.ShowSizeColumn)
                    or nameof(MainViewModel.ShowModifiedColumn)
                    or nameof(MainViewModel.ShowLastBackupColumn)
                    or nameof(MainViewModel.ShowConfigurationColumn)
                    or nameof(MainViewModel.ShowConfigurationVersionColumn)
                    or nameof(MainViewModel.ShowActionsColumn)
                    or nameof(MainViewModel.ShowFavoritesButton)
                    or nameof(MainViewModel.ShowPinnedButton))
                {
                    RefreshGridLines();
                }

                // Меню закладок (issue #356): сочетание — часть системных биндингов
                // закладок, перерегистрируется отдельно (RegisterFavoriteHotkeys).
                if (e.PropertyName is nameof(MainViewModel.HotkeyBookmarksMenu))
                {
                    try { RegisterFavoriteHotkeys(); } catch { /* ignore */ }
                    return;
                }

                if (e.PropertyName is nameof(MainViewModel.HotkeyEnterprise)
                    or nameof(MainViewModel.HotkeyConfigurator)
                    or nameof(MainViewModel.HotkeyFavorite)
                    or nameof(MainViewModel.HotkeyEdit)
                    or nameof(MainViewModel.HotkeyDelete)
                    or nameof(MainViewModel.HotkeyClearCache)
                    or nameof(MainViewModel.HotkeyAdd)
                    or nameof(MainViewModel.HotkeyPin)
                    or nameof(MainViewModel.HotkeyShowAll)
                    or nameof(MainViewModel.HotkeyShowFavorites)
                    or nameof(MainViewModel.HotkeyShowRecent)
                    or nameof(MainViewModel.HotkeyShowRunning)
                    or nameof(MainViewModel.HotkeyRightPanelDetails)
                    or nameof(MainViewModel.HotkeyFindInList)
                    or nameof(MainViewModel.HotkeySwitchUser)
                    // Меню служб (issue #11, пункт 6): настраиваемый хоткей.
                    or nameof(MainViewModel.HotkeyServicesMenu)
                    // «Собрать диагностику» (issue #99, v1.0.1.23): настраиваемый хоткей.
                    or nameof(MainViewModel.HotkeyCollectDiagnostics)
                    // Автообновление платформы 1С (функция 9, этап 0.3.9.214):
                    // сочетание применяется сразу после «ОК», а не после перезапуска
                    // (issue #38).
                    or nameof(MainViewModel.HotkeyPlatformUpdate))
                {
                    try { RegisterLaunchHotkeys(); } catch { /* ignore */ }
                    // RegisterLaunchHotkeys снимает и системные биндинги закладок (все
                    // сочетания, кроме Alt+…): восстанавливаем их сразу, иначе настроенное
                    // сочетание меню закладок (issue #356) пропадало до перезапуска.
                    try { RegisterFavoriteHotkeys(); } catch { /* ignore */ }
                }
            };
        }


        private DoubleAnimation? _exportBounceAnimation;
        private bool _exportAnimating;












        private enum TrayIconKind
        {
            Open, Database, Enterprise, Configurator, Sync, Settings, Exit, Favorite,
            Service, Group
        }



        private static readonly Dictionary<TrayIconKind, Drawing.Image> TrayIconCache = new();



        /// <summary>Активна ли тёмная тема приложения (для палитры меню трея, issue #107).
        /// Читается при каждом рендере, поэтому смена темы «на лету» сразу красит меню.</summary>
        internal static bool TrayMenuIsDark =>
            Workspace1C.Themes.ThemeManager.CurrentTheme == Workspace1C.Themes.ThemeManager.DarkThemeName;

        /// <summary>Современный рендерер меню трея (скругление, hover; палитра
        /// выбирается по активной теме — светлый/тёмный вариант, issue #107).</summary>
        private sealed class ModernTrayMenuRenderer : Forms.ToolStripProfessionalRenderer
        {
            public ModernTrayMenuRenderer() : base(new ModernTrayColorTable())
            {
                RoundedEdges = true;
            }

            protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected && !e.Item.Pressed)
                {
                    base.OnRenderMenuItemBackground(e);
                    return;
                }

                var rect = new Drawing.Rectangle(2, 0, e.Item.Width - 4, e.Item.Height);
                // Светлая тема: светло-голубой hover; тёмная (issue #107): slate-700.
                using var b = new Drawing.SolidBrush(TrayMenuIsDark
                    ? Drawing.Color.FromArgb(51, 65, 85)
                    : Drawing.Color.FromArgb(239, 246, 255));
                e.Graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var path = RoundedRect(rect, 6);
                e.Graphics.FillPath(b, path);
            }

            protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item is Forms.ToolStripLabel
                    ? (TrayMenuIsDark
                        ? Drawing.Color.FromArgb(148, 163, 184)   // slate-400 — заголовки блоков
                        : Drawing.Color.FromArgb(100, 116, 139))
                    : (TrayMenuIsDark
                        ? Drawing.Color.FromArgb(241, 245, 249)   // slate-100 — текст пунктов
                        : Drawing.Color.FromArgb(30, 41, 59));
                base.OnRenderItemText(e);
            }

            protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
            {
                var y = e.Item.Height / 2;
                using var pen = new Drawing.Pen(TrayMenuIsDark
                    ? Drawing.Color.FromArgb(51, 65, 85)
                    : Drawing.Color.FromArgb(226, 232, 240));
                e.Graphics.DrawLine(pen, 28, y, e.Item.Width - 8, y);
            }

            private static Drawing.Drawing2D.GraphicsPath RoundedRect(Drawing.Rectangle bounds, int radius)
            {
                var path = new Drawing.Drawing2D.GraphicsPath();
                int d = radius * 2;
                path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
                path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
                path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
                path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }
        }

        /// <summary>Цветовая таблица меню трея: палитра выбирается по активной теме
        /// приложения (светлая/тёмная — issue #107). Значения читаются при каждом
        /// рендере, поэтому смена темы «на лету» сразу перекрашивает меню.</summary>
        private sealed class ModernTrayColorTable : Forms.ProfessionalColorTable
        {
            // Тёмная палитра (issue #107): фон #1E293B (ContentBackground тёмной темы),
            // hover #334155, границы #334155, текст задаёт OnRenderItemText рендерера.
            public override Drawing.Color MenuBorder => TrayMenuIsDark
                ? Drawing.Color.FromArgb(51, 65, 85)
                : Drawing.Color.FromArgb(226, 232, 240);
            public override Drawing.Color MenuItemBorder => Drawing.Color.Transparent;
            public override Drawing.Color MenuItemSelected => TrayMenuIsDark
                ? Drawing.Color.FromArgb(51, 65, 85)
                : Drawing.Color.FromArgb(239, 246, 255);
            public override Drawing.Color MenuItemSelectedGradientBegin => MenuItemSelected;
            public override Drawing.Color MenuItemSelectedGradientEnd => MenuItemSelected;
            public override Drawing.Color MenuItemPressedGradientBegin => TrayMenuIsDark
                ? Drawing.Color.FromArgb(51, 65, 85)
                : Drawing.Color.FromArgb(219, 234, 254);
            public override Drawing.Color MenuItemPressedGradientEnd => MenuItemPressedGradientBegin;
            public override Drawing.Color ImageMarginGradientBegin => ToolStripDropDownBackground;
            public override Drawing.Color ImageMarginGradientMiddle => ToolStripDropDownBackground;
            public override Drawing.Color ImageMarginGradientEnd => ToolStripDropDownBackground;
            public override Drawing.Color ToolStripDropDownBackground => TrayMenuIsDark
                ? Drawing.Color.FromArgb(30, 41, 59)
                : Drawing.Color.FromArgb(255, 255, 255);
            public override Drawing.Color SeparatorDark => TrayMenuIsDark
                ? Drawing.Color.FromArgb(51, 65, 85)
                : Drawing.Color.FromArgb(226, 232, 240);
            public override Drawing.Color SeparatorLight => TrayMenuIsDark
                ? Drawing.Color.FromArgb(51, 65, 85)
                : Drawing.Color.FromArgb(241, 245, 249);
        }





























        // ---- Динамический порядок колонок списка баз ----

        // Статический порядок колонок данных в сетке (заголовке и строке базы) после
        // фиксированных колонок слева (кнопки групп, компенсатор, избранное, закрепление,
        // название). Совпадает с порядком по умолчанию: «Действия» сразу после «Режим запуска».
        private static readonly string[] StaticDataColumnKeys =
            { "Version", "LaunchMode", "Actions", "ServerBase", "LastLaunch", "Size", "Modified", "LastBackup", "Configuration", "ConfigurationVersion" };

        // Индекс первой колонки данных в сетке заголовка / строки базы.
        // Строка базы и заголовок имеют одинаковый набор ведущих колонок
        // (кнопки групп + компенсатор + избранное + закрепление + название),
        // поэтому данные строк точно совпадают по горизонтали с заголовками.
        private const int HeaderFirstDataColumn = 5; // после «Названия» заголовка
        private const int RowFirstDataColumn = 5;    // после «Названия» строки базы (как у заголовка)

        // Метка сетки строки базы: по ней находим созданные строки при обходе дерева.
        private static readonly object RowGridMarker = new();
        // Метка сетки заголовка группы: строки групп тоже перестраиваются по порядку колонок,
        // чтобы команды группы оставались в колонке «Действия» на уровне строк баз.
        private static readonly object GroupGridMarker = new();

        /// <summary>
        /// Ключ логической колонки элемента сетки (для динамического порядка колонок).
        /// Используется вместо Tag, т.к. Tag занят другими целями (сортировка, двойной клик).
        /// </summary>
        public static readonly DependencyProperty ColumnKeyProperty =
            DependencyProperty.RegisterAttached(
                "ColumnKey", typeof(string), typeof(MainWindow), new PropertyMetadata(null));

        public static void SetColumnKey(DependencyObject obj, string? value) =>
            obj.SetValue(ColumnKeyProperty, value);

        public static string? GetColumnKey(DependencyObject obj) =>
            (string?)obj.GetValue(ColumnKeyProperty);

        /// <summary>
        /// Признак «порядок колонок уже применён к этой сетке строки/заголовка» (issue #255).
        /// При Recycling-виртуализации экземпляр сетки переиспользуется под новые строки, и флаг
        /// сохраняется: повторные Loaded не пересобирают колонки заново (на каждую материализацию
        /// строки строились layout/массивы/List/HashSet и выполнялся обход детей — источник CPU
        /// и мусора gen2 при прокрутке). Сбрасывается в <c>ApplyColumnOrder</c> при изменении
        /// порядка колонок в настройках, после чего строки перестраиваются один раз.
        /// </summary>
        public static readonly DependencyProperty ColumnOrderAppliedProperty =
            DependencyProperty.RegisterAttached(
                "ColumnOrderApplied", typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

        public static void SetColumnOrderApplied(DependencyObject obj, bool value) =>
            obj.SetValue(ColumnOrderAppliedProperty, value);

        public static bool GetColumnOrderApplied(DependencyObject obj) =>
            (bool)obj.GetValue(ColumnOrderAppliedProperty);























        /// <summary>
        /// Блокирует автоматическую прокрутку TreeView к выделенному элементу
        /// (по умолчанию WPF вызывает BringIntoView при IsSelected/Focus — список «прыгает» вверх).
        /// </summary>



















        // Поля для ручного перетаскивания разделителя колонок.
        private ColumnDefinition? _resizeColumn;
        private double _resizeStartWidth;
        private Point _resizeStartMouse;

















        // ===================== Drag & Drop баз и групп =====================
        //
        // Модель WPF DnD (кратко):
        // 1) Источник: после порога смещения мыши вызывается DragDrop.DoDragDrop — синхронный
        //    цикл до Drop/Cancel. Пока он идёт, приходят DragOver/Drop на цели.
        // 2) Цель: AllowDrop=True; в DragOver обязательно задать e.Effects и e.Handled=true,
        //    иначе курсор «запрещено» и Drop не придёт.
        // 3) Данные: лучше свой payload с MouseDown (не с MouseMove) — иначе под курсором
        //    уже другой TreeViewItem (дочерняя база вместо группы).
        // 4) DoDragDrop возвращается после Drop → finally очищает _draggedData; во время Drop
        //    поле ещё валидно. Дополнительно кладём объект в DataObject по имени формата.

        private const string DragFormatInfobase = "Workspace1C.Infobase";
        private const string DragFormatGroup = "Workspace1C.GroupNode";


        /// <summary>
        /// Шаг накопительного отступа вложенности дерева (см. Margin у ItemsHost
        /// в ControlTemplate TreeViewItem: "18,0,0,0" на каждый уровень).
        /// Базы внутри групп смещаются вправо на этот шаг, чтобы была видна
        /// иерархия «группа в группе».
        /// </summary>
        private const double GroupTreeIndentStep = 18.0;

        /// <summary>Ширина кнопки разворота группы (px). Синхронизирована с Expander Width в XAML.</summary>
        private const double GroupTreeExpanderWidth = 26.0;












        // ===================== Стеклянный фон окна (acrylic/mica через DWM) =====================
        //
        // Повторяем визуальный эффект Avalonia-версии («прозрачное стекло») средствами WPF.
        // Механика: расширенная системная стеклянная рамка DWM (GlassFrameThickness=-1)
        // + полупрозрачная подложка из цвета темы (~0xE8) + системный acrylic/mica backdrop
        // (Windows 11) либо blur-behind (старые Windows). Если DWM недоступен — остаётся
        // просто полупрозрачный фон без размытия, окно остаётся рабочим.

        /// <summary>Альфа полупрозрачной подложки «стекла» — 0xE8 (~91% непрозрачности).</summary>
        private const byte GlassBackgroundAlpha = 0xE8;

        // DWMWA_SYSTEMBACKDROP_TYPE (38): 2 = Mica, 3 = Acrylic.
        private const int DwmSystemBackdropType = 38;
        private const int DwmBackdropAcrylic = 3;
        private const int DwmBackdropMica = 2;

        // DWMWA_WINDOW_CORNER_PREFERENCE (33): 1 = не скруглять, 2 = скруглять.
        private const int DwmWindowCornerPreference = 33;
        private const int DwmCornerRound = 2;
        private const int DwmCornerDoNotRound = 1;

        // DWM_BB_ENABLE для DwmEnableBlurBehindWindow.
        private const int DwmBbEnable = 0x00000001;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DwmBlurBehind pBlurBehind);

        [StructLayout(LayoutKind.Sequential)]
        private struct DwmBlurBehind
        {
            public int dwFlags;
            public int fEnable;
            public IntPtr hRgnBlur;
            public int fTransitionOnMaximized;
        }

        /// <summary>
        /// Применяет стеклянное оформление, когда у окна появился HWND (SourceInitialized):
        /// полупрозрачная подложка темы, системный acrylic/mica (или blur-behind) и
        /// скруглённые углы. Никакие сбои DWM не должны блокировать запуск окна.
        /// </summary>
        private void OnWindowSourceInitialized(object? sender, EventArgs e)
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                ApplyGlassBackground();
                ApplySystemBackdrop(hwnd);
                ApplyCornerPreference();
                // Главное окно оформлено собственными средствами и не проходит через
                // WindowChromeHelper.Apply, поэтому видимая рамка (issue #273) раньше
                // оставалась прямоугольной при скруглённых углах DWM — «скругление с
                // квадратной рамкой» (issue #126). Подключаем тот же механизм: радиус
                // по фактическому состоянию DWM с пересчётом после показа окна, при
                // максимизации и при смене темы/композиции.
                WindowChromeHelper.AttachFrameCornerTracking(this, WindowFrameBorder);
            }
            catch
            {
                // Не блокируем запуск из-за недоступности эффекта стекла.
            }
        }

        /// <summary>
        /// Полупрозрачная подложка окна: берём текущий цвет темы (ContentBackgroundBrush,
        /// обновляется для светлой/тёмной темы и любой цветовой схемы) и пересчитываем его
        /// с альфой ~0xE8. Именно эта подложка остаётся основным фоном, а размытие DWM
        /// лишь добавляет эффект «стекла» сквозь прозрачные области.
        /// </summary>
        private void ApplyGlassBackground()
        {
            if (TryFindResource("ContentBackgroundBrush") is SolidColorBrush brush)
            {
                var c = brush.Color;
                Background = new SolidColorBrush(Color.FromArgb(GlassBackgroundAlpha, c.R, c.G, c.B));
            }
        }

        /// <summary>
        /// Шапка главного окна реагирует на активность окна: при активном окне заливается
        /// акцентным цветом темы (<c>AccentBrush</c>), при неактивном — цветом карточки
        /// (<c>CardBackgroundBrush</c>). Вместе с фоном переключается цвет заголовка и стили
        /// кнопок управления окном, чтобы они оставались читаемыми на акцентной полосе.
        /// Задаём цвета/стили через ресурсы темы (DynamicResource), поэтому они обновляются
        /// и при смене темы/схемы, а не только при активации.
        /// </summary>
        private void UpdateTitleBarAppearance(bool active)
        {
            try
            {
                if (TitleBarBorder is null || AppTitleBlock is null)
                    return;

                TitleBarBorder.SetResourceReference(
                    Border.BackgroundProperty,
                    active ? "AccentBrush" : "CardBackgroundBrush");

                AppTitleBlock.SetResourceReference(
                    TextBlock.ForegroundProperty,
                    // Заголовок «на акценте» контрастен фактическому акценту (issue #26):
                    // при тёмном пользовательском акценте белый, при светлом — чёрный.
                    active ? "AccentTextBrush" : "TextPrimaryBrush");

                // Кнопки управления окном: на акцентной шапке нужен вариант «на акценте»
                // (значок ButtonTextBrush), на обычной — стандартный (значок TextSecondaryBrush).
                // Стиль macOS-«светофора» (Задача 3) выбирается отдельным шаблоном.
                if (MinimizeButton is not null && MaximizeButton is not null && CloseButton is not null)
                {
                    var macos = ThemeManager.CurrentWindowButtonStyle == WindowButtonStyle.MacOS;
                    var minimizeStyle = (Style?)TryFindResource(ThemeManager.WindowButtonStyleKey(close: false, onAccent: active));
                    var maximizeStyle = minimizeStyle;
                    var closeStyle = (Style?)TryFindResource(ThemeManager.WindowButtonStyleKey(close: true, onAccent: active));

                    MinimizeButton.Style = minimizeStyle;
                    MaximizeButton.Style = maximizeStyle;
                    CloseButton.Style = closeStyle;

                    // «Светофор» macOS: жёлтый круг «свернуть» и зелёный «развернуть»
                    // задаются кистью HoverBrush каждой кнопки (шаблон рисует круг
                    // всегда видимым, цвет — из этой кисти). Ссылка на ресурс, а не
                    // снимок: кисть обновляется при смене темы.
                    if (macos)
                    {
                        MinimizeButton.SetResourceReference(WindowChromeHelper.HoverBrushProperty, "WindowButtonMinimizeHoverBrush");
                        MaximizeButton.SetResourceReference(WindowChromeHelper.HoverBrushProperty, "WindowButtonMaximizeHoverBrush");
                    }
                    else
                    {
                        // Возврат к обычному стилю: снимаем локальную кисть, чтобы снова
                        // действовал DynamicResource WindowButtonHoverBrush из стиля.
                        MinimizeButton.ClearValue(WindowChromeHelper.HoverBrushProperty);
                        MaximizeButton.ClearValue(WindowChromeHelper.HoverBrushProperty);
                    }
                }
            }
            catch
            {
                // Не ломаем окно из-за сбоя перекраски шапки.
            }
        }

        /// <summary>
        /// Включает системный размытый фон: на Windows 11 — acrylic (DWMSBT_TRANSIENTWINDOW),
        /// при недоступности — mica (DWMSBT_MAINWINDOW); на старых Windows — blur-behind.
        /// Если ничего не удалось, откатываемся на полупрозрачный фон без размытия.
        /// </summary>
        private void ApplySystemBackdrop(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return;

            try
            {
                // Windows 11 (build >= 22000): системный acrylic backdrop,
                // при недоступности — mica.
                if (Environment.OSVersion.Version.Build >= 22000)
                {
                    int backdrop = DwmBackdropAcrylic;
                    if (DwmSetWindowAttribute(hwnd, DwmSystemBackdropType, ref backdrop, sizeof(int)) == 0)
                        return;

                    backdrop = DwmBackdropMica;
                    if (DwmSetWindowAttribute(hwnd, DwmSystemBackdropType, ref backdrop, sizeof(int)) == 0)
                        return;
                }

                // Старые Windows: классический blur-behind.
                var bb = new DwmBlurBehind { dwFlags = DwmBbEnable, fEnable = 1 };
                DwmEnableBlurBehindWindow(hwnd, ref bb);
            }
            catch
            {
                // Не блокируем запуск: останется полупрозрачный фон без размытия.
            }
        }

        /// <summary>
        /// Скруглённые углы окна в стиле glass на уровне DWM (Windows 11). В развёрнутом
        /// состоянии углы обнуляются, чтобы в углах окна не просвечивал рабочий стол.
        /// </summary>
        private void ApplyCornerPreference()
        {
            try
            {
                if (Environment.OSVersion.Version.Build < 22000)
                    return;

                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero)
                    return;

                int pref = WindowState == WindowState.Maximized ? DwmCornerDoNotRound : DwmCornerRound;
                DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref pref, sizeof(int));
            }
            catch
            {
                // Игнорируем: скругление — некритичное улучшение.
            }
        }

        /// <summary>
        /// При максимизации возвращаем толщину стеклянной рамки к 0: это известное
        /// обходное решение, иначе окно с WindowChrome и расширенной стеклянной рамкой
        /// (GlassFrameThickness=-1) при развороте перекрывает панель задач Windows 11.
        /// </summary>
        private void UpdateGlassFrameForMaximize()
        {
            try
            {
                var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
                if (chrome is null)
                    return;
                chrome.GlassFrameThickness = WindowState == WindowState.Maximized
                    ? new Thickness(0)
                    : new Thickness(-1);
            }
            catch
            {
                // Игнорируем: если рамку не удалось поправить, окно всё равно работает.
            }
        }

        // ===================== Собственные кнопки управления окном (без системной рамки) =====================

        /// <summary>
        /// Клик по оверлею блокировки приложения (issue #294): открывает окно ввода пароля.
        /// Сам оверлей закрывает клиентскую область, поэтому остальной ввод в окно не проходит.
        /// </summary>
        private void OnAppLockOverlay_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            _viewModel.ShowAppUnlockDialog();
        }

        /// <summary>Кнопка «Разблокировать» на оверлее блокировки (issue #294).</summary>
        private void OnAppLockOverlay_ButtonClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            _viewModel.ShowAppUnlockDialog();
        }

        /// <summary>
        /// Перетаскивание окна за фон верхней панели (окно без системной рамки, WindowChrome
        /// с <c>CaptionHeight=0</c>). Двойной клик по пустой области переключает разворот.
        /// Нажатия на интерактивных элементах (кнопки, поля, вкладки) перехватываются ими
        /// самими и сюда не доходят, поэтому случайного перетаскивания при кликах нет.
        /// </summary>
        private void OnTopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (e.ClickCount == 2)
                {
                    ToggleMaximize();
                    return;
                }

                // Развёрнутое окно не перетаскиваем: возврат к «плавающему» виду делается
                // кнопкой разворота, а DragMove по развёрнутому окну ведёт себя непредсказуемо.
                if (WindowState == WindowState.Maximized)
                    return;

                DragMove();
            }
            catch
            {
                // Игнорируем: DragMove может выбросить при клике, ушедшем в дочерний элемент.
            }
        }

        private void OnMinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void OnMaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        /// <summary>
        /// Закрытие через штатный <see cref="Window.Close"/>: уважает настройку
        /// «свернуть в трей» (<c>CloseToTray</c>), обрабатываемую в <c>OnClosing</c>.
        /// </summary>
        private void OnCloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        /// <summary>
        /// Резолвит глифы кнопок управления окном по активному набору иконок (Задача 2):
        /// свернуть/развернуть/восстановить/закрыть берутся из ресурсов набора через
        /// <see cref="Workspace1C.Converters.IconKeyToGeometryConverter.ResolveGeometry"/>.
        /// Вызывается при инициализации окна и при каждой смене полноценной темы.
        /// </summary>
        private void UpdateWindowControlGlyphs()
        {
            if (MinimizeGlyphPath is null || MaximizeGlyphPath is null ||
                RestoreGlyphPath is null || CloseGlyphPath is null)
                return;

            MinimizeGlyphPath.Data =
                Converters.IconKeyToGeometryConverter.ResolveGeometry("GlyphWindowMinimize");
            MaximizeGlyphPath.Data =
                Converters.IconKeyToGeometryConverter.ResolveGeometry("GlyphWindowMaximize");
            RestoreGlyphPath.Data =
                Converters.IconKeyToGeometryConverter.ResolveGeometry("GlyphWindowRestore");
            CloseGlyphPath.Data =
                Converters.IconKeyToGeometryConverter.ResolveGeometry("GlyphWindowClose");
        }

        /// <summary>
        /// Переключает значок кнопки «развернуть/восстановить» в зависимости от состояния окна
        /// (одинарный квадрат — в обычном состоянии, два квадрата — в развёрнутом).
        /// </summary>
        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            if (MaximizeGlyphPath == null || RestoreGlyphPath == null)
                return;

            bool maximized = WindowState == WindowState.Maximized;
            MaximizeGlyphPath.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
            RestoreGlyphPath.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;

            // При развороте обнуляем скругление углов и толщину стеклянной рамки,
            // чтобы окно корректно прилегало к краям экрана и панели задач.
            ApplyCornerPreference();
            UpdateGlassFrameForMaximize();
        }

        /// <summary>
        /// Клик по тексту индикатора фоновых загрузок (issue #359): если активные загрузки
        /// идут из разных окон — всплывающая панель выбора окна над индикатором (фон и цвета
        /// как у нижней строки состояния главного окна, панель не выходит за границы окна),
        /// иначе сразу открывается окно этой загрузки (без случайного выбора).
        /// </summary>
        private void OnDownloadsIndicatorClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel is null || sender is not System.Windows.Controls.Button btn)
                return;

            if (!_viewModel.ShouldShowDownloadPicker)
            {
                _viewModel.OpenFirstDownloadWindow();
                return;
            }

            var menu = CreateDownloadsPickerMenu(btn);
            foreach (var choice in _viewModel.DownloadChoices)
            {
                var item = new MenuItem
                {
                    Header = string.Format("{0}: {1} ({2:P0})", choice.WindowLabel, choice.Title, choice.Progress),
                    Style = TryFindResource("ModernMenuItem") as Style,
                };
                ApplyDownloadsPickerItemTheme(item);
                var id = choice.Id;
                item.Click += (_, _) => _viewModel.OpenWindowForDownload(id);
                menu.Items.Add(item);
            }
            if (menu.Items.Count == 0)
            {
                _viewModel.OpenFirstDownloadWindow();
                return;
            }

            // Отложенное открытие (DispatcherPriority.Input), как в OnUtilitiesMenuButton_Click
            // (issue #282): меню успевает измерить и отрисовать пункты.
            Dispatcher.BeginInvoke(
                new Action(() => menu.IsOpen = true),
                System.Windows.Threading.DispatcherPriority.Input);
        }

        /// <summary>
        /// Панель выбора окна у индикатора загрузок (issue #359). Фон и цвет текста —
        /// как у нижней строки состояния главного окна: <c>StatusBarBorder</c>
        /// (MainWindow.xaml) залит кистью <c>SidebarBrush</c>, текст в строке —
        /// <c>SidebarTextBrush</c>; рамка, скругление 6 и тень остаются от стиля
        /// <c>ModernContextMenu</c>. Панель ограничена по ширине и высоте размером окна
        /// (не шире главного окна), а позицию считает callback для <c>Placement=Custom</c>:
        /// панель стоит над индикатором, X зажат в границах окна — иначе меню, открытое у
        /// правого края нижней строки, вылезало за правый край окна.
        /// </summary>
        private ContextMenu CreateDownloadsPickerMenu(FrameworkElement anchor)
        {
            var margin = PopupBoundsMath.DefaultMargin;
            var menu = new ContextMenu
            {
                Style = TryFindResource("ModernContextMenu") as Style,
                PlacementTarget = anchor,
                Placement = PlacementMode.Custom,
                DataContext = DataContext,
            };

            if (TryFindResource("SidebarBrush") is Brush background)
                menu.Background = background;
            if (TryFindResource("SidebarTextBrush") is Brush foreground)
                menu.Foreground = foreground;

            menu.MaxWidth = Math.Max(menu.MinWidth, PopupBoundsMath.MaxPopupSize(ActualWidth, margin));
            menu.MaxHeight = Math.Max(menu.MinHeight, PopupBoundsMath.MaxPopupSize(ActualHeight, margin));

            // Callback для Placement=Custom: WPF вызывает его после измерения popup и ждёт
            // точку, отсчитанную от верхнего левого угла PlacementTarget (Popup.cs:2061),
            // а сам popupSize отдаёт в физических пикселях экрана (GetChildInterestPoints
            // применяет TransformToDevice), в то ActualWidth/ActualHeight и TranslatePoint
            // дают DIP. Поэтому размер popup переводится в DIP, а результат — обратно
            // в пиксели, иначе при 125%/150% меню смещалось бы относительно индикатора.
            menu.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
            {
                var toDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice
                    ?? Matrix.Identity;
                var origin = anchor.TranslatePoint(new Point(0, 0), this);
                var (left, top) = PopupBoundsMath.PlaceAboveClamped(
                    origin.X,
                    origin.Y,
                    popupSize.Width / toDevice.M11,
                    popupSize.Height / toDevice.M22,
                    ActualWidth,
                    ActualHeight,
                    margin,
                    PopupBoundsMath.DefaultGap);
                return new[]
                {
                    new CustomPopupPlacement(
                        new Point((left - origin.X) * toDevice.M11, (top - origin.Y) * toDevice.M22),
                        PopupPrimaryAxis.Horizontal),
                };
            };

            return menu;
        }

        /// <summary>
        /// Цвета пунктов панели выбора окна (issue #359): текст — как в строке состояния
        /// (<c>SidebarTextBrush</c>), подсветка — <c>SidebarHoverBrush</c>. Пункты лежат на
        /// тёмном фоне <c>SidebarBrush</c>, поэтому <c>TextPrimaryBrush</c> светлой темы
        /// (чёрный) и <c>ItemHoverBrush</c> светлой темы (бледно-жёлтый) не годятся.
        /// Подсветка в шаблоне пункта красится <c>{DynamicResource ItemHoverBrush}</c>, а цвет
        /// подписи — <c>{DynamicResource TextPrimaryBrush}</c> (LightTheme.xaml:791 и :874);
        /// разрешение ресурса идёт по логическому дереву от самого пункта, поэтому кисти
        /// кладутся в его ресурсы и перекрывают ресурсы приложения.
        /// </summary>
        private void ApplyDownloadsPickerItemTheme(MenuItem item)
        {
            if (TryFindResource("SidebarTextBrush") is Brush text)
                item.Resources["TextPrimaryBrush"] = text;
            if (TryFindResource("SidebarHoverBrush") is Brush hover)
                item.Resources["ItemHoverBrush"] = hover;
        }
    }
}
#endif
