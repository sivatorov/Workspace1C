# План: окно «Как купить программу» вместо GitHub-ссылки у кнопки «Купить» (v1.0.1.53)

Статус: **план (только анализ, код не менялся)**. Версия-цель: **1.0.1.53** (текущая 1.0.1.52).
Базируется на незакоммиченных изменениях ПРО-задачи в рабочем дереве (README «🛒 ПРО-версия»,
ключи `Pro.*`, `LicenseViewModel.cs`, `ProFeature.cs`, `LicenseWindow.cs`/`.Avalonia.cs`,
`CliLicenseCommands.cs` и др.) — **их не откатывать**; реализация аккуратно «достраивается»
поверх (см. раздел 6 о пересечениях).

---

## 1. Что найдено в репозитории о покупке ПРО

### 1.1 Цена и условия (источник: `docs/pro/PRO-EDITION.md:9`, `README.md:1359`, ключ `Pro.HowToBuy`)
- ПРО — **2000 ₽**, бессрочная в рамках старшей версии (2.x.x.x), привязка к компьютеру.
- Бесплатная 1.0.x остаётся под MIT навсегда.

### 1.2 Контакты (источник: `docs/pro/PRO-EDITION.md:14-24`, `README.md:1369-1380`)
- Telegram-**канал**: [t.me/Workspace1C](https://t.me/Workspace1C) (анонсы; канал для
  связи «в личку» не предназначен — нет @username автора для личных сообщений);
- Issues репозитория: <https://github.com/sivatorov/Workspace1C/issues>;
- репозиторий: <https://github.com/sivatorov/Workspace1C>.
- **Email в репозитории отсутствует.** Реквизиты оплаты (карта/СБП/никнейм платёжной
  системы) **в репозитории отсутствуют**.

### 1.3 Пошаговый порядок покупки (источник: `README.md:1369-1371`, `PRO-EDITION.md:113-124`)
1. Скопировать **код компьютера**: окно «Лицензия» («Утилиты»/«Справка» → «Лицензия…»),
   кнопка «Скопировать код», либо `Workspace1C --license-info`.
2. Отправить код на контакт проекта — Telegram-канал или Issues.
3. Автор выпускает ключ утилитой `LicenseIssuer` (приватный репозиторий
   `../Workspace1C.Pro/tools/LicenseIssuer/`: `KeyIssuer.cs`, `Program.cs` — CLI
   `LicenseIssuer issue --machine … --owner … [--email …]`; окно `IssueKeyWindow.cs`;
   журнал продаж `issued.csv`).
4. Пользователь вставляет ключ в окно «Лицензия» → «Активировать» или
   `Workspace1C --activate "W1C2.…"`. Активация офлайн.

### 1.4 donat.png
`donat.png` в корне репозитория — спонсорская картинка, встроенная в ресурсы сборки и
показываемая в «О программе» (`Workspace1C/Views/SettingsWindow.Platforms.cs:430-465`,
`SettingsWindow.Avalonia.cs:5019-5210`). **Не подтверждено**, что она содержит реквизиты,
пригодные для оплаты ПРО (скорее разовые донаты). Нужно уточнить у владельца (см. раздел 3).

### 1.5 Кнопка «Купить» сегодня — все точки, ведущие на GitHub
Единственный источник URL — константа
[`ProFeatures.BuyUrl`](Workspace1C/Services/Licensing/ProFeature.cs:90) =
`https://github.com/sivatorov/Workspace1C#-про-версия`. Используется **только в двух местах**:

| # | Файл | Место |
|---|------|-------|
| 1 | [`LicenseWindow.cs:50-55`](Workspace1C/Views/LicenseWindow.cs) (WPF/Windows) | кнопка `Pro.Buy` → `OneCLauncher.OpenUrl(ProFeatures.BuyUrl)` |
| 2 | [`LicenseWindow.Avalonia.cs:71-76`](Workspace1C/Views/LicenseWindow.Avalonia.cs) (Linux) | кнопка `Pro.Buy` → `Services.OneCLauncher.OpenUrl(ProFeatures.BuyUrl)` |

