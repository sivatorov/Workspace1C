Исправлено в версии 1.0.0.69 (Windows и Linux): общая авторизация стала раздельной по режимам запуска — отдельные общие учётные данные для «1С:Предприятие» и для «Конфигуратор».

В окне настроек блок «Общая авторизация» разбит на два подблока, в каждом свой режим аутентификации (запрос / автоматический вход / аутентификация ОС), логин и пароль ([`SettingsWindow.xaml`](Workspace1C/Views/SettingsWindow.xaml), [`SettingsWindow.Avalonia.cs`](Workspace1C/Views/SettingsWindow.Avalonia.cs)).

Цепочка выбора для базы без собственной авторизации: своя авторизация режима → авторизация базы → раздельная общая для режима (`CommonEnterpriseAuth` для «1С:Предприятия», `CommonConfiguratorAuth` для Конфигуратора) → прежняя общая `CommonAuthorization` (для обоих режимов, обратная совместимость со старыми `settings.json`) → запрос платформы ([`InfobaseAuthResolver.cs`](Workspace1C/Services/InfobaseAuthResolver.cs)).

Раздельные общие учитываются при запуске 1С ([`OneCLauncher.Arguments.Shared.cs`](Workspace1C/Services/OneCLauncher.Arguments.Shared.cs)) и при построении COM-строки подключения ([`OneCComConnector.cs`](Workspace1C/Services/OneCComConnector.cs)); настройки хранятся в `settings.json` ([`AppSettings.cs`](Workspace1C/Models/AppSettings.cs)).

Добавлены юнит-тесты `CommonAuthResolverTests`, `CommonAuthLaunchArgsTests`, `CommonAuthComTests` и новый `CommonAuthSettingsStorageTests` (миграция старого `settings.json`).

Реализовано на обеих платформах.
