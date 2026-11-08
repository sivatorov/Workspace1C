# Аудит UI на стилистическое единство — Workspace 1C 1.0.1.10

Подзадача A из [plan-ui-audit-v100110.md](plan-ui-audit-v100110.md). Аудит без правок кода;
все правки ниже — конкретные указания для подзадачи B (режим code). Dual-фреймворк:
каждая правка выполняется в WPF (`*.xaml`/`*.cs`) и в Avalonia (`*.axaml`/`*.Avalonia.cs`).

---

## 1. Сводка по 5 аспектам

| # | Аспект | Нарушений | Из них: высокий приоритет |
|---|--------|-----------|---------------------------|
| 1 | Типографика | 9 | 2 |
| 2 | Цветовая палитра | 14 | 4 |
| 3 | Иерархия элементов | 7 | 1 |
| 4 | Иконки и символы | 6 | 2 |
| 5 | Тексты и локализация | 11 | 3 |
| | **Итого** | **47** | **12** |

**Что уже является нормой (ломать нельзя, зафиксировано тестами):**
- Базовая типографика: база 13pt, заголовок окна 15pt SemiBold, вторичный текст 12pt,
  подсказки 11pt — подтверждается большинством окон (MainWindow, CacheClean, ClearHistory,
  DetectConfigurations и др.).
- Кисти тем: `TextPrimaryBrush` / `TextSecondaryBrush` / `FormLabelBrush` / `CardBackgroundBrush`
  / `BorderColorBrush` / `AccentBrush` / `FavoriteTextBrush` — привязка через `DynamicResource`
  (WPF) и `ThemeBrushes.Bind` (Avalonia). Гарантии: [Issue61DarkPaletteAuditTests.cs](Workspace1C.Tests/Issue61DarkPaletteAuditTests.cs),
  [AccentColorIssue26Tests.cs](Workspace1C.Tests/AccentColorIssue26Tests.cs), [ButtonThemeBindingTests.cs](Workspace1C.Tests/ButtonThemeBindingTests.cs),
  [Issue63InputBackgroundBrushTests.cs](Workspace1C.Tests/Issue63InputBackgroundBrushTests.cs), [IconSetCoverageTests.cs](Workspace1C.Tests/IconSetCoverageTests.cs).
- Кнопки окна (titlebar) — через `WindowButtonStyle` / `ApplyWindowButtonTheme` (Issue26* тесты).
- Опасные кнопки — `DangerBrush`/`DangerTextBrush` (все 4 файла тем).
- Плашка «Избранное» #FEF3C7 + `FavoriteTextBrush` — осознанное решение issue #61, не трогать.

---

## 2. Реестр окон/модулей (объём аудита)

- **Главное окно**: [MainWindow.xaml](Workspace1C/Views/MainWindow.xaml) (~3200 строк) + Avalonia-версии
  (MainWindow.Avalonia*.cs, 6 partial-файлов) — дерево баз, панели, статус-бар, трей-оверлеи.
- **Окно настроек**: [SettingsWindow.xaml](Workspace1C/Views/SettingsWindow.xaml) + 12 partial-вкладок
  (WPF: Fonts/Display/Hotkeys/Language/Logging/Platforms/Profile/Schemes/Sync/Accounts/ItsAccounts)
  и Avalonia-аналоги.
- **Массовые операции**: CacheClean, ClearHistory, DetectConfigurations, FindLostBases, BatchOperations,
  MaintenanceCenter, UsageStatistics, ProcessInspector.
- **Сервер/кластеры**: ServerMonitor, ServerEdit, ClusterImport, ClusterMigration, CloneServerInfobase,
  CloneServerProgress, RacInfobase* (Create/Drop/DropRetry/Properties), PortsEdit, ServiceGroup*,
  Services, ServicesPicker, NetworkDiagnostics, ServerAlerts, ChainRetry, SessionLock.
- **Обновления/релизы**: PlatformUpdate, PlatformDownload, PlatformVersionPicker, PlatformDistributionPicker,
  PlatformOldVersions, PlatformChange, UpdateCheck, UpdateFileChoice, ActualReleases, ConfigUpdateLink,
  ConfigDiff* (Setup/Progress/Result), ConfigTypesEdit, ConfigTypeEdit, EditionEdit, ExportsList.
- **Репозиторий/техжурнал**: RepositoryBrowser, RepositoryBatchUpdate, RepositoryProgress,
  TechLogWindow, TechLogAnalysis, JobDetails, MetadataExplorer(+Progress), EventLog.
- **Скрипты/бэкапы/расписание**: ScriptScenarios, ScriptScenarioEdit, ScriptPick, ScriptTokenPicker,
  BackupScenarios, BackupScenarioEdit, ScheduledTasks, ScheduledTaskEdit.
