### Добавлено

Девять новых инструментов в меню **«Утилиты» → «Операции»** (сразу после «Обозревателя
метаданных»); при сохранённой пользовательской компоновке меню они попадают в подменю
**«Новинки программы»**. Все окна есть на Windows (WPF) и Linux (Avalonia).

- **Журнал регистрации** (завершение функции, начатой в 0.3.9.161) — окно просмотра
  журнала файловой базы (каталог `1Cv8Log`) или любого указанного каталога журнала
  (для клиент-серверной базы — `srvinfo\reg_<порт>\<id базы>\1Cv8Log` на сервере).
  Новый ридер последовательного формата `.lgf`/`.lgp`
  ([`SequentialLgfReader.cs`](Workspace1C/Services/EventLog/SequentialLgfReader.cs),
  потоковый разбор скобочного формата — [`LgfBracketParser.cs`](Workspace1C/Services/EventLog/LgfBracketParser.cs)),
  отбор по периоду (час / сегодня / вчера / 7 / 30 дней / весь журнал), важности,
  пользователю, событию и тексту, лимит записей, подробности записи, экспорт в CSV для
  Excel ([`EventLogQuery.cs`](Workspace1C/Services/EventLog/EventLogQuery.cs),
  [`EventLogViewModel.cs`](Workspace1C/ViewModels/EventLogViewModel.cs)). Имена событий
  и приложений — по-русски. SQLite-ридер (`1Cv8.lgd`) получил выборку за период на
  стороне SQLite и поддержку реальных имён колонок `severity`, `metadataCodes`,
  `dataPresentation`, `session`.
- **Расширения конфигурации (.cfe)** — пакетная установка/обновление одного расширения
  в отмеченные базы (`/LoadCfg -Extension` + `/UpdateDBCfg -Extension`), список
  расширений базы (`/DumpDBCfgList -AllExtensions`), выгрузка расширения в файл
  и удаление ([`ConfigExtensionService.cs`](Workspace1C/Services/ConfigExtensionService.cs)).
  Новый тип задания по расписанию **«Установить расширение (.cfe)»** (имя расширения —
  имя файла). Общий запуск конфигуратора с произвольными ключами —
  [`DesignerCommandRunner.cs`](Workspace1C/Services/DesignerCommandRunner.cs).
- **Версионирование конфигурации в Git** — выгрузка конфигурации базы в XML
  (`/DumpConfigToFiles`) в подкаталог репозитория, `git add -A`, коммит с шаблоном
  сообщения (`{base}`, `{date}`, `{version}`) и, при желании, `git push`
  ([`GitVersioningService.cs`](Workspace1C/Services/GitVersioningService.cs)). Настройки
  хранятся по базам в `git_versioning.json`; тип задания по расписанию **«Выгрузить
  конфигурацию в Git»**. Нужен установленный Git.
- **Технологический журнал** — готовые шаблоны `logcfg.xml`: исключения (EXCP), блокировки
  (TLOCK/TTIMEOUT/TDEADLOCK), долгие запросы к СУБД и долгие серверные вызовы с порогом
  в мс; срок хранения логов (`history`), предпросмотр, включение с резервной копией
  прежнего файла и отключение ([`TechLogService.cs`](Workspace1C/Services/TechLogService.cs)).
- **Публикация на веб-сервере** — публикация базы утилитой `webinst` из каталога платформы
  базы на IIS, Apache 2.4 или 2.2, удаление публикации, имя публикации по умолчанию —
  транслитерация имени базы ([`WebPublishService.cs`](Workspace1C/Services/WebPublishService.cs)).
- **Мониторинг и оповещения** — фоновая проверка серверов из справочника «Серверы 1С»
  (порог числа сеансов, памяти рабочего процесса, ожидающих блокировок) и новых ошибок
  в журналах регистрации файловых баз; оповещения системным уведомлением, в Telegram
  и на почту с подавлением повторов
  ([`ServerAlertsService.cs`](Workspace1C/Services/ServerAlertsService.cs)). Новое событие
  уведомлений `Monitoring` и настройка `NotifyOnMonitoring` (по умолчанию включена).
- **Резервные копии средствами СУБД** — PostgreSQL (`pg_dump -Fc`) и MS SQL Server
  (`BACKUP DATABASE … WITH COPY_ONLY` через `sqlcmd`), ротация копий, выгрузка в облако
  по WebDAV (Яндекс.Диск, Nextcloud) ([`DbmsBackupService.cs`](Workspace1C/Services/DbmsBackupService.cs));
  тип задания по расписанию **«Копия средствами СУБД»**.
