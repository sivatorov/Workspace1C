# План правок для релиза 1.0.0.70 — issue #41 «Миграция баз» (поле «Имя БД» + понятная ошибка)

Версия программы: **1.0.0.70** (сейчас в [`Workspace1C.csproj`](Workspace1C/Workspace1C.csproj:62) — 1.0.0.69; версия увеличивается только последним числом, 69 → 70). Обе платформы: WPF (Windows) и Avalonia (Linux). Issues самим **не закрываем** — только комментарий после фикса. Сборка, релиз и Telegram в этом плане **не описываются** (отдельные задачи после реализации).

---

## 0. Стратегия версии и порядок коммитов

| Коммит | Состав |
|---|---|
| C0 | Версия 69 → 70 в [`Workspace1C.csproj`](Workspace1C/Workspace1C.csproj:62) (четыре тега: `Version`/`AssemblyVersion`/`FileVersion`/`InformationalVersion`) — «v1.0.0.70: версия 69→70» |
| C1 | Фикс issue #41 — поле «Имя БД» + override + валидация + локализации + юнит-тесты |
| C2 | CHANGELOG: новая секция `[1.0.0.70]` вверху; README: бейдж/актуальная версия 1.0.0.70, актуализация строки миграции |
| C3 | Комментарий к issue #41 (черновик `_issue41_comment.md`) — публикуется в GitHub, issue НЕ закрывается |

---

## 1. Диагноз (подтверждён по коду)

1. Исключение `ArgumentException: Не указано имя базы данных. (Parameter 'request')` бросает [`RacClient.InfobaseCreateArgs`](Workspace1C/Services/RacClient.cs:584) — проверка `string.IsNullOrWhiteSpace(request.DbName)` при сборке аргументов `infobase create` (rac требует `--db-name`).
2. Запрос строит [`ClusterMigrationService.BuildCreateRequest`](Workspace1C/Services/ClusterMigrationService.cs:62); сейчас `DbName = source.DbName ?? string.Empty` (строка 77) — для конкретной базы 7OH источник вернул пустой `db-name` (парсер [`RacOutputParser.ToInfobaseSummaries`](Workspace1C/Services/RacOutputParser.cs:764) корректно читает табличный (кол. 5) и блочный 8.5.x форматы (`db-name`/`dbName`), но rac может просто не отдать значение колонки).
3. Исправить/увидеть значение пользователь не мог: на форме миграции есть поля «Тип СУБД»/«Сервер СУБД» (v1.0.0.69, строки [`ClusterMigrationWindow.xaml`](Workspace1C/Views/ClusterMigrationWindow.xaml:255)), а поля «Имя БД» — нет; override для `DbName` в `BuildCreateRequest` отсутствует.
4. Существующий механизм автозаполнения (`_dbDetailsTouched`, `NormalizeDbms`, блок в [`ClusterMigrationViewModel.ApplySourceBases`](Workspace1C/ViewModels/ClusterMigrationViewModel.cs:729)) переиспользуется для нового поля без изменений подхода.

---

## 2. Архитектура фикса

### 2.1 Семантика `overrideDbName` — ЗАФИКСИРОВАНО (fallback-семантика)

Поле «Имя БД» **одно на форму** и автозаполняется из **первой** базы источника. При применении к каждой базе приоритета формы (как у `Dbms`/`DbServer`) поле формы (имя первой базы) перезаписало бы собственные корректные `db-name` остальных баз при переносе нескольких баз. Зафиксировано по согласованию:

```csharp
DbName = FirstNonEmpty(source.DbName, overrideDbName);
```

**Источник приоритетнее; значение формы используется только для баз, где rac не вернул `db-name`.** Это ровно лечит баг 7OH (пустой `db-name` у конкретной базы), не задевает базы с корректными именами и согласуется с подсказкой на форме «для баз, где rac не вернул db-name». Параметр `overrideDbName` в сигнатуре — с умолчанием `""`; существующие вызовы с 4–5 аргументами (тесты, старые перегрузки) не ломаются.

### 2.2 Затрагиваемые файлы и характер правки

