# Workspace 1C 1.0.1.4

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.4

### Исправлено / Добавлено

- **Автообновление на Linux скачивает Linux-сборку, а не Windows-EXE (issue #89,
  Windows и Linux).** Имя Linux-ассета приведено к контракту публикации —
  `Workspace1C-linux-x64`; любые `.exe` на Linux отбрасываются как не
  платформенные. Загрузчик больше не скачивает `Workspace1C.exe`.

- **Вкладка «Лицензии»: утилита `ring` вызывается как есть (issue #83, Windows и
  Linux).** Доступность проверяется запуском `where.exe ring` / `which ring`
  (запасной способ — `ring --version`), а сама команда выполняется как `ring`
  без подмены полного пути — поиск по PATH выполняет ОС.

- **Иконка группы «Новинки программы» в «Утилиты» — та же NewBox, что в
  контекстном меню (issue #81, Windows и Linux).** Учтены все легаси-«звёзды»
  из старых layout; назначенный пользователем внешний файл иконки сохраняется.

- **Мастер «Развернуть тестовую копию» больше не падает при открытии (issue #79,
  Windows и Linux).** Полю `PasswordBox` больше не назначается стиль с
  `TargetType="TextBox"` (источник `InvalidOperationException`); пароль получает
  темированный implicit-стиль.

- **Окно «Серверы 1С»: перетаскивание колонок работает как в 1С (issue #75,
  Windows и Linux).** Перехват перенесён на уровень грида (WPF), зона «ручки»
  расширена до 24 px (Avalonia) — работают обычный и Ctrl-режимы.

- **Окна «Настройки» и «Настройка информационной базы» нельзя сжать ниже минимума
  (issue #71, Windows и Linux).** Гарантированный зажим к `MinWidth`/`MinHeight`:
  WPF — перехват `WM_GETMINMAXINFO` с учётом DPI, Avalonia — общий зажим по
  `ClientSizeProperty`.

- **Публикация на веб-сервере: Apache-службы с номерами получают собственный
  конфиг (issue #63, Windows и Linux).** Конфиг берётся из строки запуска службы
  (`-d`/`-f`, например `conf\httpd27.conf`), исходные данные службы пишутся в
  лог, файл алиасов `1c8Aliases27.conf` находится по суффиксу из конфига.

- **Главное окно нельзя сжать меньше минимума (issue #55, Windows и Linux).**
  Тот же механизм гарантии минимального размера применён к главному окну.

- **Тёмная тема: надписи «Синхронизации настроек» и кнопки управления окна
  (issue #26, Windows и Linux).** Надписи привязаны к тематическим кистям;
  при неактивном окне кнопки используют приглушённую подложку — белая
  «подсветка» на тёмной шапке убрана.

### Тесты

- [`GitHubReleaseServiceTests.cs`](Workspace1C.Tests/GitHubReleaseServiceTests.cs):
  выбор Linux-ассета, отбрасывание `.exe` — #89;
- [`Issue83LicenseTabTests.cs`](Workspace1C.Tests/Issue83LicenseTabTests.cs):
  вызов `ring` как есть — #83;
- [`Issue81NewBoxIconTests.cs`](Workspace1C.Tests/Issue81NewBoxIconTests.cs):
  легаси-«звёзды» и гарантия NewBox — #81;
- [`Issue79TestDeployWindowTests.cs`](Workspace1C.Tests/Issue79TestDeployWindowTests.cs):
  `PasswordBox` без стиля `TargetType="TextBox"` — #79;
- [`Issue75ServerMonitorColumnsTests.cs`](Workspace1C.Tests/Issue75ServerMonitorColumnsTests.cs):
  грид-уровневый перехват и зона 24 px — #75;
- [`Issue71SettingsWindowLayoutTests.cs`](Workspace1C.Tests/Issue71SettingsWindowLayoutTests.cs):
  зажим минимума — #71;
- [`ApacheInstanceDetectorTests.cs`](Workspace1C.Tests/ApacheInstanceDetectorTests.cs)
  и [`Apache27ConfRegressionTests.cs`](Workspace1C.Tests/Apache27ConfRegressionTests.cs):
  службы с номерами, `-d`/`-f`, файл алиасов — #63;
- [`Issue55AdaptiveLayoutTests.cs`](Workspace1C.Tests/Issue55AdaptiveLayoutTests.cs):
  минимум главного окна — #55;
- [`ThemeFeatureMapTests.cs`](Workspace1C.Tests/ThemeFeatureMapTests.cs)
  и [`Issue26SyncWindowButtonsTests.cs`](Workspace1C.Tests/Issue26SyncWindowButtonsTests.cs):
  контраст надписей и кнопок окна — #26.

Итого в проекте **~5 048 юнит-тестов**; Linux-сборка (Avalonia) также зелёная.

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).