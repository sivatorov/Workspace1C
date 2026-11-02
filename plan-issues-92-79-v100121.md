# План исправлений v1.0.1.21 — issues #92, #79

Дата плана: 2026-11-02. Источник: `_issues_to_work_2026-11-02.md` (issues #92, #79 — последние комментарии 7OH от 2026-11-02 с архивами диагностики `diagnostics_20261102_082030.zip` / `diagnostics_20261102_081427.zip`; архивы в репозитории отсутствуют — анализ по описаниям и коду).

Версия: текущая **1.0.1.20** → следующая **1.0.1.21** (увеличивается только последнее число; в `Workspace1C/Workspace1C.csproj` поля Version/AssemblyVersion/FileVersion/InformationalVersion).

---

## 1. Issue #92 — «выгрузка списка баз в файл *.dt»

### 1.1 Root causes (по коду)

**RC-92-1 (главный, «зависание» без процессов): прерывание всего прогона вместо перехода к следующей базе.**
В [`BatchOperationsViewModel.RunAsync`](Workspace1C/ViewModels/BatchOperationsViewModel.cs:400) ветка fail-fast (клиент-серверная база без профиля СУБД, автоподстановка из rac не дала данных) после записи статуса ошибки выполняет **`return`** (строка 411) вместо **`continue`**:

```csharp
if (dbmsResolveFailed && resolvedProfile is null)
{
    failed++;
    ...
    item.IsBusy = false;
    return;          // ← прерывает ВЕСЬ RunAsync, а не только текущую базу
}
```

Последствия, точно совпадающие с репортом 7OH («на первой ошибку выдало, на второй просто повисло», «в диспетчере процессов 1cv8/ibcmd нет»):
- остальные отмеченные базы (вторая и далее) остаются в статусе Pending и никогда не обрабатываются — никакие процессы не запускаются;
- `IsRunning` не сбрасывается в false, `Summary` не формируется → окно «Массовых операций» остаётся заблокированным («повисло»), кнопка «Остановить» неактивна для возобновления, прогресс не движется.

**RC-92-2 (ошибка на первой базе): пустой тип СУБД из rac 8.5.4.**
В [`ClusterInfobaseCredentialsResolver.ToProfile`](Workspace1C/Services/ClusterInfobaseCredentialsResolver.cs:205) тип СУБД берётся из колонки «dbms» вывода «infobase summary list». У rac 8.5.4 (блочный формат вывода) колонка dbms в summary-выводе может быть пустой/отсутствующей — в логе 7OH: `тип СУБД «» не поддерживается утилитой ibcmd`. [`ParseDbms`](Workspace1C/Services/ClusterInfobaseCredentialsResolver.cs:227) возвращает null → профиль не строится → fail-fast → срабатывает RC-92-1. Дополнительный путь той же природы: «База не найдена в кластерах» при несовпадении имени — уже диагностируется (v1.0.1.17, содержимое кластера в логе).

**RC-92-3 (сопутствующее «зависанию»): длительные rac-пробы при нескольких кластерах/адресах.**
Таймаут одной rac-команды — 30 с ([`RacClient.CommandTimeout`](Workspace1C/Services/RacClient.cs:42)), но резолвер перебирает кластеры последовательно; при недоступном сервере несколько подряд по 30 с выглядят как зависание без внятного прогресса. ibcmd имеет таймаут 3 часа ([`IbcmdService.DefaultTimeout`](Workspace1C/Services/IbcmdService.cs:41)) — запуск ibcmd к недоступной СУБД «повисает» на часы; пользователь не видит, запустился ли процесс.

### 1.2 Правки

| Файл | Правка |
|------|--------|
| [`Workspace1C/ViewModels/BatchOperationsViewModel.cs`](Workspace1C/ViewModels/BatchOperationsViewModel.cs:411) | В fail-fast ветке заменить `return` на `continue` (записать статус/лог/время, снять `IsBusy`, продолжить цикл). Проверить, что при исключении/отмене `IsRunning` всегда сбрасывается (try/finally вокруг основного цикла). |
| [`Workspace1C/Services/ClusterInfobaseCredentialsResolver.cs`](Workspace1C/Services/ClusterInfobaseCredentialsResolver.cs:109) | Fallback при пустом `Dbms` в summary: для совпавшей ИБ догрузить полные свойства командой «infobase info» ([`IRacClient.GetInfobaseInfoAsync`](Workspace1C/Services/IRacClient.cs:220)) — там dbms/db-server/db-name присутствуют полнее; строить профиль из них. Логировать фактическое значение колонки (уже частично есть). |
| [`Workspace1C/Services/ClusterInfobaseCredentialsResolver.cs`](Workspace1C/Services/ClusterInfobaseCredentialsResolver.cs:74) | Общий таймаут на резолв одной базы (linked CTS с `CancelAfter` ~30 с на всю последовательность кластеров; rac-клиенту передаётся уже этот токен) — резолв не может тянуться дольше минуты; в лог — длительность каждой пробы. |
| [`Workspace1C/Services/IbcmdService.cs`](Workspace1C/Services/IbcmdService.cs:294) | Добавить статическое событие/колбэк `ProcessStarted(exePath, pid)`; в [`BatchOperationsViewModel`](Workspace1C/ViewModels/BatchOperationsViewModel.cs:414) писать в журнал «запущен ibcmd (PID …)» — пользователь видит, что процесс реально стартовал (иначе «повисло» неотличимо от работы). Опционально: для массовых операций таймаут ibcmd понижать до настраиваемого (по умолчанию оставить 3 ч, настройку добавить в AppSettings). |
| `Workspace1C/Models/AppSettings.cs` + окно настроек (обе платформы) | Опционально (если решим делать таймаут настраиваемым): «Таймаут ibcmd для массовых операций, мин». Минимальный вариант — не делать, только лог запуска. |
| Тесты: `Workspace1C.Tests/BatchOperationsTests.cs` | 1) Прогон, где первая база fail-fast, а вторая успешна: Summary = «1 из 2», вторая база обработана (регрессия RC-92-1). 2) `IsRunning == false` после прогона с ошибками. 3) Резолвер: summary с пустым dbms + фейковый `GetInfobaseInfoAsync` с dbms → профиль строится (RC-92-2). |

