Исправлено в версии **1.0.1.5** (Windows и Linux):

Мастер «Развернуть тестовую копию» — добавлена автоподстановка учётных данных для выгрузки через ibcmd и кнопки перехода к настройкам без закрытия мастера:

- в мастере появился блок «Данные для выгрузки через ibcmd» ([`TestDeployWindow.cs`](Workspace1C/Views/TestDeployWindow.cs), [`TestDeployWindow.Avalonia.cs`](Workspace1C/Views/TestDeployWindow.Avalonia.cs)): чекбокс «Использовать авторизацию информационной базы», поля логина/пароля и кнопка **«Подставить из базы…»** — логин/пароль подставляются из исходной базы через `AutofillCredentialFromSource` (учитывает общую авторизацию «Конфигуратора» и legacy-общую);
- добавлены кнопки **«Учётные данные для ibcmd…»** (открывает свойства исходной базы — вкладка авторизации) и **«Общая авторизация…»** (открывает настройки приложения — раздел «Общая авторизация») — мастер при этом не закрывается;
- сценарий хранит учётные данные в `TestDeployScenario.Credential` ([`TestDeployScenario.cs`](Workspace1C/Models/TestDeployScenario.cs)), выгрузка передаёт их в ibcmd/конфигуратор ([`TestDeployService`](Workspace1C/Services/TestDeployService.cs));
- при ошибке шага «Выгрузка» в сообщение добавляется подсказка, где задать учётные данные (в мастере, в свойствах исходной базы или в «Общей авторизации»).

Покрыто тестами [`Issue79TestDeployWindowTests.cs`](Workspace1C.Tests/Issue79TestDeployWindowTests.cs): панель ibcmd, `BuildScenario` с `Credential`, `AutofillCredentialFromSource` (в т.ч. не перезаписывает введённое при включённой авторизации ИБ).

Спасибо за сообщение! Проверьте, пожалуйста, в новом релизе.

Релиз **1.0.1.5** (Windows и Linux) будет опубликован в ближайшее время.