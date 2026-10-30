Исправлено в версии **1.0.1.6** (Windows) по подсказке из обсуждения — спасибо!

Параметры Apache-служб теперь читаются из реестра: у служб, созданных обёрткой 1С/Apache monitor, ключи `-d`/`-f` лежат не в ImagePath, а в реестровом параметре `HKLM\SYSTEM\CurrentControlSet\Services\<служба>\Parameters\ConfigArgs` (REG_MULTI_SZ) — из-за этого все службы сводились к дефолтному `httpd.conf`.
[`ApacheInstanceDetector.DetectWindows`](Workspace1C/Services/ApacheInstanceDetector.Windows.cs) теперь читает `ConfigArgs` и передаёт их в новую 5-аргументную перегрузку
[`ParseWindowsServiceImagePath`](Workspace1C/Services/ApacheInstanceDetector.cs); при конфликте `-d`/`-f` приоритет у `ConfigArgs` (фактические аргументы запуска). `RawCommandLine` в логе CM_PUBLISH показывает оба источника параметров службы (`ImagePath="…"; ConfigArgs=…`) — источник `-d`/`-f` сразу виден.

Конфиги Apache-служб должны определиться корректно.

Спасибо за сообщение! Проверьте, пожалуйста, в новом релизе.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.6
