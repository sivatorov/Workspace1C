Исправлено в версии 1.0.0.67 (Windows и Linux): вкладка настроек «Меню „Утилиты“» переименована в «Правка меню» и объединяет оба редактора; для контекстного меню базы добавлены галочка «Правка структуры контекстного меню» и кнопка «Настроить структуру…».

Редактор позволяет перемещать пункты (вверх/вниз, в группу/в корень), скрывать их, группировать и добавлять разделители; новые пункты программы автоматически попадают в подменю «Новинки программы». Компоновка хранится в файле `context_menu_layout.json` (атомарная запись, нормализация битого файла).

Изменённые файлы: [`ContextMenuLayout.cs`](Workspace1C/Models/ContextMenuLayout.cs), [`ContextMenuCatalog.cs`](Workspace1C/Services/ContextMenuCatalog.cs), [`ContextMenuResolver.cs`](Workspace1C/Services/ContextMenuResolver.cs), [`ContextMenuLayoutStore.cs`](Workspace1C/Services/ContextMenuLayoutStore.cs), [`ContextMenuEditorViewModel.cs`](Workspace1C/ViewModels/ContextMenuEditorViewModel.cs), [`ContextMenuEditorWindow.xaml`](Workspace1C/Views/ContextMenuEditorWindow.xaml), [`ContextMenuEditorWindow.Avalonia.cs`](Workspace1C/Views/ContextMenuEditorWindow.Avalonia.cs), [`MainWindow.ContextMenu.cs`](Workspace1C/Views/MainWindow.ContextMenu.cs), [`MainWindow.Avalonia.Tree.cs`](Workspace1C/Views/MainWindow.Avalonia.Tree.cs), [`SettingsWindow.xaml`](Workspace1C/Views/SettingsWindow.xaml).

Реализовано на обеих платформах (WPF и Avalonia).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.67
