Исправлено в версии **1.0.1.5** (Windows и Linux):

Загрузка расширений конфигурации теперь учитывает «общую авторизацию» приложения:

- в [`OneCLauncher.BuildAuthArgument`](Workspace1C/Services/OneCLauncher.Arguments.Shared.cs) пакетные операции конфигуратора (установка/выгрузка расширений) резолвят учётные данные через полный резолвер с общими авторизациями — `CommonConfiguratorAuthorization` и legacy `CommonAuthorization` (приоритет прежний: собственная авторизация базы/режима → раздельная «Конфигуратора» → общая). База без собственного логина/пароля больше не падает с «The infobase user is not authenticated»;
- при ошибке аутентификации конфигуратора добавлен автоматический fallback на ibcmd-ветку: [`ConfigExtensionService.IsAuthenticationError`](Workspace1C/Services/ConfigExtensionService.cs) распознаёт «The infobase user is not authenticated», а [`ShouldFallbackToIbcmd`](Workspace1C/Services/ConfigExtensionService.cs) объединяет её с прежним признаком «command-line options».

Покрыто тестами [`CommonAuthLaunchArgsTests.cs`](Workspace1C.Tests/CommonAuthLaunchArgsTests.cs) (общая авторизация Конфигуратора/legacy в `BuildAuthArgument`, приоритет собственной) и [`ConfigExtensionServiceTests.cs`](Workspace1C.Tests/ConfigExtensionServiceTests.cs) (`IsAuthenticationError`, `ShouldFallbackToIbcmd`).

Спасибо за сообщение! Проверьте, пожалуйста, в новом релизе.

Релиз **1.0.1.5** (Windows и Linux) будет опубликован в ближайшее время.