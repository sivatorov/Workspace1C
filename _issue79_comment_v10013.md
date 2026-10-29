Исправлено в версии **1.0.1.3** (Windows и Linux):

Мастер «Развернуть тестовую копию» больше не падает при открытии с некорректными данными базы:

- `PreviewSteps`/`PreviewSummary` в [`TestDeployViewModel`](Workspace1C/ViewModels/TestDeployViewModel.cs) переведены на безопасный режим: при ошибке показывается нейтральный текст `TestDeploy.PreviewUnavailable` (добавлена локализация ru/en), а не исключение;
- окно открывается корректно для любых типов баз.

Спасибо за сообщение! Проверьте, пожалуйста, в новом релизе.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.3