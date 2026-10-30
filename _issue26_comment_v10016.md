Исправлено в версии **1.0.1.6** (Windows и Linux):

1. **Тёмная тема: поля окна «Синхронизация настроек».** Все контролы ввода WPF-окна (поля «Интервал автозапуска», «Файлы для синхронизации», журнал операций и др.) получают тематические стили `ModernTextBox`/`ModernComboBox`/`ModernListBoxItem` и кисти темы
([`SettingsSyncWindow.ApplyModernStyles`](Workspace1C/Views/SettingsSyncWindow.cs) по образцу `TestDeployWindow`); Avalonia-версия окна — `.Styled(ControlThemes.ModernTextBox/…)`.

2. **Белая подсветка неактивных кнопок главного окна.** В тёмной теме
[`ThemeFeatureMap.InactiveHoverHex`](Workspace1C/Themes/ThemeFeatureMap.cs) теперь возвращает тёмный slate `#334155` и для стиля Glass (исключение убрано — полупрозрачно-белая подложка Glass и была «белой подсветкой»; macOS-«светофор» с цветными кругами не тронут); Avalonia-метрики
([`WindowButtonThemeMetrics`](Workspace1C/Themes/WindowButtonThemeMetrics.Avalonia.cs)) больше не захардкожены на светлый вариант — fallback учитывает фактическую тему; ресурсы кнопок окна обновляются при каждом применении темы (`ApplyWindowButtonStyle` вызывается и из `ApplyTheme`).

Спасибо за сообщение! Проверьте, пожалуйста, в новом релизе.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.6
