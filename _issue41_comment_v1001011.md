Исправлено в версии **1.0.1.1** (Windows и Linux):

В мастере «Импорт/Миграция из кластера 1С» при установленной галочке «Не удалять
в старом кластере» вопрос о замене строк подключения больше не задаётся: копии
добавляются в список с корректным подключением, старые строки остаются на месте.
При переносе (галочка снята) поведение прежнее
([`ClusterMigrationViewModel.cs`](Workspace1C/ViewModels/ClusterMigrationViewModel.cs)).

Покрыто тестами
[`ClusterMigrationViewModelTests.cs`](Workspace1C.Tests/ClusterMigrationViewModelTests.cs)
и
[`Issue41WindowLayoutTests.cs`](Workspace1C.Tests/Issue41WindowLayoutTests.cs)
(стражи окон).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.1