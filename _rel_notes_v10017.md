# Workspace 1C 1.0.1.7

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.7

### Исправлено / Добавлено

- **Общая авторизация при раздельной авторизации базы в режиме Windows (issue #57,
  Windows и Linux).** Исправлена подстановка общей авторизации для баз с раздельной
  авторизацией (`Windows`, пустой пользователь): общие учётные данные теперь
  подставляются корректно
  ([`InfobaseAuthResolver.Resolve`](Workspace1C/Services/InfobaseAuthResolver.cs)).
  Гарантирована синхронизация общей авторизации при запуске через CLI; в диагностику
  `CM_BASE_RUN` добавлен источник авторизации — следующий репорт диагностируем с
  первого сообщения.

- **«Развернуть тестовую копию»: пред-валидация профиля СУБД для ibcmd-выгрузки
  (issue #79, Windows и Linux).** Для клиент-серверных баз перед ibcmd-выгрузкой
  выполняется пред-валидация профиля СУБД с понятным сообщением об ошибке; добавлена
  кнопка «Профиль СУБД для ibcmd…»
  ([`TestDeployWindow.cs`](Workspace1C/Views/TestDeployWindow.cs),
  [`TestDeployWindow.Avalonia.cs`](Workspace1C/Views/TestDeployWindow.Avalonia.cs)).
  Исправлена кнопка «Учётные данные ИБ для ibcmd…» — открывала не то окно.

- **Останов фонового RAC-мониторинга (issue #90, Windows и Linux).** Фоновый
  RAC-мониторинг останавливается при закрытии окна «Серверы 1С» и при выключении
  автообновления (`CancellationToken` + guard) — больше нет утечки фоновых опросов;
  записи `CM_RAC` помечены источником (`MONITOR`/`ALERTS`).

- **Вкладка «Лицензии»: понятное предупреждение об отсутствии модуля `ring license`
  (issue #83, Windows и Linux).** Если модуль `license` утилиты `ring` не установлен
  («Module "license" is not registered»), вкладка показывает понятное предупреждение
  с подсказкой о перезапуске приложения после установки модуля (устаревший `PATH`);
  предупреждение больше не «прячется» из-за инвертированных триггеров видимости.

- **Зона захвата перетаскивания колонок в «Серверы 1С» (issue #75, Windows и Linux).**
  Исправлена off-by-one ошибка: при перетаскивании двигалась не та колонка; граница
  колонки теперь определяется по координате X; усиленная диагностика `CM_COLUMNS`
  ([`ServerMonitorWindow.xaml.cs`](Workspace1C/Views/ServerMonitorWindow.xaml.cs),
  [`ServerMonitorWindow.Avalonia.cs`](Workspace1C/Views/ServerMonitorWindow.Avalonia.cs)).

- **Новые массовые операции: выгрузка списка баз в .dt и загрузка конфигурации из .cf
  (issue #91, Windows и Linux).** В меню «Утилиты» → «Операции» добавлены массовые
  операции по списку баз: выгрузка баз в `.dt` и загрузка конфигурации из `.cf`.
  Загрузка внешних отчётов/обработок по списку — планируется.

### Тесты

- [`Issue57WindowsAuthOverrideTests.cs`](Workspace1C.Tests/Issue57WindowsAuthOverrideTests.cs),
  [`Issue57CommonAuthSourceTests.cs`](Workspace1C.Tests/Issue57CommonAuthSourceTests.cs):
  подстановка общей авторизации при раздельной авторизации Windows и источник
  авторизации в диагностике — #57;
- [`Issue79TestDeployWindowTests.cs`](Workspace1C.Tests/Issue79TestDeployWindowTests.cs):
  пред-валидация профиля СУБД для ibcmd и кнопки мастера — #79;
- [`Issue90RacCancelTests.cs`](Workspace1C.Tests/Issue90RacCancelTests.cs):
  останов фонового RAC-мониторинга (`CancellationToken` + guard) — #90;
- [`Issue83LicenseTabTests.cs`](Workspace1C.Tests/Issue83LicenseTabTests.cs):
  предупреждение о незарегистрированном модуле `license` — #83;
- [`Issue75ServerMonitorColumnsTests.cs`](Workspace1C.Tests/Issue75ServerMonitorColumnsTests.cs):
  зона захвата перетаскивания колонок по координате X — #75;
- [`BatchOperationsTests.cs`](Workspace1C.Tests/BatchOperationsTests.cs):
  массовые операции .dt/.cf по списку баз — #91.

Итого в проекте **5 175 юнит-тестов** (было 5 147); Linux-сборка (Avalonia) также зелёная.

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).
