## [1.0.0.65] — 2026-10-23

### Исправлено

- **Переключение вкладок «Все / Избранное / Недавние» больше не пересоздаёт корни
  дерева (issue #21, Windows и Linux)** — при чистой смене вкладки коллекция
  `GroupNodes` всегда содержит стабильный набор корней (плоский узел + все группы),
  а переключение меняет только видимость узлов (`IsVisible`) и содержимое плоского
  узла. Раньше переход «Все» (N групп) ↔ «Недавние» (1 плоский узел) давал нулевое
  пересечение составов, и `ReplaceItemWise` уходил в полный `Reset` — корни дерева
  пересоздавались целиком, что давало рост памяти (`heapAfterGcMb` 40 → 79 МБ за
  ~100 переключений в логе тестировщика v1.0.0.62) и накопление задержек.
  Реализовано на обеих платформах (WPF и Avalonia): стабильные корни поддерживают
  `GroupNodeViewModel.IsVisible`, `MainViewModel.ApplyStableRoots`/`SyncStableRoots`
  и `IsFlatModeActive`, обход дерева (RevealPlanner.cs, MainWindow.Tree.cs,
  MainWindow.Hotkeys.cs, MainViewModel.Batch.cs) и привязки видимости в
  представлениях (MainWindow.xaml DataTrigger, MainWindow.Avalonia.cs).
  Диагностика `CM_TREESWITCH` сохранена: при чистом переключении `collReset=0`.
  Добавлены юнит-тесты TreeSwitchStableRootsTests (7 шт.).
