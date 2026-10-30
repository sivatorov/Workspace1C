# Анализ открытых issues — sivatorov/Workspace1C

Цикл разработки после релиза **1.0.1.4** (текущая версия программы).
- Собрано: 2026-10-30T08:33:41Z (UTC)
- Всего открыто issues: 11
- Репозиторий: https://github.com/sivatorov/Workspace1C

## Критерии отбора

1. Issues **без комментариев** — отобраны (изменения на основе описания).
2. Issues, где **последний комментарий не от @sivatorov** — отобраны (ориентир — последние комментарии не от автора).
3. Issues, где **последний комментарий от @sivatorov** — пропущены (считаются уже обработанными).

## Полный список открытых issues

| # | Issue | Автор | Комм. | Последний комментарий | Отобран? |
|---|-------|-------|-------|------------------------|----------|
| #26 | [Исправить отображение сохраненных акцентных цветов и контрастность текста в тёмной теме](https://github.com/sivatorov/Workspace1C/issues/26) | @ArtyoZ | 29 | @sivatorov, 2026-10-30T07:16:43Z | нет |
| #55 | [Поправить формы и элементы](https://github.com/sivatorov/Workspace1C/issues/55) | @ArtyoZ | 13 | @sivatorov, 2026-10-30T07:16:40Z | нет |
| #57 | [добавить общую авторизацию](https://github.com/sivatorov/Workspace1C/issues/57) | @ixijixi | 5 | @sivatorov, 2026-10-26T17:08:52Z | нет |
| #63 | [Доработка публикации баз](https://github.com/sivatorov/Workspace1C/issues/63) | @7OH | 27 | @sivatorov, 2026-10-30T07:16:36Z | нет |
| #71 | [Ширина 1.0.0.79](https://github.com/sivatorov/Workspace1C/issues/71) | @RizvanShikhammatov | 14 | @RizvanShikhammatov, 2026-10-30T08:26:27Z | ДА |
| #75 | [Серверы 1С - колонки](https://github.com/sivatorov/Workspace1C/issues/75) | @7OH | 13 | @7OH, 2026-10-30T08:24:05Z | ДА |
| #79 | [Развернуть тестовую копию](https://github.com/sivatorov/Workspace1C/issues/79) | @7OH | 9 | @7OH, 2026-10-30T08:32:15Z | ДА |
| #83 | [Информация о лицензиях в Серверы 1С](https://github.com/sivatorov/Workspace1C/issues/83) | @7OH | 7 | @7OH, 2026-10-30T08:22:54Z | ДА |
| #85 | [Групповое применение. Не доступны команды для групп](https://github.com/sivatorov/Workspace1C/issues/85) | @leonvlas | 4 | @sivatorov, 2026-10-29T17:43:15Z | нет |
| #87 | [Загрузка расширения конфигурации в клиент-серверную базу](https://github.com/sivatorov/Workspace1C/issues/87) | @aepkugesi | 2 | @aepkugesi, 2026-10-30T07:23:31Z | ДА |
| #89 | [Скачивание EXE на Линукс](https://github.com/sivatorov/Workspace1C/issues/89) | @7OH | 1 | @sivatorov, 2026-10-30T07:16:14Z | нет |

Отобрано для дальнейшей работы: **5** из 11.

---

## Детально: отобранные issues

### #71 — Ширина 1.0.0.79

- **Ссылка:** https://github.com/sivatorov/Workspace1C/issues/71
- **Автор:** @RizvanShikhammatov (создано 2026-10-26T18:16:09Z, обновлено 2026-10-30T08:26:27Z)
- **Обоснование отбора:** Последний комментарий от @RizvanShikhammatov (2026-10-30T08:26:27Z) — не от @sivatorov

**Описание:**

<img width="1693" height="692" alt="Image" src="https://github.com/user-attachments/assets/370e7989-9900-43fe-acb4-42d0df6abc8c" />

Что-то очень сильно размахнулись настройки

**Комментарии (14):**

1. **@7OH** — 2026-10-26T21:21:05Z — [https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6427609809](https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6427609809)

Да - тоже заметил

<img width="1196" height="910" alt="Image" src="https://github.com/user-attachments/assets/7ae6d29b-b5f9-44b9-88bb-779cd8ec0e97" />

Похоже, после фикса #55 , чтобы окно не обрезалось при сужении - надписи смогли наконец растянуться на всю ширину.
Тут надо было подходить коплексно: 
или И разрешать прокрутку горизонтально И надписям и таблицам\спискам делать максимальную ширину,где надо
или всё таки у формы делать минимальную ширину и не давать сужать мышкой меньше, чем возможно.

2. **@sivatorov** — 2026-10-27T15:03:23Z — [https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6445460959](https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6445460959)

Исправлено в версии 1.0.0.82 (Windows и Linux) по последнему комментарию (жалоба на ширину окна 1.0.0.79):

**Ширина окна «Настройки»** — после фикса #55 настроечные вкладки «размахнулись»: многие элементы стали шире самого окна, и почти на всех вкладках появилась горизонтальная прокрутка. Принята единая со стратегией #55 политика «минимальные размеры + запрет сужения»: окну «Настройки» задан минимум 960×720 (было 820×620), окну «Настройка информационной базы» — 760×780 (было 680×680), сузить окно мышкой меньше минимума теперь нельзя; при этих размерах всё содержимое вкладок помещается без обрезания и без горизонтальных полос; жёсткие широкие размеры полей заменены на MinWidth со звёздными колонками, длинные подписи переносятся по словам ([`SettingsWindow.xaml`](Workspace1C/Views/SettingsWindow.xaml), [`SettingsWindow.Avalonia.cs`](Workspace1C/Views/SettingsWindow.Avalonia.cs), [`ConnectionSettingsWindow.xaml`](Workspace1C/Views/ConnectionSettingsWindow.xaml), [`ConnectionSettingsWindow.Avalonia.cs`](Workspace1C/Views/ConnectionSettingsWindow.Avalonia.cs)).

Покрыто тестами `Issue71SettingsWindowLayoutTests` (+8).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.82

3. **@RizvanShikhammatov** — 2026-10-27T18:30:03Z — [https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6450358059](https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6450358059)

