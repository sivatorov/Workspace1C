# Workspace 1C 1.0.1.6

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.6

### Исправлено

- **Автообновление на Linux больше не применяет EXE (issue #89, Linux).** Скачанный
  файл обновления теперь валидируется по магическим байтам платформы перед
  применением: Linux-сборка обязана быть ELF (`7F 45 4C 46`), Windows — PE (`MZ`)
  ([`UpdatePayload.ValidatePlatformMagic`](Workspace1C/Services/UpdatePayload.cs)). Неподходящий
  бинарник (например Windows-EXE у «застрявшей» старой версии) не применяется: файл
  переименовывается в `*.wrong-platform` (карантин для диагностики), решение пишется
  в журнал, а пользователь получает понятную ошибку с подсказкой скачать сборку
  `Workspace1C-linux-x64` и установить вручную. Дополнительно добавлена диагностика
  выбора ассета: имя и размер выбранного ассета релиза пишутся в журнал по
  диагностическому флагу `CM_UPDATES`
  ([`GitHubReleaseService.FindAsset`](Workspace1C/Services/GitHubReleaseService.cs)).
  Windows-путь уже был защищён проверкой сигнатуры (issue #358): ELF отклоняется
  тем же механизмом.

- **«Общая авторизация» перекрывает авторизацию ОС базы (issue #57, Windows и Linux).**
  База с «выключенной насильной авторизацией» (режим `Windows`, фактически `/WA+` без
  `/N`/`/P`) больше не блокирует общую авторизацию: явные общие учётные данные режима
  (`CommonEnterpriseAuthorization`/`CommonConfiguratorAuthorization`/legacy) подставляются
  в любом случае ([`InfobaseAuthResolver.Resolve`](Workspace1C/Services/InfobaseAuthResolver.cs)).
  Connection перекрывает общую только если задана режимом Credentials с пользователем
  (собственные учётные данные — высший приоритет) либо это режим ОС без заданных общих
  кредов. Выполнен сквозной аудит мест использования пароля (запуск Предприятия/Конфигуратора,
  пакетные команды конфигуратора, COM-подключение, ibcmd, пользователи ИБ, токены скриптов —
  все пути входа в ИБ идут через единый резолвер; итог зафиксирован комментарием в коде).
  После резолва в журнал пишется режим и источник авторизации без пароля (CM_BASE_RUN) —
  следующий репорт диагностируем с первого сообщения.

- **Публикация баз: конфиги Apache-служб из `Parameters\ConfigArgs` (issue #63, Windows).**
  У служб, созданных обёрткой 1С/Apache monitor, ключи `-d`/`-f` лежат не в ImagePath, а в
  реестровом параметре `HKLM\SYSTEM\CurrentControlSet\Services\<служба>\Parameters\ConfigArgs`
  (REG_MULTI_SZ) — из-за этого все службы сводились к дефолтному `httpd.conf`.
  [`ApacheInstanceDetector.DetectWindows`](Workspace1C/Services/ApacheInstanceDetector.Windows.cs)
  теперь читает `ConfigArgs` и передаёт их в новую 5-аргументную перегрузку
  [`ParseWindowsServiceImagePath`](Workspace1C/Services/ApacheInstanceDetector.cs);
  при конфликте `-d`/`-f` приоритет у `ConfigArgs` (фактические аргументы запуска).
  `RawCommandLine` в логе CM_PUBLISH показывает оба источника параметров службы
  (`ImagePath="…"; ConfigArgs=…`) — источник `-d`/`-f` сразу виден.

- **Тёмная тема: поля «Синхронизация настроек» и подсветка кнопок неактивного окна
  (issue #26, Windows и Linux).** Все контролы ввода WPF-окна «Синхронизация настроек»
  (поля «Интервал автозапуска», «Файлы для синхронизации», журнал операций и др.)
  получают тематические стили `ModernTextBox`/`ModernComboBox`/`ModernListBoxItem` и кисти
  темы ([`SettingsSyncWindow.ApplyModernStyles`](Workspace1C/Views/SettingsSyncWindow.cs) по
  образцу `TestDeployWindow`); Avalonia-версия окна — `.Styled(ControlThemes.ModernTextBox/…)`.
  Исправлена белая подсветка кнопок управления окном при неактивном главном окне:
  [`ThemeFeatureMap.InactiveHoverHex`](Workspace1C/Themes/ThemeFeatureMap.cs) в тёмной теме
  возвращает тёмный slate `#334155` и для стиля Glass (исключение убрано —
  полупрозрачно-белая подложка Glass и была «белой подсветкой»; macOS-«светофор» с цветными
  кругами не тронут); Avalonia-метрики ([`WindowButtonThemeMetrics`](Workspace1C/Themes/WindowButtonThemeMetrics.Avalonia.cs))
  больше не захардкожены на светлый вариант — fallback учитывает фактическую тему;
  ресурсы кнопок окна обновляются при каждом применении темы
  (`ApplyWindowButtonStyle` вызывается и из `ApplyTheme`).

### Тесты

- Всего в проекте 5 123 юнит-теста — все пройдены.

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).
