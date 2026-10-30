### Исправлено

- **Миграция баз: поле «Имя БД» и понятная ошибка вместо «Не указано имя базы данных» (issue #41, Windows и Linux)** — на форму миграции добавлено поле **«Имя БД»** (`db-name`) — в группе с «Тип СУБД»/«Сервер СУБД», выше логина и пароля
  ([`ClusterMigrationWindow.xaml`](Workspace1C/Views/ClusterMigrationWindow.xaml),
  [`ClusterMigrationWindow.axaml`](Workspace1C/Views/ClusterMigrationWindow.axaml)); поле
  автозаполняется из первой базы источника после «Получить базы» и не перезаписывает
  ручные правки (`ClusterMigrationViewModel.DbName`); при создании базы в приёмнике
  значение формы используется как fallback для баз, где `rac` не вернул имя БД, —
  остальные переносятся со своими именами
  ([`ClusterMigrationService.BuildCreateRequest`](Workspace1C/Services/ClusterMigrationService.cs));
  если имя БД не указано — вместо прежнего `ArgumentException` показывается сообщение с
  именем базы и подсказкой
  ([`MigrateAsync`](Workspace1C/ViewModels/ClusterMigrationViewModel.cs)). Покрыто тестами
  `ClusterMigrationServiceTests`/`ClusterMigrationViewModelTests`/`Issue41WindowLayoutTests`.