<img width="794" height="753" alt="Image" src="https://github.com/user-attachments/assets/c0a4fb57-0173-4021-b5c7-706fd430c381" />

Размах исправлен. Но такого сужения тоже не хочется. Надо бы ограничить минимальную ширину.


4. **@7OH** — 2026-10-27T20:05:58Z — [https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6452329561](https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6452329561)

Похоже нейронка в красоту не умеет
Надо звать человека, чтобы он формочки просмотрел и выставил корректные значения

5. **@sivatorov** — 2026-10-28T13:20:31Z — [https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6470080964](https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6470080964)

Исправлено в версии 1.0.0.101 (Windows и Linux) по последним комментариям (RizvanShikhammatov: «такого сужения тоже не хочется. Надо бы ограничить минимальную ширину»; 7OH: «надо звать человека, чтобы он формочки просмотрел и выставил корректные значения»):

**Стартовые размеры окон настроек выставлены «человечески», минимальные сохранены.**

1. **Окно «Настройки»** — стартовые **1100×760** (было 960×720), минимум **960×720**
   сохранён: окно открывается с комфортной шириной, а не сразу в минимуме; сузить
   мышкой ниже минимума по-прежнему нельзя, содержимое вкладок не обрезается
   и горизонтальная прокрутка не появляется
   ([`SettingsWindow.xaml`](Workspace1C/Views/SettingsWindow.xaml),
   [`SettingsWindow.Avalonia.cs`](Workspace1C/Views/SettingsWindow.Avalonia.cs)).

2. **Окно «Настройка информационной базы»** — стартовые **900×800** (было 760×780),
   минимум **760×780** сохранён: на скриншоте 794×753 окно выглядело «суженным» —
   теперь оно открывается шире и выше, а при ручном сужении до минимума контент
   вкладок помещается без обрезания и без горизонтальной полосы
   ([`ConnectionSettingsWindow.xaml`](Workspace1C/Views/ConnectionSettingsWindow.xaml),
   [`ConnectionSettingsWindow.Avalonia.cs`](Workspace1C/Views/ConnectionSettingsWindow.Avalonia.cs)).

3. **Горизонтальная прокрутка вкладок** остаётся отключённой (Disabled) на обеих
   платформах — «размах» из 1.0.0.79 не возвращается.

Покрыто тестами-стражами [`Issue71SettingsWindowLayoutTests.cs`](Workspace1C.Tests/Issue71SettingsWindowLayoutTests.cs) (+4: стартовые размеры на обеих платформах).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.101

6. **@RizvanShikhammatov** — 2026-10-28T13:32:22Z — [https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6470360412](https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6470360412)

<img width="455" height="822" alt="Image" src="https://github.com/user-attachments/assets/8834c848-4397-4212-a416-2935e4b82de3" />
нет


7. **@7OH** — 2026-10-28T13:34:48Z — [https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6470422507](https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6470422507)

<img width="196" height="67" alt="Image" src="https://github.com/user-attachments/assets/76bc92c0-583a-48fc-be13-df751a721d26" />

Я даже меньше скажу )

8. **@sivatorov** — 2026-10-29T04:21:07Z — [https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6487840264](https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6487840264)

Исправлено в версии **1.0.1.0** (Windows и Linux) по комментариям RizvanShikhammatov
(«нет» + скриншот сжатого окна 455×822) и 7OH («Я даже меньше скажу)»):

- **Причина.** Профиль мог сохранить размер главного окна из старой версии или с
  маленького монитора — при восстановлении размеров окно открывалось уже суженным
  ниже минимально допустимого и выглядело «сжатым» (сжатие видно прямо на
  скриншотах).

- **Исправление.** Восстановленные из профиля размеры главного окна теперь
  зажимаются к `MinWidth`/`MinHeight`: в WPF
  ([`MainWindow.Events.cs`](Workspace1C/Views/MainWindow.Events.cs)) и в Avalonia
  ([`MainWindow.Avalonia.cs`](Workspace1C/Views/MainWindow.Avalonia.cs)) —
  `Math.Max(width, MinWidth)` / `Math.Max(height, MinHeight)`. Окно открывается не
  уже минимума, сохранённые позиция и размер при этом продолжают уважаться.

- Стартовые размеры окон «Настройки» (1100×760) и «Настройка информационной базы»
  (900×800) при минимумах 960×720/760×780 сохранены (заданы в 1.0.0.101) —
  поведение не изменилось.

Покрыто тестами-стражами
[`Issue71SettingsWindowLayoutTests.cs`](Workspace1C.Tests/Issue71SettingsWindowLayoutTests.cs)
(+2: восстановленные размеры главного окна не опускаются ниже минимумов — WPF и
Avalonia).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.0

9. **@7OH** — 2026-10-29T06:39:44Z — [https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6489828329](https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6489828329)

<img width="247" height="117" alt="Image" src="https://github.com/user-attachments/assets/3b89a6ef-afba-47e2-a4c1-f497df720435" />

Я всё ещё могу сжать настройки почти до нуля

10. **@sivatorov** — 2026-10-29T17:45:00Z — [https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6504099244](https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6504099244)

Исправлено в версии **1.0.1.3** (Windows и Linux):

Окна «Настройки» и «Настройка информационной базы» больше нельзя сжать мышью ниже минимального размера:

- добавлен программный зажим размеров к `MinWidth`/`MinHeight` при любом способе ресайза (WPF — `SizeChanged`, Avalonia — подписка на `ClientSizeProperty`);
- восстановление размеров из профиля тоже зажимается к минимуму.

Спасибо за сообщение! Проверьте, пожалуйста, в новом релизе.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.3

11. **@RizvanShikhammatov** — 2026-10-29T19:05:29Z — [https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6505861070](https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6505861070)

<img width="1148" height="788" alt="Image" src="https://github.com/user-attachments/assets/6c6fa37c-0d28-4dec-9906-cd7110db8f05" />

Первоначальный размер

<img width="284" height="776" alt="Image" src="https://github.com/user-attachments/assets/e074c519-84d0-41db-bc3e-e43a340207ef" />

После

12. **@7OH** — 2026-10-29T19:45:40Z — [https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6506690927](https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6506690927)

