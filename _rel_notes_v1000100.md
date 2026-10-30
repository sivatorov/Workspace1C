# Workspace 1C 1.0.0.100

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.100

### Исправлено

- **Цвета фона строк таблиц в тёмной теме (issue #76, 7OH)** — в окне «Миграция
  между кластерами 1С» строка таблицы баз оставалась светло-серой (#E9EBE9) вместо
  тёмной кисти схемы; по замечанию «только реально найти все» проверены и другие
  формы с таблицами.

  **Причина:** локальные `DataGrid.CellStyle` окон (вертикаль по центру, рамка,
  отступы) задавались БЕЗ `BasedOn` на тематический стиль `DataGridCell` — терялись
  триггеры темы (`IsSelected → Transparent`), и выбранная строка красилась системной
  кистью выделения неактивного окна (`SystemColors.InactiveSelectionHighlightBrush`).

  **Исправление:**
  1. Окно миграции: явные `RowBackground`/`AlternatingRowBackground`/`Background` —
     кисть `CardBackgroundBrush`, `AlternationCount=2`, явный `DataGrid.RowStyle`
     с триггерами наведения/выделения на `ItemHoverBrush`/`ItemSelectedBrush`,
     `DataGrid.CellStyle` — `BasedOn` на тематический стиль
     ([`ClusterMigrationWindow.xaml`](Workspace1C/Views/ClusterMigrationWindow.xaml)).
  2. Аудит всех форм: локальным `DataGrid.CellStyle` в 12 окнах (18 блоков:
     `ServerMonitorWindow` ×6, `RepositoryBrowserWindow` ×2, `MaintenanceCenterWindow`,
     `ItsAccountsWindow`, `ActualReleasesWindow`, `ConfigTypesEditWindow`,
     `PlatformUpdateWindow`, `DetectConfigurationsWindow`, `ProcessInspectorWindow`,
     `ConnectionReplaceWindow`, `ClusterImportWindow`, `UsageStatisticsWindow`) добавлен
     `BasedOn="{StaticResource {x:Type DataGridCell}}"` — тематические триггеры
     выделения/наведения сохранены во всех таблицах.
  3. Linux/Avalonia: чеклист баз окна миграции (`ListBox`) получил явный фон
     `CardBackgroundBrush` ([`ClusterMigrationWindow.axaml`](Workspace1C/Views/ClusterMigrationWindow.axaml)).
  4. Проверка на жёсткие светлые фоны строк (`White`, `#FFF`, `#FFFFFF`, `Snow`,
     `AliceBlue` и т.п.) в разметке окон — их не осталось; намеренные цветовые плашки
     «Предупреждение»/«Избранное» сохранены.

### Тесты

- [`Issue76RowBackgroundTests.cs`](Workspace1C.Tests/Issue76RowBackgroundTests.cs):
  +6 — запрет жёстких светлых фонов строк в разметке окон; локальные
  `DataGrid.CellStyle` обязаны наследовать тематический стиль через `BasedOn`; окно
  миграции использует тематические кисти строк (WPF) и тематический фон списка
  (Avalonia); стили `DataGridRow`/`DataGridCell` тем — только кисти схемы без
  системных `Highlight`/`HotTrack`.

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).
