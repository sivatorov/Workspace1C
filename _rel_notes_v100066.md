### Добавлено

- **Смена платформы в массовой замене строк подключения (issue #54, Windows и Linux)** —
  в окно **«Замена строк подключения и платформы…»** добавлена галочка «Сменить платформу»
  и список установленных платформ: смена платформы применяется к выделенным базам вместе
  со сменой строки подключения, операция поддерживает откат («Отменить последнюю замену»);
  пункты меню переименованы. Единая логика планировщика и модели
  ([`ConnectionReplacementPlanner.cs`](Workspace1C/Services/ConnectionReplacementPlanner.cs),
  [`ConnectionReplaceViewModel.cs`](Workspace1C/ViewModels/ConnectionReplaceViewModel.cs))
  и представления ([`ConnectionReplaceWindow.xaml`](Workspace1C/Views/ConnectionReplaceWindow.xaml),
  [`ConnectionReplaceWindow.Avalonia.cs`](Workspace1C/Views/ConnectionReplaceWindow.Avalonia.cs)),
  локализация обновлена. Добавлены юнит-тесты `ConnectionReplacementPlannerTests` /
  `ConnectionReplaceViewModelTests` / `ConnectionReplaceLocalizationTests` (+15).

### Изменено

- **Запоминание выбранной вкладки списка (issue #52, Windows и Linux)** — выбранная вкладка
  «Все / Избранное / Недавние / Запущенные» автоматически запоминается и восстанавливается
  при следующем запуске; режим хранится канонической строкой в `settings.json`
  (`AppSettings.ListMode`, не зависит от локали;
  [`ListViewMode.cs`](Workspace1C/Models/ListViewMode.cs),
  [`AppSettings.cs`](Workspace1C/Models/AppSettings.cs)), старые булевы флаги
  `ShowFavoritesOnly`/`ShowRunningOnly` мигрируются в единый режим
  (`AppSettings.NormalizeListMode`). Реализовано на обеих платформах (WPF и Avalonia).
  Добавлены юнит-тесты `AppSettingsListModeTests` (25).
- **Пункт «Для выделенных» доступен и для одной базы (issue #53, Windows и Linux)** — блок
  команд «Для выделенных (N)…» (назначить тег, «Переместить в группу» и остальные массовые
  команды) теперь показывается и при выделении одной базы без Ctrl; мультивыделение
  сохраняет приоритет. Единое правило эффективного числа строк для обеих платформ —
  `BatchSelectionHelper.EffectiveBatchCount`
  ([`BatchSelectionHelper.cs`](Workspace1C/Services/BatchSelectionHelper.cs)), команды меню
  и подключения ([`MainViewModel.BatchCommands.cs`](Workspace1C/ViewModels/MainViewModel.BatchCommands.cs),
  [`MainWindow.Events.cs`](Workspace1C/Views/MainWindow.Events.cs)). WPF + Avalonia.
  Добавлены юнит-тесты `BatchSelectionHelperTests` (+4).
- **Переработка миграции между кластерами 1С (issue #41, Windows и Linux)**:
  - импорт из кластера возвращён к исходному виду: кнопка «Мигрировать» удалена,
    «Выделить все» отмечает только импортируемые строки, подсказка про «атавизм» убрана
    ([`ClusterImportViewModel.cs`](Workspace1C/ViewModels/ClusterImportViewModel.cs),
    [`ClusterImportWindow.xaml`](Workspace1C/Views/ClusterImportWindow.xaml));
  - в окне «Серверы 1С» кнопка «Импорт \ Миграция» разделена на две отдельные кнопки —
    «Импорт» и «Миграция»
    ([`ServerMonitorWindow.xaml`](Workspace1C/Views/ServerMonitorWindow.xaml),
    [`ServerMonitorWindow.Avalonia.cs`](Workspace1C/Views/ServerMonitorWindow.Avalonia.cs));
  - команда «Утилиты» снова называется «Импорт из кластера 1С»; добавлена новая команда
    «Миграция между кластерами 1С» (без хоткея)
    ([`UtilitiesMenuCatalog.cs`](Workspace1C/Services/UtilitiesMenuCatalog.cs));
  - новое отдельное окно миграции с двухпанельной раскладкой «Источник»/«Приёмник»:
    сервер 1С + подключение, кластер + «Получить базы», «Загружено баз» под «Кластер»;
    колонки «Статус источника» и «Статус приёмника»; серверы источника и приёмника не
    пересекаются (проверка при выборе, из списков не удаляются)
    ([`ClusterMigrationViewModel.cs`](Workspace1C/ViewModels/ClusterMigrationViewModel.cs),
    [`ClusterMigrationRow.cs`](Workspace1C/ViewModels/ClusterMigrationRow.cs),
    [`ClusterMigrationWindow.xaml`](Workspace1C/Views/ClusterMigrationWindow.xaml),
    [`ClusterMigrationWindow.Avalonia.cs`](Workspace1C/Views/ClusterMigrationWindow.Avalonia.cs));
  - «Получить статусы» проставляет «Есть в кластере»/пусто по совпадению имени базы; галочки:
    все — после загрузки источника, только новые — после получения статусов приёмника;
    «Мигрировать» недоступна, если отмечена строка со статусом «Есть в кластере»;
  - добавлена галочка «Не удалять в старом кластере (копия без удаления)» рядом с
    «Создавать группу по имени кластера»; логин и пароль СУБД — в одну линию; после миграции
    базы добавляются в новую группу (имя кластера-приёмника), при желании заменяются строки
    подключения;
  - реализовано на обеих платформах (WPF и Avalonia). Юнит-тесты `ClusterMigrationViewModelTests`
    переписаны, `ClusterImportViewModelTests` и `UtilitiesMenuLayoutTests` (+1) дополнены.

### Исправлено

- **Обрезка элементов в формах «Настройки» и «Настройка информационной базы» (issue #55,
  Windows и Linux)** — при уменьшении размеров окон элементы больше не обрезаются: колонка
  подписей в «Настройке информационной базы» стала `Auto` (`MinWidth`), метки чекбоксов
  «Размер вручную»/«Приватная база» переносятся по словам; вкладка «Платформы» окна
  «Настройки» получила прокрутку
  ([`SettingsWindow.xaml`](Workspace1C/Views/SettingsWindow.xaml),
  [`SettingsWindow.Avalonia.cs`](Workspace1C/Views/SettingsWindow.Avalonia.cs),
  [`ConnectionSettingsWindow.xaml`](Workspace1C/Views/ConnectionSettingsWindow.xaml),
  [`ConnectionSettingsWindow.Avalonia.cs`](Workspace1C/Views/ConnectionSettingsWindow.Avalonia.cs)).
- **Цвет подчёркивания полей при активности — из темы (issue #51, Windows и Linux;
  продолжение 1.0.0.64)** — активная полоса подчёркивания текстовых полей (Material Design
  WPF) переопределена кисть `MaterialDesign.Brush.Primary` → акцентный цвет светлой/тёмной
  темы и обновляется при смене акцента
  ([`LightTheme.xaml`](Workspace1C/Themes/LightTheme.xaml),
  [`DarkTheme.xaml`](Workspace1C/Themes/DarkTheme.xaml)); поля редакторов в Avalonia (сервер 1С,
  учётные записи ITS, создание ИБ, репозиторий) стилизованы
  `ModernTextBox`/`ModernPasswordBox` с акцентным контуром
  ([`ServerEditWindow.Avalonia.cs`](Workspace1C/Views/ServerEditWindow.Avalonia.cs),
  [`ItsAccountEditWindow.Avalonia.cs`](Workspace1C/Views/ItsAccountEditWindow.Avalonia.cs),
  [`RacInfobaseCreateWindow.Avalonia.cs`](Workspace1C/Views/RacInfobaseCreateWindow.Avalonia.cs),
  [`RepositoryBrowserWindow.Avalonia.cs`](Workspace1C/Views/RepositoryBrowserWindow.Avalonia.cs)).
  Добавлены юнит-тесты `UnderlineThemeBindingTests` (+3).
- **Единый контраст текста выбранного пункта панели «Настройки» (issue #26, Windows и
  Linux)** — цвет текста выбранного пункта левой панели «Настроек» теперь единый и контрастный
  во всех разделах, включая «Резервное копирование» и «Учетные записи»: в WPF неработавший
  `TemplateBinding` заменён привязкой через `TemplatedParent`, код-вкладки получают привязку
  подписи к `TabItem.Foreground`
  ([`SettingsWindow.xaml`](Workspace1C/Views/SettingsWindow.xaml),
  [`SettingsWindow.Accounts.cs`](Workspace1C/Views/SettingsWindow.Accounts.cs),
  [`SettingsWindow.Profile.cs`](Workspace1C/Views/SettingsWindow.Profile.cs)).
  Добавлены юнит-тесты `SettingsTabItemContrastTests` (9).