<img width="280" height="133" alt="Image" src="https://github.com/user-attachments/assets/c604674e-86d8-4795-879a-59398c86b33d" />

ничего не изменилось - смотри скрин - окно спокойно ужимается до размера заголовка

13. **@sivatorov** — 2026-10-30T07:16:32Z — [https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6517194315](https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6517194315)

Исправлено в версии **1.0.1.4** (Windows и Linux):

Окна «Настройки» и «Настройка информационной базы» больше нельзя сжать ниже минимального размера — единый механизм гарантии минимума:

- WPF — перехват `WM_GETMINMAXINFO` (`ptMinTrackSize` с учётом DPI) в [`WindowChromeHelper`](Workspace1C/Views/WindowChromeHelper.cs);
- Avalonia — общий зажим по `ClientSizeProperty` в [`ModalWindowBase.ClampWindowToMinSize`](Workspace1C/Views/ModalWindowBase.cs).

Окна больше не «ужимаются до размера заголовка».

Добавлены юнит-тесты [`Issue71SettingsWindowLayoutTests.cs`](Workspace1C.Tests/Issue71SettingsWindowLayoutTests.cs): зажим минимума для WPF и Avalonia.

Спасибо за сообщение! Проверьте, пожалуйста, в новом релизе.

Релиз **1.0.1.4** (Windows и Linux) будет опубликован в ближайшее время.