### 1.3 Критерии проверки

1. Список из 2+ клиент-серверных баз, у первой нет профиля СУБД и rac её не находит: первая — Failed с понятной причиной, вторая — обрабатывается, итоговая строка Summary выводится, кнопки окна разблокируются.
2. База с rac 8.5.4 (пустой dbms в summary): профиль СУБД резолвится через «infobase info», выгрузка идёт.
3. В журнале окна видна строка «запущен ibcmd (PID …)» на каждую обрабатываемую базу.

---

## 2. Issue #79 — «Развернуть тестовую копию»

### 2.1 Root cause (по коду)

Поля СУБД на форме мастера («Сервер СУБД», «Имя базы данных в СУБД», пользователь/пароль) описывают **новую (целевую) тестовую базу** — они нужны для создания БД (CrSQLDB, [`TestDeployService.CreateTarget`](Workspace1C/Services/TestDeployService.cs:203)). Но они же используются как **переопределение параметров СУБД для выгрузки ИСХОДНОЙ базы** через ibcmd: [`TestDeployService.BuildDbmsProfileOverride`](Workspace1C/Services/TestDeployService.cs:410) собирает профиль из `scenario.DbServer/DbName/DbUser/DbPassword`.

При этом с v1.0.1.14 (та же issue #79) поле «Имя базы данных в СУБД» автоподставляется из поля «Имя базы на сервере (Ref)» — а Ref предложен как имя **целевой** базы («<источник>_t», [`TestDeployPlanner.ProposeTargetName`](Workspace1C/Services/TestDeployPlanner.cs:48), [`TestDeployWindow.AutofillDbNameFromServerDbName`](Workspace1C/ViewModels/TestDeployViewModel.cs:91)).

Итог: выгрузка ibcmd выполняется к SQL-БД с именем **целевой** базы (которой на сервере СУБД ещё нет) → сообщение «База отсутствует в сервере». Это в точности формулировка 7OH: «попытка сделать копию с базы, которая должна по итогу получиться, а не той, которая исходная». Раньше баг не проявлялся, пока сервер СУБД/имена совпадали; на «чужих» серверах и с автоподстановками — воспроизводится стабильно (v1.0.1.20).

Дополнительно: `EnsureIbcmdProfileFromForm` ([`TestDeployViewModel.cs:218`](Workspace1C/ViewModels/TestDeployViewModel.cs:218)) и `TestDeployPlanner.Validate` ([`TestDeployPlanner.cs:277`](Workspace1C/Services/TestDeployPlanner.cs:277)) считают заполненные (целевыми значениями) поля СУБД достаточными для выгрузки — валидация «зелёная», а выгрузка гарантированно падает.

### 2.2 Правки

| Файл | Правка |
|------|--------|
| [`Workspace1C/Services/TestDeployService.cs`](Workspace1C/Services/TestDeployService.cs:277) | Переработать `DumpAsync` (движок ibcmd): профиль СУБД для выгрузки **источника** резолвится по цепочке: 1) сохранённый профиль источника (`DbmsBackupService.Load().Find(source.Id)`); 2) автоподстановка из rac по исходной базе (`ClusterInfobaseCredentialsResolver.ResolveAsync(source)` — данные из кластера, где реально живёт источник); 3) поля формы — **только** если они заведомо описывают источник (совпадают с `source.Connection.DatabaseName`/сохранённым профилем; иначе не использовать). `BuildDbmsProfileOverride` из полей целевой базы для выгрузки больше не применяется. |
| [`Workspace1C/Services/TestDeployService.cs`](Workspace1C/Services/TestDeployService.cs:62) | Разрешение профиля выгрузки — до старта операций: если ни профиль, ни rac, ни подходящие поля формы не дали данных — понятная ошибка валидации ДО шага «Выгрузка» («Для выгрузки исходной базы через ibcmd нужен профиль СУБД исходной базы…»), шаги создания/загрузки не стартуются. |
| [`Workspace1C/ViewModels/TestDeployViewModel.cs`](Workspace1C/ViewModels/TestDeployViewModel.cs:218) | `EnsureIbcmdProfileFromForm`: автосбор параметров выгрузки — из профиля **источника** (и rac), а не из полей целевой базы; автоподстановка `DbName` из Ref целевой базы не должна влиять на выгрузку. |
| [`Workspace1C/Services/TestDeployPlanner.cs`](Workspace1C/Services/TestDeployPlanner.cs:226) | `Validate`: требование `TestDeploy.NeedDbmsProfile` для движка ibcmd проверять по источнику (профиль/rac/совпадающие поля), а не по целевым полям формы. |
| [`Workspace1C/Views/TestDeployWindow.cs`](Workspace1C/Views/TestDeployWindow.cs:489) и `TestDeployWindow.Avalonia.cs` | Уточнить подписи/подсказки: блок СУБД формы — «параметры новой базы (для создания)»; в подсказке кнопки «Подставить из профиля СУБД» указать, что это параметры **исходной** базы для выгрузки. Минимально — тексты локализации, без перестройки формы. |
| [`Workspace1C/Localization/Languages/ru.json`](Workspace1C/Localization/Languages/ru.json:4258) и `en.json` | Переформулировать `TestDeploy.NeedDbmsProfile` (указать «исходной базы»), добавить ключ для ошибки «не найден профиль СУБД исходной базы и не удалось определить из кластера» и уточнить хинты блока СУБД. |
| Тесты: `Workspace1C.Tests/` (TestDeployTests / новый файл) | 1) `BuildDbmsProfileOverride`-замена: целевые поля формы с DbName «источник_t» НЕ попадают в профиль выгрузки. 2) Валидация проходит при наличии профиля источника, даже если поля формы целевые. 3) Ошибка выгрузки при полном отсутствии данных СУБД источника — текст с подсказкой. |