Прочие точки входа к покупке **уже** открывают окно «Лицензия» (не GitHub) и менять их не нужно:
- «Утилиты» → «Лицензия…» (`UtilitiesMenuCatalog.cs:680`, `MainWindow.UtilitiesMenu.cs:331-332`, `MainWindow.Avalonia.Tree.cs:2283-2284`);
- гейт `ProGate.Ensure(...)` во всех ПРО-командах (MainViewModel.*.cs, SchedulerService,
  ScheduledTaskEditWindow и т.д.) → `ProGate.ShowLicenseDialog` → `LicenseWindow`;
- CLI `--activate`/`--license-info` (`CliLicenseCommands.cs`) — без UI.

Вывод: изменение поведения — **только два обработчика кнопок `Pro.Buy`** + новое окно.

---

## 2. Решение по окну

**Новое окно «Как купить» (HowToBuyWindow)**, а не расширение LicenseWindow — причины:
- окно «Лицензия» уже плотное (код ПК, ключ, список функций); инструкция покупки нужна
  пользователю один раз и в отдельном модальном окне читается лучше;
- ViewModel остаётся переиспользуемым: окно берёт `MachineCode` из `LicenseViewModel`
  (или лёгкий `HowToBuyViewModel`, обёртка над `MachineIdProvider.Current`);
- паттерн проекта — окна, построенные кодом (как `LicenseWindow`: WPF `WpfUi.*`-хелперы,
  Avalonia `AvaloniaUi.*`), общего XAML/AXAML у этого окна нет — двойной UI сохраняется.

### 2.1 Состав окна (ru/en локализация через `LocalizationManager.T`)
1. Заголовок: `Pro.HowToBuy.Title` («Как купить программу» / "How to buy").
2. Пошаговая инструкция (статичный текст, ключи `Pro.HowToBuy.Step1..Step4`):
   1) скопировать код компьютера (в окне — поле + кнопка «Скопировать код»);
   2) связаться с автором (контакты — см. 2.2) и оплатить 2000 ₽;
   3) получить ключ `W1C2.…`;
   4) вставить ключ в окно «Лицензия» → «Активировать» (или `--activate`). Без перезапуска.
3. Блок контактов с действиями:
   - кнопка «Открыть Telegram» → `OpenUrl("https://t.me/Workspace1C")`;
   - кнопка «Открыть Issues» → `OpenUrl("https://github.com/sivatorov/Workspace1C/issues")`;
   - кнопки «Скопировать …» — только для данных, которые даст владелец (email, карта/СБП,
     @username) — до получения данных не реализовывать, места зарезервировать.
4. Кнопка «Открыть раздел в README» → прежнее поведение `OpenUrl(ProFeatures.BuyUrl)`
   (ссылка сохраняется как справочная, `Pro.HowToBuy.OpenReadme`).
5. Кнопка «Готово»/«Закрыть».

### 2.2 Новые файлы
- `Workspace1C/ViewModels/HowToBuyViewModel.cs` — MachineCode/MachineCodeText,
  CopyCode() (переиспользуемый), ContactUrl-константы; данные контактов — из
  `ProFeatures` (новые константы: `TelegramUrl`, `IssuesUrl` и, после ответа владельца,
  `SupportEmail`, `SupportTelegramHandle`, `PaymentInfo`).
- `Workspace1C/Views/HowToBuyWindow.cs` — WPF (`#if WINDOWS`, как `LicenseWindow.cs`).
- `Workspace1C/Views/HowToBuyWindow.Avalonia.cs` — Avalonia (`ModalWindowBase`,
  `ShowDialogSync(owner)`, как `LicenseWindow.Avalonia.cs`).

### 2.3 Точки вызова нового окна
- `LicenseWindow.cs` (WPF): обработчик кнопки `Pro.Buy` → `new HowToBuyWindow { Owner = this }.ShowDialog()`
  вместо `OpenUrl(BuyUrl)` ([`LicenseWindow.cs:50-55`](Workspace1C/Views/LicenseWindow.cs));
- `LicenseWindow.Avalonia.cs`: → `new HowToBuyWindow().ShowDialogSync(this)`
  ([`LicenseWindow.Avalonia.cs:71-76`](Workspace1C/Views/LicenseWindow.Avalonia.cs));
- `ProFeatures.BuyUrl` остаётся — используется кнопкой «Открыть раздел в README» внутри окна.
- Контекстное меню / командная палитра / ПРО-гейты — без изменений (они открывают
  `LicenseWindow`, который теперь сам ведёт в окно покупки).

---