- **Команды Telegram-бота** — бот уведомлений принимает команды `/status`, `/tasks`,
  `/run N` (запуск задания по расписанию, включается отдельно), `/alerts`, `/help`;
  только из разрешённых чатов, по умолчанию выключено
  ([`TelegramBotCommandService.cs`](Workspace1C/Services/TelegramBotCommandService.cs)).
- **Общие списки баз отдела** — подписка на общий `ibases.v8i` (сетевая папка или
  http-адрес): базы источника появляются в группе с именем списка и синхронизируются при
  запуске и по интервалу — новые добавляются, изменённые обновляются, удалённые из файла
  убираются; личные базы не затрагиваются ([`SharedListService.cs`](Workspace1C/Services/SharedListService.cs),
  новое поле базы `SharedSource`).

### Исправлено

- **Цвет иконки поиска совпадает с крестиком очистки (issue #60, Windows и Linux)** — в
  светлой теме оба тёмные, в тёмной — рассинхрон: иконка поиска `Magnify` в поле поиска
  теперь использует ту же кисть `TextSecondaryBrush`, что и крестик очистки `Close`, и не
  полупрозрачна (убрана `Opacity="0.7"`)
  ([`MainWindow.xaml`](Workspace1C/Views/MainWindow.xaml)). На Linux (Avalonia) обе
  иконки уже строились одной кистью — правка не требовалась. Покрыто тестами
  `Issue60SearchIconColorTests`.
- **Окно «Сменить платформу» закрывается по Esc (issue #54, Windows и Linux)** — в
  массовых действиях окно смены платформы у выделенных баз закрывается по Esc, как
  остальные диалоги. Для новых диалоговых окон WPF добавлено общее правило — базовый
  класс [`EscClosableWindow`](Workspace1C/Views/EscClosableWindow.cs) (закрытие по Esc
  без модификаторов в `PreviewKeyDown`), окно переведено на него, кнопка «Отмена» —
  `IsCancel` ([`PlatformChangeWindow.xaml`](Workspace1C/Views/PlatformChangeWindow.xaml:46)).
  На Linux (Avalonia) правило уже даёт общий
  [`ModalWindowBase`](Workspace1C/Views/ModalWindowBase.cs) — правка не требовалась.
  Покрыто тестами `Issue54EscapeCloseTests`.
- **Миграция баз: автоподстановка сервера из «Серверы 1С» и поля «Тип СУБД»/«Сервер
  СУБД» (issue #41, Windows и Linux)** — при запуске миграции из окна «Серверы 1С»
  текущий сервер подставляется в поле (сопоставление по Id справочника, кластер
  запоминается для автовыбора после загрузки)
  ([`ClusterMigrationViewModel.SetInitialSource`](Workspace1C/ViewModels/ClusterMigrationViewModel.cs)).
  Устранена «странная ошибка» создания баз в приёмнике на другом сервере СУБД: на форму
  добавлены поля **«Тип СУБД»** и **«Сервер СУБД»**
  ([`ClusterMigrationWindow.xaml`](Workspace1C/Views/ClusterMigrationWindow.xaml),
  [`ClusterMigrationWindow.axaml`](Workspace1C/Views/ClusterMigrationWindow.axaml)); они
  автозаполняются из первой базы источника после «Получить базы» и не перезаписывают
  ручные правки; [`ClusterMigrationService.BuildCreateRequest`](Workspace1C/Services/ClusterMigrationService.cs)
  использует значения формы с приоритетом над данными источника. Покрыто тестами
  `ClusterMigrationViewModelTests`/`ClusterMigrationServiceTests`/`Issue41WindowLayoutTests`.
- **Общая авторизация стала раздельной по режимам запуска (issue #57, Windows и
  Linux)** — в настройках блок «Общая авторизация» разбит на два подблока:
  **«1С:Предприятие»** и **«Конфигуратор»**, в каждом свои режим аутентификации, логин
  и пароль. При запуске базы без собственной авторизации для Enterprise применяется
  `CommonEnterpriseAuth`, для конфигуратора — `CommonConfiguratorAuth`; если раздельная
  для режима не задана — используется прежняя `CommonAuthorization` (legacy, обратная
  совместимость). Раздельные общие учитываются и при запуске 1С, и при построении
  COM-строки подключения ([`InfobaseAuthResolver.cs`](Workspace1C/Services/InfobaseAuthResolver.cs),
  [`OneCLauncher.Arguments.Shared.cs`](Workspace1C/Services/OneCLauncher.Arguments.Shared.cs),
  [`OneCComConnector.cs`](Workspace1C/Services/OneCComConnector.cs),
  [`SettingsWindow.xaml`](Workspace1C/Views/SettingsWindow.xaml)). Покрыто тестами
  `CommonAuthResolverTests`/`CommonAuthLaunchArgsTests`/`CommonAuthComTests`
  и новым `CommonAuthSettingsStorageTests` (миграция старого `settings.json`).