### 2.3 Критерии проверки

1. Мастер для серверной базы, у тестовой копии другое имя Ref и нет профиля СУБД: выгрузка идёт по данным из rac (имя БД — источника), ошибка «База отсутствует в сервере» не возникает.
2. Профиль СУБД источника есть, поля формы заполнены целевыми значениями: выгрузка идёт по профилю источника, создание целевой БД — по полям формы.
3. Ни профиля, ни rac: падение ДО старта с понятным сообщением, целевая база не создаётся наполовину.

---

## 3. Фича (issue #92, запрос aepkugesi): завершение сеансов и блокировки перед выгрузкой/загрузкой

Инфраструктура уже есть: [`IRacClient.TerminateSessionAsync`](Workspace1C/Services/IRacClient.cs:153), [`DisconnectConnectionAsync`](Workspace1C/Services/IRacClient.cs:170), [`GetSessionsAsync`](Workspace1C/Services/IRacClient.cs:77), [`UpdateInfobaseAsync`](Workspace1C/Services/IRacClient.cs:193) с ключами `--sessions-deny`, `--scheduled-jobs-deny`, `--denied-from/to` ([`RacClient.cs:706`](Workspace1C/Services/RacClient.cs:706)); для файловых баз — [`SessionLockService`](Workspace1C/Services/SessionLockService.cs:16) (файл 1cv8.cdn). Кластер/ИБ для rac находит [`ClusterInfobaseCredentialsResolver`](Workspace1C/Services/ClusterInfobaseCredentialsResolver.cs:26) (расширить: возвращать не только профиль, но и clusterId/infobaseId + RacConnectionParams).

