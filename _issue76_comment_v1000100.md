Исправлено в версии 1.0.0.100 (Windows и Linux):

1. **Строка таблицы в «Миграции между кластерами 1С» больше не светлая в тёмной
   теме.** По вашему скриншоту: белой оставалась выбранная строка таблицы баз
   (#E9EBE9 — системная кисть выделения неактивного окна). Причина: локальные
   `DataGrid.CellStyle` окна (вертикаль по центру, рамка, отступы) перекрывали
   тематический стиль ячейки БЕЗ `BasedOn` — терялся триггер темы
   (`IsSelected → Transparent`), и строка красилась системными кистями. Теперь
   у таблицы миграции: явные `RowBackground`/`AlternatingRowBackground`/`Background` —
   `CardBackgroundBrush`, `AlternationCount=2`, явный `DataGrid.RowStyle` с триггерами
   наведения/выделения на `ItemHoverBrush`/`ItemSelectedBrush`, а `DataGrid.CellStyle`
   наследует тематический стиль через `BasedOn`.

2. **Аудит всех форм «только реально найти все».** Локальные `DataGrid.CellStyle`
   в 12 окнах (всего 18 блоков: `ServerMonitorWindow` ×6, `RepositoryBrowserWindow`
   ×2, `MaintenanceCenterWindow`, `ItsAccountsWindow`, `ActualReleasesWindow`,
   `ConfigTypesEditWindow`, `PlatformUpdateWindow`, `DetectConfigurationsWindow`,
   `ProcessInspectorWindow`, `ConnectionReplaceWindow`, `ClusterImportWindow`,
   `UsageStatisticsWindow`) переведены на `BasedOn="{StaticResource {x:Type DataGridCell}}"`
   — тематические триггеры выделения/наведения сохраняются во всех таблицах.
   Проверка на жёсткие светлые фоны строк (`White`, `#FFFFFF`, `Snow` и т.п.)
   в разметке окон показала: таких мест не осталось (кроме намеренных цветовых
   плашек «Предупреждение»/«Избранное»).

3. **Linux/Avalonia** — чеклист баз окна миграции (ListBox) получил явный фон
   `CardBackgroundBrush` ([`ClusterMigrationWindow.axaml`](Workspace1C/Views/ClusterMigrationWindow.axaml)).

Покрыто тестами-стражами [`Issue76RowBackgroundTests.cs`](Workspace1C.Tests/Issue76RowBackgroundTests.cs) (+6).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.100
