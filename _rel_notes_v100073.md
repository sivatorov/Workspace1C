# Workspace 1C 1.0.0.73

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).

### Добавлено

- **Подстановки для сценариев: полные строки запуска и платформа (issue #67,
  Windows и Linux)** — добавлены токены из первого описания issue:
  `%full_launch_enterprise%` (полная строка запуска для режима «1С:Предприятие»),
  `%full_launch_config%` (для «Конфигуратора»), `%full_exe_name%` (полный путь к
  `1cv8.exe`/`1cv8`) и `%platform_version%` (версия платформы, например `8.5.4.1878`).
  Значения собираются тем же резолвером платформы, что использует стартер
  (`ScriptLaunchContextResolver` на базе `OneCPlatformLocator` и сборки аргументов
  `OneCLauncher`); токены видны в справочнике окна сценария и в диалоге «Выбрать…»
  с реальными значениями для базы. Покрыто тестами `ScriptParameterResolverTests`,
  `ScriptTokenPickerViewModelTests`, `ScriptLaunchContextResolverTests`.

- **Поле «Локаль» в миграции баз (issue #41, Windows и Linux)** — в окне
  «Миграция между кластерами 1С» после логина/пароля появился список выбора локали
  (как при добавлении базы). Локаль автозаполняется из первой базы источника (rac
  8.3/частичные версии 8.5 её не отдают — остаётся «ru» по умолчанию), вручную
  меняется без перезаписи и передаётся в rac «infobase create» с приоритетом над
  данными источника (двухбуквенный код). Покрыто тестами `ClusterMigrationServiceTests`,
  `ClusterMigrationViewModelTests`, `Issue41WindowLayoutTests`.

### Изменено

- **Парсер конфигов Apache понимает `Define` и `${Переменные}` (issue #63,
  Windows и Linux)** — директивы `Define SRVROOT …`/`Define OneCPort 8027`
  собираются и раскрываются в `DocumentRoot`/`Listen`/`Include`: каталог
  публикации становится «c:\Apache\Apache24\htdocs\8027\demo_27_file» (вместо
  литералов `${…}`), а порт из `Listen 0.0.0.0:${OneCPort}` подставляется в URL
  подсказки (`http://localhost:8027/demo_27/`). Покрыто тестами
  `ApacheConfIncludeParserTests`, `ApacheInstanceDetectorTests`, `WebPublishIssue63Tests`.

- **Меню трея: блоки в группе снова работают (issue #62, Windows и Linux)** —
  перенесённые в пользовательскую группу блоки «Избранные базы», «Недавние базы»,
  «Службы» и вложенные группы сохраняют динамическое содержимое и обработчики
  запуска (адаптеры WPF и Avalonia размещают вложенные блоки полным контентом, а
  не «мёртвыми» пунктами). Добавлен тест-страж адаптеров
  `TrayMenuNestedAdapterContractTests`.

### Исправлено

- **Тёмная тема: жёсткие тёмные цвета текста (issue #61, Windows и Linux)** —
  найден и заменён фиксированный чёрный по умолчанию в построителе бейджей
  Avalonia (подпись бейджа без явного цвета снова привязана к кисти темы);
  добавлены тесты-стражи `Issue61DarkPaletteAuditTests`: аудит WPF-разметки и
  Avalonia-кода на тёмные Foreground-константы и проверка контраста «текст на
  выделении» (ItemSelected/ItemHover против TextPrimary) для встроенных и
  генерируемых схем (WCAG ≥ 4.5:1).

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).