### Предлагаемое решение

1. **UI** — в окне «Массовые операции» ([`BatchOperationsWindow.xaml`](Workspace1C/Views/BatchOperationsWindow.xaml) / `.Avalonia.cs`) блок «Перед операцией» (для выгрузки и загрузки):
   - ☐ «Завершить сеансы пользователей» (только клиент-серверные базы; rac `session terminate` по всем сеансам ИБ, с кредами администратора ИБ из базы/общей авторизации);
   - ☐ «Установить блокировку начала сеансов» + поле «Код разрешения» (опционально) и «Время действия блокировки» (по умолчанию «до конца операции»);
   - ☐ «Блокировать регламентные задания» (`--scheduled-jobs-deny=on`);
   - после операции — автоматическое снятие блокировки сеансов/заданий (restore), если ставились.
2. **Сервис** — новый чистый `BatchOpsPreparationService` (или методы в `ClusterInfobaseCredentialsResolver`): на каждую клиент-серверную базу — find cluster/infobase → terminate sessions (если включено) → `infobase update --sessions-deny=on [--permission-code=…] --scheduled-jobs-deny=on` → выполнение ibcmd-операции → `infobase update` обратно (deny off). Ошибки подготовки — в журнал окна, база пропускается (Skipped) либо (по флагу) операция продолжается; для файловых баз — `SessionLockService` (1cv8.cdn) без rac. Текущее состояние deny восстанавливать только на то, что было off (не трогать базы, уже заблокированные администратором).
3. **Настройки** — состояние чекбоксов сохранять в AppSettings (`BatchOps.TerminateSessions/…`), код разрешения — в памяти сессии, не сохранять (секретоподобное).
4. **Расширение резолвера** — `ResolveAsync` возвращает расширенный результат (профиль + clusterId + infobaseId + параметры подключения), чтобы подготовка не делала rac-запросы повторно.
5. **Расширение `RacInfobaseUpdateRequest`** — поле «код разрешения» (в rac — ключ `--permission-code` / denied-parameter; сверить со справкой rac «infobase update») — если ещё отсутствует.
6. Локализация: новые ключи `BatchOps.Prepare.*` (ru/en).
7. Одиночные выгрузки/загрузки (окно «Резервные копии СУБД»/BackupService) — в этот релиз **не включать**; зафиксировать в комментарии к issue #92, что фича сначала появляется в массовых операциях, далее распространяется.

### Оценка скоупа

Блок лёгкий к среднему (UI-блок на двух платформах + сервис). Допустимо разбить: v1.0.1.21 — фиксы RC-92-1/2/3 и RC-79 (обязательное), фича блокировок — тем же релизом при наличии ресурса, иначе — v1.0.1.22 с отдельным комментарием в issue #92.

---

## 4. Список файлов релиза v1.0.1.21

