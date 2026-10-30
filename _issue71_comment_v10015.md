Исправлено в версии **1.0.1.5** (Windows и Linux):

Окна «Настройки» и «Настройка информационной базы» теперь запоминают размеры и открываются с последними размерами:

- общий хелпер [`WindowSizeStore`](Workspace1C/Services/WindowSizeStore.cs) (`ApplyMin`/`IsValid`/`SaveSize`) + 4 новых поля в [`AppSettings`](Workspace1C/Models/AppSettings.cs): `SettingsWindowWidth/Height` и `ConnectionSettingsWindowWidth/Height`;
- при закрытии окна сохраняются фактические размеры, при открытии восстанавливаются с зажимом к `MinWidth`/`MinHeight` (значение 0 — «не задано» → открывается стартовым размером);
- WPF — [`SettingsWindow.xaml.cs`](Workspace1C/Views/SettingsWindow.xaml.cs), [`ConnectionSettingsWindow.xaml.cs`](Workspace1C/Views/ConnectionSettingsWindow.xaml.cs); Avalonia — [`SettingsWindow.Avalonia.cs`](Workspace1C/Views/SettingsWindow.Avalonia.cs), [`ConnectionSettingsWindow.Avalonia.cs`](Workspace1C/Views/ConnectionSettingsWindow.Avalonia.cs).

Минимумы 960×720 / 760×780 и гарантия минимума (1.0.1.3/1.0.1.4) сохраняются — восстановленный размер клампится к `MinWidth`/`MinHeight`.

Покрыто тестами [`Issue71SettingsWindowLayoutTests.cs`](Workspace1C.Tests/Issue71SettingsWindowLayoutTests.cs): `WindowSizeStore` (`ApplyMin`/`IsValid`/`SaveSize`), восстановление и зажим размеров для WPF и Avalonia.

Спасибо за сообщение! Проверьте, пожалуйста, в новом релизе.

Релиз **1.0.1.5** (Windows и Linux) будет опубликован в ближайшее время.