Исправлено в версии **1.0.1.1** (Windows и Linux):

Перетаскивание колонок во вкладках «Сеансы/Соединения/Блокировки/Рабочие процессы»
окна «Серверы 1С» теперь действительно «как в 1С»:

- обычное перетаскивание границы двигает ВСЕ колонки справа как блок — их ширины
  не меняются, при переполнении области появляется горизонтальная прокрутка;
- перетаскивание с зажатым Ctrl меняет ширину ТОЛЬКО двух смежных колонок
  (минимальные ширины соблюдаются).

Причина поломки предыдущих фиксов — перехват вешался на Thumb-ручку по имени
шаблона; теперь перехват идёт ДО внутренней обработки DataGrid
(`PreviewMouseLeftButtonDown`/`CaptureMouse`), а единая математика вынесена в
[`ServerMonitorColumnWidthsStore.ApplyDragMoveBlock()`](Workspace1C/Services/ServerMonitorColumnWidthsStore.cs)
и
[`ApplyDragResizePair()`](Workspace1C/Services/ServerMonitorColumnWidthsStore.cs) —
одна реализация для WPF
([`ServerMonitorWindow.xaml.cs`](Workspace1C/Views/ServerMonitorWindow.xaml.cs))
и Avalonia
([`ServerMonitorWindow.Avalonia.cs`](Workspace1C/Views/ServerMonitorWindow.Avalonia.cs)).

Покрыто регрессионными тестами
[`ServerMonitorColumnWidthsTests.cs`](Workspace1C.Tests/ServerMonitorColumnWidthsTests.cs)
и
[`Issue75ServerMonitorColumnsTests.cs`](Workspace1C.Tests/Issue75ServerMonitorColumnsTests.cs)
(эмуляция drag, capture-циклы).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.1