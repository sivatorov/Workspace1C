Исправлено в версии **1.0.1.1** (Windows и Linux):

При подключении/переключении сервера в «Серверы 1С» таймер автообновления
останавливается ДО начала загрузки и возобновляется после успешного подключения,
если галка автообновления стоит. Раньше «микс» от автообновления и подключения
давал ошибки
([`ServerMonitorViewModel.cs`](Workspace1C/ViewModels/ServerMonitorViewModel.cs)).

Покрыто тестами
[`ServerMonitorViewModelTests.cs`](Workspace1C.Tests/ServerMonitorViewModelTests.cs)
(пауза таймера при подключении).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.1