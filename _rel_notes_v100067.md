### Добавлено

- **Правка структуры контекстного меню базы (issue #56, Windows и Linux)** — вкладка
  настроек «Меню „Утилиты“» переименована в **«Правка меню»** и объединяет оба редактора:
  для контекстного меню базы добавлены галочка **«Правка структуры контекстного меню»**
  и кнопка **«Настроить структуру…»**. Редактор позволяет перемещать пункты (вверх/вниз,
  в группу/в корень), скрывать их, группировать и добавлять разделители; новые пункты
  программы автоматически попадают в подменю «Новинки программы». Компоновка хранится
  в файле `context_menu_layout.json` (атомарная запись, нормализация битого файла).
  Единые модель, каталог, резолвер и хранилище
  ([`ContextMenuLayout.cs`](Workspace1C/Models/ContextMenuLayout.cs),
  [`ContextMenuCatalog.cs`](Workspace1C/Services/ContextMenuCatalog.cs),
  [`ContextMenuResolver.cs`](Workspace1C/Services/ContextMenuResolver.cs),
  [`ContextMenuLayoutStore.cs`](Workspace1C/Services/ContextMenuLayoutStore.cs)),
  редактор ([`ContextMenuEditorViewModel.cs`](Workspace1C/ViewModels/ContextMenuEditorViewModel.cs),
  [`ContextMenuEditorWindow.xaml`](Workspace1C/Views/ContextMenuEditorWindow.xaml),
  [`ContextMenuEditorWindow.Avalonia.cs`](Workspace1C/Views/ContextMenuEditorWindow.Avalonia.cs));
  построители меню: WPF
  ([`MainWindow.ContextMenu.cs`](Workspace1C/Views/MainWindow.ContextMenu.cs), меню перенесено
  из XAML в код) и Avalonia ([`MainWindow.Avalonia.Tree.cs`](Workspace1C/Views/MainWindow.Avalonia.Tree.cs));
  вкладка настроек — [`SettingsWindow.xaml`](Workspace1C/Views/SettingsWindow.xaml),
  [`SettingsWindow.Avalonia.cs`](Workspace1C/Views/SettingsWindow.Avalonia.cs).
  Добавлены юнит-тесты `ContextMenuCatalogTests` / `ContextMenuResolverTests` /
  `ContextMenuLayoutStoreTests` / `ContextMenuEditorViewModelTests`.
- **Общая авторизация для всех информационных баз (issue #57, Windows и Linux)** —
  в окне настроек добавлен блок **«Общая авторизация»**: запрос имени и пароля /
  автоматический вход / аутентификация ОС. Если у базы не задана собственная авторизация
  (для режима или базы), при запуске подставляются общие учётные данные — fallback-цепочка
  «своя Enterprise/Configurator/Connection → общая → запрос платформы» в
  [`InfobaseAuthResolver.cs`](Workspace1C/Services/InfobaseAuthResolver.cs). Общая
  авторизация применяется при запуске 1С:Предприятия и Конфигуратора
  ([`OneCLauncher.Arguments.Shared.cs`](Workspace1C/Services/OneCLauncher.Arguments.Shared.cs),
  статик `OneCLauncher.CommonAuthorization`) и при COM-подключении
  ([`OneCComConnector.cs`](Workspace1C/Services/OneCComConnector.cs)); хранится
  в `settings.json` (`AppSettings.CommonAuthorization`,
  [`AppSettings.cs`](Workspace1C/Models/AppSettings.cs)). «Запуск с аутентификацией»
  (issue #48) по-прежнему имеет приоритет (`/WA-`). Добавлены юнит-тесты
  `CommonAuthResolverTests` / `CommonAuthLaunchArgsTests` / `CommonAuthComTests`.
