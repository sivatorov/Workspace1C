# Workspace 1C 1.0.1.1

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.1

### Исправлено

- **Перетаскивание колонок «Серверы 1С» — как в 1С (issue #75, Windows и Linux).**
  Обычный drag двигает все колонки справа блоком (ширины не меняются, при
  переполнении — горизонтальная прокрутка), drag с Ctrl меняет ширину только двух
  смежных колонок (минимальные ширины соблюдаются). Перехват идёт до внутренней
  обработки DataGrid (`PreviewMouseLeftButtonDown`/`CaptureMouse`), единая
  математика — `ApplyDragMoveBlock`/`ApplyDragResizePair` на обеих платформах.

- **Автообновление «Серверов 1С» при подключении/переключении сервера (issue #77,
  Windows и Linux).** Таймер автообновления останавливается до начала загрузки и
  возобновляется после успешного подключения, если галка стоит; «микс» от
  автообновления и подключения больше не даёт ошибок.

- **Удаление базы из кластера: завершение сеансов с повторами (issue #69, Windows
  и Linux).** До 5 попыток завершения сеансов/разрыва соединений с паузами (1–2 с)
  и индикацией «Попытка N из M…»; ошибка «освобождаемый контекст» — пауза и
  повтор; при исчерпании — понятное сообщение и диалог повтора.

- **Вкладка «Лицензии» в «Серверы 1С» (issue #83, Windows и Linux).** Без ring —
  INFO в журнале (не WARN-спам) и понятное сообщение во вкладке с указанием, где
  ищется утилита (PATH/каталог bin рядом с rac); вкладка перенесена перед
  «Информация о кластере».

- **Мастер «Импорт/Миграция из кластера 1С» (issue #41, Windows и Linux).** При
  галочке «Не удалять в старом кластере» вопрос о замене строк подключения больше
  не задаётся; при переносе поведение прежнее.

### Тесты

- [`ServerMonitorColumnWidthsTests`](Workspace1C.Tests/ServerMonitorColumnWidthsTests.cs) /
  [`Issue75ServerMonitorColumnsTests`](Workspace1C.Tests/Issue75ServerMonitorColumnsTests.cs)
  (эмуляция drag, capture-циклы);
- [`ServerMonitorViewModelTests`](Workspace1C.Tests/ServerMonitorViewModelTests.cs)
  (+пауза таймера, +ретраи #69);
- [`Issue83LicenseTabTests`](Workspace1C.Tests/Issue83LicenseTabTests.cs) (новый);
- [`ClusterMigrationViewModelTests`](Workspace1C.Tests/ClusterMigrationViewModelTests.cs) /
  [`Issue41WindowLayoutTests`](Workspace1C.Tests/Issue41WindowLayoutTests.cs) (#41);
- стабилизация флака FeatureDataStore (`DisableParallelization`).

Итого в проекте **~4 985 юнит-тестов**; Linux-сборка (Avalonia) также зелёная.

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).