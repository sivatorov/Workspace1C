# Workspace1C 1.0.1.14

Автоподстановка учётных данных СУБД из rac для массовой выгрузки ibcmd, фикс зависания закрытия окна «Настройки подключения» (#94), подробная диагностика лицензий ring (#83), тематизация остатков цветов и оптимизации производительности (Windows и Linux).

## Новые функции

- **Автоподстановка учётных данных СУБД для массовой выгрузки серверных баз ibcmd (Windows и Linux).** При массовой выгрузке DT/CF серверных баз учётные данные СУБД автоматически подставляются из данных кластера `rac` ([`ClusterInfobaseCredentialsResolver`](Workspace1C/Services/ClusterInfobaseCredentialsResolver.cs)) — больше не нужно вводить их вручную для каждой базы (снимает блокер issue #92).

## Исправления

- **NRE при закрытии окна «Настройки подключения» (Windows и Linux).** Добавлена null-защита в `FuncDataTemplate.BuildIdTab` ([`ConnectionSettingsWindow.Avalonia.cs`](Workspace1C/Views/ConnectionSettingsWindow.Avalonia.cs)) — устранено зависание кнопки «Отмена» на Linux (issue #94).
- **Диагностика вкладки лицензий ring (Windows и Linux).** Логируется результат каждого кандидата пути `ring`; при «пустом» выводе `ring` в журнал пишется то, что он вернул; исправлен квотинг `cmd.exe` для путей с пробелами (issue #83).
- **Строка DESIGNER для серверной базы при установке расширения (Windows и Linux).** Добавлен регрессионный тест на корректное формирование команды DESIGNER для серверной информационной базы (issue #87).
- **Массовая выгрузка: поля шаблона/каталога в тёмной теме (Windows и Linux).** Поля «Шаблон имени» и «Каталог выгрузки» корректно отображаются в тёмной теме; шаблон по умолчанию — с примером даты `{Имя}_{Дата:yyyy-MM-dd_HH-mm}`; подсказка по токенам выводится построчно (issue #92).
- **Формулировки мастера тестовой копии (Windows и Linux).** Уточнено, что именно найдено и что произойдёт на каждом шаге мастера «Развернуть тестовую копию» (issue #79).
- **Остатки хардкод-цветов заменены на ресурсы тем (Windows и Linux).** [`ServicesWindow`](Workspace1C/Views/ServicesWindow.xaml), дерево в [`MainWindow`](Workspace1C/MainWindow.xaml), профильные поля в [`SettingsWindow`](Workspace1C/Views/SettingsWindow.xaml) и утилиты [`AvaloniaUi`](Workspace1C/Views/AvaloniaUi.Avalonia.cs) переведены на тематизированные кисти.
- **Обработка ошибок асинхронных команд (Windows и Linux).** `async void`-методы [`MainViewModel`](Workspace1C/ViewModels/MainViewModel.cs) (резервное копирование, клонирование, скрипты) и fire-and-forget хоткеи обёрнуты в `try/catch` с записью исключений в журнал.
- **«Копировать строку подключения» (Windows и Linux).** Проверена работоспособность функции и покрыта юнит-тестами ([`CopyConnectionStringTests`](Workspace1C.Tests/CopyConnectionStringTests.cs)).

## Производительность

- **TTL-кэш пути ring в RingLicenseService (Windows и Linux).** Путь `ring` кэшируется на 5 минут — `where.exe` и реестр больше не опрашиваются при каждой проверке лицензий (прежде — каждые 5 секунд).
- **Устранён busy-wait в GlobalHotkeyService (Linux).** `Thread.Sleep(10)` в цикле обработки хоткеев заменён на блокирующее ожидание по сокету X-соединения — поток не крутится вхолостую.

---

- Тесты: полный набор пройден (5386 тестов зелёные).
- Полный список изменений — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).

## Ассеты

| Файл | Платформа |
|------|-----------|
| `Workspace1C.exe` | Windows (self-contained) |
| `Workspace1C-linux-x64` | Linux x64 (self-contained) |
