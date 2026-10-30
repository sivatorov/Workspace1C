Исправлено в версии **1.0.1.4** (Windows и Linux):

Главное окно больше нельзя сжать меньше минимального размера — живой ресайз мышью не сжимает его ниже `MinWidth`/`MinHeight`:

- Avalonia — общий зажим `ModalWindowBase.ClampWindowToMinSize` применён к главному окну ([`MainWindow.Avalonia.cs`](Workspace1C/Views/MainWindow.Avalonia.cs));
- WPF — зажим через `WM_GETMINMAXINFO` (тот же механизм, что для окон «Настройки»).

Добавлены юнит-тесты [`Issue55AdaptiveLayoutTests.cs`](Workspace1C.Tests/Issue55AdaptiveLayoutTests.cs): минимум главного окна.

Спасибо за сообщение! Проверьте, пожалуйста, в новом релизе.

Релиз **1.0.1.4** (Windows и Linux) будет опубликован в ближайшее время.