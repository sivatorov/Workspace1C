Исправлено в версии 1.0.0.74 (Windows и Linux) по последнему комментарию:

1. **Полоса прогресса на всю ширину окна.** «Просьба сделать полосу прогресса побольше по ширине, влево ещё много места, но сейчас она только под локалью» — индикатор перенесён в отдельную строку и растянут на всю ширину (WPF [`ClusterMigrationWindow.xaml`](Workspace1C/Views/ClusterMigrationWindow.xaml), Avalonia [`ClusterMigrationWindow.axaml`](Workspace1C/Views/ClusterMigrationWindow.axaml)); закреплено тестом `Issue41WindowLayoutTests` (у полосы нет фиксированной ширины 120).
2. **Статусы приёмника обновляются автоматически при ошибке.** «Если произошла ошибка — стоит обновить статусы в приёмнике автоматически»: после неудачного переноса (`infobase create`/`drop`) окно само перечитывает статусы приёмника (без ручной кнопки «Получить статусы») — сразу видно, какие базы уже оказались в новом кластере ([`ClusterMigrationViewModel.MigrateAsync`](Workspace1C/ViewModels/ClusterMigrationViewModel.cs)); проверяется тестом `ClusterMigrationViewModelTests.Migrate_CreateFailure_RefreshesTargetStatusesAutomatically`.
3. **Имя группы приёмника с сервером и портом.** «Создало папку "Локальный кластер"… Надо или имя сервера добавлять или порт, если не стандартный» — группа именуется «Хост - Кластер (порт)», порт — только когда НЕ стандартный (1541), по примеру пользователя «Платформа 27 - Локальный кластер (27541)»; при пустом хосте — просто имя кластера (старые миграции не переименовываются) ([`ClusterMigrationService.BuildGroupName`](Workspace1C/Services/ClusterMigrationService.cs)).

Покрыто тестами `ClusterMigrationServiceTests` (+4), `ClusterMigrationViewModelTests`, `Issue41WindowLayoutTests`.

Подтверждённые ранее доработки миграции (1.0.0.51–1.0.0.73) из issue не удаляются.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.74