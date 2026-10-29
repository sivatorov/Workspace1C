# Workspace 1C 1.0.1.3

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.3

### Исправлено / Добавлено

- **Установка расширений (.cfe) в клиент-серверную базу снова работает (issue #87,
  Windows и Linux).** Исправлены аргументы `/UpdateDBCfg` — убран лишний ключ
  `-Extension`; добавлена диагностика ошибки «command-line options» и
  автоматический fallback на ibcmd-ветку (`config load --extension` +
  `config apply --force`) — установка больше не падает с «Error in command-line
  options».

- **Настройки, вкладка «Клавиши»: сочетания кнопок выровнены (issue #86, Windows и
  Linux).** Колонке подписей задана фиксированная ширина **260 px** (WPF и
  Avalonia) — все поля HotkeyBox начинаются с одной вертикали.

- **Вкладка «Лицензии»: утилита `ring` ищется в PATH (issue #83, Windows и
  Linux).** `ring` вызывается из PATH (через PATHEXT), каталог bin платформы —
  запасной вариант; RING-записи переведены на категорийный лог.

- **Иконка группы «Новинки программы» больше не подменяется на «звезду» (issue
  #81, Windows и Linux).** Распознаётся легаси-иконка «звезда» — меню «Утилиты»
  корректно получает иконку NewBox; назначенная пользователем иконка
  сохраняется.

- **Настройка логирования приведена к категориям (issue #80, Windows и Linux).**
  Записи «Запущена база…» → `BaseRun`, «Резервная копия ibases.v8i» → `Ibases`,
  cookie обновлений → INFO, RING-записи → `CM_RAC`; INFO-спам при выключенном
  логировании устранён.

- **Мастер «Развернуть тестовую копию» больше не падает при открытии (issue #79,
  Windows и Linux).** Предпросмотр переведён в безопасный режим: при некорректных
  данных базы показывается нейтральный текст `TestDeploy.PreviewUnavailable`
  (локализация ru/en), а не исключение.

- **Окно «Серверы 1С» — работа с колонками (issue #75, Windows и Linux).** Зона
  кликов для перемещения/изменения колонок расширена (16 → 24 px); подписки на
  заголовки колонок догружаются по `LayoutUpdated` (WPF) и не теряются при
  добавлении новых колонок.

- **Окна «Настройки» и «Настройка информационной базы» нельзя сжать меньше
  минимума (issue #71, Windows и Linux).** Программный зажим размеров к
  `MinWidth`/`MinHeight` при любом способе ресайза; восстановление размеров из
  профиля тоже зажимается.

- **Аудит форм (issue #55, Windows и Linux).** Правки форм включены в #71/#86:
  минимальные размеры окон и выравнивание полей хоткеев; дополнительных жёстких
  ширин, обрезающих содержимое, не выявлено.

- **Тёмная тема: «зебра» строк переведена на тематические кисти (issue #26,
  Windows и Linux).** Чередование строк использует
  `RowBackground=CardBackgroundBrush` и `AlternatingRowBackground=ZebraRowBrush` из
  активной темы; проверен контраст кнопок и тумблеров.

### Тесты

- [`ConfigExtensionServiceTests.cs`](Workspace1C.Tests/ConfigExtensionServiceTests.cs):
  строки `/UpdateDBCfg` без лишнего `-Extension`, fallback на ibcmd — #87;
- [`Issue83LicenseTabTests.cs`](Workspace1C.Tests/Issue83LicenseTabTests.cs):
  поиск `ring` в PATH и категорийные RING-логи — #83;
- [`Issue81NewBoxIconTests.cs`](Workspace1C.Tests/Issue81NewBoxIconTests.cs):
  распознавание легаси-«звезды» и подстановка NewBox — #81;
- [`LoggingCategoriesTests.cs`](Workspace1C.Tests/LoggingCategoriesTests.cs):
  категории `BaseRun`/`Ibases`/`UpdatesFlag`/`CM_RAC` — #80;
- [`Issue79TestDeployWindowTests.cs`](Workspace1C.Tests/Issue79TestDeployWindowTests.cs):
  открытие мастера не бросает исключений при некорректных данных базы — #79;
- [`Issue71SettingsWindowLayoutTests.cs`](Workspace1C.Tests/Issue71SettingsWindowLayoutTests.cs):
  зажим размеров окна к `MinWidth`/`MinHeight` — #71;
- [`Issue55AdaptiveLayoutTests.cs`](Workspace1C.Tests/Issue55AdaptiveLayoutTests.cs):
  выравнивание полей хоткеев — #55/#86.

Итого в проекте **~5 009 юнит-тестов**; Linux-сборка (Avalonia) также зелёная.

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).