### Код
| Файл | Изменение | Issue |
|------|-----------|-------|
| [`Workspace1C/ViewModels/BatchOperationsViewModel.cs`](Workspace1C/ViewModels/BatchOperationsViewModel.cs:411) | `return` → `continue` в fail-fast; try/finally для `IsRunning`; лог «запущен ibcmd (PID…)»; хуки подготовки (п.3) | #92 |
| [`Workspace1C/Services/ClusterInfobaseCredentialsResolver.cs`](Workspace1C/Services/ClusterInfobaseCredentialsResolver.cs:205) | fallback «infobase info» при пустом dbms; общий таймаут резолва базы; расширенный результат (cluster/infobase id) | #92 |
| [`Workspace1C/Services/IbcmdService.cs`](Workspace1C/Services/IbcmdService.cs:294) | событие/колбэк `ProcessStarted(exe, pid)` | #92 |
| `Workspace1C/Services/BatchOpsPreparationService.cs` (новый) | завершение сеансов + блокировки сеансов/регламентных заданий до операции, восстановление после | #92 (aepkugesi) |
| [`Workspace1C/Services/TestDeployService.cs`](Workspace1C/Services/TestDeployService.cs:277) | профиль выгрузки — из данных ИСТОЧНИКА (профиль → rac → совместимые поля); пред-валидация до старта шагов | #79 |
| [`Workspace1C/ViewModels/TestDeployViewModel.cs`](Workspace1C/ViewModels/TestDeployViewModel.cs:218) | `EnsureIbcmdProfileFromForm` по источнику | #79 |
| [`Workspace1C/Services/TestDeployPlanner.cs`](Workspace1C/Services/TestDeployPlanner.cs:226) | валидация `NeedDbmsProfile` по источнику | #79 |
| [`Workspace1C/Views/BatchOperationsWindow.xaml`](Workspace1C/Views/BatchOperationsWindow.xaml) / `.xaml.cs` / `.Avalonia.cs` | блок «Перед операцией» (чекбоксы + код разрешения), обе платформы | #92 (aepkugesi) |
| [`Workspace1C/Views/TestDeployWindow.cs`](Workspace1C/Views/TestDeployWindow.cs:489) / `TestDeployWindow.Avalonia.cs` | уточнение подписей/хинтов блока СУБД | #79 |
| `Workspace1C/Models/RacInfobaseUpdateRequest.cs` (в `RacModels.cs`) | поле кода разрешения (если отсутствует) | #92 (aepkugesi) |

### Локализация
| Файл | Изменение | Issue |
|------|-----------|-------|
| [`Workspace1C/Localization/Languages/ru.json`](Workspace1C/Localization/Languages/ru.json:4258) | ключи `BatchOps.Prepare.*` (завершение сеансов, блокировки, код разрешения, восстановление); переформулировка `TestDeploy.NeedDbmsProfile`, хинты блока СУБД мастера | #92, #79 |
| `Workspace1C/Localization/Languages/en.json` | те же ключи, английские строки | #92, #79 |

### Тесты
| Файл | Изменение | Issue |
|------|-----------|-------|
| `Workspace1C.Tests/BatchOperationsTests.cs` | continue вместо return; IsRunning после ошибок; резолвер с пустым dbms → «infobase info» | #92 |
| `Workspace1C.Tests/` (TestDeployTests / новый) | выгрузка не использует целевые поля формы; валидация по источнику | #79 |
| `Workspace1C.Tests/` (новый по подготовке) | terminate/lock/restore по фейковому rac; пропуски файловых баз | #92 (aepkugesi) |

### Версия и документация
| Файл | Изменение |
|------|-----------|
| [`Workspace1C/Workspace1C.csproj`](Workspace1C/Workspace1C.csproj:62) | 1.0.1.20 → 1.0.1.21 (Version/AssemblyVersion/FileVersion/InformationalVersion) |
| [`CHANGELOG.md`](CHANGELOG.md:14) | новая запись `[1.0.1.21] — 2026-11-02` |
| [`README.md`](README.md) | бейдж версии и «актуальная версия — 1.0.1.21» |

