Исправлено в версии 1.0.0.98 (Windows и Linux):

По реальным конфигам из 27_conf.zip (`-d c:\Apache\Apache24 -f conf\httpd27.conf`,
`Include conf/httpd.conf`, `Define SRVROOT`/`Define OneCPort`,
`Include "${SRVROOT}/conf/1c8Aliases${OneCPort}.conf"`) и по последнему комментарию
от 27.10 («Файл алиасов в списке не появился, как последствие — остальное тоже без
изменений»):

1. **Файл алиасов снова в списке: точный путь Include-строки проверяется напрямую.**
   Если Include-граф по какой-то причине не прочитал файл алиасов, но строка
   `Include "${SRVROOT}/conf/1c8Aliases${OneCPort}.conf"` раскрыта в полный путь —
   `c:\Apache\Apache24\conf\1c8Aliases27.conf` — VM добавляет его в ConfTargets по
   ТОЧНОМУ пути, даже если файл лежит вне стандартных каталогов поиска (другой диск,
   `conf\vhosts\...`), а не только ищет имя файла рядом с конфигом
   ([`FindUsedAliasFileNear`](Workspace1C/ViewModels/WebPublishViewModel.cs)).

2. **Include-шаблоны алиасов раскрываются по финальному словарю `Define`.**
   Цели `1c8Aliases*.conf`, запомненные на проходе с неполным словарём, пересобираются
   после стабилизации Include-графа — в путях кандидатов не остаётся сырых `${...}`
   ([`PopulateIncludes`](Workspace1C/Services/ApacheInstanceDetector.cs)).

3. **Номер инстанса берётся и из `Define OneCPort 27`.** Суффикс «27» добавляется к
   кандидатам `1c8Aliases<n>.conf`, даже когда DocumentRoot/Listen/Include-граф
   суффикс не дали; финальный Define-граф сохраняется в инстансе
   ([`ApacheInstance.Defines`](Workspace1C/Models/ApacheInstance.cs)).

Итог на ваших конфигах: в списке файлов ровно один алиас — `1c8Aliases27.conf`,
каталог публикации — `c:\Apache\Apache24\htdocs\27\demo_27`, адрес —
`http://localhost:8027/demo_27/` (без `${...}` в интерфейсе).

Покрыто регрессионными тестами `Apache27ConfRegressionTests` (+3), включая
end-to-end тест с файлами на диске по точным путям:
`PopulateIncludes_RealConfigsOnDisk_ExactPaths_ReadsAliasFileAndConfTargets`,
`Fallback_ExpandedAliasIncludePathOutsideStandardDirs_AddsExactlyOneAliasFile`,
`Fallback_DefinePortValue_AddsAliasFileNearConf`. Всего тестов: **4884** (все
проходят на Windows; Linux — 0 ошибок).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.98
