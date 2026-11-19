# Workspace 1C 1.0.1.91

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).

## Исправлено

- **Единый стиль опасных действий во всех окнах (аудит UI, пункт P2-1).**
  Раньше `DangerButton` (красная заливка, белый текст) применялся только в семи
  окнах, а в списках сценариев, действий, программ, параметров, расписаний и
  настроек кнопка «Удалить» выглядела обычной или вторичной. Теперь удаление
  везде оформлено единой опасной кнопкой:
  - `BackupScenarioEditWindow`, `BackupScenariosWindow`;
  - `CustomActionsWindow`, `CustomProgramsWindow`;
  - `LaunchParametersWindow`;
  - `ScheduledTasksWindow`, `ScriptScenariosWindow`;
  - `SettingsWindow` (удаление темы, пути платформы, каталога шаблонов,
    «Удалить отсутствующие базы», «Очистить список баз»).
- В `LaunchParametersWindow` убрана «псевдо-опасная» кнопка: красная иконка
  больше не вставляется внутрь `SecondaryButton`. Кнопка «Удалить» стала
  `DangerButton`, цвет задаёт сам стиль, значок приведён к 16 px (было 15).
- «Очистить историю» (`ClearHistoryWindow`) уже была `DangerButton` и не
  менялась. «Остановить» в окнах поиска и диагностики оставлена красной:
  остановка процесса необратима.
- Изменения симметричны в WPF и Avalonia (`*.Avalonia.cs`). Строки групп,
  навигация, правая панель, отступы, размеры иконок в других местах и остальные
  стили кнопок не затрагивались.

## Известные ограничения

- Удаление без отдельного диалога подтверждения (в этой задаче новый диалог не
  добавлялся): `BackupScenarioEditWindow` (удаление каталога из сценария),
  `LaunchParametersWindow` (удаление пользовательского параметра),
  `CustomProgramsWindow` (удаление программы), `SettingsWindow` (удаление пути
  платформы и каталога шаблонов). Подтверждение есть в `BackupScenariosWindow`,
  `CustomActionsWindow`, `ScheduledTasksWindow`, `ScriptScenariosWindow`,
  `SettingsWindow` (удаление темы, «Удалить отсутствующие», «Очистить список баз»).

## Тесты

- Добавлен `AuditP21DangerButtonTests`:
  - в 8 XAML-окнах кнопка удаления использует `DangerButton`;
  - в 8 `*.Avalonia.cs` — `ControlThemes.DangerButton`;
  - ни в одном из файлов нет `Foreground="{DynamicResource ErrorBrush}"` внутри
    `SecondaryButton` для удаления;
  - значок удаления в `LaunchParametersWindow` — 16 px.
- `ButtonThemeBindingTests.LaunchParametersWindow_AddAndRemoveButtons_AreThemed`
  обновлён: «Удалить» — `DangerButton`, «Добавить» — `SecondaryButton`.
- Всего тестов: **7399**, все зелёные.

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).
