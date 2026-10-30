Исправлено в версии **1.0.1.1** (Windows и Linux):

При удалении базы из кластера после успешной блокировки регламентных заданий
выполняется до 5 попыток завершения сеансов/разрыва соединений с паузами (1–2 с)
и индикацией «Попытка N из M…» в статус-строке. Ошибка «освобождаемый контекст»
обрабатывается паузой и повторной попыткой; при исчерпании попыток — понятное
сообщение и диалог повтора (программа не зацикливается)
([`ServerMonitorViewModel.cs`](Workspace1C/ViewModels/ServerMonitorViewModel.cs)).

Покрыто тестами
[`ServerMonitorViewModelTests.cs`](Workspace1C.Tests/ServerMonitorViewModelTests.cs)
(ретраи завершения сеансов).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.1