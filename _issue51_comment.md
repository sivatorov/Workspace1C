Исправлено в версии 1.0.0.66 (Windows и Linux) — продолжение фикса 1.0.0.64: теперь подчёркивание текстовых полей ПРИ АКТИВНОСТИ (акцентная полоса снизу при фокусе) берёт цвет из темы.

На Windows кисть активной полосы MaterialDesign переопределена с жёсткого янтаря на AccentColor выбранной темы (обновляется при смене акцента в настройках): [`LightTheme.xaml`](Workspace1C/Themes/LightTheme.xaml), [`DarkTheme.xaml`](Workspace1C/Themes/DarkTheme.xaml).

В Linux/Avalonia поля редакторов (сервер 1С, учётные записи ITS, создание ИБ, репозиторий) получили стиль `ModernTextBox`/`ModernPasswordBox` с акцентным контуром: [`ServerEditWindow.Avalonia.cs`](Workspace1C/Views/ServerEditWindow.Avalonia.cs), [`ItsAccountEditWindow.Avalonia.cs`](Workspace1C/Views/ItsAccountEditWindow.Avalonia.cs), [`RacInfobaseCreateWindow.Avalonia.cs`](Workspace1C/Views/RacInfobaseCreateWindow.Avalonia.cs), [`RepositoryBrowserWindow.Avalonia.cs`](Workspace1C/Views/RepositoryBrowserWindow.Avalonia.cs).

Добавлены юнит-тесты `UnderlineThemeBindingTests` (+3).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.66