14. **@RizvanShikhammatov** — 2026-10-30T08:26:27Z — [https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6518461763](https://github.com/sivatorov/Workspace1C/issues/71#issuecomment-6518461763)

Да, теперь работает. Но нельзя ли запоминать размеры этого окна? И в следующий раз открывать с последними размерами?

---

### #75 — Серверы 1С - колонки

- **Ссылка:** https://github.com/sivatorov/Workspace1C/issues/75
- **Автор:** @7OH (создано 2026-10-27T10:42:45Z, обновлено 2026-10-30T08:24:05Z)
- **Обоснование отбора:** Последний комментарий от @7OH (2026-10-30T08:24:05Z) — не от @sivatorov

**Описание:**

Просьба реализовать следующее.

Колонки во вкладках Сеансы, Соединения, Блокировки и Рабочие процессы
должны иметь по-умолчанию размер, достаточный для отображения самого заголовка и пиктограммы направления сортировки.

В текущей реализации это невозможно - нужно включить горизонтальную прокрутку, чтобы колонка могла уходить за границу окна.
Для этого инструмента - это как раз нормально.

И, по возможности, сохранять размеры колонок на каждой вкладке - желательно, чтобы сохранение переживало закрытие программы.

**Комментарии (13):**

1. **@7OH** — 2026-10-27T14:11:05Z — [https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6444211969](https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6444211969)

Сюда же добавлю.
При изменении размера колонки мышкой - сейчас двигается граница двух смежных колонок.
Можно сделать как в 1С: перемещение границы - двигает все колонки справа от границы без изменения размера - меняется только позиция.
При попытке передвинуть границу с зажатым CTRL - меняется как сейчас - только граница между двумя колонками, не трогая ни размеры, ни положение других колонок.

2. **@sivatorov** — 2026-10-27T15:02:05Z — [https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6445429626](https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6445429626)

Исправлено в версии 1.0.0.82 (Windows и Linux):

1. **Колонки по содержимому заголовка** — на вкладках «Сеансы», «Соединения», «Блокировки» и «Рабочие процессы» монитора серверов колонки получают по умолчанию ширину, достаточную для отображения самого заголовка и пиктограммы направления сортировки (MinWidth под самый длинный заголовок в ru/en) ([`ServerMonitorWindow.xaml`](Workspace1C/Views/ServerMonitorWindow.xaml), [`ServerMonitorWindow.Avalonia.cs`](Workspace1C/Views/ServerMonitorWindow.Avalonia.cs)).
2. **Горизонтальная прокрутка** — таблицы получили горизонтальную прокрутку: колонки могут уходить за границу окна, а при сужении окна появляется именно полоса прокрутки, а не «схлопывание» колонок (WPF и Avalonia).
3. **Сохранение размеров колонок по вкладкам** — размеры колонок каждой вкладки сохраняются отдельно (новое хранилище `ServerMonitorColumnWidthsStore`) и переживают закрытие программы; при переключении вкладок восстановленные ширины применяются сразу ([`ServerMonitorColumnWidthsStore.cs`](Workspace1C/Services/ServerMonitorColumnWidthsStore.cs), [`AppSettings.cs`](Workspace1C/Models/AppSettings.cs)).

Покрыто тестами `ServerMonitorColumnWidthsTests` (+14), `Issue75ServerMonitorColumnsTests` (+6).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.82

3. **@7OH** — 2026-10-27T19:58:32Z — [https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6452176932](https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6452176932)

Прокрутка есть.
Ширина тоже достаточна для просмотра содержимого.
Но когда пытаешься подвинуть колонку - оно двигает одну две - они упираются в третью и всё - больше не двигаются.
А если в первой сильно длинная надпись - что делать ?
Должно быть двигать дальше - пусть скрол появляется, но двигать.

Ну и изменение размера с контролом - тоже надо доделать, чтобы изменять 2 колонки не двигая остальные

4. **@7OH** — 2026-10-27T20:02:37Z — [https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6452261467](https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6452261467)

Не уверен - из-за этой ли доработки, но при открытии формы - ошибка, даже две сразу

<img width="831" height="858" alt="Image" src="https://github.com/user-attachments/assets/e848a6d1-8ef8-4409-95fb-f446f509abd8" />

И после этого похоже - список соединений ничего не показывает.
Возможно это влияет косвенно и на возможность удалить базу

5. **@7OH** — 2026-10-27T20:51:31Z — [https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6453246607](https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6453246607)

Логи текстом

2026-10-27 23:50:10.766 [ERROR] Ошибка интерфейса
Исключение: System.InvalidCastException
Сообщение: Specified cast is not valid.
StackTrace:
System.InvalidCastException: Specified cast is not valid.
   at System.Linq.Enumerable.CastICollectionIterator`1.MoveNext()
   at Workspace1C.ServerMonitorWindow.AttachHeaderWidthTracking(DataGrid grid, String tabKey) in D:\a\Workspace1C\Workspace1C\Workspace1C\Views\ServerMonitorWindow.xaml.cs:line 325
   at Workspace1C.ServerMonitorWindow.<>c__DisplayClass23_0.<AttachColumnWidthTracking>b__1() in D:\a\Workspace1C\Workspace1C\Workspace1C\Views\ServerMonitorWindow.xaml.cs:line 315
   at System.Windows.Threading.ExceptionWrapper.InternalRealCall(Delegate callback, Object args, Int32 numArgs)
   at System.Windows.Threading.ExceptionWrapper.TryCatchWhen(Object source, Delegate callback, Object args, Int32 numArgs, Delegate catchHandler)

2026-10-27 23:50:10.835 [ERROR] Ошибка интерфейса
Исключение: System.InvalidCastException
Сообщение: Specified cast is not valid.
StackTrace:
System.InvalidCastException: Specified cast is not valid.
   at System.Linq.Enumerable.CastICollectionIterator`1.MoveNext()
   at Workspace1C.ServerMonitorWindow.AttachHeaderWidthTracking(DataGrid grid, String tabKey) in D:\a\Workspace1C\Workspace1C\Workspace1C\Views\ServerMonitorWindow.xaml.cs:line 325
   at Workspace1C.ServerMonitorWindow.<>c__DisplayClass23_0.<AttachColumnWidthTracking>b__1() in D:\a\Workspace1C\Workspace1C\Workspace1C\Views\ServerMonitorWindow.xaml.cs:line 315
   at System.Windows.Threading.ExceptionWrapper.InternalRealCall(Delegate callback, Object args, Int32 numArgs)
   at System.Windows.Threading.ExceptionWrapper.TryCatchWhen(Object source, Delegate callback, Object args, Int32 numArgs, Delegate catchHandler)

6. **@sivatorov** — 2026-10-28T08:21:09Z — [https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6464164724](https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6464164724)

Исправлено в версии 1.0.0.96 (Windows и Linux):

1. **Ошибка при открытии окна «Серверы 1С» (InvalidCastException, две сразу)** —
   падал [`AttachHeaderWidthTracking()`](Workspace1C/Views/ServerMonitorWindow.xaml.cs):
   коллекция `presenter.Items` презентера заголовков DataGrid в момент применения
   (DispatcherPriority.ApplicationIdle) содержит НЕ только `DataGridColumnHeader`,
   но и сырые колонки (`DataGridColumn`) — `Cast<DataGridColumnHeader>()` бросал
   `System.InvalidCastException: Specified cast is not valid`. После ошибки вкладка
   «Соединения» могла не показывать список. Сбор заголовков теперь идёт безопасным
   `OfType<DataGridColumnHeader>()` (лишние элементы пропускаются), плюс «досъём»
   контейнеров по `ItemContainerGenerator.StatusChanged` — заголовки, которые
   материализуются позже (переключение вкладок), тоже подхватываются.

2. **Перетаскивание колонок теперь как в 1С** (обе платформы):
   - обычное перетаскивание границы двигает ВСЕ колонки справа как блок — их
     ширины не меняются, меняются только позиции; при переполнении области
     появляется горизонтальная прокрутка, границу можно двигать дальше (в т.ч.
     если у первой колонки длинный заголовок);
   - перетаскивание с зажатым Ctrl меняет ширину ТОЛЬКО двух смежных колонок
     (левая растёт/ужимается, правая — зеркально), остальные колонки не двигаются;
   - колонка не сжимается ниже минимума «заголовок + стрелка сортировки»;
   - математика вынесена в чистые функции
     [`ServerMonitorColumnWidthsStore.MovedLeftColumnWidth()`](Workspace1C/Services/ServerMonitorColumnWidthsStore.cs)
     и
     [`ResizedTwoAdjacentWidths()`](Workspace1C/Services/ServerMonitorColumnWidthsStore.cs) —
     одна реализация для WPF и Avalonia. WPF перехватывает штатную «ручку»
     `PART_RightHeaderGripper` заголовка DataGrid, Avalonia — собственную ручку
     шапки таблицы. Сохранение фактических ширин на вкладку (переживают закрытие
     программы) работает как раньше.

Покрыто регрессионными тестами: `Wpf_HeaderTracking_UsesSafeTyping_NoInvalidCastException`,
`BothPlatforms_UseOneCStyleColumnDragSemantics`
([`Issue75ServerMonitorColumnsTests.cs`](Workspace1C.Tests/Issue75ServerMonitorColumnsTests.cs)),
а также `MovedLeftColumnWidth_*`/`ResizedTwoAdjacentWidths_*`
([`ServerMonitorColumnWidthsTests.cs`](Workspace1C.Tests/ServerMonitorColumnWidthsTests.cs)).
Всего тестов: 4862 (все проходят на Windows; Linux — 0 ошибок).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.96


7. **@7OH** — 2026-10-28T08:32:48Z — [https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6464365710](https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6464365710)

Ошибка ушла - отлично.
Теперь бы логику движения и изменения размеров сменить (https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6444211969), так как изменений по работе с шириной колонок - не замечено

8. **@sivatorov** — 2026-10-29T05:33:00Z — [https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6488864667](https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6488864667)

Исправлено в версии **1.0.1.1** (Windows и Linux):

Перетаскивание колонок во вкладках «Сеансы/Соединения/Блокировки/Рабочие процессы»
окна «Серверы 1С» теперь действительно «как в 1С»:

- обычное перетаскивание границы двигает ВСЕ колонки справа как блок — их ширины
  не меняются, при переполнении области появляется горизонтальная прокрутка;
- перетаскивание с зажатым Ctrl меняет ширину ТОЛЬКО двух смежных колонок
  (минимальные ширины соблюдаются).

Причина поломки предыдущих фиксов — перехват вешался на Thumb-ручку по имени
шаблона; теперь перехват идёт ДО внутренней обработки DataGrid
(`PreviewMouseLeftButtonDown`/`CaptureMouse`), а единая математика вынесена в
[`ServerMonitorColumnWidthsStore.ApplyDragMoveBlock()`](Workspace1C/Services/ServerMonitorColumnWidthsStore.cs)
и
[`ApplyDragResizePair()`](Workspace1C/Services/ServerMonitorColumnWidthsStore.cs) —
одна реализация для WPF
([`ServerMonitorWindow.xaml.cs`](Workspace1C/Views/ServerMonitorWindow.xaml.cs))
и Avalonia
([`ServerMonitorWindow.Avalonia.cs`](Workspace1C/Views/ServerMonitorWindow.Avalonia.cs)).

Покрыто регрессионными тестами
[`ServerMonitorColumnWidthsTests.cs`](Workspace1C.Tests/ServerMonitorColumnWidthsTests.cs)
и
[`Issue75ServerMonitorColumnsTests.cs`](Workspace1C.Tests/Issue75ServerMonitorColumnsTests.cs)
(эмуляция drag, capture-циклы).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.1

9. **@7OH** — 2026-10-29T06:18:45Z — [https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6489503602](https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6489503602)

Нет - не работает.
Всё по старому 

10. **@sivatorov** — 2026-10-29T17:44:49Z — [https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6504095067](https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6504095067)

Исправлено в версии **1.0.1.3** (Windows и Linux):

Окно «Серверы 1С» — работа с колонками:

- расширена зона кликов для перемещения/изменения колонок (16 → 24 px) — перетаскивание стало удобнее и точнее;
- подписки на заголовки колонок догружаются по `LayoutUpdated` (WPF-версия) — больше не теряются при добавлении новых колонок.

Спасибо за обратную связь! Проверьте, пожалуйста, в новом релизе.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.3

11. **@7OH** — 2026-10-29T19:42:52Z — [https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6506635145](https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6506635145)

Вообще ничего не меняется ((
Вместо движения всех колонок справа - оно ужимает 2 справа до определенного предела и всё.
С нажатым контролом аналогично, вместо изменения размера двух соседних.

12. **@sivatorov** — 2026-10-30T07:16:29Z — [https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6517193433](https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6517193433)

Исправлено в версии **1.0.1.4** (Windows и Linux):

Окно «Серверы 1С» — перетаскивание колонок теперь работает как в 1С:

- WPF: перехват перетаскивания границ перенесён с заголовков на уровень грида (`PreviewMouseLeftButtonDown` на гриде, захват мыши гридом) — обычный режим двигает «блок» колонок, Ctrl-режим меняет две соседние;
- Avalonia: ширина «ручки» захвата расширена до 24 px (как в WPF).

Покрыто тестами [`Issue75ServerMonitorColumnsTests.cs`](Workspace1C.Tests/Issue75ServerMonitorColumnsTests.cs): грид-уровневый перехват и зона 24 px.

Спасибо за сообщение! Проверьте, пожалуйста, в новом релизе.

Релиз **1.0.1.4** (Windows и Linux) будет опубликован в ближайшее время.

13. **@7OH** — 2026-10-30T08:24:05Z — [https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6518424308](https://github.com/sivatorov/Workspace1C/issues/75#issuecomment-6518424308)

Ничего не изменилось - поведение всё то же

---

### #79 — Развернуть тестовую копию

- **Ссылка:** https://github.com/sivatorov/Workspace1C/issues/79
- **Автор:** @7OH (создано 2026-10-28T05:04:04Z, обновлено 2026-10-30T08:32:15Z)
- **Обоснование отбора:** Последний комментарий от @7OH (2026-10-30T08:32:15Z) — не от @sivatorov

**Описание:**

Просто скрин оставлю

<img width="1045" height="567" alt="Image" src="https://github.com/user-attachments/assets/941b0393-19b6-45c2-a4c2-34fefcd1a5c8" />

**Комментарии (9):**

1. **@7OH** — 2026-10-28T06:43:08Z — [https://github.com/sivatorov/Workspace1C/issues/79#issuecomment-6462543974](https://github.com/sivatorov/Workspace1C/issues/79#issuecomment-6462543974)

Вангую - без указания локали (а этого поля нет на форме) оно в конце ошибку выдаст

2. **@sivatorov** — 2026-10-28T07:33:38Z — [https://github.com/sivatorov/Workspace1C/issues/79#issuecomment-6463364631](https://github.com/sivatorov/Workspace1C/issues/79#issuecomment-6463364631)

Исправлено в версии 1.0.0.94 (Windows и Linux):

1. **Список «Движок выгрузки» в мастере «Развернуть тестовую копию» показывал CLR-имя типа** вместо текста — в выпадающем списке отображалось «Workspace1C.TestDeployWindow+ScheduleOptionValue» вместо «Конфигуратор» / «ibcmd (без окна)». Причина: пара «ключ/текст» не переопределяла `ToString()`, и выпадающий список рендерил имя типа.
2. Теперь список движков построен на общем классе [`TestDeployEngineOption`](Workspace1C/Models/TestDeployEngineOption.cs) (`ToString() => Text`) и используется в обеих платформах: WPF ([`TestDeployWindow.cs`](Workspace1C/Views/TestDeployWindow.cs)) и Avalonia ([`TestDeployWindow.Avalonia.cs`](Workspace1C/Views/TestDeployWindow.Avalonia.cs)). Выбор движка по-прежнему сохраняется в сценарий (`DumpEngine`).

Покрыто регрессионными тестами `EngineOption_ToString_ReturnsTextNotTypeName` и `ViewModel_EngineIndex_MapsToDumpEngine` ([`TestDeployTests.cs`](Workspace1C.Tests/TestDeployTests.cs)).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.94


3. **@7OH** — 2026-10-28T07:51:18Z — [https://github.com/sivatorov/Workspace1C/issues/79#issuecomment-6463650655](https://github.com/sivatorov/Workspace1C/issues/79#issuecomment-6463650655)

Просьба навести порядок в оформлении - одни поля серые, вторые нормальные и с окантовкой.
В поле "Имя базы данных СУБД" можно подставлять как минимум такое же имя, как "Имя базы на сервере"

<img width="547" height="243" alt="Image" src="https://github.com/user-attachments/assets/5672c0e4-0522-4f6b-9295-a780bb0b303a" />

Логично сразу эту кнопку видеть - чтобы тут же нажать и открыть свойства 
А нельзя брать из свойств базы разве ?

Через конфигуратор 

<img width="625" height="667" alt="Image" src="https://github.com/user-attachments/assets/ec7961be-c76a-4049-b1c9-63af1daf6e67" />

Наверное надо или что-то предлагать сделать соответствующими кнопками или иметь кнопки в окошке, чтобы админ сам мог это сделать не выходя из этого окна

4. **@sivatorov** — 2026-10-29T04:21:10Z — [https://github.com/sivatorov/Workspace1C/issues/79#issuecomment-6487841152](https://github.com/sivatorov/Workspace1C/issues/79#issuecomment-6487841152)

Исправлено в версии **1.0.1.0** (Windows и Linux) по комментарию 7OH от 2026-10-28 07:51
(мастер «Развернуть тестовую копию»):

- **Единое оформление редактируемых полей.** Поля мастера переведены на тематические
  `ModernTextBox`/`ModernComboBox` (WPF; в Avalonia поля уже были тематическими) —
  внешний вид полей теперь одинаков с остальными окнами приложения и корректно
  выглядит в обеих темах.

- **Автоподстановка «Имя базы данных СУБД».** Пока поле имени СУБД пустое, в него
  автоматически подставляется «Имя базы на сервере»
  ([`TestDeployViewModel.AutofillDbNameFromServerDbName()`](Workspace1C/ViewModels/TestDeployViewModel.cs)).
  После подстановки пользователь может свободно изменить имя вручную.

- **Кнопки «Свойства исходной базы…» и «Конфигуратор».** Рядом с источником добавлены
  кнопки: «**Свойства исходной базы…**» открывает окно настройки информационной базы,
  не выходя из мастера, «**Конфигуратор**» запускает «Конфигуратор» исходной базы —
  администратору не нужно закрывать мастер, чтобы посмотреть свойства или открыть
  конфигуратор исходной базы
  ([`TestDeployWindow.cs`](Workspace1C/Views/TestDeployWindow.cs),
  [`TestDeployWindow.Avalonia.cs`](Workspace1C/Views/TestDeployWindow.Avalonia.cs)).

- Локализация интерфейса ru/en.

Покрыто тестами
[`Issue79TestDeployWindowTests.cs`](Workspace1C.Tests/Issue79TestDeployWindowTests.cs)
(новый: оформление полей мастера, автоподстановка имени СУБД, кнопки «Свойства
исходной базы…»/«Конфигуратор», локализация).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.0

5. **@7OH** — 2026-10-29T06:16:45Z — [https://github.com/sivatorov/Workspace1C/issues/79#issuecomment-6489474322](https://github.com/sivatorov/Workspace1C/issues/79#issuecomment-6489474322)

Ну вот теперь просто ошибка при открытии (а ведь покрыто тестами....)

<details>
<summary>Ошибка</summary>

<img width="906" height="911" alt="Image" src="https://github.com/user-attachments/assets/e5d3c984-d653-448a-b583-3850ca647eee" />

</details>

6. **@sivatorov** — 2026-10-29T17:44:38Z — [https://github.com/sivatorov/Workspace1C/issues/79#issuecomment-6504090917](https://github.com/sivatorov/Workspace1C/issues/79#issuecomment-6504090917)

Исправлено в версии **1.0.1.3** (Windows и Linux):

Мастер «Развернуть тестовую копию» больше не падает при открытии с некорректными данными базы:

- `PreviewSteps`/`PreviewSummary` в [`TestDeployViewModel`](Workspace1C/ViewModels/TestDeployViewModel.cs) переведены на безопасный режим: при ошибке показывается нейтральный текст `TestDeploy.PreviewUnavailable` (добавлена локализация ru/en), а не исключение;
- окно открывается корректно для любых типов баз.

Спасибо за сообщение! Проверьте, пожалуйста, в новом релизе.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.3

7. **@7OH** — 2026-10-29T19:41:22Z — [https://github.com/sivatorov/Workspace1C/issues/79#issuecomment-6506605848](https://github.com/sivatorov/Workspace1C/issues/79#issuecomment-6506605848)

По прежнему ошибка

<details>
<summary>Логи</summary>

2026-10-29 22:40:33.859 [ERROR] Ошибка интерфейса
Исключение: System.InvalidOperationException
Сообщение: TargetType "TextBox" не соответствует типу элемента "PasswordBox".
StackTrace:
System.InvalidOperationException: TargetType "TextBox" не соответствует типу элемента "PasswordBox".
   at System.Windows.Style.CheckTargetType(Object element)
   at System.Windows.FrameworkElement.OnStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
   at System.Windows.FrameworkElement.OnPropertyChanged(DependencyPropertyChangedEventArgs e)
   at System.Windows.Controls.PasswordBox.OnPropertyChanged(DependencyPropertyChangedEventArgs e)
   at System.Windows.DependencyObject.UpdateEffectiveValue(EntryIndex entryIndex, DependencyProperty dp, PropertyMetadata metadata, EffectiveValueEntry oldEntry, EffectiveValueEntry& newEntry, Boolean coerceWithDeferredReference, Boolean coerceWithCurrentValue, OperationType operationType)
   at System.Windows.FrameworkElement.set_Style(Style value)
   at Workspace1C.TestDeployWindow.ApplyModernStyles(DependencyObject root) in D:\a\Workspace1C\Workspace1C\Workspace1C\Views\TestDeployWindow.cs:line 627
   at Workspace1C.TestDeployWindow.ApplyModernStyles(DependencyObject root) in D:\a\Workspace1C\Workspace1C\Workspace1C\Views\TestDeployWindow.cs:line 631
   at Workspace1C.TestDeployWindow.ApplyModernStyles(DependencyObject root) in D:\a\Workspace1C\Workspace1C\Workspace1C\Views\TestDeployWindow.cs:line 631
   at Workspace1C.TestDeployWindow.ApplyModernStyles(DependencyObject root) in D:\a\Workspace1C\Workspace1C\Workspace1C\Views\TestDeployWindow.cs:line 631
   at Workspace1C.TestDeployWindow..ctor(Infobase source, IReadOnlyList`1 allInfobases, IReadOnlyList`1 groups) in D:\a\Workspace1C\Workspace1C\Workspace1C\Views\TestDeployWindow.cs:line 394
   at Workspace1C.ViewModels.MainViewModel.ExecuteShowTestDeployAsync(Infobase source) in D:\a\Workspace1C\Workspace1C\Workspace1C\ViewModels\MainViewModel.Commands.cs:line 1948
   at Workspace1C.ViewModels.MainViewModel.<get_TestDeployCommand>b__271_0(Object _) in D:\a\Workspace1C\Workspace1C\Workspace1C\ViewModels\MainViewModel.Commands.cs:line 1940
   at System.Threading.Tasks.Task.<>c.<ThrowAsync>b__124_0(Object state)
   at System.Windows.Threading.ExceptionWrapper.InternalRealCall(Delegate callback, Object args, Int32 numArgs)
   at System.Windows.Threading.ExceptionWrapper.TryCatchWhen(Object source, Delegate callback, Object args, Int32 numArgs, Delegate catchHandler)


</details>

8. **@sivatorov** — 2026-10-30T07:16:25Z — [https://github.com/sivatorov/Workspace1C/issues/79#issuecomment-6517192624](https://github.com/sivatorov/Workspace1C/issues/79#issuecomment-6517192624)

Исправлено в версии **1.0.1.4** (Windows и Linux):

Мастер «Развернуть тестовую копию» больше не падает при открытии:

- в [`TestDeployWindow.ApplyModernStyles`](Workspace1C/Views/TestDeployWindow.cs) полю `PasswordBox` больше не назначается стиль `ModernTextBox` с `TargetType="TextBox"` — именно это бросало `InvalidOperationException` при открытии окна;
- пароль теперь получает темированный implicit-стиль `TargetType="PasswordBox"`.

Добавлены юнит-тесты [`Issue79TestDeployWindowTests.cs`](Workspace1C.Tests/Issue79TestDeployWindowTests.cs): `PasswordBox` не получает стиль `TargetType="TextBox"`.

Спасибо за сообщение! Проверьте, пожалуйста, в новом релизе.

Релиз **1.0.1.4** (Windows и Linux) будет опубликован в ближайшее время.

9. **@7OH** — 2026-10-30T08:32:15Z — [https://github.com/sivatorov/Workspace1C/issues/79#issuecomment-6518554837](https://github.com/sivatorov/Workspace1C/issues/79#issuecomment-6518554837)

1. Выгружаю через ibcmd.
То есть я должен закрыть окно со всеми введенными данными и пойти найти где это вводится ?

<details>
<summary>Скрин</summary>

<img width="555" height="250" alt="Image" src="https://github.com/user-attachments/assets/c4b1a6d0-3613-4ee4-9351-f8016eaef9e0" />

</details>

Можно внизу кнопку добавить, которая отроет окно нужное прямо тут ?

2. А зачем мне отдельно вводить, если все данные вот они - уже введены выше ?

---

### #83 — Информация о лицензиях в Серверы 1С

- **Ссылка:** https://github.com/sivatorov/Workspace1C/issues/83
- **Автор:** @7OH (создано 2026-10-28T09:46:14Z, обновлено 2026-10-30T08:22:54Z)
- **Обоснование отбора:** Последний комментарий от @7OH (2026-10-30T08:22:54Z) — не от @sivatorov

**Описание:**

В логах часто вижу вот это

2026-10-28 12:43:09.462 [WARN] RING: ring (PATH) — не удалось получить лицензии: ring: An error occurred trying to start process 'ring' with working directory 'D:\1cBases\Add\WorkSpace1C'. Не удается найти указанный файл.

Думаю при таком исходе - информации во вкладке Лицензии ждать не стОит.
Ну и вкладку можно поместить перед "Информация о кластере" - данные по лицензиям явно интереснее

**Комментарии (7):**

1. **@sivatorov** — 2026-10-29T05:33:48Z — [https://github.com/sivatorov/Workspace1C/issues/83#issuecomment-6488876049](https://github.com/sivatorov/Workspace1C/issues/83#issuecomment-6488876049)

Исправлено в версии **1.0.1.1** (Windows и Linux):

Вкладка «Лицензии» в «Серверы 1С»:

- если утилита ring не найдена, в журнал пишется INFO (а не WARN-спам при
  автообновлении каждые ~5 с), а во вкладке показывается понятное сообщение с
  указанием, где искалась утилита (PATH и каталог bin платформы рядом с rac)
  ([`RingLicenseService.cs`](Workspace1C/Services/RingLicenseService.cs));
- вкладка «Лицензии» перенесена ПЕРЕД «Информация о кластере» (WPF
  [`ServerMonitorWindow.xaml`](Workspace1C/Views/ServerMonitorWindow.xaml) и
  Avalonia).

Покрыто тестами
[`Issue83LicenseTabTests.cs`](Workspace1C.Tests/Issue83LicenseTabTests.cs)
(новый: INFO вместо WARN при отсутствии ring, понятное сообщение вкладки,
порядок вкладок).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.1

2. **@7OH** — 2026-10-29T06:15:24Z — [https://github.com/sivatorov/Workspace1C/issues/83#issuecomment-6489455147](https://github.com/sivatorov/Workspace1C/issues/83#issuecomment-6489455147)

Вопрос же не в том, что в журнал пишется. Блин.
Вопрос в том - ГДЕ программа утилиту ищет и ЗАЧЕМ ?
Открываем командную строку, да хоть на диске Е

<img width="311" height="55" alt="Image" src="https://github.com/user-attachments/assets/3b4c4ab3-baca-4e4b-96c8-830d691375e4" />

И всё работает.

Но в логах же про другое похоже.
Совсем про другое.
Ну не будет рядом со стартером этой утилиты.
Она может быть установлена в разные места даже самой 1С.

Давайте просто вызывать, а не искать её рядом

3. **@sivatorov** — 2026-10-29T17:43:25Z — [https://github.com/sivatorov/Workspace1C/issues/83#issuecomment-6504062540](https://github.com/sivatorov/Workspace1C/issues/83#issuecomment-6504062540)

Исправлено в версии **1.0.1.3** (Windows и Linux):

Вкладка «Лицензии» в «Серверы 1С»:

- утилита `ring` теперь ищется в PATH (через PATHEXT, только .exe/.com) — вызывается как есть, а не рядом со стартером/rac;
- каталог bin платформы (рядом с rac) — запасной вариант, а не первый;
- RING-записи логирования переведены на категорийный лог (управляются на вкладке «Логирование»).

Спасибо за замечание! Проверьте, пожалуйста, в новом релизе.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.3

4. **@7OH** — 2026-10-29T19:39:25Z — [https://github.com/sivatorov/Workspace1C/issues/83#issuecomment-6506566605](https://github.com/sivatorov/Workspace1C/issues/83#issuecomment-6506566605)

По прежнему ищем утилиту в папке программы
Её тут НИКОГДА не будет

<details>
<summary>Логи</summary>

2026-10-29 22:37:03.396 [INFO] [CM_RAC] RING: ring (PATH) — утилита лицензий недоступна: ring: An error occurred trying to start process 'ring' with working directory 'D:\1cBases\Add\WorkSpace1C'. Не удается найти указанный файл.

</details>

Да - логи теперь пишет только по галочкам в настройках - тут спасибо.

5. **@7OH** — 2026-10-29T19:58:55Z — [https://github.com/sivatorov/Workspace1C/issues/83#issuecomment-6506956851](https://github.com/sivatorov/Workspace1C/issues/83#issuecomment-6506956851)

Можно же просто проверить запуском без указания каталога ?
Запустили "ring --version" и посмотрели - ошибка или работает.
Зачем её искать ?
"where.exe ring" и будет результат (под линукс "which ring")

6. **@sivatorov** — 2026-10-30T07:16:18Z — [https://github.com/sivatorov/Workspace1C/issues/83#issuecomment-6517190862](https://github.com/sivatorov/Workspace1C/issues/83#issuecomment-6517190862)

Исправлено в версии **1.0.1.4** (Windows и Linux):

Вкладка «Лицензии» в «Серверы 1С» — утилита `ring` теперь вызывается как есть, без ручного поиска:

- в [`RingLicenseService`](Workspace1C/Services/RingLicenseService.cs) доступность утилиты проверяется запуском `where.exe ring` / `which ring` (запасной способ — `ring --version`);
- сама команда выполняется как `ring` без подмены полного пути — поиск по PATH выполняет ОС;
- недоступность утилиты пишется INFO-записью, а не WARN-спамом при автообновлении.

Покрыто тестами [`Issue83LicenseTabTests.cs`](Workspace1C.Tests/Issue83LicenseTabTests.cs): вызов `ring` как есть и проверка через `where`/`which`.

Спасибо за сообщение! Проверьте, пожалуйста, в новом релизе.

Релиз **1.0.1.4** (Windows и Linux) будет опубликован в ближайшее время.

7. **@7OH** — 2026-10-30T08:22:54Z — [https://github.com/sivatorov/Workspace1C/issues/83#issuecomment-6518406001](https://github.com/sivatorov/Workspace1C/issues/83#issuecomment-6518406001)

Что-то не легчает
Ошибка всё та же

<details>
<summary>Логи</summary>

2026-10-30 11:21:27.803 [INFO] [CM_RAC] RING: ring (PATH) — утилита лицензий недоступна: ring: An error occurred trying to start process 'ring' with working directory 'D:\1cBases\Add\WorkSpace1C'. Не удается найти указанный файл.

</details>


---

### #87 — Загрузка расширения конфигурации в клиент-серверную базу

- **Ссылка:** https://github.com/sivatorov/Workspace1C/issues/87
- **Автор:** @aepkugesi (создано 2026-10-29T08:09:28Z, обновлено 2026-10-30T07:24:07Z)
- **Обоснование отбора:** Последний комментарий от @aepkugesi (2026-10-30T07:23:31Z) — не от @sivatorov

**Описание:**

Попытался воспользоваться функционалом загрузки расширения в клиент-серверную базу.
Не получилось
10:52:38 Установка «РасширениеКонфигурации» в базу «База разработки ЗУП»…
10:52:45 База разработки ЗУП: Конфигуратор завершился с кодом 1: Error in command-line options.
10:52:45 Готово: успешно 0 из 1.
Сам конфигуратор с приложения открывается.
Список расширений из базы тоже грузится.
Но расширение в базу не грузится.

**Комментарии (2):**

1. **@sivatorov** — 2026-10-29T17:42:06Z — [https://github.com/sivatorov/Workspace1C/issues/87#issuecomment-6504033973](https://github.com/sivatorov/Workspace1C/issues/87#issuecomment-6504033973)

Исправлено в версии **1.0.1.3** (Windows и Linux):

Установка расширения конфигурации (.cfe) в клиент-серверную базу через окно «Расширения» больше не падает с ошибкой «Конфигуратор завершился с кодом 1: Error in command-line options»:

- убран повторный ключ `-Extension` у операции `/UpdateDBCfg` в [`ConfigExtensionService.BuildInstallArguments`](Workspace1C/Services/ConfigExtensionService.cs);
- добавлен детектор ошибки «command-line options» и диагностическое логирование в [`DesignerCommandRunner.RunAsync`](Workspace1C/Services/DesignerCommandRunner.cs);
- при отказе конфигуратора автоматически выполняется fallback на ibcmd-ветку (`config load --extension` + `config apply --force`) в [`ExtensionsViewModel.InstallToCheckedAsync`](Workspace1C/ViewModels/ExtensionsViewModel.cs).

Благодарю за сообщение! Если в вашей конфигурации что-то пойдёт не так, пожалуйста, напишите — добавим в следующий релиз.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.3


2. **@aepkugesi** — 2026-10-30T07:23:31Z — [https://github.com/sivatorov/Workspace1C/issues/87#issuecomment-6517301447](https://github.com/sivatorov/Workspace1C/issues/87#issuecomment-6517301447)

Если в параметрах самой базы указать логин и пароль для авторизации - расширение загружается.
Если логин и пароль указаны в настройках приложения в параметрах авторизации по умолчанию - расширение не загружается.
Лог ошибки:
10:05:55 Установка «РасширениеКонфигурации» в базу «РД_001 3.0 (агенты)»…
10:06:11 РД_001 3.0 (агенты): Конфигуратор завершился с кодом 1: The infobase user is not authenticated
10:06:11 Готово: успешно 0 из 1.

---
