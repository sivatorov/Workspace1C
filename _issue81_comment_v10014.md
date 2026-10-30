Исправлено в версии **1.0.1.4** (Windows и Linux):

Иконка группы «Новинки программы» в меню «Утилиты» — теперь всегда та же NewBox, что и в контекстном меню:

- построители меню ([`MainWindow.UtilitiesMenu.cs`](Workspace1C/Views/MainWindow.UtilitiesMenu.cs), [`MainWindow.Avalonia.Tree.cs`](Workspace1C/Views/MainWindow.Avalonia.Tree.cs)) всегда рисуют группе «Новинки программы» каталоговую иконку NEW (#22C55E);
- в [`UtilitiesMenuCatalog`](Workspace1C/Services/UtilitiesMenuCatalog.cs) расширен список легаси-«звёзд» (StarOutline/StarBorder и варианты), которые могли попасть в layout пользователя, — меню больше не подменяет иконку на «звезду»;
- назначенный пользователем внешний файл иконки сохраняется.

Добавлены юнит-тесты [`Issue81NewBoxIconTests.cs`](Workspace1C.Tests/Issue81NewBoxIconTests.cs): распознавание легаси-«звёзд» и гарантия иконки NewBox.

Спасибо за сообщение! Проверьте, пожалуйста, в новом релизе.

Релиз **1.0.1.4** (Windows и Linux) будет опубликован в ближайшее время.