# План Workspace 1C 1.0.1.120: пароли учётных записей и шифрование, Linux-баги меню, СУБД-копии, релиз с модулями ПРО

Дата анализа: **2026-11-25**. Базовая версия — **1.0.1.119**
([`Workspace1C.csproj:62`](Workspace1C/Workspace1C/Workspace1C.csproj:62)). Целевая версия —
**1.0.1.120**, повышается **однократно** и только последним числом (задача T14).

Документ — самодостаточное ТЗ. Код в нём **не меняется**. Каждый пункт раздела 7
выполняется **отдельной задачей** (`new_task`, режим `code`). Задачи T1–T18 идут строго
по порядку; зависимости указаны явно.

Источник требований — открытые issues репозитория `sivatorov/Workspace1C`
(досье: `g:/h/Workspace1C/_issues_live/issue-*.md`). Исправляются **только** перечисленные
issues: #143, #146, #157, #158, #159, #160, #161, #162, #163, #164, #165.

---

## 0. Обязательные ограничения релиза

1. **Issues не закрывать.** После публикации — комментарий в каждый issue: что исправлено,
   в какой версии (**1.0.1.120**), что проверить, просьба подтвердить. Комментарии
   публикуются **после релиза** (T18). До T18 никаких `gh issue close`.
2. **Паритет WPF (Windows) и Avalonia (Linux).** Любое изменение UI/поведения делается в
   обеих оболочках, кроме случаев, где баг платформенный (тогда фиксируется причина и
   проверяется, что вторая платформа не регрессировала).
3. **Локализация ru/en.** Каждая новая строка — ключ в
   [`ru.json`](Workspace1C/Workspace1C/Localization/Languages/ru.json:1) и
   [`en.json`](Workspace1C/Workspace1C/Localization/Languages/en.json:1); тест на наличие
   ключа в обоих файлах.
4. **Тесты на каждое исправление.** Новые тест-классы `Issue120*` (или `Issue<номер>*`),
   существующие тесты не ослаблять; при изменении ожидаемых размеров/структуры — обновлять
   ожидания осознанно, с комментарием.
5. **CHANGELOG и README.** Для каждого изменения — запись в
   [`CHANGELOG.md`](Workspace1C/CHANGELOG.md:14) и обновление
   [`README.md`](Workspace1C/README.md:3) по образцу 1.0.1.119 (версия, таблица бинарей,
   раздел изменений, ПРО).
6. **Релизные заметки** — `rel_notes_1.0.1.120.md` в корне репозитория (по образцу
   [`rel_notes_1.0.1.119.md`](Workspace1C/rel_notes_1.0.1.119.md:1)).
7. **Сборка: ровно один исполняемый файл** для Windows
   ([`build-windows-single-file.ps1`](Workspace1C/Workspace1C/build-windows-single-file.ps1:1))
   и **один** для Linux
   ([`build-linux-single-file.sh`](Workspace1C/Workspace1C/build-linux-single-file.sh:1)).
   **Модули ПРО обязательны** (`PRO_MODULES`); сборка без них должна падать до публикации.
8. **Публикация:** коммиты + push в `main`, тег `v1.0.1.120` → workflow
   [`.github/workflows/release.yml`](Workspace1C/.github/workflows/release.yml:1) собирает
   Windows и Linux, публикует GitHub Release и отправляет Telegram-анонс в чат
   `-1004425443528`. После релиза — независимая проверка доставки (HTTP 200, `ok:true`,
   ссылка на пост) и файл подтверждения в `docs/` (T17).
9. **Git-корень** — `g:/h/Workspace1C/Workspace1C`, ветка `main`, remote
   `origin = https://github.com/sivatorov/Workspace1C`, `gh` авторизован как `sivatorov`.
   Локальный клон ПРО — `g:/h/Workspace1C/Workspace1C.Pro`.

---

## 1. Состояние рабочего дерева: незакоммиченные изменения модели «одна программа с модулями ПРО»

В дереве уже есть незакоммиченные правки (переход от отдельной ПРО-сборки к одной программе,
доступ открывает лицензия). Их нельзя потерять: T13 проверяет, дописывает и фиксирует их.

### 1.1. Что уже готово (проверено по коду)

| Файл | Состояние |
|---|---|
| [`Workspace1C.csproj:905-937`](Workspace1C/Workspace1C/Workspace1C.csproj:905) | Поиск клона ПРО (`ProSourceDir`, соседние каталоги), `HasProModules`, `DefineConstants=PRO_MODULES`, `Compile Include`, target `ReportProModules` |
| [`build-windows-single-file.ps1:53-78`](Workspace1C/Workspace1C/build-windows-single-file.ps1:53) | Модули обязательны, `-AllowCommunity`, проверка `HasProModules` **до** publish, понятная ошибка |
| [`build-linux-single-file.sh:42-47`](Workspace1C/Workspace1C/build-linux-single-file.sh:42) | `ALLOW_COMMUNITY`, `REQUIRE_PRO`, `ProSourceDir` подхватывается csproj |
| [`.github/workflows/release.yml:97-160`](Workspace1C/.github/workflows/release.yml:97) | Windows job: checkout `sivatorov/Workspace1C.Pro` по `PRO_REPO_TOKEN`, падение без секрета, проверка `proModules=true` по `--license-info --json` |
| [`.github/workflows/release.yml:205-270`](Workspace1C/.github/workflows/release.yml:205) | Linux job: тот же checkout, проверка `proModules=true` |
| [`.github/workflows/release.yml:375-426`](Workspace1C/.github/workflows/release.yml:375) | Telegram-шаг: фиксированный `chat_id`, текст из `docs/_telegram_msg_v<версия>.txt` |
| [`ru.json:4915`](Workspace1C/Workspace1C/Localization/Languages/ru.json:4915) / [`en.json:4916`](Workspace1C/Workspace1C/Localization/Languages/en.json:4916) | Ключ `Pro.OfficialBuildOnlyFormat` |
| [`MainWindow.UtilitiesMenu.cs:122-128`](Workspace1C/Workspace1C/Views/MainWindow.UtilitiesMenu.cs:122), [`MainWindow.Avalonia.Tree.cs:2901-2909`](Workspace1C/Workspace1C/Views/MainWindow.Avalonia.Tree.cs:2901) | Placeholder-пункты «только в официальной сборке» без External-экшена |
| [`Issue119ProVisibilityTests.cs:375-380`](Workspace1C/Workspace1C.Tests/Issue119ProVisibilityTests.cs:375) | Проверка ключей `Pro.OfficialBuildOnlyFormat` и статусов |
| [`README.md:3-25`](Workspace1C/README.md:3), [`docs/pro/PRO-EDITION.md:1-52`](Workspace1C/docs/pro/PRO-EDITION.md:1), [`docs/pro/HOW-TO-BUY-PRO.md`](Workspace1C/docs/pro/HOW-TO-BUY-PRO.md:1) | Описание модели «одна программа, доступ по лицензии» |

### 1.2. Что нужно дописать/проверить (входит в T13)

1. **Linux-скрипт проверяет модули ПОСЛЕ publish** —
   [`build-linux-single-file.sh:111-123`](Workspace1C/Workspace1C/build-linux-single-file.sh:111).
   Перенести проверку `HasProModules` **до** `dotnet publish` (как в Windows), чтобы
   отсутствие ПРО не тратило время и не давало «полусобранный» артефакт.
2. **Windows-скрипт:** убедиться, что при `-AllowCommunity` в лог пишется явное
   предупреждение, а `ReportProModules` в выводе `dotnet build` показывает редакцию.
