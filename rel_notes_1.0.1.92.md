# Workspace 1C 1.0.1.92

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).

## Изменено

- **Единый набор стилей кнопок (аудит UI, пункт P2-2).** В окнах больше нет
  локальных копий стилей кнопок — все они сведены к шести каноническим ключам
  темы:
  - `PrimaryButton` — акцентная кнопка (алиас `ModernButton` + полужирный
    текст и padding 14,8);
  - `SecondaryButton` — карточка + контур, при наведении меняется только рамка
    (контракт issue #107);
  - `DangerButton` — опасные действия (семантика P2-1 сохранена);
  - `IconButton` — 32×32, прозрачный фон, hover `ItemHoverBrush`;
  - `SelectAllButton` — компактная кнопка «Выбрать все» для таблиц;
  - `RowActionButton` — иконочная кнопка в колонке «Действия» главного окна.
- Удалены 15 копий `PrimaryButtonStyle`, 4 варианта `OutlineButtonStyle`,
  3 варианта `IconOnlyButton`, 2 варианта `SelectAllButtonStyle`, а также
  `ActionButton`, `CommandButton`, `DialogButton`, `RowIconButton`,
  `RowActionChipButton`, `OutlinedButton`, `PaletteColorButton`, `IconPickButton`
  и `OptionRadioButton`.
- В `PlatformDownloadWindow` и `UpdateCheckWindow` убрана заливка вторичных
  кнопок при наведении — осталась только акцентная рамка.
- Padding текстовых кнопок приведён к 14,8 при `MinHeight 36`, иконочных — к 8;
  радиус скругления — 6 из темы.
- «Голые» кнопки в `ScheduledTasksWindow`, `CustomProgramsWindow`,
  `ServerJobsWindow`, `WhatsNewWindow` и `GroupPickerWindow` переведены на
  стили темы.
- Изменения симметричны в WPF (`LightTheme.xaml`, `DarkTheme.xaml`) и Avalonia
  (`Controls.axaml`, `ControlThemes.Avalonia.cs`).

## Не менялось

- Строки групп, навигация, правая панель действий, размеры иконок, сетка
  отступов и типографика — это отдельные пункты плана.
- Семантика опасных кнопок (1.0.1.91) сохранена.

## Тесты

- Добавлен `ButtonStyleUnificationP22Tests`:
  - в `Workspace1C/Views/*.xaml` нет объявлений `<Style x:Key="…Button…">`;
  - все ключи стилей, на которые ссылаются окна, есть в словарях WPF и Avalonia;
  - `SecondaryButton` при hover не меняет фон — только `BorderBrush`;
  - `IconButton` — 32×32 и `ItemHoverBrush`;
  - `PrimaryButton`/`ModernButton` — padding 14,8.
- `Issue107AddWindowTests` и `SettingsWindowXamlResourcesTests` обновлены под
  перенос стилей в тему.
- Всего тестов: **7413**, все зелёные.

## Известные ограничения

- Визуальная проверка в среде сборки недоступна: перечисленные окна проверены
  source-тестами и сравнением определений стилей.
- Посторонний сбой CI: job `Linux` в workflow «Сборка» падает ~107 тестами
  (Windows-санитизация путей и др.); к этому выпуску отношения не имеет и не
  исправлялся.

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).
