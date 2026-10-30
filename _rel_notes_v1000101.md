# Workspace 1C 1.0.0.101

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.101

### Исправлено

- **Окна «Настройки» и «Настройка информационной базы»: комфортные стартовые размеры
  (issue #71, комментарии RizvanShikhammatov и 7OH)** — после фикса 1.0.0.82 «размах»
  вкладок исчез, но окна открывались сразу в минимальном размере и выглядели
  «суженными» (скриншот 794×753). По замечанию «надо звать человека, чтобы он
  формочки просмотрел и выставил корректные значения» выставлены корректные
  стартовые размеры при сохранении минимальных:

  1. Окно «Настройки»: стартовые **1100×760** (было 960×720), минимум **960×720**
     сохранён — сузить окно мышкой ниже минимума нельзя, содержимое вкладок
     не обрезается и горизонтальная прокрутка не появляется
     ([`SettingsWindow.xaml`](Workspace1C/Views/SettingsWindow.xaml),
     [`SettingsWindow.Avalonia.cs`](Workspace1C/Views/SettingsWindow.Avalonia.cs)).
  2. Окно «Настройка информационной базы»: стартовые **900×800** (было 760×780),
     минимум **760×780** сохранён
     ([`ConnectionSettingsWindow.xaml`](Workspace1C/Views/ConnectionSettingsWindow.xaml),
     [`ConnectionSettingsWindow.Avalonia.cs`](Workspace1C/Views/ConnectionSettingsWindow.Avalonia.cs)).
  3. Горизонтальная прокрутка вкладок остаётся отключённой (Disabled) на обеих
     платформах — при любом сужении до минимума контент не «уезжает» за правый край.

### Тесты

- [`Issue71SettingsWindowLayoutTests.cs`](Workspace1C.Tests/Issue71SettingsWindowLayoutTests.cs):
  +4 — стартовые размеры окон заданы на обеих платформах (WPF и Avalonia) и больше
  минимальных; минимальные размеры и запрет горизонтальной прокрутки вкладок
  сохранены.

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).