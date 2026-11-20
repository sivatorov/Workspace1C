## Workspace 1C 1.0.1.98

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia). Главное изменение — полностью реализованные и подключённые модули редакции **ПРО 2.x**.

### Модули ПРО

- **Автотесты в тестовом контуре (`AutoTests`)** — сценарии Vanessa Automation, YAXUnit и произвольной команды, разбор JUnit обоих форматов, таймаут, первые N упавших тестов, аудит `autotest.run`, Telegram зелёный/красный, планировщик `AutoTests`, шаг конвейера `RunAutoTests`, MCP `run_autotests`, окна WPF/Avalonia.
- **Тренды производительности (`PerformanceTrends`)** — дневные снимки p50/p95/max и APDEX, сравнение периодов, топ ухудшившихся контекстов, планировщик `PerformanceSnapshot`, сохранение по хуку анализа техжурнала, аудит `perf.snapshot`, оповещение о росте p95, окна WPF/Avalonia.
- **Копии в S3 и шифрование (`CloudBackup`)** — собственная подпись AWS Signature V4 (проверена на эталонном векторе AWS), S3-клиент PUT/multipart по 64 МБ с повторами, list/delete, path-style и virtual-hosted-style, потоковое AES-256-GCM (формат `W1CENC1`, PBKDF2-SHA256 600 000 итераций, блоки 1 МБ), загрузчик для `BackupUploadHooks`, ротация облачных копий, аудит `cloud.upload`, CLI `--decrypt`, расшифровка в «Списке выгрузок», аудит `cloud.decrypt`, окна WPF/Avalonia.
- Существующие модули (`AuditLog`, `Pipelines`, `MultiBaseQuery`, `EnvironmentDiff`) приведены в согласованное состояние: меню, планировщик, CLI, MCP, окна обеих платформ и тесты.

### Публичное ядро

- Точки расширения `SchedulerExecutors`, `BackupUploadHooks`, `CloudDecryptHooks`, `PerfSnapshotHooks`, `McpToolHooks`, `ProCliCommands`, `FeatureDataStore` и `ProFeatures.MarkImplemented`.
- Новые виды заданий планировщика `AutoTests` и `PerformanceSnapshot` с локализацией ru/en.
- «Список выгрузок»: кнопка «Расшифровать» для копий `.w1cenc` при подключённом модуле ПРО.
- Анализ техжурнала передаёт результат в `PerfSnapshotHooks` для сохранения дневного снимка.
- Внешние пункты меню «Утилиты» корректно получают парный ключ геометрии иконки.

### Проверка

- Публичные тесты: **7545/7545**.
- Тесты ПРО: **114/114**.
- Сборка Windows и кросс-сборка Avalonia для Linux: 0 ошибок.

Полный список изменений — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).
