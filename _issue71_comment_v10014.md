Исправлено в версии **1.0.1.4** (Windows и Linux):

Окна «Настройки» и «Настройка информационной базы» больше нельзя сжать ниже минимального размера — единый механизм гарантии минимума:

- WPF — перехват `WM_GETMINMAXINFO` (`ptMinTrackSize` с учётом DPI) в [`WindowChromeHelper`](Workspace1C/Views/WindowChromeHelper.cs);
- Avalonia — общий зажим по `ClientSizeProperty` в [`ModalWindowBase.ClampWindowToMinSize`](Workspace1C/Views/ModalWindowBase.cs).

Окна больше не «ужимаются до размера заголовка».

Добавлены юнит-тесты [`Issue71SettingsWindowLayoutTests.cs`](Workspace1C.Tests/Issue71SettingsWindowLayoutTests.cs): зажим минимума для WPF и Avalonia.

Спасибо за сообщение! Проверьте, пожалуйста, в новом релизе.

Релиз **1.0.1.4** (Windows и Linux) будет опубликован в ближайшее время.