| Файл | Что менять |
|---|---|
| [`ClusterMigrationViewModel.cs`](Workspace1C/ViewModels/ClusterMigrationViewModel.cs:61) | 1) Поле `private string _dbName = string.Empty;` рядом с `_dbms`/`_dbServer` (после строки 64). 2) Публичное свойство `public string DbName` по образцу [`DbServer`](Workspace1C/ViewModels/ClusterMigrationViewModel.cs:371): сеттер выставляет `_dbDetailsTouched = true` и `SetProperty(ref _dbName, (value ?? string.Empty).Trim())`. 3) В блоке автозаполнения [`ApplySourceBases`](Workspace1C/ViewModels/ClusterMigrationViewModel.cs:729) добавить: `var dbName = firstSource.DbName ?? string.Empty; if (SetProperty(ref _dbName, dbName)) OnPropertyChanged(nameof(DbName));` (только пока `!_dbDetailsTouched`, запись напрямую в поле, как у `Dbms`/`DbServer`). |
| [`ClusterMigrationViewModel.cs`](Workspace1C/ViewModels/ClusterMigrationViewModel.cs:1055) | Вызов в `MigrateAsync`: `BuildCreateRequest(row.Source!, DbUser, DbPassword, Dbms, DbServer, DbName)`. Сразу после построения запроса и **до** `_rac.CreateInfobaseAsync` — валидация: `if (string.IsNullOrWhiteSpace(request.DbName)) { ErrorMessage = string.Format(LocalizationManager.T("ClusterMigration.Error.DbNameEmptyFormat"), row.Name); return; }` (сырой `ArgumentException` из rac больше не доходит до пользователя; вызов rac для такой базы не производится). |
| [`ClusterMigrationService.cs`](Workspace1C/Services/ClusterMigrationService.cs:62) | Расширить сигнатуру `BuildCreateRequest(..., string overrideDbName = "")`; `DbName = FirstNonEmpty(source.DbName, overrideDbName)`; обновить XML-док (параметр `overrideDbName`: приоритет у значения источника, форма — для пустых `db-name`). |
| [`ClusterMigrationWindow.xaml`](Workspace1C/Views/ClusterMigrationWindow.xaml:272) | Новая строка сетки между строкой «Тип СУБД/Сервер СУБД» (Row=3) и «Учётными данными СУБД» (Row=4): `<Grid Grid.Row="4">` — TextBlock «Имя БД:» + TextBox `x:Name="DbNameBox"` c `Text="{Binding DbName, UpdateSourceTrigger=PropertyChanged}"`; нижние строки сдвинуть: учётные данные 4→5, прогресс 5→6, действия 6→7 (RowDefinitions файла, строки 40-48). Расположение — «группа» с полями СУБД, выше логина/пароля. |
| [`ClusterMigrationWindow.axaml`](Workspace1C/Views/ClusterMigrationWindow.axaml:74) | Аналогично: новая строка `Grid Grid.Row="4"` с TextBlock `ClusterMigration.DbName` + TextBox `x:Name="DbNameBox"` (`Text="{Binding DbName, UpdateSourceTrigger=PropertyChanged}"`); учётные данные Row 4→5. |
| [`ClusterMigrationWindow.Avalonia.cs`](Workspace1C/Views/ClusterMigrationWindow.Avalonia.cs:237) | Привязка: `var dbNameBox = this.FindControl<TextBox>("DbNameBox")!; dbNameBox.Bind(TextBox.TextProperty, new Binding("DbName", BindingMode.TwoWay));` рядом с привязками `DbServerBox` (двусторонняя — выбор в окне пишется в VM, автозаполнение VM отражается в поле). |
| [`ru.json`](Workspace1C/Localization/Languages/ru.json:2424) / [`en.json`](Workspace1C/Localization/Languages/en.json:2424) | Ключи: `ClusterMigration.DbName` («Имя БД:» / «DB name:») и `ClusterMigration.Error.DbNameEmptyFormat` («У базы «{0}» не указано имя базы данных в СУБД (rac не вернул db-name). Введите имя БД в поле «Имя БД» на форме миграции.» / «Infobase “{0}” has no DB name in the DBMS (rac returned no db-name). Enter the DB name in the “DB name” field of the migration window.»). |

### 2.3 Альтернатива (не включается в v1.0.0.70)

«Выделять строку/валидировать на уровне строки» (п. d ТЗ) — не включаем: колонки DataGrid («Статус источника/приёмника») заняты, а одна форма-поле уже даёт пользователю путь исправления; доп. индикация — отдельная задача при повторном обращении.

---

## 3. Юнит-тесты (10 тестов; минимум 2 на фикс — по стандарту проекта, xUnit)

### 3.1 [`ClusterMigrationServiceTests.cs`](Workspace1C.Tests/ClusterMigrationServiceTests.cs:64) (в блок `BuildCreateRequest`)

1. `BuildCreateRequest_FallsBackToOverrideDbName_WhenSourceDbNameEmpty` — `source.DbName=""`, `overrideDbName="db1"` → `request.DbName == "db1"` (главный кейс фикса).
2. `BuildCreateRequest_SourceDbNameWins_WhenOverrideProvided` — `source.DbName="buho"`, `overrideDbName="newdb"` → `request.DbName == "buho"` (fallback: форма НЕ перезаписывает корректный db-name источника).
3. `BuildCreateRequest_SourceDbNameWins_WhenOverrideEmpty` — `source.DbName="buho"`, `overrideDbName=""` → `"buho"` (регрессия текущего поведения без override).
4. `BuildCreateRequest_KeepsEmptyDbName_WhenBothEmpty` — оба пусты → `request.DbName == string.Empty` (защита: валидация VM обязана сработать до rac).

