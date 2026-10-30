# Workspace 1C 1.0.1.5

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.5

### Исправлено / Добавлено

- **Загрузка расширений конфигурации учитывает «общую авторизацию» приложения
  (issue #87, Windows и Linux).** Пакетные операции конфигуратора (установка/
  выгрузка расширений) резолвят учётные данные через полный резолвер с общими
  авторизациями — база без собственного логина/пароля больше не падает с
  «The infobase user is not authenticated»; при ошибке аутентификации
  конфигуратора добавлен автоматический fallback на ibcmd-ветку.

- **Утилита `ring` во вкладке «Лицензии» запускается, если установлена как
  командный файл (issue #83, Windows и Linux).** Найденный путь разлагается на
  «исполняемый вызов»: `.cmd`/`.bat` запускаются через `cmd.exe /c`, `.exe`/
  нативные — напрямую по полному пути; улучшена подсказка, если утилита
  доступна из PATH.

- **Окна «Настройки» и «Настройка информационной базы» запоминают размеры
  (issue #71, Windows и Linux).** Общий хелпер `WindowSizeStore` + 4 новых поля
  в настройках: размеры сохраняются при закрытии и восстанавливаются при
  открытии с зажимом к `MinWidth`/`MinHeight`.

- **Мастер «Развернуть тестовую копию»: автоподстановка учётных данных и кнопки
  перехода (issue #79, Windows и Linux).** Добавлен блок «Данные для выгрузки
  через ibcmd»: автоподстановка логина/пароля из исходной базы, кнопки
  «Учётные данные для ibcmd…» и «Общая авторизация…» без закрытия мастера;
  сценарий хранит `Credential` и передаёт его в выгрузку; при ошибке выгрузки
  добавлена подсказка, где задать учётные данные.

- **Окно «Серверы 1С»: усилен механизм перетаскивания колонок (issue #75,
  Windows и Linux).** Второй пояс перехвата на презентере заголовков,
  стабильный старт перетаскивания, гашение штатного ресайза, добавлена
  диагностика.

### Тесты

- [`CommonAuthLaunchArgsTests.cs`](Workspace1C.Tests/CommonAuthLaunchArgsTests.cs)
  и [`ConfigExtensionServiceTests.cs`](Workspace1C.Tests/ConfigExtensionServiceTests.cs):
  общая авторизация и fallback на ibcmd — #87;
- [`Issue83LicenseTabTests.cs`](Workspace1C.Tests/Issue83LicenseTabTests.cs):
  `ToLaunchCommand` для `.exe`/`.cmd`/нативных — #83;
- [`Issue71SettingsWindowLayoutTests.cs`](Workspace1C.Tests/Issue71SettingsWindowLayoutTests.cs):
  `WindowSizeStore`, восстановление и зажим размеров — #71;
- [`Issue79TestDeployWindowTests.cs`](Workspace1C.Tests/Issue79TestDeployWindowTests.cs):
  панель ibcmd, `BuildScenario` с `Credential`, `AutofillCredentialFromSource` — #79;
- [`Issue75ServerMonitorColumnsTests.cs`](Workspace1C.Tests/Issue75ServerMonitorColumnsTests.cs):
  event-level перехват, второй пояс, блок-семантика — #75.

Итого в проекте **более 5 000 юнит-тестов**; Linux-сборка (Avalonia) также зелёная.

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).