### Диагностика пользователей (не в репо)
Архивы `diagnostics_20261102_082030.zip` (#92) и `diagnostics_20261102_081427.zip` (#79) в репозитории отсутствуют. Если будут распакованы — дополнить план: сверить фактические строки журнала CM_RAC/CM_*, имена баз/кластеров, конфиг trace. Планирование выше выполнено по описаниям и коду; RC-92-1 и RC-79 подтверждаемы кодом напрямую.

---

## 5. Формулировки для CHANGELOG и README

### CHANGELOG (заготовка)

```markdown
## [1.0.1.21] — 2026-11-02

### Исправлено
- Массовые операции (issue #92): ошибка на одной базе больше не прерывает весь
  прогон — остальные базы обрабатываются, итоговая строка выводится, окно не
  «зависает» (ранее прогон молча останавливался с заблокированными кнопками).
- Массовые операции (issue #92): автоподстановка данных СУБД из rac — при пустом
  типе СУБД в «infobase summary list» (rac 8.5.4) параметры догружаются командой
  «infobase info»; добавлен общий таймаут резолва базы и строки журнала о ходе
  и длительности проб кластеров; в журнал пишется «запущен ibcmd (PID …)».
- Мастер «Развернуть тестовую копию» (issue #79): выгрузка исходной базы через
  ibcmd больше не выполняется по параметрам СУБД новой (целевой) базы — данные
  берутся из профиля СУБД исходной базы либо из кластера rac; из-за прежнего
  поведения возникала ошибка «База отсутствует в сервере». Поля СУБД на форме
  применяются только к созданию новой базы.

### Новое
- Массовые операции (issue #92, пожелание aepkugesi): опции перед выгрузкой/
  загрузкой — завершение сеансов пользователей, блокировка начала сеансов
  (с кодом разрешения) и блокировка регламентных заданий; после операции
  блокировки снимаются автоматически.
```

### README
- Обновить бейдж/строку версии на 1.0.1.21. В разделе про массовые операции — упомянуть новые опции завершения сеансов/блокировок одной строкой.

---

## 6. Тексты комментариев к issues

### Комментарий к issue #92 (после релиза 1.0.1.21)

> Исправлено в **v1.0.1.21**. Спасибо за логи и архив диагностики — они помогли найти причину.
>
> Что было:
> 1. Ошибка на первой базе (нет данных СУБД) **прерывала весь прогон**: остальные базы оставались в ожидании, кнопки окна блокировались, процессы не запускались — это и есть «повисло». Теперь прогон продолжается по следующей базе, по итогам выводится сводка.
> 2. Автоподстановка СУБД из rac: в rac 8.5.4 в «infobase summary list» тип СУБД может приходить пустым («тип СУБД «» не поддерживается») — теперь при пустом значении приложение догружает параметры команды «infobase info» и строит профиль. Также добавлен таймаут на автоподстановку (чтобы перебор кластеров не выглядел зависанием) и в журнал пишется строка «запущен ibcmd (PID …)» на каждую базу — сразу видно, что операция реально стартовала.
>
> Также по пожеланию @aepkugesi добавлены опции перед выгрузкой/загрузкой (блок «Перед операцией» в окне массовых операций): завершение сеансов пользователей, блокировка начала сеансов (с кодом разрешения) и блокировка регламентных заданий; после операции блокировки снимаются автоматически. Для файловых баз используется файл блокировки 1cv8.cdn.
>
> Проверьте, пожалуйста, на v1.0.1.21: массовая выгрузка на базах с разных серверов. Если проблема останется — приложите, пожалуйста, новый архив диагностики: теперь в журнале окна видны запуски процессов и ход автоподстановки.

### Комментарий к issue #79 (после релиза 1.0.1.21)

> Исправлено в **v1.0.1.21**. Вы правы: выгрузка исходной базы действительно выполнялась по параметрам СУБД **новой (целевой)** базы — поля «Сервер СУБД»/«Имя базы данных в СУБД» на форме мастера используются и для создания новой базы, и (ошибочно) как параметры подключения ibcmd при выгрузке. Поскольку имя БД в СУБД автоподставлялось из имени новой базы, ibcmd искал базу «…_t», которой на сервере ещё нет — отсюда «База отсутствует в сервере».
>
> Теперь выгрузка исходной базы через ibcmd выполняется по данным **исходной** базы: сначала сохранённый профиль СУБД исходной базы («Резервные копии СУБД»), если его нет — параметры автоматически запрашиваются из кластера rac по имени исходной базы. Поля СУБД на форме применяются только к созданию новой базы. Если у исходной базы нет ни профиля, ни доступного rac — об этом будет сказано до старта, без полусозданной копии.
>
> Проверьте, пожалуйста, на v1.0.1.21. Если ошибка сохранится — пришлите текст шага «Выгрузка исходной базы в .dt» из журнала мастера.
