# Workspace 1C 1.0.1.8

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.8

### Исправлено / Добавлено

- **Автообновление: платформо-зависимое имя скачиваемого файла и диагностика (issue #89,
  Linux).** Отображаемое имя загрузки по платформе (`Workspace1C-linux-x64`, а не
  `Workspace1C.exe`); перед загрузкой в журнал `CM_UPDATES` пишутся полный URL и
  ожидаемый размер; сообщение карантина «wrong-platform» дополнено версией приложения
  и подсказкой для старых версий. В [`release.yml`](.github/workflows/release.yml)
  добавлен `workflow_dispatch` с параметром prerelease — тестовые сборки, не затрагивающие
  обычных пользователей.

- **Общая авторизация: передача общих логина/пароля в командную строку запуска
  (issue #57, Windows и Linux).** Исправлена причина «пустого пользователя»: раздельная
  авторизация режима `Credentials` с пустым пользователем больше не блокирует общие
  учётные данные режима; защитная ветка против пустого `/N` в аргументах; аудит
  синхронизации статиков общей авторизации (WPF, Avalonia, CLI/проводник).

- **Окно Свойств (Linux): трассировка закрытия модальных окон (issue #94, Linux).**
  Диагностика зависания «Отмена» после вкладки «Идентификатор»: включается
  `CM_MODAL_TRACE=1`, лог пишется в `/tmp/cm_modal_trace.log`.

- **Серверы 1С / лицензии: поиск `ring` по PATH из реестра и известным каталогам
  (issue #83, Windows).** Кандидаты: PATH процесса, свежий PATH из реестра
  (`HKCU\Environment\Path` + `HKLM\...\Session Manager\Environment\Path`), известные
  каталоги 1С. Найденный `ring` запускается по полному пути без перезапуска приложения;
  диагностика `CM_RAC` показывает проверенные каталоги и размеры PATH.

- **Публикация баз: кодировка вывода webinst (cp866), без дублей
  `LoadModule _1cws_module`, пути через `${переменные}` как в существующих записях,
  `.bak`-копия конфига, `1c8Aliases*` по умолчанию (issue #63, Windows).**

### Реорганизация интерфейса

- **Окно настроек (issue #95, Windows и Linux):** вкладка «Настройки» разбита на
  под-вкладки «Поведение и язык» / «Функциональность» / «COM-Коннектор» /
  «Общая авторизация»; «Настройки» — первая вкладка, «Платформы» — перед «ibases.v8i».

- **Свойства базы (issue #93, Windows и Linux):** вкладка «Запуск» разделена на
  «Режим запуска клиента» и «События»; окно компактнее — высота 680, минимум 640
  (кнопки доступны на 1366×768).

- **Массовая выгрузка в DT/CF (issue #92, Windows и Linux):** пункт «Выгрузить в DT\CF…»
  в подменю «Для выделенных (N)…» — шаблон имени файла (`{Имя}`, `{Дата}`), выбор папки,
  переключатель DT / CF; шаблон и папка сохраняются между сессиями.

### Тесты

- [`Issue89PlatformValidationTests.cs`](Workspace1C.Tests/Issue89PlatformValidationTests.cs):
  выбор ассета по платформе и отображаемое имя загрузки — #89;
- [`Issue57EmptyModeUserTests.cs`](Workspace1C.Tests/Issue57EmptyModeUserTests.cs):
  режим `Credentials` с пустым пользователем не блокирует общую авторизацию — #57;
- [`Issue93And95SubtabsTests.cs`](Workspace1C.Tests/Issue93And95SubtabsTests.cs):
  порядок вкладок и под-вкладки окон настроек и свойств базы — #95, #93;
- [`Issue92BatchDumpTests.cs`](Workspace1C.Tests/Issue92BatchDumpTests.cs):
  шаблон имени файла (`{Имя}`, `{Дата}`), санитизация, `DumpCf` — #92;
- [`Issue83RingPathRegistryTests.cs`](Workspace1C.Tests/Issue83RingPathRegistryTests.cs):
  кандидаты поиска `ring` (PATH процесса, реестр, известные каталоги) — #83;
- [`WebPublishConfPostProcessTests.cs`](Workspace1C.Tests/WebPublishConfPostProcessTests.cs),
  [`WebPublishIssue63Tests.cs`](Workspace1C.Tests/WebPublishIssue63Tests.cs):
  дедупликация `LoadModule`, `${переменные}`, `.bak`, `1c8Aliases*` по умолчанию — #63.

Итого в проекте **5 245 юнит-тестов** (было 5 175); Linux-сборка (Avalonia) также зелёная.

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).
