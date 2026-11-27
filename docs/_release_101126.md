# Отчёт о релизе v1.0.1.126 (задача T5)

- **Релиз:** Workspace 1C 1.0.1.126
- **Тег:** `v1.0.1.126`
- **URL:** https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.126
- **Репозиторий:** `sivatorov/Workspace1C`
- **Целевой коммит (target_commitish):** `8a0b4a2a0cb4ebd73c62b5d47d59241364539055`
  - Сообщение: `1.0.1.126: #167, #173 — исправления, сборка, документация`
- **Статус latest:** ДА (`gh api repos/sivatorov/Workspace1C/releases/latest` → `tag_name = v1.0.1.126`)
- **isDraft:** false
- **isPrerelease:** false
- **Дата создания (UTC):** 2026-11-27

## Ассеты

| Имя | Размер (байт) | Состояние | Источник |
|-----|---------------|-----------|----------|
| `Workspace1C.exe` | 85 709 078 | uploaded | `_rel_101126/Workspace1C.exe` (FileVersion 1.0.1.126; дубль `bin/release-1.0.1.126/win-x64/Workspace1C.exe`) |
| `Workspace1C-linux-x64` | 53 996 716 | uploaded | `_rel_101126/Workspace1C-linux-x64` (ELF; дубль `bin/release-1.0.1.126/linux-x64/Workspace1C`) |

Размеры ассетов на GitHub совпадают с исходными артефактами:
- `Workspace1C.exe` — 85 709 078 байт;
- `Workspace1C-linux-x64` — 53 996 716 байт.

SHA-256 на GitHub:
- `Workspace1C.exe` — `a3206c4450a01c1c1a3c0372faeee7b4555e0dbe9186bd8a2b6c18753f2bc37c`;
- `Workspace1C-linux-x64` — `4a44f4814e76de2589ee97ae946a90729d929433dc1731522958cb5d40c3d8c4`.

## Заметки к релизу
- Источник: `Workspace1C/rel_notes_1.0.1.126.md` (в git-репозитории).

## Содержание версии
- **#167** — `PlatformDistributionPicker`, `OneCPlatformCatalogParser`, `PlatformDownloadViewModel`,
  `PlatformDownloadWindow.xaml(.cs)`, `PlatformDownloadWindow.Avalonia.cs`, тесты `Issue167PlatformDownloadSectionTests`.
- **#173** — `ViewModels/PinnedSectionSync.cs` (новый), `MainViewModel.Commands.cs`,
  `MainViewModel.Avalonia.Launch.cs`, `Workspace1C.csproj` (helper в Linux-сборке),
  `Workspace1C.Tests/Issue173PinFromRecentTests.cs` (новый).
- Версия `1.0.1.126` в `Workspace1C/Workspace1C/Workspace1C.csproj`, обновлены `CHANGELOG.md`, `README.md`.

## Примечания по выполнению
- В коммит `8a0b4a2a` включены только исходники, тесты, документация и `rel_notes_1.0.1.126.md`.
  Артефакты (`bin/`, `_rel_*`, `dist/`, `obj/`, логи сборок) в коммит не добавлялись.
- Релиз создан одной командой `gh release create` с полным SHA в `--target`; оба ассета загрузились успешно.
- Поле `isLatest` недоступно в текущей версии `gh release view`, статус latest подтверждён через `releases/latest`.
- Релиз не удалялся и не пересоздавался.

## Подготовленные локальные ассеты
- Каталог: `g:/h/Workspace1C/_rel_101126/`
  - `Workspace1C.exe` — 85 709 078 байт
  - `Workspace1C-linux-x64` — 53 996 716 байт (ELF)

## Для последующих задач
- Использовать URL релиза выше для анонса и ссылок в issues.
- Отчёт доступен в `docs/_release_101126.md`.
