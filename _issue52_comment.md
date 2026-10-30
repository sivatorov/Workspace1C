Исправлено в версии 1.0.0.66 (Windows и Linux): выбранная вкладка списка баз (Все/Избранное/Недавние/Запущенные) теперь автоматически запоминается и восстанавливается при следующем запуске приложения — отдельная галочка в настройках не требуется.

Добавлено поле `AppSettings.ListMode` ([`AppSettings.cs`](Workspace1C/Models/AppSettings.cs), [`ListViewMode.cs`](Workspace1C/Models/ListViewMode.cs)); режим хранится канонической строкой в `settings.json` (не зависит от локали). Старые флаги настроек `ShowFavoritesOnly`/`ShowRunningOnly` автоматически мигрируют в единый режим при загрузке (`AppSettings.NormalizeListMode`).

Реализовано на обеих платформах (WPF и Avalonia).

Добавлены юнит-тесты `AppSettingsListModeTests` (25 шт.).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.66