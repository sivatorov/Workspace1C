# План релиза 1.0.1.107

Дата: 2026-11-23. Версия увеличивается только последним числом: 1.0.1.106 → 1.0.1.107.

## Открытые issues, требующие реакции

| Issue | Последний комментарий | Решение |
|-------|-----------------------|---------|
| #153 | нет комментариев | Процесс 1С сопоставлять со всеми записями списка с тем же подключением (`FindDirectOwners`, `MatchProcesses`). |
| #146 | 7OH, 2026-11-23 | Поле «Отображение» для групп верхнего уровня; вложенные — всегда с подписью; корректная иконка в правке структуры; высоты окон. |
| #143 | RafaIS, 2026-11-23 | Убрать блокировки UI: `SaveAsync` с таймаутом, таймауты проверок, фоновый резолвер кластера, скрытие прогресса в покое, ограниченное чтение вывода процесса. |
| #134 | 7OH, 2026-11-23 | Фокус в таблице инспектора: отложенная навигация после активации окна, повторные попытки фокуса, возврат фокуса после завершения процесса. |

Issues не закрываются — после релиза добавляются комментарии с версией и просьбой
подтвердить исправление.

## Изменения

1. `RunningInfobaseMatcher.FindDirectOwners` + `MatchProcesses` — все совпадения по подключению.
2. `UtilitiesMenuEditorViewModel` — `NewGroupWillBeNested`, `SelectedGroupIsTopLevel`, принудительная подпись вложенных групп, `UtilitiesGroupEditParams` по умолчанию `IconWithLabel`.
3. `UtilitiesMenuGroupPresentation.Build(..., isTopLevel)` — вложенные группы всегда с подписью.
4. `UtilitiesGroupEditWindow` (WPF/Avalonia) — выбор «Иконка» / «С надписью», скрытый для вложенных групп.
5. `DbmsBackupViewModel` — `SaveAsync`, `SaveTimeout`, `CheckTimeout`.
6. `ProcessRunner` — `OutputDrainTimeout` и ограниченное ожидание вывода после завершения процесса.
7. `DbmsBackupWindow` (WPF/Avalonia) — асинхронное сохранение, фоновый резолвер кластера, скрытие ряда прогресса в покое.
8. `PlatformUpdateViewModel` — защита от запоздавших `Progress.Report` (финальные 100%).
9. Локализация: `Utilities.Editor.DisplayHeader/DisplayIcon/DisplayIconWithLabel`, `Dbms.Saving`, `Dbms.SaveTimeout`.
10. Тесты: `Issue153SharedConnectionTests`, `Issue146GroupDisplayModeTests`, `Issue143ResponsivenessTests`; обновлены тесты #134/#146/#143.

## Сборка и публикация

1. Полный прогон тестов.
2. `build-windows-single-file.ps1` → `Workspace1C.exe`.
3. `build-linux-single-file.sh` → `Workspace1C-linux-x64` (CI на ubuntu).
4. Коммит, push, тег `v1.0.1.107`, GitHub Release.
5. Комментарии в #153, #146, #143, #134.
6. Новость в Telegram-канал `-1004425443528` (workflow + `docs/_telegram_msg_v1.0.1.107.txt`).
