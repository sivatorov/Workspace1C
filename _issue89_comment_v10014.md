Исправлено в версии **1.0.1.4** (Windows и Linux):

Автообновление на Linux снова работает — программа скачивает Linux-сборку, а не Windows-EXE:

- в [`GitHubReleaseService`](Workspace1C/Services/GitHubReleaseService.cs) имя Linux-ассета приведено к контракту публикации — `Workspace1C-linux-x64` (см. `release.yml`);
- любые `.exe` на Linux отбрасываются как неплатформенные, поэтому загрузчик больше не скачивает `Workspace1C.exe` и не «зависает» на обновлении.

Добавлены юнит-тесты [`GitHubReleaseServiceTests.cs`](Workspace1C.Tests/GitHubReleaseServiceTests.cs): выбор точного Linux-ассета и отбрасывание `.exe`.

Спасибо за сообщение! Проверьте, пожалуйста, в новом релизе.

Релиз **1.0.1.4** (Windows и Linux) будет опубликован в ближайшее время.