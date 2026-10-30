Исправлено в версии 1.0.0.66 (Windows и Linux): пункт «Для выделенных» контекстного меню базы в списке теперь виден и для одной выделенной базы — без нажатия Ctrl (ранее — только при выделении нескольких баз).

При правом клике по базе блок «Для выделенных» (включая «Переместить в группу» и остальные массовые команды) показывается и работает по выделенной базе; мультивыделение по-прежнему имеет приоритет.

Добавлена единая логика `BatchSelectionHelper.EffectiveBatchCount` ([`BatchSelectionHelper.cs`](Workspace1C/Services/BatchSelectionHelper.cs)); команды меню и подключения обновлены ([`MainViewModel.BatchCommands.cs`](Workspace1C/ViewModels/MainViewModel.BatchCommands.cs), [`MainWindow.Events.cs`](Workspace1C/Views/MainWindow.Events.cs)). WPF + Avalonia.

Добавлены юнит-тесты `BatchSelectionHelperTests` (+4: только одна выделенная база, приоритет набора и др.).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.66