## [1.0.1.122] — 2026-11-25

### Исправлено

- **Текст уведомления об обновлении (issue #169, Windows и Linux).**
  Тост о доступном обновлении содержит только «Доступна новая версия X»: ссылка
  `release.HtmlUrl` больше не добавляется в текст (`BuildUpdateAvailableMessage`
  в `UpdateService` и `UpdateService.Avalonia`). Переход к странице релиза остаётся
  в диалоге обновления.

- **Пустое поле внизу программы (дефект из сообщения пользователя, Windows и Linux).**
  Прозрачная полоса внизу окна, через которую был виден рабочий стол, когда окно
  занимает всю рабочую область. Причина — `WindowChrome.GlassFrameThickness = -1`
  (расширенная рамка) применялась и в положении «окно на всю рабочую область».
  Добавлена чистая политика `WindowGlassFramePolicy.Resolve(maximized, windowBounds,
  workArea, tolerance)`: **0** при максимизации и прижатии окна к верхней/нижней
  границе рабочей области, иначе **-1**. Политика применена в
  `MainWindow.UpdateGlassFrameForMaximize` (пересчёт на `StateChanged`, `SizeChanged`,
  `LocationChanged`, `Loaded`) и в `WindowChromeHelper` для диалогов;
  `WindowWorkAreaLimiter.GetWorkAreaDip` стал `public`. Acrylic сохраняется,
  при максимизации поведение прежнее.

### Тесты

- `Issue169UpdateNotificationTextTests` — текст уведомления об обновлении без URL.
- `Issue170WindowGlassFrameTests` — политика толщины стеклянной рамки (максимизация,
  прижатие к верхней/нижней границе, допуск).

### Что проверить

- **Уведомление об обновлении (#169):** при наличии новой версии тост содержит только
  «Доступна новая версия X» — без ссылки на GitHub; диалог обновления по-прежнему
  позволяет перейти к странице релиза и скачать сборку.
- **Нижняя полоса:** разверните окно на всю рабочую область (без максимизации, вручную
  или двойным щелчком по заголовку) — внизу нет прозрачной полосы, через которую виден
  рабочий стол; acrylic-эффект сохранён. Проверьте то же для диалоговых окон.
- **Максимизация:** поведение прежнее, окно максимизируется штатно.

### Сборка

- Single-file сборки Windows (WPF) и Linux (Avalonia), версия **1.0.1.122**.
- Полный прогон тестов: **8277** тестов, **8277** пройдено, **0** падений,
  **0** пропущено (длительность 4 m 46 s).

Собранные артефакты (single-file, self-contained, версия `1.0.1.122`):

| Платформа | Файл | Размер | Версия |
|---|---|---|---|
| Windows x64 (WPF) | `dist/win-x64/Workspace1C.exe` | ≈81,7 МБ | `FileVersion` = `1.0.1.122` |
| Linux x64 (Avalonia) | `dist/linux-x64/Workspace1C` | ≈51,5 МБ | `1.0.1.122` (в релизе — `Workspace1C-linux-x64`) |

Windows-бинарь собирается штатным `build-windows-single-file.ps1 -Configuration Release`;
smoke-проверка `Workspace1C.exe --list` (код 0, `stderr` пуст) и подтверждение
`--license-info --json` → `"proModules": true`, `FileVersion` = `1.0.1.122`. Linux-бинарь —
кросс-сборкой `build-linux-single-file.ps1 -Configuration Release` (`-p:ForceLinux=true`,
linux-x64, `HasProModules=true`, строка версии `1.0.1.122` в бинаре); запуск и smoke-тест
Linux-бинаря выполняются в CI на `ubuntu-latest`. Точные размеры и результаты проверок —
в [`docs/_build_summary_101122.md`](docs/_build_summary_101122.md).

### Ссылки

- Полная история изменений: [`CHANGELOG.md`](CHANGELOG.md).
- Политика редакций и лицензирования: [`docs/pro/PRO-EDITION.md`](docs/pro/PRO-EDITION.md).
- Как купить и активировать ПРО: [`docs/pro/HOW-TO-BUY-PRO.md`](docs/pro/HOW-TO-BUY-PRO.md).
- Issues: [#169](https://github.com/sivatorov/Workspace1C/issues/169).
- Релиз: <https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.122>.
- Обсуждение и новости: [t.me/Workspace1C](https://t.me/Workspace1C).

Ключи для 1.x выпускаются с `major=1`; ключи `major=2` к версии 1.0.1.122 не подходят.
