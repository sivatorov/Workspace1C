# План: issue #113 — отсутствует подменю «Для группы (N)» у узла «Без группы» (v1.0.1.41)

## Контекст

Issue #113 (автор 7OH): после исправления в v1.0.1.40 (ПКМ выделяет узел «Без группы»,
меню наполняется контекстом) пользователь сообщает: «Почти всё — ок. **Отсутствует
подменю "Для группы (N)"**».

## Причина (найдена)

В `Workspace1C/ViewModels/MainViewModel.Batch.cs` (строки 266 и 282) пакетные цели и
признак группового контекста требуют `Group != null`:

- `BatchOperationTargetsIncludingGroup`: `SelectedGroupNode is { Group: not null } groupNode`
  → у узла «Без группы» `Group == null`, ветка не срабатывает, целей 0 →
  batch-блок скрыт полностью (заголовок и пункты «Выгрузить в DT\CF…», «Блокировать
  сеансы…», «Разблокировать…», «Массовая загрузка cf…»).
- `IsGroupBatchTarget`: то же условие → заголовок остался бы «Для выделенных (N)…».

В WPF (`MainWindow.Columns.cs:118`) и Avalonia (`MainWindow.Avalonia.Tree.cs:2286`)
видимость и заголовок берутся из этих свойств VM — правки во вьюхах не нужны, только VM.

## Изменения

### 1. Workspace1C/ViewModels/MainViewModel.Batch.cs

- Добавить внутренний хелпер `GetNoGroupInfobases()`: базы из `Infobases`, у которых
  `Group` null/пустой/whitespace (доступ internal для юнит-тестов).
- `BatchOperationTargetsIncludingGroup`: при выделенном узле с маркером
  `GroupNodeViewModel.NoGroupMarker` возвращать `GetNoGroupInfobases()`.
- `IsGroupBatchTarget`: `true` и для узла «Без группы» → заголовок «Для группы (N)…».
- Узлы «Закреплённые» и «Все базы» не трогаем (вне скоупа issue).

### 2. Тесты (Workspace1C.Tests)

- Расширить `Issue113NoGroupMenuTests` или добавить `Issue113NoGroupBatchTests`:
  - цели для узла «Без группы» = базы с пустым `Group` (базы в группах не попадают);
  - source-тест: `MainViewModel.Batch.cs` содержит `NoGroupMarker`.

### 3. Версия 1.0.1.41

- `Workspace1C/Workspace1C.csproj` (строка 62): `Version/AssemblyVersion/FileVersion/
  InformationalVersion` → `1.0.1.41` (увеличивается только последнее число).
- `CHANGELOG.md`: запись `## [1.0.1.41] — 2026-11-06` с описанием исправления issue #113.
- `README.md`: обновить блок версии/последних изменений (по образцу предыдущих релизов).

### 4. Сборка

- Создать `_build_v100141.ps1` по образцу `_build_v100140.ps1`: сборка Windows
  (WPF publish) + Linux (`BuildLinux=true`, Avalonia) + упаковка (package/linux).

### 5. Публикация

- Коммит `Release 1.0.1.41: подменю «Для группы (N)» для узла «Без группы» (#113)`,
  push в `main`.
- `gh release create v1.0.1.41` с релиз-нотами и вложениями (Windows + Linux).

### 6. Комментарий в issue #113

- Комментарий от sivatorov: «Исправлено в v1.0.1.41» с описанием и просьбой проверить.
  Issue НЕ закрывать.

### 7. Telegram

- Пост о релизе в канал `-1004425443528` (механизм из docs/telegram-dev-bridge.md /
  существующие скрипты публикации).

## Порядок работ

1. Код + тесты + версия + CHANGELOG/README.
2. Сборка Windows + Linux, прогон юнит-тестов.
3. Коммит, push, релиз.
4. Комментарий в #113.
5. Telegram-пост.