3. **csproj:** проверить оба пути поиска клона: `..\..\Workspace1C.Pro\src\`
   (локальный `g:/h/Workspace1C/Workspace1C.Pro`) и `..\Workspace1C.Pro\src\`
   (клон рядом с репозиторием); добавить в T13 прогон `dotnet msbuild -getProperty:HasProModules`
   для обоих вариантов и для `-p:ProSourceDir=...`.
4. **CI:** для push тега `pro_ref` пуст — checkout ПРО идёт по ветке по умолчанию; это
   допустимо, но зафиксировать в комментарии workflow и добавить шаг «Печать редакции»
   (`dotnet msbuild -getProperty:HasProModules`) перед сборкой.
5. **Тесты ПРО-видимости:** после T8 (редактируемая группа ПРО) ожидания
   [`Issue119ProVisibilityTests.cs:216-221`](Workspace1C/Workspace1C.Tests/Issue119ProVisibilityTests.cs:216)
   и `:299-302` (группа ПРО внутри `GroupTools` после `ResolveForDisplay`) нужно обновить:
   проверять наличие группы в **эффективной компоновке** и её видимость по настройкам,
   сохранив проверки гейтинга (`ExecuteExternalGated`, суффикс « · ПРО», placeholder).
6. **Документация:** в README/`PRO-EDITION.md`/`HOW-TO-BUY-PRO.md` согласовать формулировки
   с фактическим поведением 1.0.1.120 (модули обязательны, публичная сборка — только явно
   `-AllowCommunity`/`ALLOW_COMMUNITY=1`, проверка `proModules`).
7. **Локальная проверка «падение без модулей»:** `build-windows-single-file.ps1` без клона
   ПРО должен завершиться с кодом 1 **до** publish; `build-linux-single-file.sh` — так же
   после исправления п.1.

---

## 2. Issue #164 — «Не спрашивает пароль при старте»

### 2.1. Причина

- Окно входа показывается только при нескольких учётных записях:
  [`App.xaml.cs:121-152`](Workspace1C/Workspace1C/App.xaml.cs:121) и аналогичный код в
  [`App.axaml.cs`](Workspace1C/Workspace1C/App.axaml.cs:1) — условие `Profiles.Count > 1`.
  При одной защищённой паролем записи вход выполняется молча.
- В окне входа подпись поля статична — «Пароль»
  ([`LoginWindow.xaml:51-54`](Workspace1C/Workspace1C/Views/LoginWindow.xaml:51),
  [`LoginWindow.Avalonia.cs:138-153`](Workspace1C/Workspace1C/Views/LoginWindow.Avalonia.cs:138)),
  имя учётной записи не выводится.
- Токен `%app_user%` отсутствует в резолвере заголовка:
  [`TitleSuffixResolver.cs:18-41`](Workspace1C/Workspace1C/Services/TitleSuffixResolver.cs:18)
  (есть только `%computer_name%`, `%user_name%`, `%user_domain%`, `%computer_ip%`,
  `%display_server%`), подсказка в настройках перечисляет только их.

### 2.2. Требования

1. При старте, если у **активной** учётной записи задан пароль, окно входа/разблокировки
   показывается всегда — даже если запись одна.
2. Отмена входа завершает приложение (как сейчас).
3. В окне разблокировки видно, в какую запись входим: подпись
   «Пароль учётной записи **<Имя>**» (имя жирным), ключ локализации с `{0}`.
4. В шаблон дополнения заголовка окна добавить `%app_user%` — имя активной учётной записи.
5. Поведение одинаково в WPF и Avalonia; ru/en.

### 2.3. План правок

- [`App.xaml.cs:123`](Workspace1C/Workspace1C/App.xaml.cs:123) и `App.axaml.cs`: условие
  `profileService.Profiles.Count > 1 || profileService.CurrentProfile?.HasPassword == true`.
  Убедиться, что `EnsureInitialized()` уже выбрал активный профиль
  ([`ProfileService.cs`](Workspace1C/Workspace1C/Services/ProfileService.cs:1)).
- [`LoginWindow.xaml.cs:117-124`](Workspace1C/Workspace1C/Views/LoginWindow.xaml.cs:117):
  при одном профиле скрывать список (или показывать одну неизменяемую строку), подсказку
  заменить на «Введите пароль учётной записи»; подпись поля собирать из префикса и
  `Run`/`TextBlock` с `FontWeight=Bold` (WPF `Run` внутри `TextBlock`).
- [`LoginWindow.Avalonia.cs:95-181`](Workspace1C/Workspace1C/Views/LoginWindow.Avalonia.cs:95):
  аналогично; подпись — `TextBlock` с `Inlines` (`Run` с `FontWeight.Bold`) либо два
  `TextBlock` в `StackPanel`.
- Локализация: новые ключи `Auth.PasswordForAccountFormat` («Пароль учётной записи {0}» /
  «Account password for {0}»), `Auth.EnterPasswordHint`; существующий `Auth.Password`
  оставить для обратной совместимости.
- [`TitleSuffixResolver.cs`](Workspace1C/Workspace1C/Services/TitleSuffixResolver.cs:16):
  `AppUserToken = "%app_user%"`, добавить в `Tokens`, в `Resolve` — параметр
  `string? appUser` (перегрузка `Resolve(template, appUser)`), подстановка пустой строки,
  если имя неизвестно.
- Передать имя активной записи из
  [`MainViewModel.Display.cs:103-114`](Workspace1C/Workspace1C/ViewModels/MainViewModel.Display.cs:103)
  и [`MainViewModel.Avalonia.Display.cs:724-736`](Workspace1C/Workspace1C/ViewModels/MainViewModel.Avalonia.Display.cs:724)
  (взять `IProfileService.CurrentProfile?.Name`).
- Подсказка в настройках заголовка (WPF XAML и Avalonia) — добавить `%app_user%` в текст.

### 2.4. Тесты

Новый `Workspace1C.Tests/Issue164AccountPasswordPromptTests.cs`:

1. `SingleProtectedProfile_RequiresPasswordPrompt` — источник `App.xaml.cs`/`App.axaml.cs`
   содержит условие с `HasPassword` (source-guard) + логика «нужен вход».
2. `SingleUnprotectedProfile_NoPrompt`.
3. `LoginWindow_ShowsAccountNameInPasswordLabel` — оба файла содержат формат и жирное имя.
4. `TitleSuffix_AppUserToken_ResolvesProfileName`.
5. `TitleSuffix_UnknownAppUser_Empty`.
6. `Localization_HasAccountPasswordKeys` (ru/en).

Обновить существующие тесты `TitleSuffix*`, если они перечисляют `Tokens`.

### 2.5. Риски

- Окно входа при одном профиле может измениться визуально (список из одной строки) —
  скрыть список, оставить заголовок и поле.
- При повреждённом `profiles.json` `CurrentProfile` может быть null — условие должно быть
  безопасным (`?.`).
- Не сломать сценарий `--profile`/CLI: окно входа не должно появляться в headless-режиме
  (проверять только GUI-путь `App`).

### 2.6. DoD

- Защищённая запись с паролем: при старте спрашивается пароль, без пароля вход невозможен.
- В окне видно «Пароль учётной записи **Имя**».
- `%app_user%` работает в заголовке; подсказка обновлена; тесты зелёные; WPF и Avalonia.

---

## 3. Issue #163 — «Хранение паролей от баз» (шифрование паролей)

Зависит от T1 (#164): ключ шифрования — пароль учётной записи, который теперь гарантированно
запрашивается при старте. Требование из последних комментариев 7OH — итоговое ТЗ.

### 3.1. Причина/текущее состояние

Пароли хранятся открытым текстом в JSON профиля:

- `infobases.json`: [`InfobaseAuthSettings.Password`](Workspace1C/Workspace1C/Models/InfobaseAuthSettings.cs:16),
  [`RepositorySettings.Password`](Workspace1C/Workspace1C/Models/RepositorySettings.cs:19);
  загрузка/сохранение — [`InfobaseRepository.cs:84-134`](Workspace1C/Workspace1C/Services/InfobaseRepository.cs:84).
- `its_accounts.json`: [`ItsAccount.Password`](Workspace1C/Workspace1C/Models/ItsAccount.cs:22),
  [`ItsAccountsStore.cs:67-90`](Workspace1C/Workspace1C/Services/ItsAccountsStore.cs:67).
- `settings.json` и файлы `FeatureDataStore`: `UpdatesPassword`
  ([`AppSettings.cs:1072-1078`](Workspace1C/Workspace1C/Models/AppSettings.cs:1072)),
  `ManagementServer.Password`, `NotificationChannelSettings.SmtpPassword`,
  `DbmsBackupProfile.Password`, `BackupCredential.Password`, `TestDeployScenario.DbPassword`
  и `SourceDbPassword`.
- Уже есть шифрование заметок — [`NoteCrypto.cs:59-120`](Workspace1C/Workspace1C/Services/NoteCrypto.cs:59)
  (DPAPI/AES-GCM, префиксы `v1:`/`v2:`) — его можно переиспользовать по идее конверта,
  но ключ здесь должен быть производным от пароля учётной записи, а не DPAPI/файла.

Профиль хранит только **хэш** пароля
([`UserProfile.cs:25-32`](Workspace1C/Workspace1C/Models/UserProfile.cs:25)), поэтому
шифровать можно лишь при наличии введённого пароля в памяти сеанса.

### 3.2. Требования

1. В «Настройки» → «Учётные записи» под галочкой «Защитить паролем»
   ([`ProfilesPanel.xaml:129-130`](Workspace1C/Workspace1C/Views/ProfilesPanel.xaml:129),
   [`ProfilesWindow.Avalonia.cs:30`](Workspace1C/Workspace1C/Views/ProfilesWindow.Avalonia.cs:30))
   добавить галочку **«Шифровать этим все пароли»**.
2. Галочка доступна только если у записи задан пароль; при включении — все пароли
   (баз, ИТС, СУБД, SMTP, обновлений, сценариев) шифруются паролем учётной записи.
3. При смене пароля учётной записи — перешифровка всех сохранённых паролей.
4. При снятии галочки — пароли снова записываются в открытом виде (осознанное действие,
   подтверждение).
5. Пароль учётной записи по-прежнему хранится только хэшем; ключ шифрования не пишется на
   диск, живёт в памяти сеанса.
6. Если ключ недоступен (нет пароля в сеансе), приложение не падает: значения помечаются
   как «зашифровано, ключ недоступен», при попытке использования — запрос пароля.
7. Совместимость: старые файлы с открытыми паролями читаются; при включении галочки
   перешифровываются; формат JSON остаётся читаемым (конверт — строка с префиксом).
8. ru/en, тесты, обе платформы.

### 3.3. Проектирование

**Новый сервис** `Workspace1C/Services/Secrets/PasswordProtector.cs`:

- `IPasswordProtector`:
  `bool IsProtected(string? value)`, `string Protect(string? value)`,
  `ProtectResult Unprotect(string? value)` (статус `Ok/NoKey/WrongKey/Corrupted`).
- Конверт: `enc:v1:<base64(nonce|tag|ciphertext)>`, AES-256-GCM.
- Ключ: `PBKDF2-SHA256(password, salt, 200_000, 32)`, `salt` — 16 байт в
  `profiles/<Id>/secrets.json` (`{ "salt": "...", "verifier": "enc:v1:..." }`), verifier —
  шифрование известной строки для проверки пароля без раскрытия.
- `PasswordSession` (singleton): хранит `byte[] key` и `string? profileId`; методы
  `SetFromPassword(profileId, password)`, `Clear()`, `HasKey`, `TryGetKey`.
- `SecretJsonProtector`: обход `JsonNode` и шифрование/дешифрование **только** значений
  свойств из белого списка:
  `Password`, `DbPassword`, `SourceDbPassword`, `InfobasePassword`, `SmtpPassword`,
  `UpdatesPassword` (и `Repository.Password` через свойство `Password`).
  **Исключения:** `PasswordHash`, `AppLockPasswordHash`, `PasswordEncrypted`,
  `HasPassword`, `ProtectWithPassword`, `PasswordIsSet`, `CannotChangePassword`.

**Интеграция хранилищ** (все — через `SecretJsonProtector` при чтении/записи):

- [`InfobaseRepository.cs:84-134`](Workspace1C/Workspace1C/Services/InfobaseRepository.cs:84)
  (Load/Save, включая `SaveGroups` не трогать).
- [`ItsAccountsStore.cs:67-90`](Workspace1C/Workspace1C/Services/ItsAccountsStore.cs:67).
- [`FeatureDataStore.cs`](Workspace1C/Workspace1C/Services/FeatureDataStore.cs:14)
  (`Load/Save/LoadAsync/SaveAsync`) — покрывает `dbms_backups.json`, сценарии, уведомления,
  `settings.json` (если через него).
- [`AppSettings`](Workspace1C/Workspace1C/Models/AppSettings.cs:1) — `UpdatesPassword`:
  шифруется при сохранении в `settings.json` (через общий JSON-путь репозитория).

**Флаг**: `UserProfile.EncryptPasswordsWithProfilePassword` (bool) в `profiles.json`;
добавить в [`UserProfile.cs`](Workspace1C/Workspace1C/Models/UserProfile.cs:14) и в
`ProfileService` (сеттер с сохранением реестра). Для активного профиля — свойство
`IProfileService.IsPasswordEncryptionEnabled`.

**Смена пароля** — [`ProfileService.SetPassword`](Workspace1C/Workspace1C/Services/ProfileService.cs:233):
если флаг включён — расшифровать старым ключом, сменить хэш, вывести новый ключ, зашифровать
и перезаписать все хранилища; при снятии пароля — расшифровать и выключить флаг.

### 3.4. UI

- [`ProfilesViewModel.cs:130-135`](Workspace1C/Workspace1C/ViewModels/ProfilesViewModel.cs:130):
  `EncryptPasswords` (bool, двусторонняя привязка), доступность —
  `CanEncryptPasswords => ProtectWithPassword`.
- [`ProfilesPanel.xaml`](Workspace1C/Workspace1C/Views/ProfilesPanel.xaml:129): вторая
  `CheckBox` под «Защитить паролем», `IsEnabled` по `ProtectWithPassword`.
- [`ProfilesWindow.Avalonia.cs:30`](Workspace1C/Workspace1C/Views/ProfilesWindow.Avalonia.cs:30):
  второй `CheckBox`, те же правила.
- Сохранение ([`ProfilesViewModel.cs:245-280`](Workspace1C/Workspace1C/ViewModels/ProfilesViewModel.cs:245)):
  при включении — запрос подтверждения и проверка наличия пароля; при выключении —
  подтверждение «пароли будут храниться открыто».
- Если пользователь включает шифрование, а пароль не введён в этом сеансе — запросить пароль
  (переиспользовать `PrivateBasesUnlockWindow`/`LoginWindow`-механизм или отдельный
  `PasswordPromptWindow`).

### 3.5. Тесты

Новый `Workspace1C.Tests/Issue163PasswordEncryptionTests.cs`:

1. `Protector_RoundTrip_And_Prefix`.
2. `Protector_WrongPassword_ReturnsWrongKey`.
3. `SecretJsonProtector_EncryptsOnlyWhitelistedFields` (JSON с `Password`, `PasswordHash`,
   `SmtpPassword`, `DbPassword`).
4. `InfobaseRepository_RoundTrip_Encrypted_WhenEnabled` (temp dir).
5. `ItsAccountsStore_RoundTrip_Encrypted`.
6. `DisableEncryption_RestoresPlaintext`.
7. `ChangePassword_ReEncryptsAllStores`.
8. `NoSessionKey_DoesNotCrash_AndMarksUnavailable`.
9. `ProfilesViewModel_EncryptCheckbox_RequiresPassword`.
10. `Localization_HasEncryptionKeys` (ru/en).

Обновить тесты, читающие `infobases.json`/`its_accounts.json` напрямую, если они ожидают
открытый пароль (добавлять `PasswordSession` или отключать шифрование в фикстуре).

### 3.6. Риски

- Потеря пароля учётной записи = потеря сохранённых паролей. Обязательно: предупреждение,
  `secrets.json` включается в резервные копии профиля, документация.
- `ProfileBackupService` копирует файлы как есть — зашифрованные значения переносимы вместе
  с `secrets.json`; восстановление на другой машине требует того же пароля.
- Нельзя шифровать `PasswordHash` (иначе вход невозможен).
- Резкие изменения формата ломают ручное редактирование JSON — конверт должен быть
  строкой, файл остаётся валидным JSON.

### 3.7. DoD

- Галочка есть, работает в обеих оболочках, требует пароль.
- Включение шифрует все перечисленные пароли; выключение возвращает открытый вид.
- Смена пароля перешифровывает; без ключа приложение не падает и просит пароль.
- В файлах нет открытых паролей при включённой галочке; тесты зелёные; документация.

---

## 4. Остальные issues

### 4.1. Issue #165 — «Как купить PRO»: кнопки не видны

**Причина.** Окно фиксированного размера 620×600
([`HowToBuyWindow.cs:32`](Workspace1C/Workspace1C/Views/HowToBuyWindow.cs:32),
[`HowToBuyWindow.Avalonia.cs:37-42`](Workspace1C/Workspace1C/Views/HowToBuyWindow.Avalonia.cs:37)),
контент длинный; кнопки в нижней строке, но при 125–150 % и на низких экранах окно
обрезается/уходит за рабочую область. В Avalonia `root = Rows(0,-1,0)` — кнопки в строке 2,
но высота фиксирована и не клампится по рабочей области.

**Требования.** Кнопки «Активировать»/«Закрыть» видны всегда; окно выше/шире, клампится по
рабочей области; прокручивается только содержимое (шаги, контакты), не кнопки.

**Правки.**

- WPF: `WpfUi.SetupWindow(this, "Pro.HowToBuy.Title", 720, 720)` (или
  `WindowBoundsPolicy.Recommend` + `WindowWorkAreaLimiter.Attach`), `MinHeight = 520`,
  `MaxHeight` по рабочей области; `root` — `DockPanel`/`Grid` с `ScrollViewer` только для
  `panel`, кнопки `Dock.Bottom`.
- Avalonia: `Width = 720`, `Height = 720`, `MinHeight = 520`,
  `ApplyWorkAreaLimits()` по образцу `CustomProgramsWindow.Avalonia.cs:65-77`; `Rows(0,-1,0)`
  сохранить, кнопки — вне `ScrollViewer`.
- Проверить, что строка статуса (`StatusText`) не выталкивает кнопки.

**Тесты.** Новый `Issue165HowToBuyLayoutTests`:
`Buttons_AreOutsideScrollViewer_BothPlatforms`, `Window_ClampsToWorkArea`,
`StartSize_AtLeast_720x640`; обновить ожидание в
[`Issue119AdaptiveWindowTests.cs:289`](Workspace1C/Workspace1C.Tests/Issue119AdaptiveWindowTests.cs:289)
(было 620×600) и при необходимости `HowToBuyWindowV100153Tests`.

**DoD.** На 1366×768@100 % и 1920×1080@125 % кнопки видны без прокрутки; прокрутка — только
контент; окно не выходит за экран; тесты обновлены.

---

### 4.2. Issue #157 — «Очистка кеша — индикация расчёта» (текст)

**Причина.** Пока идёт расчёт, показывается «Идёт расчёт размеров кэша…»
([`ru.json:1385-1386`](Workspace1C/Workspace1C/Localization/Languages/ru.json:1385)),
пользователь просит «Размеры кэша обновляются», а после исчезновения полосы прогресса —
«Размеры кэша рассчитаны» (последнее уже есть, `CacheClean.CalculatingDone`).

**Правки.**

- `ru.json`: `CacheClean.Calculating` → «Размеры кэша обновляются»;
  `CacheClean.CalculatingProgress` → «Размеры кэша обновляются… {0} из {1}».
  `en.json`: «Cache sizes are being updated», «… {0} of {1}».
- Логика уже корректна: [`CacheCleanWindow.xaml.cs:792-811`](Workspace1C/Workspace1C/Views/CacheCleanWindow.xaml.cs:792)
  и [`CacheCleanWindow.Avalonia.cs:517-537`](Workspace1C/Workspace1C/Views/CacheCleanWindow.Avalonia.cs:517)
  — при `IsComplete` статус «рассчитаны», полоса скрывается в `finally`.
- Убедиться, что при завершении прогресс-бар скрывается **до** смены текста (порядок в
  `UpdateCalculatingState`), и что `UpdateProgressStatus` не перезаписывает финальный текст.

**Тесты.** Дополнить `Issue157CacheCleanProgressTests`:
`StatusText_WhileCalculating_IsUpdating`, `StatusText_AfterComplete_IsCalculated`,
`ProgressBar_Hidden_WhenComplete`, `Localization_UsesNewWording` (ru/en).

**DoD.** Во время расчёта — «Размеры кэша обновляются», после — «Размеры кэша рассчитаны»;
полоса исчезает; WPF/Avalonia; ru/en.

---

### 4.3. Issue #162 — «Скачивание версии платформы — баги»

**Причина (а).** В тёмной теме значения в полях «Учётная запись ИТС» и «Папка загрузки»
нечитаемы: учётная запись выводится обычным `TextBlock` без явного цвета
([`PlatformDownloadWindow.xaml:199-213`](Workspace1C/Workspace1C/Views/PlatformDownloadWindow.xaml:199)),
у `TextBox` папки нет явных кистей/рамки
([`PlatformDownloadWindow.xaml:222-230`](Workspace1C/Workspace1C/Views/PlatformDownloadWindow.xaml:222)).
В Avalonia — аналогично в [`PlatformDownloadWindow.Avalonia.cs`](Workspace1C/Workspace1C/Views/PlatformDownloadWindow.Avalonia.cs:1).

**Причина (б).** Итоговое имя файла формируется с префиксом версии:
[`OneCUpdatesService.BuildTargetFileName`](Workspace1C/Workspace1C/Services/OneCUpdatesService.cs:2019)
возвращает `"{version}_{fileName}"`; вызывается из
[`PlatformDownloadViewModel.cs:741-742`](Workspace1C/Workspace1C/ViewModels/PlatformDownloadViewModel.cs:741)
и [`PlatformUpdateViewModel.cs:432`](Workspace1C/Workspace1C/ViewModels/PlatformUpdateViewModel.cs:432),
`:642`. В логе показывается исходное имя — расхождение.

**Правки.**

- WPF: учётной записи — `Foreground="{DynamicResource TextPrimaryBrush}"`, обернуть строку
  в `Border` с `BorderBrush="{DynamicResource BorderBrushColor}"` и `Padding`;
  `TextBox` папки — `Background="{DynamicResource ContentBackgroundBrush}"`,
  `Foreground="{DynamicResource TextPrimaryBrush}"`,
  `BorderBrush="{DynamicResource BorderBrushColor}"`, `BorderThickness="1"`, `Padding`.
- Avalonia: те же кисти через `ThemeBrushes.Bind`, рамка `Border` вокруг строки учётной
  записи; проверить обе темы.
- `BuildTargetFileName`: возвращать **только** `SanitizeFileName(fileName)`; версию
  использовать лишь если имя пустое. Если имя уже начинается с версии — не дублировать.
  Обновить XML-комментарий и все вызовы; лог/UI должны показывать то же имя, что пишется
  на диск.
- Проверить `PlatformUpdateViewModel` (сценарий «Скачать и установить»): префикс больше не
  нужен; при совпадении имён — стандартная обработка конфликта.

**Тесты.** Новый `Issue162PlatformDownloadTests`:
`BuildTargetFileName_NoVersionPrefix`, `BuildTargetFileName_EmptyName_UsesVersion`,
`BuildTargetFileName_AlreadyPrefixed_NoDuplicate`,
`PlatformDownloadViewModel_SavesUnderLogName` (мок загрузчика),
`Wpf_DarkTheme_FieldsHaveContrastBrushes` (source-guard),
`Avalonia_AccountRow_HasBorderAndPrimaryForeground`.
Обновить существующие `PlatformDownload*` тесты, если они ожидали префикс.

**DoD.** В тёмной теме текст читаем, у полей есть рамка; файл сохраняется под именем из лога
(например `server64_8_5_1_1621.zip`), без префикса версии; тесты зелёные.

---

### 4.4. Issue #143 — «Резервные копии средствами СУБД»

Требования последнего комментария RafaIS:

1. Подпись «Каталог с программой …» вместо неочевидного «Каталог pg_dump / sqlcmd:».
2. После «шевеления» возникает ошибка при успешных проверках, файл копии не создаётся
   (хотя начал создаваться) — найти причину по `dbms-backup-debug.log`/логам и устранить.
3. Окно при работе — **немодальное**, со своей плашкой на панели задач; задание работает в
   фоне, можно переключиться на стартер.

**Причина/точки.**

- Подпись: [`ru.json:4668`](Workspace1C/Workspace1C/Localization/Languages/ru.json:4668)
  (`Dbms.Tools`), используется в
  [`DbmsBackupWindow.cs:66-68`](Workspace1C/Workspace1C/Views/DbmsBackupWindow.cs:66) и
  [`DbmsBackupWindow.Avalonia.cs:70-72`](Workspace1C/Workspace1C/Views/DbmsBackupWindow.Avalonia.cs:70).
- Ошибка: копия пишется в `target`
  ([`DbmsBackupService.cs:512-596`](Workspace1C/Workspace1C/Services/DbmsBackupService.cs:512)),
  при сбое удаляется (`TryDeleteAsync(target...)`, строки ~478-479), затем ротация
  (`BackupRotation.Apply`, ~590-596). Возможные причины: путь с пробелами/кавычками,
  нехватка прав, занятый файл, ошибка утилиты после частичной записи, удаление только что
  созданного файла ротацией при малом `KeepCount`, неверные аргументы `pg_dump`/`sqlcmd`.
  Диагностика: `CM_DEBUG_DBMS=1` →
  [`DbmsBackupTrace.cs`](Workspace1C/Workspace1C/Services/DbmsBackupTrace.cs:1)
  (`dbms-backup-debug.log`, этапы `click/enter/profile/validate/upsert/enqueue/worker-start/io-begin/io-end/timeout-*/exit/error`).
- Модальность: WPF — `window.ShowDialog()`
  ([`MainViewModel.Features.cs:220-224`](Workspace1C/Workspace1C/ViewModels/MainViewModel.Features.cs:220));
  Avalonia — `window.ShowDialogSync(OwnerWindow())`
  ([`MainViewModel.Avalonia.Features.cs:172-173`](Workspace1C/Workspace1C/ViewModels/MainViewModel.Avalonia.Features.cs:172)).

**Правки.**

- Локализация: `Dbms.Tools` → «Каталог с программой pg_dump / sqlcmd:» / «Folder with
  pg_dump / sqlcmd:»; добавить `Dbms.ToolsHint` («Укажите папку, где лежит утилита…»).
- Диагностика: добавить в `DbmsBackupTrace` запись кода возврата утилиты, длительности и
  **последних строк stderr** (без паролей); в сообщение об ошибке — этап, путь и код
  возврата.
- Найти и устранить причину по логу (T7 начинается с воспроизведения и анализа лога,
  приложенного/собранного пользователем; если лога нет — воспроизвести на временном
  каталоге с пробелами и на UNC). Обязательно покрыть:
  - путь с пробелами/кириллицей/завершающим слэшем;
  - `KeepCount` меньше числа файлов (новый файл не должен удаляться);
  - частичная запись + ненулевой код возврата → файл удаляется, сообщение понятное;
  - успешный сценарий → файл существует и имя совпадает с ожидаемым.
- Немодальность:
  - WPF: `window.Show()` (с `Owner`, `ShowInTaskbar = true`,
    `WindowStartupLocation = CenterOwner`), не блокировать главное окно; `IsBusy`
    остаётся в VM; при закрытии окна во время работы — не отменять задание без
    подтверждения (или явная кнопка «Отмена»).
  - Avalonia: `window.Show(OwnerWindow())`, `ShowInTaskbar = true`;
    `ShowDialogSync` убрать из меню. В `TestDeployWindow` оставить модальный вызов или
    перевести на `Show` — решение зафиксировать в коде и тесте.
  - Убедиться, что окно не становится «фантомным»: `Closed` не убивает фоновую задачу
    преждевременно; прогресс/журнал продолжают обновляться.

**Тесты.** Дополнить `Issue143DbmsBackupTests`/`Issue143ResponsivenessTests`:
`Run_ToolFailsAfterPartialWrite_RemovesFile_AndReportsStage`,
`Run_Success_CreatesFile_AndRotationKeepsIt`,
`OutputDirectory_WithSpacesAndTrailingSlash_Normalized`,
`Window_IsShownNonModally_FromMenu` (source-guard WPF/Avalonia),
`ToolsLabel_MentionsProgramFolder` (ru/en),
`Trace_ContainsExitCode_AndStderrTail_WithoutPassword`.

**DoD.** Подпись понятна; ошибка не возникает при повторных проверках/запусках, причина
устранена и покрыта тестом; копия создаётся; окно немодальное, есть плашка на панели задач,
задание работает в фоне; WPF/Avalonia; логи без паролей.

---

### 4.5. Issue #146 — «Сломанное меню Утилиты»: ПРО-подменю не подчиняется правке

**Причина.** Группа ПРО не является частью сохранённой компоновки: она **внедряется при
отображении** в [`UtilitiesMenuResolver.ResolveForDisplay`](Workspace1C/Workspace1C/Services/UtilitiesMenuResolver.cs:45)
(`AddProGroup`, строки 93-118), при этом принудительно `tools.Visible = true` и удаляются
старые узлы. Редактор структуры загружает компоновку из хранилища — группы ПРО там нет,
поэтому её нельзя скрыть/переместить/отредактировать; видимость раздела «Инструменты»
игнорируется. В стандартной компоновке `GroupPro` отсутствует
([`UtilitiesMenuCatalog.cs:366`](Workspace1C/Workspace1C/Services/UtilitiesMenuCatalog.cs:366) —
константа есть, узла нет).

**Требования.** ПРО-подменю — обычная группа компоновки: скрывается, перемещается,
редактируется как остальные; при скрытом разделе «Инструменты» не всплывает; пункты
остаются гейтированными (без ключа — « · ПРО» и окно «Лицензия», в публичной сборке —
placeholder).

**Правки.**

- [`UtilitiesMenuLayouts.Default`](Workspace1C/Workspace1C/Services/UtilitiesMenuCatalog.cs:1100)
  (или соответствующий метод): добавить узел `GroupPro` в `GroupTools` со стабильным
  `NodeId`, `Visible = true`, детьми — пунктами каталога с `RequiredFeature` вне public core.
- `ResolveForDisplay`: не внедрять группу заново каждый раз; синхронизировать **только
  состав** (добавлять новые пункты, убирать исчезнувшие) внутри существующего узла,
  сохраняя `Visible`, позицию, `DisplayMode`, `NodeId`. Не выставлять `tools.Visible = true`.
  Если узла нет (старая компоновка) — добавить его один раз и сохранить в хранилище при
  первой правке.
- Редактор `UtilitiesMenuEditorWindow` (WPF/Avalonia): показывать группу ПРО в дереве,
  разрешить скрытие/перемещение/переименование по общим правилам; для пунктов — те же
  действия. Убедиться, что `UtilitiesMenuResolver.Resolve` (для редактора) возвращает
  компоновку с группой.
- Гейтинг не трогать: `ExecuteExternalGated`, `RequiresProKey`, placeholder-пункты.

**Тесты.** Обновить `Issue119ProVisibilityTests` (см. §1.2 п.5), новый
`Issue146ProGroupEditingTests`:
`DefaultLayout_ContainsProGroupUnderTools`,
`ResolveForDisplay_RespectsHiddenProGroup`,
`ResolveForDisplay_HiddenTools_DoesNotForceVisible`,
`Editor_SeesProGroup_AndCanHideIt`,
`ProItems_RemainGated_WithoutKey`,
`PublicBuild_Placeholders_RemainInProGroup`.

**DoD.** ПРО-подменю скрывается/перемещается/редактируется; настройки правки меню
применяются; гейтинг сохранён; тесты обновлены; WPF/Avalonia.

---

### 4.6. Issue #158 — «Linux — мои программы»

**Причины (по коду).**

1. Группа скрыта при отсутствии программ:
   [`MainWindow.Avalonia.Tree.cs:1792-1793`](Workspace1C/Workspace1C/Views/MainWindow.Avalonia.Tree.cs:1792)
   — `root.Bind(IsVisible, HasCustomPrograms)`; `HasCustomPrograms`
   ([`MainViewModel.CustomPrograms.cs:34-35`](Workspace1C/Workspace1C/ViewModels/MainViewModel.CustomPrograms.cs:34)).
2. Иконка шестерёнки — ключ `IconCog`
   ([`MainWindow.Avalonia.Tree.cs:1810-1813`](Workspace1C/Workspace1C/Views/MainWindow.Avalonia.Tree.cs:1810)),
   которого нет в наборах иконок; резолвер откатывается на `IconFolder`
   ([`ThemeFeatureMap.cs:217-219`](Workspace1C/Workspace1C/Themes/ThemeFeatureMap.cs:217)).
3. Ошибка при добавлении `/usr/bin/mc` — вероятно, извлечение иконки исполняемого файла
   ([`Converters/Avalonia/CustomProgramIconConverter.Avalonia.cs:29-46`](Workspace1C/Workspace1C/Converters/Avalonia/CustomProgramIconConverter.Avalonia.cs:29))
   или сохранение/чтение списка; точная причина — по `app-*.log` (T9 начинается с
   воспроизведения и чтения лога).
4. В редакторе нет строки с добавленной программой — проверить, что и меню, и редактор
   используют один и тот же `ICustomProgramsStore` и каталог активного профиля
   ([`CustomProgramsStore.cs:45-54`](Workspace1C/Workspace1C/Services/CustomProgramsStore.cs:45),
   [`CustomProgramsViewModel.cs:26-42`](Workspace1C/Workspace1C/ViewModels/CustomProgramsViewModel.cs:26),
   [`MainViewModel.CustomPrograms.cs:66-84`](Workspace1C/Workspace1C/ViewModels/MainViewModel.CustomPrograms.cs:66)).

**Требования.** При отсутствии программ — пустая группа «Мои программы» с кнопкой
настроек (и подсказкой «Добавьте программу»); шестерёнка — иконка настроек; добавление
программы не даёт ошибку; редактор показывает добавленную программу.

**Правки.**

- Видимость: заголовок + шестерёнка видны всегда; список/иконки — только при наличии
  программ; при пустом списке — `TextBlock` `Launch.CustomPrograms.EmptyHint` и кнопка
  «Настроить программы…».
- Иконка: заменить `IconCog` на `IconSettings` (есть в наборах,
  [`ThemeFeatureMap.cs:388`](Workspace1C/Workspace1C/Themes/ThemeFeatureMap.cs:388)) либо
  добавить `IconCog` во все наборы; тест покрытия иконок.
- Ошибка: обернуть извлечение иконки в безопасный путь (возврат `null` без исключения),
  проверить `CustomProgramLauncher`/сохранение; в лог — понятная причина.
- Редактор: синхронизировать источник (singleton store), после сохранения — `ReloadCustomPrograms`
  и обновление `Programs` в открытом редакторе; при открытии редактора из настроек —
  перечитывать файл.

**Тесты.** Новый `Issue158CustomProgramsLinuxTests`:
`EmptyGroup_IsVisible_WithSettingsButton`,
`GearIcon_UsesExistingIconKey`,
`AddProgram_WithBinaryPath_DoesNotThrow`,
`Editor_LoadsPrograms_FromSameStore`,
`CustomProgramsWindow_UsesIconSettingsKey`,
`ReloadAfterSave_UpdatesPanel`.

**DoD.** Пустая группа с кнопкой настроек; иконка шестерёнки; нет ошибки при добавлении;
строка в редакторе; тесты; Linux.

---

### 4.7. Issue #159 — «Linux — правый клик на Утилиты»: не открывается редактор

**Причина.** Оба входа (правый клик и настройки) вызывают один и тот же
`UtilitiesMenuEditorWindow`, а исключения глушатся:
[`MainWindow.Avalonia.Columns.cs:738-752`](Workspace1C/Workspace1C/Views/MainWindow.Avalonia.Columns.cs:738)
и [`SettingsWindow.Avalonia.cs:5220-5233`](Workspace1C/Workspace1C/Views/SettingsWindow.Avalonia.cs:5220).
Поэтому «не открывается» = конструктор/показ падает, а пользователь не видит причины.
Правый клик навешан на `PointerPressed` кнопки
([`MainWindow.Avalonia.cs:1241-1248`](Workspace1C/Workspace1C/Views/MainWindow.Avalonia.cs:1241),
`:1298-1305`) — проверить, что событие доходит (кнопка не гасит его).

**Правки.**

- Убрать «немой» `catch`: логировать `IAppLogger.Error` с типом и сообщением, показывать
  пользователю понятное сообщение (`UtilitiesMenuEditor.OpenFailed`) вместо тишины.
- Воспроизвести на Linux-сборке (`FORCE_LINUX=1`, xvfb), найти исключение (вероятные
  кандидаты: отсутствующий ключ иконки/ресурс, null-store, несовместимый контрол),
  исправить.
- Проверить оба входа: правый клик по кнопке раздела и кнопка в настройках; убедиться, что
  редактор открывается и сохраняет компоновку.
- Добавить smoke-тест конструирования окна (без показа) в тестах.

**Тесты.** Новый `Issue159UtilitiesEditorLinuxTests`:
`Editor_Constructs_WithNullStore_AndWithStore`,
`OpenFromSettings_LogsAndShowsError_OnFailure`,
`RightClickHandler_IsAttached_ToSectionButtons`,
`Editor_Save_RoundTripsLayout` (с временным store).

**DoD.** Редактор открывается по правому клику и из настроек; ошибки логируются; тесты;
Linux.

---

### 4.8. Issue #160 — «Linux — лишний ESC»

**Причина.** ESC обрабатывается в [`ModalWindowBase.OnKeyDown`](Workspace1C/Workspace1C/Views/ModalWindowBase.cs:946)
(`CloseAsCancel`), а также в главном окне
([`MainWindow.Avalonia.Hotkeys.cs:489-504`](Workspace1C/Workspace1C/Views/MainWindow.Avalonia.Hotkeys.cs:489)).
При вложенном диалоге (настройки → редактор) фокус может остаться в родителе: ESC закрывает
родителя, а вместе с ним — и дочернее окно. Нужен инвариант «ESC закрывает только самое
верхнее окно».

**Правки.**

- В `ModalWindowBase` перед `CloseAsCancel()` проверять наличие видимого дочернего окна
  (`OwnedWindows`/`Owner`-цепочка): если есть — не закрывать родителя, передать фокус
  ребёнку (`Activate()`), `e.Handled = true`.
- Убедиться, что `ShowDialogSync` активирует дочернее окно (уже есть `Dispatcher.Post(Activate)`,
  строка ~911) — проверить на Linux/X11, при необходимости `Focus()` на первом контроле.
- В главном окне закрывать ровно один верхний диалог (уже `TopmostModalDialog()`), не
  трогать остальные.
- Добавить трассировку `ModalCloseTrace` для ESC с именем окна (для диагностики).

**Тесты.** Новый `Issue160EscapeRoutingTests`:
`Escape_WithChildDialog_DoesNotCloseParent` (source-guard/логика),
`Escape_ClosesOnlyTopmost`,
`ModalWindowBase_ActivatesChildOnOpen`,
`MainWindow_Escape_DoesNotCloseAllDialogs`.

**DoD.** ESC в редакторе закрывает только редактор; настройки и структура остаются; тесты;
Linux (и проверка, что Windows не изменился).

---

### 4.9. Issue #161 — «Положение блока „Доля цвета папки“»

**Причина.** В WPF блок «Доля цвета папки» стоит **после** редактора цветов:
[`SettingsWindow.xaml:1527-1575`](Workspace1C/Workspace1C/Views/SettingsWindow.xaml:1527)
(цвета) и `:1577-1624` (доли). В Avalonia блок уже сверху — паритет нарушен.

**Правки.** Переместить `GroupBox` «Доля цвета папки» (строки 1577-1624) **перед**
`GroupBox` «Цвета» (1527-1575) в `SettingsWindow.xaml`; проверить порядок в
`SettingsWindow.Avalonia.cs` (вкладка «Тема») и при необходимости привести к тому же
порядку; имена контролов (`GroupColorTintSlider`, `GroupColorHoverTintSlider`) не менять.

**Тесты.** Новый `Issue161GroupColorTintOrderTests`:
`Wpf_TintBlock_BeforeColorsBlock`, `Avalonia_TintBlock_BeforeColorsBlock`,
`Sliders_StillBound_AndHandlersPresent`.

**DoD.** В Windows блок сверху, как в Linux; значения и обработчики работают; тесты.

---

## 5. Сводная таблица изменений по файлам

| Issue | Основные файлы |
|---|---|
| #164 | `App.xaml.cs`, `App.axaml.cs`, `Views/LoginWindow.xaml(.cs)`, `Views/LoginWindow.Avalonia.cs`, `Services/TitleSuffixResolver.cs`, `ViewModels/MainViewModel.Display*.cs`, локализация |
| #163 | `Services/Secrets/*` (новые), `Services/InfobaseRepository.cs`, `Services/ItsAccountsStore.cs`, `Services/FeatureDataStore.cs`, `Models/UserProfile.cs`, `Services/ProfileService.cs`, `ViewModels/ProfilesViewModel.cs`, `Views/ProfilesPanel.xaml`, `Views/ProfilesWindow.Avalonia.cs`, локализация |
| #165 | `Views/HowToBuyWindow.cs`, `Views/HowToBuyWindow.Avalonia.cs` |
| #157 | `Localization/Languages/ru.json`, `en.json` (+ тесты) |
| #162 | `Views/PlatformDownloadWindow.xaml`, `Views/PlatformDownloadWindow.Avalonia.cs`, `Services/OneCUpdatesService.cs`, `ViewModels/PlatformDownloadViewModel.cs`, `ViewModels/PlatformUpdateViewModel.cs` |
| #143 | `Services/DbmsBackupService.cs`, `Services/DbmsBackupTrace.cs`, `ViewModels/DbmsBackupViewModel.cs`, `Views/DbmsBackupWindow.cs/.Avalonia.cs`, `ViewModels/MainViewModel.Features.cs`, `ViewModels/MainViewModel.Avalonia.Features.cs`, локализация |
| #146 | `Services/UtilitiesMenuResolver.cs`, `Services/UtilitiesMenuCatalog.cs` (default layout), `Views/UtilitiesMenuEditorWindow.*`, `Views/MainWindow.UtilitiesMenu.cs`, `Views/MainWindow.Avalonia.Tree.cs` |
| #158 | `Views/MainWindow.Avalonia.Tree.cs`, `ViewModels/MainViewModel.CustomPrograms.cs`, `Services/CustomProgramsStore.cs`, `Views/CustomProgramsWindow.Avalonia.cs`, `Converters/Avalonia/CustomProgramIconConverter.Avalonia.cs`, `Themes/ThemeFeatureMap.cs`, локализация |
| #159 | `Views/MainWindow.Avalonia.Columns.cs`, `Views/SettingsWindow.Avalonia.cs`, `Views/UtilitiesMenuEditorWindow.Avalonia.cs` |
| #160 | `Views/ModalWindowBase.cs`, `Views/MainWindow.Avalonia.Hotkeys.cs` |
| #161 | `Views/SettingsWindow.xaml`, `Views/SettingsWindow.Avalonia.cs` |
| ПРО-сборка | `Workspace1C.csproj`, `build-windows-single-file.ps1`, `build-linux-single-file.sh`, `.github/workflows/release.yml`, README/PRO-доки, `Issue119ProVisibilityTests.cs` |

---

## 6. Порядок выполнения и зависимости

```
T1 (#164) ──► T2 (#163 ядро) ──► T3 (#163 UI/сценарии)
                                   │
T4 (#165) ─ T5 (#157) ─ T6 (#162) ─┤
T7 (#143) ─────────────────────────┤
T8 (#146) ──► T9 (#158) ──► T10 (#159) ──► T11 (#160)
T12 (#161) ────────────────────────┤
                                   ▼
                    T13 (ПРО-сборка/доки) ──► T14 (версия/CHANGELOG/README/заметки)
                                   ▼
                    T15 (полный прогон + single-file WPF/Linux)
                                   ▼
                    T16 (коммит/push/тег/релиз) ──► T17 (проверка Telegram)
                                   ▼
                    T18 (комментарии в issues, без закрытия)
```

T1–T12 можно выполнять последовательно; T13 не зависит от UI-правок, но должен быть
завершён до T14. T14 — единственная задача, меняющая версию. T15 — после T14.

---

## 7. Декомпозиция на задачи `new_task` (режим `code`)

Каждая задача самодостаточна: в её тексте должны быть цель, файлы, требования, тесты, DoD
и запрет менять версию (кроме T14) и закрывать issues.

### T1 — #164: запрос пароля учётной записи при старте, `%app_user%`, имя в окне входа

**Границы.** Только `App.xaml.cs`, `App.axaml.cs`, `LoginWindow*`, `TitleSuffixResolver`,
`MainViewModel.Display*`, локализация, новые тесты `Issue164*`. Версию не менять.
**Сделать.** Условие показа входа при одной защищённой записи; скрытие списка при одной
записи; подпись «Пароль учётной записи **Имя**» (жирное имя) в WPF и Avalonia; токен
`%app_user%` (+ подсказка в настройках); тесты; ru/en.
**Проверки.** `dotnet build` WPF и `-p:ForceLinux=true`; `dotnet test --filter Issue164`;
`dotnet test --filter "TitleSuffix|Login"`.
**DoD.** §2.6.

### T2 — #163 (часть 1): криптоядро, сеансовый ключ, шифрование хранилищ

**Границы.** Новые `Services/Secrets/*`, `Models/UserProfile.cs` (флаг),
`Services/ProfileService.cs` (сеансовый ключ/флаг), `InfobaseRepository`, `ItsAccountsStore`,
`FeatureDataStore`, тесты `Issue163*` (крипто/хранилища). UI не трогать (T3). Версию не менять.
**Сделать.** `IPasswordProtector` (AES-GCM, PBKDF2, конверт `enc:v1:`), `PasswordSession`,
`SecretJsonProtector` с белым списком полей; интеграция чтения/записи; миграция «открытый →
зашифрованный» только при включённом флаге; отсутствие ключа не роняет приложение.
**Проверки.** `dotnet test --filter Issue163`; ручная проверка, что в `infobases.json`/
`its_accounts.json` нет открытых паролей при включённом флаге.
**DoD.** §3.7 (крипто/хранилища).

### T3 — #163 (часть 2): галочка «Шифровать этим все пароли», смена пароля, UI

**Границы.** `ProfilesViewModel`, `ProfilesPanel.xaml`, `ProfilesWindow.Avalonia.cs`,
`ProfileService.SetPassword`, локализация, тесты `Issue163*` (UI/сценарии). Зависит от T2.
**Сделать.** Галочка под «Защитить паролем» (доступна только при пароле); подтверждения
включения/выключения; запрос пароля, если ключа нет; перешифровка при смене пароля;
возврат к открытому виду при снятии; обе платформы; ru/en.
**Проверки.** `dotnet test --filter Issue163`; сценарии: включить → перезапуск → пароли
читаются; сменить пароль → читаются; снять галочку → открытый вид.
**DoD.** §3.7.

### T4 — #165: размеры и кнопки окна «Как купить PRO»

**Границы.** `HowToBuyWindow.cs`, `HowToBuyWindow.Avalonia.cs`, обновление
`Issue119AdaptiveWindowTests`/`HowToBuyWindowV100153Tests`, новый `Issue165*`.
**Сделать.** Увеличить/клампить окно по рабочей области, кнопки вне прокрутки, прокрутка
только контента; обе платформы.
**Проверки.** `dotnet test --filter "Issue165|HowToBuy|Issue119AdaptiveWindow"`.
**DoD.** §4.1.

### T5 — #157: формулировки статуса расчёта кэша

**Границы.** Только `ru.json`, `en.json`, тесты `Issue157*`.
**Сделать.** «Размеры кэша обновляются» во время расчёта, «Размеры кэша рассчитаны» после;
проверить порядок скрытия полосы и финального текста в обоих окнах.
**Проверки.** `dotnet test --filter Issue157`.
**DoD.** §4.2.

### T6 — #162: контраст полей и имя файла без префикса версии

**Границы.** `PlatformDownloadWindow.xaml/.Avalonia.cs`, `OneCUpdatesService`,
`PlatformDownloadViewModel`, `PlatformUpdateViewModel`, тесты `Issue162*`/`PlatformDownload*`.
**Сделать.** Кисти/рамки для учётной записи ИТС и папки загрузки в тёмной теме;
`BuildTargetFileName` без префикса версии; согласовать лог и имя файла.
**Проверки.** `dotnet test --filter "Issue162|PlatformDownload"`.
**DoD.** §4.3.

### T7 — #143: подпись каталога утилиты, причина ошибки копии, немодальное окно

**Границы.** `DbmsBackupService`, `DbmsBackupTrace`, `DbmsBackupViewModel`,
`DbmsBackupWindow*`, `MainViewModel.Features.cs`, `MainViewModel.Avalonia.Features.cs`,
локализация, тесты `Issue143*`.
**Сделать.** «Каталог с программой pg_dump / sqlcmd»; воспроизвести ошибку с
`CM_DEBUG_DBMS=1`, найти причину по `dbms-backup-debug.log`, исправить и покрыть тестами
(частичная запись, ротация, пути с пробелами/UNC); перевести окно в немодальный режим с
плашкой на панели задач и фоновым заданием; обе платформы.
**Проверки.** `dotnet test --filter Issue143`; ручной сценарий: проверки → копия → файл
существует; окно не блокирует стартер.
**DoD.** §4.4.

### T8 — #146: ПРО-подменю как обычная редактируемая группа

**Границы.** `UtilitiesMenuResolver`, default layout в `UtilitiesMenuCatalog`,
`UtilitiesMenuEditorWindow*`, построители меню, `Issue119ProVisibilityTests`,
новый `Issue146ProGroupEditingTests`.
**Сделать.** Группа ПРО в сохранённой компоновке; `ResolveForDisplay` не форсирует
видимость и не пересоздаёт узел; редактор видит/скрывает/перемещает группу; гейтинг
сохранён; обе платформы.
**Проверки.** `dotnet test --filter "Issue146|Issue119ProVisibility|ProLicensing|Issue106FreePublicCore"`.
**DoD.** §4.5.

### T9 — #158: Linux «Мои программы» — пустая группа, иконка, ошибка, редактор

**Границы.** `MainWindow.Avalonia.Tree.cs`, `MainViewModel.CustomPrograms.cs`,
`CustomProgramsStore`, `CustomProgramsWindow.Avalonia.cs`, Avalonia-конвертер иконки,
`ThemeFeatureMap`, локализация, тесты `Issue158*`.
**Сделать.** Видимая пустая группа с кнопкой настроек; иконка `IconSettings` (или
добавить `IconCog`); устранить ошибку добавления (по логу); редактор показывает программу;
обе платформы (WPF — проверить, что не сломан).
**Проверки.** `dotnet test --filter Issue158`; ручной сценарий на Linux-сборке.
**DoD.** §4.6.

### T10 — #159: Linux — открытие редактора «Утилиты» по ПКМ и из настроек

**Границы.** `MainWindow.Avalonia.Columns.cs`, `SettingsWindow.Avalonia.cs`,
`UtilitiesMenuEditorWindow.Avalonia.cs`, локализация, тесты `Issue159*`. Зависит от T8.
**Сделать.** Убрать немой `catch`, логировать и показывать ошибку; найти и устранить
исключение; проверить оба входа; smoke-тест конструирования окна.
**Проверки.** `dotnet test --filter Issue159`; ручной сценарий на Linux.
**DoD.** §4.7.

### T11 — #160: Linux — ESC закрывает только верхнее окно

**Границы.** `ModalWindowBase`, `MainWindow.Avalonia.Hotkeys.cs`, тесты `Issue160*`.
Зависит от T10 (сценарий настройки → редактор).
**Сделать.** Инвариант «ESC закрывает только верхний диалог»; активация дочернего окна;
трассировка; тесты.
**Проверки.** `dotnet test --filter Issue160`; ручной сценарий на Linux.
**DoD.** §4.8.

### T12 — #161: блок «Доля цвета папки» наверх в Windows

**Границы.** `SettingsWindow.xaml`, `SettingsWindow.Avalonia.cs`, тесты `Issue161*`.
**Сделать.** Переставить блок перед редактором цветов; паритет с Linux; проверить
обработчики слайдеров.
**Проверки.** `dotnet test --filter "Issue161|Issue71SettingsWindowLayout|Issue93And95Subtabs"`.
**DoD.** §4.9.

### T13 — незакоммиченные изменения ПРО-сборки: проверить, дописать, зафиксировать

**Границы.** `Workspace1C.csproj`, `build-windows-single-file.ps1`,
`build-linux-single-file.sh`, `.github/workflows/release.yml`, README/`docs/pro/*`,
`Issue119ProVisibilityTests` (ожидания после T8), новые тесты/скрипт-проверки.
**Сделать.** §1.2 полностью: проверка модулей в Linux **до** publish; проверка путей
`ProSourceDir` (локальный `g:/h/Workspace1C/Workspace1C.Pro`, `-p:ProSourceDir`);
печать `HasProModules` в CI; согласование README/доков с моделью «одна программа»;
обновление тестов ПРО-видимости под T8; проверка «падение без модулей» и
`-AllowCommunity`/`ALLOW_COMMUNITY=1`.
**Проверки.**
- `dotnet msbuild Workspace1C/Workspace1C/Workspace1C.csproj -getProperty:HasProModules` = `true`;
- `build-windows-single-file.ps1` без ПРО → код 1 до publish; с ПРО → один exe;
- `FORCE_LINUX=1 ALLOW_COMMUNITY=1 ./build-linux-single-file.sh` → предупреждение;
- `dotnet test --filter "Issue119ProVisibility|ProLicensing|Issue106FreePublicCore"`.
**DoD.** Модель «одна программа с модулями ПРО» собирается и проверяется локально и в CI;
документация согласована; версия не менялась.

### T14 — версия 1.0.1.120, CHANGELOG, README, PRO-доки, релизные заметки, текст Telegram

**Границы.** [`Workspace1C.csproj:62-65`](Workspace1C/Workspace1C/Workspace1C.csproj:62),
`CHANGELOG.md`, `README.md`, `docs/pro/PRO-EDITION.md`, `docs/pro/HOW-TO-BUY-PRO.md`,
`rel_notes_1.0.1.120.md`, `docs/_telegram_msg_v1.0.1.120.txt`. Код не менять.
**Сделать.** Однократно повысить все четыре поля версии (`119` → `120`); добавить раздел
`## [1.0.1.120] — 2026-11-25` со всеми изменениями T1–T13 (включая шифрование паролей и
ПРО-модель); обновить README (бейдж, версия, таблица бинарей — размеры заполнить в T15,
раздел изменений, ПРО); обновить PRO-доки; создать `rel_notes_1.0.1.120.md`; текст анонса
для `-1004425443528` со ссылкой
`https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.120`.
**Проверки.** `dotnet msbuild ... -getProperty:Version/-getProperty:FileVersion` = `1.0.1.120`;
поиск `1.0.1.119` не находит в csproj/workflow/build-скриптах; тест локализации.
**DoD.** Версия 1.0.1.120 везде; документация полная; релизные заметки и текст Telegram
готовы.

### T15 — полный прогон тестов и сборка двух single-file бинарей

**Границы.** Только проверки и фиксация чисел в CHANGELOG/README. Код — только если
найдены регрессии (исправления допустимы).
**Сделать.** Полный `dotnet test` (WPF-конфигурация, без RID); сборка Windows
`build-windows-single-file.ps1 -Configuration Release` (модули ПРО обязательны, один exe,
smoke `--license-info --json` → `proModules=true`, `FileVersion=1.0.1.120`); сборка Linux
`FORCE_LINUX=1 ./build-linux-single-file.sh Release` (один файл, версия, при возможности
smoke под xvfb); зафиксировать число тестов, размеры, `FileVersion` в CHANGELOG/README.
**Проверки.** 0 падений; ровно один файл в `dist/win-x64` и `dist/linux-x64`;
`proModules=true`.
**DoD.** Все тесты зелёные; оба бинаря собраны; числа зафиксированы.

### T16 — коммит, push, тег `v1.0.1.120`, публикация релиза

**Границы.** Git-операции и наблюдение за workflow. Код не менять (кроме исправления
сбоя CI с фиксацией причины).
**Сделать.** Коммит только релизных файлов явным списком (без `bin/`, `obj/`, `dist/`,
без `git add -A`); push `main`; тег `v1.0.1.120` и push тега; дождаться workflow
«Публикация релиза»: jobs Windows/Linux/GitHub Release — success, шаг «Новость в Telegram» —
success; проверить Release: не prerelease, latest, assets `Workspace1C.exe` и
`Workspace1C-linux-x64`, `FileVersion` скачанного exe = `1.0.1.120`.
**Проверки.** `gh run view`, `gh release view`; отсутствие `proModules=false` в логах.
**DoD.** Релиз опубликован, оба ассета на месте, версия совпадает.

### T17 — независимая проверка доставки Telegram и файл подтверждения

**Границы.** Только чтение и `docs/_tg_verify_101120.txt`. Код не менять.
**Сделать.** Проверить лог шага (HTTP 200, `ok:true`, `chat_id=-1004425443528`, токен
замаскирован); независимо — публичная лента канала (`t.me/Workspace1C/<id>`), ссылка на
пост; сохранить в `docs/_tg_verify_101120.txt` дату/время UTC, run id, шаг, HTTP-код,
фрагмент ответа, `message_id`, `chat_id`, текст без токена. При сбое — отправить вручную
и также зафиксировать.
**DoD.** Файл подтверждения создан; пост найден; ссылка в документе.

### T18 — комментарии в issues #143, #146, #157–#165 (без закрытия)

**Границы.** Только `gh issue comment`. Issues **не закрывать**.
**Сделать.** В каждый issue — комментарий по шаблону: что исправлено, версия **1.0.1.120**,
ссылка на релиз `https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.120`, что
проверить (по разделу issue), просьба подтвердить. Для #163 — отдельно про галочку и
перешифровку; для #164 — про запрос пароля и `%app_user%`; для #143 — про подпись,
немодальность и просьбу прислать `dbms-backup-debug.log` при повторе; для #146 — про
редактируемость ПРО-подгруппы; для #158–#161 — Linux-сценарии; для #162 — контраст и имя
файла; для #157 — новые формулировки; для #165 — кнопки.
**DoD.** Комментарии опубликованы, issues остаются открытыми, тексты согласованы с
фактическим релизом.

---

## 8. Риски и меры

| Риск | Мера |
|---|---|
| Потеря паролей при включённом шифровании (забыт пароль учётной записи) | Явное предупреждение; `secrets.json` в резервной копии профиля; документация; verifier для раннего обнаружения неверного пароля |
| Регресс входа: окно при одной записи мешает CLI/автозапуску | Показ только в GUI-пути; CLI/`--profile` без окна; тесты |
| Шифрование ломает ручное редактирование JSON | Конверт — строка в том же поле; файл остаётся валидным JSON; обратная совместимость |
| #143: причина ошибки не воспроизводится | T7 начинает с лога `CM_DEBUG_DBMS=1`; добавить расширенную диагностику (код возврата, stderr-хвост) и тесты на частичную запись/ротацию; при отсутствии лога — воспроизведение на путях с пробелами/UNC |
| #146: редактирование ПРО-группы ломает гейтинг | Гейтинг остаётся в `ExecuteExternalGated`; тесты `Issue106FreePublicCore`/`ProLicensing`/`Issue119ProVisibility` |
| Linux-баги (#158–#160) не воспроизводятся на CI | Ручной прогон Linux-сборки (xvfb) в T9–T11; source-guard тесты; логи |
| Немодальное окно СУБД теряет прогресс при закрытии | Фоновая задача в VM; `Closed` не отменяет без подтверждения; тест |
| Сборка без модулей ПРО уходит в релиз | Проверка `HasProModules` до publish (T13), падение CI без `PRO_REPO_TOKEN`, проверка `proModules=true` в обоих бинарях |
| Telegram-анонс не доставлен | Workflow-шаг + независимая проверка T17; при сбое — ручная отправка и фиксация |
| Рассинхрон версии/документации | T14 — единственная точка смены версии; T15 фиксирует числа; поиск `1.0.1.119` в релизных файлах |

---

## 9. Общий DoD релиза 1.0.1.120

1. Все 11 issues исправлены, покрыты тестами, паритет WPF/Avalonia, ru/en.
2. Полный прогон тестов — 0 падений; существующие стражи (`Issue106FreePublicCore`,
   `ProLicensing`, `Issue119*`, `Issue146*`, `Issue143*`) не ослаблены.
3. Собраны ровно два single-file бинаря (Windows и Linux) с `PRO_MODULES`; проверка
   `proModules=true`; версия `1.0.1.120` в `FileVersion`/`InformationalVersion`.
4. CHANGELOG, README, `docs/pro/*`, `rel_notes_1.0.1.120.md`,
   `docs/_telegram_msg_v1.0.1.120.txt` обновлены.
5. Релиз `v1.0.1.120` опубликован (не prerelease, latest), ассеты на месте.
6. Telegram-анонс доставлен, подтверждение — `docs/_tg_verify_101120.txt`.
7. Комментарии в issues опубликованы; issues **не закрыты**.
