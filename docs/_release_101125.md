# Отчёт о релизе v1.0.1.125 (задача T11)

- **Релиз:** Workspace 1C 1.0.1.125
- **Тег:** `v1.0.1.125`
- **URL:** https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.125
- **Репозиторий:** `sivatorov/Workspace1C`
- **Целевой коммит (target_commitish):** `009e77e13601e10351e50666a1614a7c82e42e65`
  - Сообщение: `1.0.1.125: #143, #159, #167, #170, #171, #172 — исправления, сборка, документация`
- **Статус latest:** ДА (`gh api repos/sivatorov/Workspace1C/releases/latest` → `tag_name = v1.0.1.125`)
- **isDraft:** false
- **isPrerelease:** false
- **Дата создания (UTC):** 2026-11-26

## Ассеты

| Имя | Размер (байт) | Состояние | Источник |
|-----|---------------|-----------|----------|
| `Workspace1C.exe` | 85 708 123 | uploaded | `bin/release-1.0.1.125/win-x64/Workspace1C.exe` (FileVersion 1.0.1.125) |
| `Workspace1C-linux-x64` | 53 996 012 | uploaded | `bin/release-1.0.1.125/linux-x64/Workspace1C` (ELF, magic `7F 45 4C 46`) |

Размеры ассетов на GitHub совпадают с исходными артефактами.

## Заметки к релизу
- Источник: `Workspace1C/rel_notes_1.0.1.125.md` (в git-репозитории).

## Примечания по выполнению
- Первичная попытка `gh release create --target 009e77e1` завершилась `HTTP 422: Release.target_commitish is invalid`.
  Причина: API GitHub не разрешает короткий SHA в `target_commitish`. Повтор с полным SHA
  `009e77e13601e10351e50666a1614a7c82e42e65` прошёл успешно, оба ассета загрузились в одной команде.
- Поле `isLatest` недоступно в текущей версии `gh release view`, статус latest подтверждён через `releases/latest`.
- Релиз не удалялся и не пересоздавался.

## Подготовленные локальные ассеты
- Каталог: `g:/h/Workspace1C/_rel_101125/`
  - `Workspace1C.exe` — 85 708 123 байт
  - `Workspace1C-linux-x64` — 53 996 012 байт (ELF)

## Для последующих задач
- T12 (Telegram): использовать URL релиза выше.
- T13 (issues): ссылаться на тег `v1.0.1.125` и ассеты.
- T14: отчёт доступен в `docs/_release_101125.md`.
