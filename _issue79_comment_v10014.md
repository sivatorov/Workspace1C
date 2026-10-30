Исправлено в версии **1.0.1.4** (Windows и Linux):

Мастер «Развернуть тестовую копию» больше не падает при открытии:

- в [`TestDeployWindow.ApplyModernStyles`](Workspace1C/Views/TestDeployWindow.cs) полю `PasswordBox` больше не назначается стиль `ModernTextBox` с `TargetType="TextBox"` — именно это бросало `InvalidOperationException` при открытии окна;
- пароль теперь получает темированный implicit-стиль `TargetType="PasswordBox"`.

Добавлены юнит-тесты [`Issue79TestDeployWindowTests.cs`](Workspace1C.Tests/Issue79TestDeployWindowTests.cs): `PasswordBox` не получает стиль `TargetType="TextBox"`.

Спасибо за сообщение! Проверьте, пожалуйста, в новом релизе.

Релиз **1.0.1.4** (Windows и Linux) будет опубликован в ближайшее время.