Исправлено в версии **1.0.1.4** (Windows и Linux):

Тёмная тема — надписи окна «Синхронизация настроек» и кнопки управления окна:

- подписи полей привязаны к кисти `FormLabelBrush`, значение «последняя синхронизация» — к `TextSecondaryBrush` ([`SettingsSyncWindow.cs`](Workspace1C/Views/SettingsSyncWindow.cs)) — надписи больше не теряются на тёмном фоне;
- для кнопок управления окном при НЕактивном окне добавлена отдельная кисть подложки `WindowButtonInactiveHoverBrush` (тёмная в тёмной теме) — белая «подсветка» кнопок на тёмной шапке убрана (WPF — [`App.xaml`](Workspace1C/App.xaml), [`ThemeFeatureMap.cs`](Workspace1C/Themes/ThemeFeatureMap.cs); Avalonia — [`WindowButtonThemeMetrics.Avalonia.cs`](Workspace1C/Themes/WindowButtonThemeMetrics.Avalonia.cs), [`MainWindow.Avalonia.Controls.cs`](Workspace1C/Views/MainWindow.Avalonia.Controls.cs)).

Покрыто тестами [`ThemeFeatureMapTests.cs`](Workspace1C.Tests/ThemeFeatureMapTests.cs) и [`Issue26SyncWindowButtonsTests.cs`](Workspace1C.Tests/Issue26SyncWindowButtonsTests.cs): контраст надписей и кнопок управления окна.

Спасибо за внимательную проверку оформления! Проверьте, пожалуйста, в новом релизе.

Релиз **1.0.1.4** (Windows и Linux) будет опубликован в ближайшее время.