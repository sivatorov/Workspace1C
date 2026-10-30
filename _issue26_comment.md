Исправлено в версии 1.0.0.66 (Windows и Linux): цвет текста выбранного пункта левой панели «Настроек» теперь единый и контрастный во всех разделах — включая «Резервное копирование» и «Учетные записи», где он был чёрным вместо светлого.

Причина: в шаблоне вкладок WPF цвет заголовка не наследовался (`TemplateBinding` не работал с attached-свойством) — заменено на `TemplatedParent`-привязку, код-вкладки получили явную привязку подписи к `TabItem.Foreground` ([`SettingsWindow.xaml`](Workspace1C/Views/SettingsWindow.xaml), [`SettingsWindow.Accounts.cs`](Workspace1C/Views/SettingsWindow.Accounts.cs), [`SettingsWindow.Profile.cs`](Workspace1C/Views/SettingsWindow.Profile.cs)). Avalonia уже использовала контрастную кисть при `IsSelected`.

Добавлены юнит-тесты `SettingsTabItemContrastTests` (9 шт.).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.66