### 3.2 [`ClusterMigrationViewModelTests.cs`](Workspace1C.Tests/ClusterMigrationViewModelTests.cs:636) (паттерн секции «Issue #41: Тип СУБД и Сервер СУБД»)

5. `LoadSourceBases_AutoFillsDbName_FromFirstBase` — после `AwaitSourceRows` `vm.DbName == "база1"` (у `FakeRacClient` в источнике `DbName="база1"/"база2"`, строки 803-804).
6. `LoadSourceBases_DoesNotOverwriteDbName_WhenUserEditedIt` — `vm.DbName="newdb"` до загрузки → после загрузки остаётся `"newdb"` (флаг touched).
7. `MigrateAsync_PassesVmDbName_IntoCreateRequest` — `vm.DbName="dbX"` → `client.Created[0].Request.DbName == "dbX"` (для базы с пустым source.DbName; при непустом source — остаётся source).
8. `MigrateAsync_ShowsClearError_AndDoesNotCallRac_WhenDbNameEmpty` — фейк с `sourceDbName: ""` (обе базы без `db-name`): `MigrateAsync` → `MigrationCompleted == false`, `client.Created` пуст, `ErrorMessage` — отформатированный `ClusterMigration.Error.DbNameEmptyFormat` (содержит имя базы «База1»), а НЕ сырое «Не указано имя базы данных.».

**Правка фейка**: в [`FakeRacClient`](Workspace1C.Tests/ClusterMigrationViewModelTests.cs:752) добавить параметр конструктора `string? sourceDbName = null`; в `GetInfobasesAsync` (строки 803-804) `DbName = _sourceDbName ?? "база1"` / `"база2"` (значение `""` → пусто у обеих, `null` → прежнее поведение).

### 3.3 [`Issue41WindowLayoutTests.cs`](Workspace1C.Tests/Issue41WindowLayoutTests.cs:72) (разметочные, по образцу тестов Dbms/DbServer)

9. `ClusterMigrationWindow_Wpf_HasDbNameField_AboveCredentialsBelowDbmsRow` — в xaml есть `ClusterMigration.DbName`, привязка `Text="{Binding DbName, UpdateSourceTrigger=PropertyChanged}"`; позиции: `dbNamePos > dbServerPos` (под строкой «Тип СУБД/Сервер СУБД») и `dbUserPos > dbNamePos` (выше учётных данных).
10. `ClusterMigrationWindow_Avalonia_HasDbNameBinding` — axaml содержит `ClusterMigration.DbName` и `x:Name="DbNameBox"`, код `ClusterMigrationWindow.Avalonia.cs` содержит `DbNameBox`.

---

## 4. CHANGELOG и README (коммит C2)

- [`CHANGELOG.md`](CHANGELOG.md:14): новая секция `## [1.0.0.70] — <дата>` вверху, раздел «Исправлено»: поле «Имя БД» на форме миграции, автозаполнение из первой базы источника, fallback `db-name` при создании базы в приёмнике, понятное сообщение об ошибке вместо `ArgumentException`, когда rac не вернул `db-name` (обе платформы); ссылки на `BuildCreateRequest`/`MigrateAsync`.
- [`README.md`](README.md:3): бейдж `Версия-1.0.0.70`; «актуальная версия — **1.0.0.70**» (строка 11); в строке миграции (строка 134) добавить «…и поле „Имя БД“ для баз, где rac не вернул db-name (1.0.0.70, issue #41)».

## 5. Комментарий к issue #41 (коммит C3, черновик в `_issue41_comment.md`)

> Исправлено в версии 1.0.0.70 (Windows и Linux): на форму миграции добавлено поле «Имя БД» (db-name) — в группе с «Тип СУБД»/«Сервер СУБД», выше логина/пароля; автозаполняется из первой базы источника и не перезаписывается после ручного изменения. При создании базы в кластере-приёмнике значение формы используется для баз, где rac не вернул db-name (остальные переносятся со своими именами). Если имя БД так и не указано — вместо прежней непонятной ошибки «Не указано имя базы данных» показывается сообщение с именем базы и подсказкой заполнить поле. Добавлены юнит-тесты (сервис, ViewModel, разметка обеих платформ).

---

## 6. Pipeline (каждый пункт — отдельная задача после утверждения плана)

```mermaid
flowchart LR
    A[code: C0 - версия 1.0.0.70 в csproj] --> B[code: C1 - фикс issue 41 код тесты локализации]
    B --> C[code: C2 - CHANGELOG секция 1.0.0.70 и README]
    C --> D[code: C3 - комментарий к issue 41 без закрытия]
```

Сборка, GitHub Release и Telegram — вне этого плана (отдельные задачи после реализации, как в прошлых релизах).