- **Прочее**: SettingsSync, TelegramBot, WebPublish, TestDeploy, ScheduledJobs, SharedLists,
  ProfilesPanel/ProfilesWindow, ItsAccounts(+Edit), NotificationChannels, TrayMenuEditor(+GroupPick),
  UtilitiesMenuEditor(+GroupEdit), ContextMenuEditor(+GroupEdit), GroupEdit, GroupPicker, TagPick,
  AddEdit, BaseSelection, BaseNotes, DeleteInfobase, NameInput, LinkInput, Login, AppLock(+Unlock),
  ColorPicker, CommandPalette, ThemePreview, CustomActions(+Edit), PrivateBasesUnlock, DownloadConflict.

---

## 3. Таблица нарушений

Приоритет: **В** — высокий (виден пользователю постоянно/ломает тему), **С** — средний, **Н** — низкий/косметика.

### Аспект 1. Типографика (9)

| Модуль/окно | Файл:строка | Что не так | Приоритет | Конкретная правка |
|---|---|---|---|---|
| 1.1 Заголовки окон | [DeleteInfobaseWindow.xaml:40](Workspace1C/Views/DeleteInfobaseWindow.xaml:40), [DeleteInfobaseWindow.axaml:27](Workspace1C/Views/DeleteInfobaseWindow.axaml:27) | FontSize=16 при норме 15 | С | `FontSize="15"` в обоих файлах |
| 1.2 GroupPicker | [GroupPickerWindow.xaml:156](Workspace1C/Views/GroupPickerWindow.xaml:156) | Заголовок 17pt — вне шкалы | С | `FontSize="15"` |
| 1.3 GroupPicker | [GroupPickerWindow.xaml:158](Workspace1C/Views/GroupPickerWindow.xaml:158), [:266](Workspace1C/Views/GroupPickerWindow.xaml:266) | Дробный 12.5pt (2 места) | Н | `FontSize="12"` |
| 1.4 GroupPicker | [GroupPickerWindow.xaml:244](Workspace1C/Views/GroupPickerWindow.xaml:244) | Имя группы 14pt вне роли заголовка | Н | `FontSize="13"` |
| 1.5 Платформенные окна | [PlatformDistributionPickerWindow.xaml:31](Workspace1C/Views/PlatformDistributionPickerWindow.xaml:31), [PlatformOldVersionsWindow.xaml:29](Workspace1C/Views/PlatformOldVersionsWindow.xaml:29) | Заголовок окна 14pt вместо 15 | Н | `FontSize="15"` |
| 1.6 ConnectionSettings | [ConnectionSettingsWindow.xaml:296](Workspace1C/Views/ConnectionSettingsWindow.xaml:296) | Hero-заголовок 18pt — уникальный размер во всём приложении | Н | Либо `FontSize="15"`, либо сознательно зафиксировать 18 как отдельный стиль `WindowHeroTitle` в словаре |
| 1.7 FontWeight=Bold | [GroupPickerWindow.xaml:53](Workspace1C/Views/GroupPickerWindow.xaml:53), [MainWindow.xaml:1804](Workspace1C/Views/MainWindow.xaml:1804), [ProfilesPanel.xaml:69](Workspace1C/Views/ProfilesPanel.xaml:69), [SettingsWindow.xaml:906](Workspace1C/Views/SettingsWindow.xaml:906), [:1923](Workspace1C/Views/SettingsWindow.xaml:1923) | Жирный Bold там, где везде SemiBold (5 мест) | С | `FontWeight="SemiBold"` (планка Alt+N и «Активен» оставить SemiBold) |
| 1.8 Лог Consolas | [PlatformUpdateWindow.xaml:182](Workspace1C/Views/PlatformUpdateWindow.xaml:182) (11pt) vs [PlatformDownloadWindow.xaml:318](Workspace1C/Views/PlatformDownloadWindow.xaml:318), [BatchOperationsWindow.xaml:110](Workspace1C/Views/BatchOperationsWindow.xaml:110), [RepositoryBatchUpdateWindow.xaml:80](Workspace1C/Views/RepositoryBatchUpdateWindow.xaml:80), [JobDetailsWindow.xaml:27](Workspace1C/Views/JobDetailsWindow.xaml:27) (12pt) | Размер моноширинного лога разный (11 vs 12) | Н | Везде `FontFamily="Consolas" FontSize="12"`; вынести в стиль `MonoLogText` |
| 1.9 Шрифт окна | 49 окон: `FontFamily="Segoe UI"` в корне Window (см. список грепа) | Шрифт задан литералом, а не через пользовательскую настройку шрифта (`SettingsWindow.Fonts.cs`, `FontFamilyResolver`); при выборе пользователем нестандартного шрифта диалоги не следуют ему | В | Убрать `FontFamily="Segoe UI"` из корней окон; значение по умолчанию задать единожды в [App.xaml](Workspace1C/App.xaml)/`TextElement.FontFamily`, а при старте применять `FontSettings.FontFamily` ко всем окнам (унифицированный хелпер в `WindowChromeHelper`/`AvaloniaUi`) |

### Аспект 2. Цветовая палитра (14)

