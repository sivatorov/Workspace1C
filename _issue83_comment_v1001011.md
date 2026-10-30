Исправлено в версии **1.0.1.1** (Windows и Linux):

Вкладка «Лицензии» в «Серверы 1С»:

- если утилита ring не найдена, в журнал пишется INFO (а не WARN-спам при
  автообновлении каждые ~5 с), а во вкладке показывается понятное сообщение с
  указанием, где искалась утилита (PATH и каталог bin платформы рядом с rac)
  ([`RingLicenseService.cs`](Workspace1C/Services/RingLicenseService.cs));
- вкладка «Лицензии» перенесена ПЕРЕД «Информация о кластере» (WPF
  [`ServerMonitorWindow.xaml`](Workspace1C/Views/ServerMonitorWindow.xaml) и
  Avalonia).

Покрыто тестами
[`Issue83LicenseTabTests.cs`](Workspace1C.Tests/Issue83LicenseTabTests.cs)
(новый: INFO вместо WARN при отсутствии ring, понятное сообщение вкладки,
порядок вкладок).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.1