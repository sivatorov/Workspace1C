Исправлено в версии **1.0.1.4** (Windows и Linux):

Публикация на веб-сервере — Apache-службы с номерами (httpd27 и т. п.) теперь получают собственный конфиг:

- в [`ApacheInstanceDetector`](Workspace1C/Services/ApacheInstanceDetector.cs) из строки запуска каждой службы (`ImagePath`) извлекается свой `-d`/`-f` (например `conf\httpd27.conf`), включая случаи незакрытой кавычки;
- исходная строка службы сохраняется в [`ApacheInstance.RawCommandLine`](Workspace1C/Models/ApacheInstance.cs) и пишется в лог CM_PUBLISH;
- fallback-поиск файла алиасов находит `1c8Aliases27.conf` по суффиксу из имени конфига службы ([`WebPublishViewModel`](Workspace1C/ViewModels/WebPublishViewModel.cs)).

Покрыто тестами [`ApacheInstanceDetectorTests.cs`](Workspace1C.Tests/ApacheInstanceDetectorTests.cs) и [`Apache27ConfRegressionTests.cs`](Workspace1C.Tests/Apache27ConfRegressionTests.cs): службы с номерами, разбор `-d`/`-f` и поиск файла алиасов.

Спасибо за сообщение! Проверьте, пожалуйста, в новом релизе.

Релиз **1.0.1.4** (Windows и Linux) будет опубликован в ближайшее время.