Греп-факты: в `Views/*.xaml` 48 вхождений красных хардкодов (#DC2626/#EF4444/#E53935/#B91C1C),
35 вхождений зелёных (#16A34A/#22C55E/#2E7D32); в темах существует только семантическая
`DangerBrush`, нет Success/Warning/Error/Info.

| Модуль/окно | Файл:строка | Что не так | Приоритет | Конкретная правка |
|---|---|---|---|---|
| 2.1 Нет семантических кистей | [DarkTheme.axaml:72](Workspace1C/Themes/DarkTheme.axaml:72), [LightTheme.axaml:73](Workspace1C/Themes/LightTheme.axaml:73) (+ .xaml-двойники) | Есть только Danger*; Success/Warning/Error/Info отсутствуют → 80+ хардкодов по окнам | В | Добавить в 4 файла тем: `SuccessBrush` (#16A34A), `WarningBrush` (#D97706), `ErrorBrush` (#DC2626), `InfoBrush` (#0EA5E9) + хук в [ThemeBrushes.Avalonia.cs](Workspace1C/Themes/ThemeBrushes.Avalonia.cs); заменить хардкоды на `DynamicResource` |
| 2.2 Разнобой ошибки | [LoginWindow.xaml:58](Workspace1C/Views/LoginWindow.xaml:58) (#E53935), [MaintenanceCenterWindow.xaml:101](Workspace1C/Views/MaintenanceCenterWindow.xaml:101), [:162](Workspace1C/Views/MaintenanceCenterWindow.xaml:162), [ProfilesPanel.xaml:140](Workspace1C/Views/ProfilesPanel.xaml:140) (#E53935) vs норма #DC2626 (15+ мест) | Три оттенка красного для ошибок | В | Все `#E53935` → `{DynamicResource ErrorBrush}`; [LoginWindow.Avalonia.cs:143](Workspace1C/Views/LoginWindow.Avalonia.cs:143) `Brushes.IndianRed` → `ThemeBrushes.Bind(…, "ErrorBrush")` |
| 2.3 Разнобой успеха | [MainWindow.xaml:608](Workspace1C/Views/MainWindow.xaml:608), [ConnectionSettingsWindow.xaml:1421](Workspace1C/Views/ConnectionSettingsWindow.xaml:1421) (#22C55E) vs #16A34A (12 мест), [ProfilesPanel.xaml:69](Workspace1C/Views/ProfilesPanel.xaml:69) (#2E7D32) | Три оттенка зелёного | В | Все → `{DynamicResource SuccessBrush}` |
| 2.4 DeleteInfobase плашка | [DeleteInfobaseWindow.xaml:90-102](Workspace1C/Views/DeleteInfobaseWindow.xaml:90) | Светлые фиксированные #FEF2F2/#FECACA/#B91C1C — в тёмной теме светлая плашка (и в .axaml:94 то же) | В | Новые кисти `DangerSoftBrush` (фон) / `DangerSoftBorderBrush` в 4 темах; текст → `ErrorBrush` |
| 2.5 MainWindow плашка закрепления | [MainWindow.xaml:2469-2473](Workspace1C/Views/MainWindow.xaml:2469) | #EDE9FE фон + #5B21B6 текст — фиксированные светлые, в тёмной теме ярко-светятся | С | Кисти `PinnedBadgeBrush`/`PinnedBadgeTextBrush` в темах (по образцу Favorite*, issue #61) |
| 2.6 GroupEdit иконки | [GroupEditWindow.xaml:51-66](Workspace1C/Views/GroupEditWindow.xaml:51), [GroupEditWindow.xaml.cs:262-279](Workspace1C/Views/GroupEditWindow.xaml.cs:262), [GroupEditWindow.Avalonia.cs:463-468](Workspace1C/Views/GroupEditWindow.Avalonia.cs:463) | Фон пикера иконок #374151/#4B5563 — тёмно-серый в светлой теме | С | Кисти `IconPickerBaseBrush`/`IconPickerHoverBrush` в темах |
| 2.7 MaintenanceCenter график (WPF) | [MaintenanceCenterWindow.xaml.cs:102-104](Workspace1C/Views/MaintenanceCenterWindow.xaml.cs:102) | #B0B0B0/#888888/#2D6CDF — фиксированные, Avalonia-версия уже на темах ([MaintenanceCenterWindow.Avalonia.cs:375+](Workspace1C/Views/MaintenanceCenterWindow.Avalonia.cs:375)) | С | WPF: `TextSecondaryBrush` для линии/подписей, `AccentBrush` для столбцов — паритет с Avalonia |
| 2.8 Серые текты вне темы | [ServerMonitorWindow.xaml:550](Workspace1C/Views/ServerMonitorWindow.xaml:550) (#64748B), [FindLostBasesWindow.xaml:216](Workspace1C/Views/FindLostBasesWindow.xaml:216), [ProcessInspectorWindow.xaml:103](Workspace1C/Views/ProcessInspectorWindow.xaml:103) (#94A3B8), [ServicesWindow.xaml:56](Workspace1C/Views/ServicesWindow.xaml:56) (#6B7280) | Фиксированные серые вместо `TextSecondaryBrush` (контраст страдает в тёмной теме) | С | → `{DynamicResource TextSecondaryBrush}` |
| 2.9 Avalonia code-behind | [ClusterImportWindow.Avalonia.cs:340](Workspace1C/Views/ClusterImportWindow.Avalonia.cs:340) `Color.Parse("#DC2626")`; [ExportsListWindow.Avalonia.cs:107-114](Workspace1C/Views/ExportsListWindow.Avalonia.cs:107) `Brushes.Gray`; [DetectConfigurationsWindow.Avalonia.cs:402](Workspace1C/Views/DetectConfigurationsWindow.Avalonia.cs:402), [FindLostBasesWindow.Avalonia.cs:553](Workspace1C/Views/FindLostBasesWindow.Avalonia.cs:553) `Brushes.Gray` разделители | Хардкод в code-behind вне ThemeBrushes | С | → `ThemeBrushes.Bind(…, "ErrorBrush")` / `"BorderColorBrush"` / `"TextSecondaryBrush"` |
| 2.10 Amber статусы | [ConfigUpdateLinkWindow.xaml:138](Workspace1C/Views/ConfigUpdateLinkWindow.xaml:138) #16A34A результат; [RepositoryBrowserWindow.xaml:271](Workspace1C/Views/RepositoryBrowserWindow.xaml:271), [ServerMonitorWindow.xaml:768](Workspace1C/Views/ServerMonitorWindow.xaml:768) #D97706 предупреждения; [ConfigTypeEditWindow.xaml:100](Workspace1C/Views/ConfigTypeEditWindow.xaml:100) #F59E0B | Amber-семейство в 3 оттенках (#D97706/#F59E0B/#FBBF24) без кистей | С | → `WarningBrush` (текст/иконки), `FavoriteBrush` для звезды-избранного |
| 2.11 Полупрозрачные белые на акценте | [MainWindow.xaml:1259](Workspace1C/Views/MainWindow.xaml:1259) #22F1F5F9, [:2656-2659](Workspace1C/Views/MainWindow.xaml:2656) #22FFFFFF/#33FFFFFF, [:2673](Workspace1C/Views/MainWindow.xaml:2673) #55FFFFFF, [:3111](Workspace1C/Views/MainWindow.xaml:3111), [:3132](Workspace1C/Views/MainWindow.xaml:3132) #99000000, [:3156](Workspace1C/Views/MainWindow.xaml:3156) #C0101018 | Оверлеи/ховеры на литералах — при кастомном акценте выглядят инородно | Н | Кисти `AccentOverlayBrush`, `OverlayScrimBrush` в темах; допустимо отложить |
| 2.12 Иконки типа элемента | [PlatformVersionPickerWindow.xaml:201-228](Workspace1C/Views/PlatformVersionPickerWindow.xaml:201), [SettingsWindow.xaml:2073-2095](Workspace1C/Views/SettingsWindow.xaml:2073) | Дублированные DataTrigger-палитры (Folder/amber, Cube/green…) в 2+ местах | Н | Вынести в общий стиль `ElementKindIconStyle` в [Controls.axaml](Workspace1C/Themes/Controls.axaml) |
| 2.13 Синие стрелки сортировки | [ServiceGroupEditWindow.xaml:85-122](Workspace1C/Views/ServiceGroupEditWindow.xaml:85), [ServicesWindow.xaml:114-181](Workspace1C/Views/ServicesWindow.xaml:114) | #2563EB (уникальный синий, не AccentBrush) для ▲▼◀▶ | Н | → `{DynamicResource AccentBrush}` |
| 2.14 Контрастные хвосты | [ClusterImportWindow.axaml:71](Workspace1C/Views/ClusterImportWindow.axaml:71), [ClusterMigrationWindow.axaml:53](Workspace1C/Views/ClusterMigrationWindow.axaml:53) #B45309 на вторичном тексте; [MetadataExplorerWindow.xaml:172](Workspace1C/Views/MetadataExplorerWindow.xaml:172) #DC2626 рядом со `ErrorMessageStyle` | Часть текстов уже со стилем, часть с хардкодом — в одном окне вперемешку | Н | Привести к `ErrorBrush`/`WarningBrush`; в Avalonia использовать `Opacity`-паттерн окна |

### Аспект 3. Иерархия элементов (7)

| Модуль/окно | Файл:строка | Что не так | Приоритет | Конкретная правка |
|---|---|---|---|---|
| 3.1 Шкала CornerRadius | 116 вхождений: 3/4/5/6/8/10/11/12 | Нет фиксированной шкалы: 12 — поиск/панель ([MainWindow.xaml:393](Workspace1C/Views/MainWindow.xaml:393), [:2482](Workspace1C/Views/MainWindow.xaml:2482)); 5 — [MainWindow.xaml:2237](Workspace1C/Views/MainWindow.xaml:2237), [SettingsWindow.xaml:1439](Workspace1C/Views/SettingsWindow.xaml:1439); 3 — [MainWindow.xaml:1773](Workspace1C/Views/MainWindow.xaml:1773), [ConnectionReplaceWindow.xaml:155](Workspace1C/Views/ConnectionReplaceWindow.xaml:155) | В | Принять шкалу токенов: **4** (чипы/мелочь), **6** (контейнеры по умолчанию), **8** (карточки/крупные плашки), **10** (pills/кнопки запуска). Привести выбросы: 12→10, 5→4 или 6, 3→4 |
| 3.2 Внешние отступы диалогов | [RacInfobaseCreateWindow.xaml:28](Workspace1C/Views/RacInfobaseCreateWindow.xaml:28), [ServerEditWindow.xaml:26](Workspace1C/Views/ServerEditWindow.xaml:26), [AppLockWindow.xaml:28](Workspace1C/Views/AppLockWindow.xaml:28), [PrivateBasesUnlockWindow.xaml:27](Workspace1C/Views/PrivateBasesUnlockWindow.xaml:27) — `Margin="16"` vs `DockPanel Margin="14"` в [ExportsListWindow.xaml:7](Workspace1C/Views/ExportsListWindow.xaml:7), [BatchOperationsWindow.xaml](Workspace1C/Views/BatchOperationsWindow.xaml) и др. | Отступ каркаса 14 vs 16 | С | Единый `WindowContentMargin=14` (или 16) — применить ко всем диалогам |
| 3.3 Нижние панели кнопок | [BackupScenariosWindow.xaml:27](Workspace1C/Views/BackupScenariosWindow.xaml:27), [ScriptScenariosWindow.xaml:27](Workspace1C/Views/ScriptScenariosWindow.xaml:27), [CustomActionsWindow.xaml:33](Workspace1C/Views/CustomActionsWindow.xaml:33), [ScheduledTasksWindow.xaml:49](Workspace1C/Views/ScheduledTasksWindow.xaml:49), [ExportsListWindow.xaml:8](Workspace1C/Views/ExportsListWindow.xaml:8) | Кнопки с фиксированной шириной (90/92/96/100/110/130) и Padding 6,4; в других окнах — ModernButton/auto-width | С | Заменить на общий стиль `DialogFooterButton` (MinWidth=96, Padding=12,5), убрать жёсткие Width |
| 3.4 Отступ заголовка | Заголовки 15pt: `Margin="0,0,0,12"` ([CloneServerInfobaseWindow.xaml:23](Workspace1C/Views/CloneServerInfobaseWindow.xaml:23), [ServerEditWindow.xaml:27](Workspace1C/Views/ServerEditWindow.xaml:27), [RacInfobaseCreateWindow.xaml:29](Workspace1C/Views/RacInfobaseCreateWindow.xaml:29)) vs `Margin="0,6,0,0"`+отдельный subtitle ([CacheCleanWindow.xaml:110](Workspace1C/Views/CacheCleanWindow.xaml:110), [ClearHistoryWindow.xaml:61](Workspace1C/Views/ClearHistoryWindow.xaml:61)) | Два паттерна шапки диалога | Н | Выбрать паттерн «заголовок + subtitle c Margin 0,6,0,0» и применить везде |
| 3.5 Чекбокс-плашки Settings | [SettingsWindow.xaml:786](Workspace1C/Views/SettingsWindow.xaml:786) CornerRadius=8 Padding 8,6 vs [ConnectionSettingsWindow.xaml:399](Workspace1C/Views/ConnectionSettingsWindow.xaml:399) CornerRadius=10 | Одинаковые «плашки-настройки» с разным радиусом | Н | Единый радиус 8 по шкале 3.1 |
| 3.6 Тени | `DropShadowEffect` в разметке окон не найден; тени только программно (`UiMetrics.AddSoftShadow`) — ок | — | — | Норма; держать тени только через `UiMetrics` |
| 3.7 Fixed-высоты кнопок панели инструментов | [ServicesWindow.xaml:113-181](Workspace1C/Views/ServicesWindow.xaml:113), [ServerMonitorWindow.xaml:195-331](Workspace1C/Views/ServerMonitorWindow.xaml:195) | Текстовые глиф-кнопки без единого размера (Padding/Width местами отсутствуют) | Н | Единый стиль `GlyphToolButton` (Width/Height=30, Content centered) |

### Аспект 4. Иконки и символы (6)

| Модуль/окно | Файл:строка | Что не так | Приоритет | Конкретная правка |
|---|---|---|---|---|
| 4.1 Эмодзи вместо IconSet в кнопках диалогов | [NetworkDiagnosticsWindow.xaml:62-98](Workspace1C/Views/NetworkDiagnosticsWindow.xaml:62) (🔎🔌🗄️⟳✖), [RepositoryBrowserWindow.xaml:78-150](Workspace1C/Views/RepositoryBrowserWindow.xaml:78) (🔗⟳⇄⇅⇩✖🔒🔓), [ClusterImportWindow.xaml:90-218](Workspace1C/Views/ClusterImportWindow.xaml:90), [ClusterMigrationWindow.xaml:90-378](Workspace1C/Views/ClusterMigrationWindow.xaml:90), [ConnectionReplaceWindow.xaml:115-207](Workspace1C/Views/ConnectionReplaceWindow.xaml:115), [MaintenanceCenterWindow.xaml:204-223](Workspace1C/Views/MaintenanceCenterWindow.xaml:204) | Эмодзи/глифы рендерятся цветным шрифтом системы, вне 6 наборов IconSet (Fluent/Glass/MacOS/Material/Outline/WindowsXp) | В | Заменить эмодзи на `IconHelper.MakeIcon(…)`/`{DynamicResource Icon*}` из [Icons.axaml](Workspace1C/Themes/Icons.axaml) (уже есть IconLink, IconSync, IconClose и т.п.; при отсутствии — добавить ключ в все 6 наборов, чтобы пройти IconSetCoverageTests) |
| 4.2 ✔/✖ в кнопках ОК/Отмена | [NameInputWindow.xaml:50-59](Workspace1C/Views/NameInputWindow.xaml:50), [BaseSelectionWindow.xaml:66-73](Workspace1C/Views/BaseSelectionWindow.xaml:66), [ColorPickerWindow.xaml:32-38](Workspace1C/Views/ColorPickerWindow.xaml:32), [AppLockWindow.xaml:45-51](Workspace1C/Views/AppLockWindow.xaml:45), [LoginWindow.xaml:69-78](Workspace1C/Views/LoginWindow.xaml:69), [TagPickWindow.xaml:64-88](Workspace1C/Views/TagPickWindow.xaml:64), [ServicesPickerWindow.xaml:25-32](Workspace1C/Views/ServicesPickerWindow.xaml:25) и ещё ~10 окон | Текстовые символы ✔/✖ (галочка зависит от шрифта; в MacOS-наборе нестилизуемо) | С | Единые ключи `IconCheck`/`IconClose` из IconSet; либо минимально — единый цвет: ✔ `SuccessBrush`, ✖ `TextSecondaryBrush` |
| 4.3 Смешение PackIcon и IconSet | MainWindow/Settings используют `materialDesign:PackIcon` (~60 мест), дерево/тулбар — IconSet (`{DynamicResource Icon*}`) | Два параллельных механизма иконок | С | Для подзадачи B: PackIcon оставить только там, где нет аналога в IconSet; перечень для миграции составить по грепу `materialDesign:PackIcon` |
| 4.4 Языкозависимые глифы сортировки | [ServerMonitorWindow.xaml:318-323](Workspace1C/Views/ServerMonitorWindow.xaml:318) | «А↓»/«Я↓» — русские буквы в кнопке, в английской локализации выглядят чуждо | С | Заменить на «A↓/Z↓»-нейтральные `⇅` или иконки IconSortAsc/Desc (аналог ▲▼ из [ServerMonitorWindow.xaml:207](Workspace1C/Views/ServerMonitorWindow.xaml:207)) |
| 4.5 Размеры иконок | PackIcon Width/Height: 12/14/15/16/18/22/28 по окнам | Нет шкалы размеров | Н | Шкала: 14 (внутри кнопок), 16 (standalone кнопки/меню), 18 (тулбар), 22 (hero-блоки). Чинить точечно при правках 4.1 |
| 4.6 ⏹/▶/⇲ в MaintenanceCenter | [MaintenanceCenterWindow.xaml:204-213](Workspace1C/Views/MaintenanceCenterWindow.xaml:204), [BatchOperationsWindow.xaml:127-144](Workspace1C/Views/BatchOperationsWindow.xaml:127) | Символы блоков/стрелок вперемешку с PackIcon в соседних кнопках | Н | → `IconPlay`/`IconStop`/`IconDownload` из IconSet |

### Аспект 5. Тексты и локализация (11)

| Модуль/окно | Файл:строка | Что не так | Приоритет | Конкретная правка |
|---|---|---|---|---|
| 5.1 ScheduledTaskEditWindow | [ScheduledTaskEditWindow.xaml:63-157](Workspace1C/Views/ScheduledTaskEditWindow.xaml:63) | ~20 русских литералов («Наименование», «Включено», «Дни недели», «Пн…Вс», «Обзор…», «Сценарии…», «OK», «Отмена») | В | Ключи `Scheduled.*` в ru.json+en.json; Content → `{loc:Loc}` |
| 5.2 BackupScenarioEditWindow | [BackupScenarioEditWindow.xaml:54-112](Workspace1C/Views/BackupScenarioEditWindow.xaml:54) | ~18 литералов («Шаблон имени файла», «Ротация», «Учётные данные»…) | В | Ключи `BackupScenario.*` |
| 5.3 ScriptScenarioEditWindow | [ScriptScenarioEditWindow.xaml:28-124](Workspace1C/Views/ScriptScenarioEditWindow.xaml:28) | ~15 литералов | В | Ключи `Script.Edit.*` |
| 5.4 BackupScenariosWindow / ScriptScenariosWindow / CustomActionsWindow / ExportsListWindow / ScheduledTasksWindow | [:27-33](Workspace1C/Views/BackupScenariosWindow.xaml:27), [:27-32](Workspace1C/Views/ScriptScenariosWindow.xaml:27), [:33-37](Workspace1C/Views/CustomActionsWindow.xaml:33), [:9-16](Workspace1C/Views/ExportsListWindow.xaml:9), [:49-55](Workspace1C/Views/ScheduledTasksWindow.xaml:49) | Кнопки футера («Выполнить», «Добавить», «Изменить», «Скопировать», «Удалить», «Обновить», «Восстановить», «Открыть папку», «Закрыть») литералами — англ. пользователь видит русский | В | Использовать существующие `Common.Add/Common.Edit/Common.Delete/Common.Close` + новые `Common.Run/Common.Copy/Common.Refresh/Common.Restore/Common.OpenFolder` |
| 5.5 CustomActionEditWindow | [CustomActionEditWindow.xaml:32-95](Workspace1C/Views/CustomActionEditWindow.xaml:32) | ~15 литералов | С | Ключи `CustomAction.Edit.*` |
| 5.6 ScriptPickWindow | [ScriptPickWindow.xaml:26-59](Workspace1C/Views/ScriptPickWindow.xaml:26) | «Выполнить/Изменить/Закрыть», «Командная строка:» | С | `Common.*` + `Script.CommandLinePreview` (ключ уже есть — Avalonia-версия использует его, [ScriptPickWindow.Avalonia.cs:132](Workspace1C/Views/ScriptPickWindow.Avalonia.cs:132)) |
| 5.7 Avalonia-двойники тех же окон | BackupScenarioEditWindow.Avalonia.cs, ScriptScenarioEditWindow.Avalonia.cs, ScheduledTaskEditWindow.Avalonia.cs и др. | Проверить: если Avalonia-версии строятся из `LocalizationManager.T` — правки только в WPF; если тоже литералы — синхронно | С | Греп `LocalizationManager.T\("BackupScenario` в Avalonia-файлах при внедрении |
| 5.8 Многоточия диалоговых команд | «Обзор…»/«Сценарии…»/«Добавить…» — есть в [ScriptScenarioEditWindow.xaml:46](Workspace1C/Views/ScriptScenarioEditWindow.xaml:46), [ScheduledTaskEditWindow.xaml:94](Workspace1C/Views/ScheduledTaskEditWindow.xaml:94), [BackupScenarioEditWindow.xaml:82](Workspace1C/Views/BackupScenarioEditWindow.xaml:82); при этом «Добавить» без … открывает редактор в [BackupScenariosWindow.xaml:29](Workspace1C/Views/BackupScenariosWindow.xaml:29) | Непоследовательное многоточие у команд, открывающих диалог | С | Правило: «…» у команд, открывающих новое окно/диалог. См. раздел 5 (Примеры формулировок) |
| 5.9 ru.json/en.json | [ru.json](Workspace1C/Localization/Languages/ru.json) (4413 строк) vs [en.json](Workspace1C/Localization/Languages/en.json) (4414 строк) | Разница в 1 строке — вероятно, хвостовая; синхронизацияguarded тестами [LocalizationCoverageServicesTests.cs](Workspace1C.Tests/LocalizationCoverageServicesTests.cs); новые ключи из 5.1–5.6 добавить в оба файла | С | После добавления ~70 новых ключей прогнать тест покрытия локализации |
| 5.10 Высокий тон заголовков | [CacheCleanWindow.xaml:110](Workspace1C/Views/CacheCleanWindow.xaml:110), [ClearHistoryWindow.xaml:61](Workspace1C/Views/ClearHistoryWindow.xaml:61), [DetectConfigurationsWindow.xaml:70](Workspace1C/Views/DetectConfigurationsWindow.xaml:70) | Окна используют `TitleHeader`-ключи как заголовок контента при пустом Title окна — ок, но заголовки в инфинитиве («Очистка кэша…» vs «Очистить кэш») — проверить единый стиль «имя существительное» | Н | Правило: заголовок окна — существительное («Очистка кэша»), кнопка — глагол («Очистить») |
| 5.11 Символ-разделители | [ConfigDiffResultWindow.xaml:63](Workspace1C/Views/ConfigDiffResultWindow.xaml:63) « · », [CustomActionEditWindow.xaml:86](Workspace1C/Views/CustomActionEditWindow.xaml:86), [ScriptScenarioEditWindow.xaml:108](Workspace1C/Views/ScriptScenarioEditWindow.xaml:108) « — » | Разные тире/разделители в композитных строках | Н | Единый разделитель «—» (en dash с пробелами) в шаблонах строк локализации |

---

## 4. Топ-10 приоритетных правок

1. **Семантические кисти Success/Warning/Error/Info** в 4 файлах тем ([DarkTheme.axaml](Workspace1C/Themes/DarkTheme.axaml), [LightTheme.axaml](Workspace1C/Themes/LightTheme.axaml) + .xaml-двойники, [ThemeBrushes.Avalonia.cs](Workspace1C/Themes/ThemeBrushes.Avalonia.cs)) и замена ~80 хардкод-вхождений #DC2626/#EF4444/#16A34A/#22C55E/#D97706/#0EA5E9 на них (2.1–2.3, 2.10).
2. **Локализация 10 окон с русскими литералами** (ScheduledTaskEdit, BackupScenarioEdit, ScriptScenarioEdit, BackupScenarios, ScriptScenarios, CustomActions, CustomActionEdit, ExportsList, ScheduledTasks, ScriptPick) — ~70 новых ключей в ru.json+en.json (5.1–5.6).
3. **Эмодзи → IconSet** в кнопках диалогов (NetworkDiagnostics, RepositoryBrowser, ClusterImport/Migration, ConnectionReplace, MaintenanceCenter, BatchOperations) (4.1, 4.6).
4. **Единый красный/зелёный**: вычистить #E53935, #2E7D32, #B91C1C, `Brushes.IndianRed`, `Brushes.Gray` (2.2, 2.3, 2.9).
5. **Тематизация DeleteInfobase** (плашка физического удаления) и MainWindow-плашки закрепления через новые мягкие кисти (2.4, 2.5).
6. **Унификация заголовков**: FontSize 15 SemiBold везде (16/17/14/18 выбросы), Bold→SemiBold (1.1–1.7).
7. **Шкала CornerRadius 4/6/8/10** и правка выбросов 3/5/11/12 (3.1, 3.5).
8. **Общий стиль футера диалогов** `DialogFooterButton` + единый `WindowContentMargin` (3.2, 3.3).
9. **Следование пользовательскому шрифту**: убрать 49 литералов `FontFamily="Segoe UI"`, применять `FontSettings.FontFamily` глобально (1.9).
10. **Паритет WPF/Avalonia MaintenanceCenter**: график на кистях темы (2.7); заодно `TextSecondaryBrush` вместо серых #64748B/#94A3B8/#6B7280 (2.8).

---

## 5. Примеры формулировок (единый тон)

**Правила:**
- Кнопка — глагол в инфинитиве («Очистить», «Скачать»), заголовок окна — существительное («Очистка кэша»).
- «…» (многоточие) — только у команд, открывающих диалог/окно выбора: «Обзор…», «Добавить…», «Сценарии…». Не ставить у команд, выполняющих действие сразу: «Добавить» (добавляет строку), «Выполнить», «Закрыть».
- Подтверждение/закрытие: «ОК» (ключ `Common.Ok`), «Отмена», «Закрыть» — не смешивать «Готово»/«ОК» в одинаковых диалогах.
- Ошибки: начинаться с сути, без «Ошибка:» префикса, оканчиваться точкой без восклицаний.

| Контекст | Сейчас | Единая формулировка (ru) | Ключ |
|---|---|---|---|
| Добавление записи в список | «Добавить» (открывает редактор) | «Добавить…» | `Common.AddEllipsis` |
| Обзор файла/папки | «Обзор…» | «Обзор…» | `Common.Browse` (новый) |
| Копирование задачи | «Скопировать» | «Создать копию» | `Common.Duplicate` (новый) |
| Запуск действия | «Выполнить» / «Выполнить сейчас» | «Выполнить» / «Выполнить сейчас» (сохранить) | `Common.Run` / `Scheduled.RunNow` |
| Ошибка подключения | (варьируется по окнам) | «Не удалось подключиться к {0}. Проверьте адрес и порт.» | `Error.ConnectFailed` (новый) |
| Ошибка скачивания | | «Не удалось скачать {0}. {1}» | `Error.DownloadFailed` (новый) |
| Подсказка пустого списка | (варьируется) | «Список пуст. Нажмите „Добавить“, чтобы создать первую запись.» | `Common.EmptyListHint` (новый) |
| Удаление с последствиями | «Удалить старые копии» | «Удалить старые копии…» (открывает подтверждение) | контекстные ключи |
| Локализация сортировки | «А↓» / «Я↓» | «Сортировка по возрастанию/убыванию» (tooltip) + иконка ⇅ | `Common.SortAsc` / `Common.SortDesc` |

Все новые ключи — в оба файла [ru.json](Workspace1C/Localization/Languages/ru.json) и [en.json](Workspace1C/Localization/Languages/en.json) (тест `LocalizationCoverageServicesTests`).

---

## 6. Отложенные/низкоприоритетные пункты (можно не делать в 1.0.1.10)

- 2.11 (полупрозрачные оверлеи MainWindow) — работает корректно при стандартных акцентах.
- 2.12 (вынос DataTrigger-палитр иконок в общий стиль) — рефакторинг без визуального эффекта.
- 1.6 (hero-заголовок 18pt в ConnectionSettings) — допустимо зафиксировать как отдельный стиль.
- Полная миграция `materialDesign:PackIcon` → IconSet (4.3) — большой объём; в 1.0.1.10 достаточно единообразия в рамках одного окна.