## 3. Недостающие данные — запросить у владельца проекта (блокер для текста окна)

| # | Данные | Зачем |
|---|--------|-------|
| 1 | **Способ оплаты ПРО**: карта (банк/номер) или СБП (телефон/банк), либо никнейм ЮMoney/Boosty и т.п. | Кнопка «Скопировать реквизиты» в окне; шаг 2 инструкции |
| 2 | **@username Telegram автора** для личных сообщений (t.me/Workspace1C — канал, писать в него нельзя) | Кнопка «Написать в Telegram» (tg://resolve?domain=…) |
| 3 | **Email** для отправки кода компьютера и получения ключа | Кнопка mailto:/копирование |
| 4 | **Канал доставки ключа**: куда приходит ключ (Telegram/email/issue)? | Формулировка шага 3 |
| 5 | Содержит ли `donat.png` реквизиты, пригодные для оплаты ПРО? Если да — стоит ли показывать картинку в окне покупки (паттерн уже есть в «О программе») | Решение по блоку оплаты |
| 6 | Подтверждение цены 2000 ₽ для окна (и её строки в en: "RUB 2,000"?) | Локализация |
| 7 | Нужен ли чек/договор (самозанятость: ссылка на чек СБП)? | Текст «Оплата и контакты» |

До получения ответов окно собирается с уже известными данными (цена, Telegram-канал,
Issues, README) — блоки «email/реквизиты» добавляются следом без изменения архитектуры.

---

## 4. Локализация

Новые ключи в `Workspace1C/Localization/Languages/ru.json` и `en.json` (рядом с блоком `Pro.*`,
[`ru.json:4675-4719`](Workspace1C/Localization/Languages/ru.json)):
- `Pro.HowToBuy.Title` — заголовок окна;
- `Pro.HowToBuy.Step1` … `Pro.HowToBuy.Step4` — шаги (в ru упоминается «2000 ₽», в en — "RUB 2,000");
- `Pro.HowToBuy.ContactsHeader` — «Контакты»;
- `Pro.HowToBuy.OpenTelegram`, `Pro.HowToBuy.OpenIssues`, `Pro.HowToBuy.OpenReadme`;
- `Pro.HowToBuy.CopyPayment`, `Pro.HowToBuy.PaymentCopied` — реквизиты (после ответа владельца);
- `Pro.HowToBuy.PaymentLabel` — «Реквизиты оплаты:» (текст из константы/ключа).

Тексты держать в ключах (не в коде) — паттерн проекта; машина/цена не хардкодятся в en.

## 5. Тесты (паттерн существующих — `Workspace1C.Tests`)

Новый файл `Workspace1C.Tests/HowToBuyWindowV100153Tests.cs` (по образцу
[`ProLicensingTests.cs`](Workspace1C.Tests/ProLicensingTests.cs) — локаль-проверки через
`ReadStrings("ru"/"en")` и source-checks через `ReadProjectFile`, как в
[`Issue83LicenseTabTests.cs`](Workspace1C.Tests/Issue83LicenseTabTests.cs)):
1. `HowToBuy_Keys_PresentInBothLanguages` — все ключи `Pro.HowToBuy.*` существуют в ru.json и en.json.
2. `HowToBuy_StepsMentionPrice` — ru-тексты содержат «2000», en — "2,000" (паттерн теста
   цены из `ProLicensingTests.cs:458-463`).
3. `Buy_Button_OpensHowToBuy_NotGitHub` — source-check: в `LicenseWindow.cs` и
   `LicenseWindow.Avalonia.cs` обработчик `Pro.Buy` больше **не** вызывает
   `OpenUrl(ProFeatures.BuyUrl)`; вызывается `HowToBuyWindow`.
4. `HowToBuyWindow_Exists_ForBothPlatforms` — существуют оба файла окна; Avalonia-версия
   наследует `ModalWindowBase`, WPF — `#if WINDOWS`.
5. `HowToBuyViewModel_MachineCode_MatchesLicenseViewModel` — `MachineCode` совпадает с `MachineIdProvider.Current`; `CopyCode` пишет в буфер (по паттерну существующих VM-тестов).
6. `BuyUrl_StillUsedByReadmeButton` — `ProFeatures.BuyUrl` сохранён и используется
   в `HowToBuyWindow.*.cs` (справочная кнопка).

Обновить существующие:
- тест версии в [`Release100127Tests.cs:42-54`](Workspace1C.Tests/Release100127Tests.cs) —
  паттерн: обновить ожидания на 1.0.1.53 (или по текущей практике релиза добавить/править
  актуальный тест версии);
- при правке `Pro.HowToBuy` (тексты шагов) — тест `Pro.HowToBuy` содержит «2000» уже есть
  в `ProLicensingTests.cs:459-463`, сохранить его зелёным.

## 6. Пересечение с незакоммиченными изменениями другой ПРО-задачи

Файлы, которые придётся править, **уже изменены** той задачей: `LicenseWindow.cs`,
`LicenseWindow.Avalonia.cs`, `ProFeature.cs` (константа `BuyUrl`), `ru.json`/`en.json`,
`README.md`. Правила работы:
- **не откатывать** и не переписывать чужие изменения — только точечные правки
  (apply_diff) по строкам обработчика `Pro.Buy` и добавление новых ключей рядом с `Pro.*`;
- новое окно и VM — **новые файлы**, конфликтов не создают;
- `README.md`: править только строку бейджа версии и, при необходимости, один абзац
  в блоке «🛒 ПРО-версия» (шаг 2: «нажмите „Купить“ в окне „Лицензия“ — откроется
  инструкция с контактами»), не трогая остальной блок;
- коммит: отдельный коммит поверх коммита другой ПРО-задачи; при её отсутствии в момент
  релиза — согласовать порядок коммитов.

## 7. Версия, CHANGELOG, README

- [`Workspace1C/Workspace1C.csproj:62-65`](Workspace1C/Workspace1C.csproj): `Version`,
  `AssemblyVersion`, `FileVersion`, `InformationalVersion` → **1.0.1.53**;
- [`CHANGELOG.md`](CHANGELOG.md): новая запись `## [1.0.1.53] — <дата>` — «Окно „Как купить
  программу“: кнопка „Купить“ в окне „Лицензия“ открывает пошаговую инструкцию покупки
  (контакты, оплата, получение и активация ключа) вместо перехода на GitHub — Windows и Linux»;
- [`README.md:3`](README.md): бейдж `Версия-1.0.1.53`; строка 11 «актуальная версия»;
- при наличии реквизитов от владельца — обновить блок «🛒 ПРО-версия» и
  `docs/pro/PRO-EDITION.md` (раздел «Оплата и контакты»).

## 8. Порядок реализации (чек-лист)

- [ ] Получить от владельца недостающие данные (раздел 3) — можно параллельно;
- [ ] `ProFeatures`: добавить константы `TelegramUrl`, `IssuesUrl` (+ `SupportEmail`,
      `PaymentInfo` после ответа) в `Workspace1C/Services/Licensing/ProFeature.cs`;
- [ ] `HowToBuyViewModel.cs` — новый файл;
- [ ] `HowToBuyWindow.cs` (WPF) и `HowToBuyWindow.Avalonia.cs` — новые файлы
      (по образцу `LicenseWindow.cs` / `.Avalonia.cs`);
- [ ] `LicenseWindow.cs:50-55` и `LicenseWindow.Avalonia.cs:71-76` — кнопка `Pro.Buy`
      открывает `HowToBuyWindow` вместо `OpenUrl(BuyUrl)`;
- [ ] `ru.json` / `en.json` — ключи `Pro.HowToBuy.*`;
- [ ] тесты `HowToBuyWindowV100153Tests.cs` + актуализация теста версии;
- [ ] bump 1.0.1.53 в csproj, CHANGELOG.md, README.md;
- [ ] `dotnet test` (Workspace1C.Tests) и ручная проверка обоих UI: окно открывается из
      «Лицензия» → «Купить», кнопки Telegram/Issues/README работают, тёмная тема — по
      общим стилям (`CardBackgroundBrush` и т.п.).

## 9. Риски

- Реквизиты/контакты не подтверждены владельцем → окно выпускается с Telegram-каналом и
  Issues, блок оплаты добавляется следом (архитектура это допускает).
- WPF `Clipboard` может быть занят другим приложением — обработать как в `LicenseWindow.cs:45-49`.
- Не забыть: тест цены (`2000`/`2,000`) и локаль-тесты сломаются, если шаги переписать без цены.
- Donat.png не показывать в окне покупки без подтверждения владельца (реквизиты могли измениться).
