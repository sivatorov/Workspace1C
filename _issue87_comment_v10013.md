Исправлено в версии **1.0.1.3** (Windows и Linux):

Установка расширения конфигурации (.cfe) в клиент-серверную базу через окно «Расширения» больше не падает с ошибкой «Конфигуратор завершился с кодом 1: Error in command-line options»:

- убран повторный ключ `-Extension` у операции `/UpdateDBCfg` в [`ConfigExtensionService.BuildInstallArguments`](Workspace1C/Services/ConfigExtensionService.cs);
- добавлен детектор ошибки «command-line options» и диагностическое логирование в [`DesignerCommandRunner.RunAsync`](Workspace1C/Services/DesignerCommandRunner.cs);
- при отказе конфигуратора автоматически выполняется fallback на ibcmd-ветку (`config load --extension` + `config apply --force`) в [`ExtensionsViewModel.InstallToCheckedAsync`](Workspace1C/ViewModels/ExtensionsViewModel.cs).

Благодарю за сообщение! Если в вашей конфигурации что-то пойдёт не так, пожалуйста, напишите — добавим в следующий релиз.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.3
