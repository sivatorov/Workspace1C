# Сборка релиза Workspace 1C 1.0.1.123 (Задача 6)

Дата сборки: 2026-11-25/26. Локальная машина: Windows 11, .NET SDK 11.0.100-rc.2.26504.105
(предупреждение NETSDK1057 о предварительной версии SDK — не ошибка).
Модули ПРО: клон `g:/h/Workspace1C/Workspace1C.Pro/src/` найден, `HasProModules=true`
(подтверждено логами сборки и smoke-выводом `"proModules": true`).

## Полный прогон тестов

Команда (рабочий каталог `g:/h/Workspace1C/Workspace1C`):

```powershell
dotnet test Workspace1C.Tests/Workspace1C.Tests.csproj -c Release `
  -p:RuntimeIdentifier= -p:SelfContained=false -p:PublishSingleFile=false
```

Лог: `_t6_full_test_123.log` (корень workspace).

| Показатель | Значение |
|---|---|
| Всего тестов | 8322 |
| Пройдено | 8322 |
| Падений | 0 |
| Пропущено | 0 |
| Длительность | 3 m 25 s |

Exit code 0. Предупреждения — анализаторы xUnit (xUnit2013/2017/2031/1031) в существующих
тестах, на результат не влияют.

## Команды publish (эквивалент release.yml)

Windows (WPF, win-x64) — [`build-windows-single-file.ps1`](../Workspace1C/Workspace1C/build-windows-single-file.ps1):

```powershell
dotnet publish Workspace1C.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=false `
  -o dist/win-x64
```

Linux (Avalonia, linux-x64, кросс-сборка из Windows) — [`build-linux-single-file.ps1`](../Workspace1C/Workspace1C/build-linux-single-file.ps1):

```powershell
dotnet publish Workspace1C.csproj -c Release -r linux-x64 --self-contained true `
  -p:ForceLinux=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=false `
  -o dist/linux-x64
```

Логи сборки: `_t6_build_win_123.log`, `_t6_build_linux_123.log` (корень workspace).
Обе сборки завершились с exit code 0, скрипты удалили внешнюю папку `Localization`
(локализация встроена в single-file).

## Артефакты

| Платформа | Путь сборки | Размер (байт) | Размер | Staging |
|---|---|---|---|---|
| Windows win-x64 | `Workspace1C/Workspace1C/dist/win-x64/Workspace1C.exe` | 85 701 676 | 81.7 MB | `_rel_101123/Workspace1C.exe` |
| Linux linux-x64 | `Workspace1C/Workspace1C/dist/linux-x64/Workspace1C` | 53 987 757 | 51.5 MB | `_rel_101123/Workspace1C-linux-x64` |

В каждом каталоге `dist` ровно **один** файл (без DLL/PDB/`Localization` рядом) —
single-file подтверждён. Оба staging-файла существуют и ненулевые.

## Версии

- Windows: `FileVersion = 1.0.1.123`, `ProductVersion = 1.0.1.123+a5b12226a4e92706615c930343cc229ca333012e`,
  `FileDescription = Workspace 1C`, `ProductName = Workspace 1C`.
- Linux: в бинаре найдена строка `1.0.1.123` (поиск по ASCII-содержимому файла);
  строка `1.0.1.122` отсутствует. Формат ELF подтверждён магией `7F 45 4C 46`.

## Проверки

- Windows smoke (`docs/_smoke_win_101123.txt`, stderr — `docs/_smoke_win_101123.err.txt`, пусто):
  - `Workspace1C.exe --license-info --json` → exit 0, `"edition":"pro"`,
    `"proModules": true`, `"status":"Valid"`, `"programMajor":1`;
  - `Workspace1C.exe --list` → exit 0 (список баз выведен, `docs/_smoke_win_101123_list.txt`).
- Linux: локальный запуск на Windows невозможен (нет Linux-рантайма) — проверка ограничена
  версией в бинаре, ELF-форматом и single-file структурой; запуск и smoke выполняются в CI
  на `ubuntu-latest` (workflow [`release.yml`](../Workspace1C/.github/workflows/release.yml)),
  `chmod +x` выполняет CI.

## Предупреждения/ошибки

Ошибок нет (exit code 0 для тестов и обеих сборок). Предупреждения компилятора — существующие
nullable-предупреждения CS8601/CS8602/CS8603/CS8604/CS8625, CS8669, CS0618, CS0649 в исходном
коде; на релиз не влияют. `NETSDK1057` — используется preview-SDK .NET 11 RC; CI собирает на
`10.0.x`.

## Для публикации (Задача 7)

- Имена ассетов в GitHub Release (согласованы с автообновлением и workflow):
  - `Workspace1C.exe` (Windows);
  - `Workspace1C-linux-x64` (Linux).
- Готовые staging-файлы: `_rel_101123/Workspace1C.exe`, `_rel_101123/Workspace1C-linux-x64`.
- Логи: `_t6_full_test_123.log`, `_t6_build_win_123.log`, `_t6_build_linux_123.log`.
