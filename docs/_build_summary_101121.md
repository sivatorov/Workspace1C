# Сборка релиза Workspace 1C 1.0.1.121 (T11)

Дата сборки: 2026-11-25. Локальная машина: Windows 11, .NET SDK 11.0.100-rc.2.26504.105
(предупреждение NETSDK1057 о предварительной версии SDK — не ошибка).
Модули ПРО: клон `g:/h/Workspace1C/Workspace1C.Pro/src/` найден, `HasProModules=true`.

## Команды publish (эквивалент release.yml)

Windows (WPF, win-x64) — [`build-windows-single-file.ps1`](../Workspace1C/Workspace1C/build-windows-single-file.ps1:93):

```powershell
dotnet publish Workspace1C.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=false `
  -o dist/win-x64
```

Linux (Avalonia, linux-x64, кросс-сборка из Windows) — [`build-linux-single-file.ps1`](../Workspace1C/Workspace1C/build-linux-single-file.ps1:63):

```powershell
dotnet publish Workspace1C.csproj -c Release -r linux-x64 --self-contained true `
  -p:ForceLinux=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=false `
  -o dist/linux-x64
```

В CI Linux-джоб использует `./build-linux-single-file.sh Release` на реальном Linux
(`net10.0` выбирается по ОС); локально использован `-p:ForceLinux=true` — тот же набор
свойств публикации.

## Артефакты

| Платформа | Путь | Размер (байт) | Размер |
|---|---|---|---|
| Windows win-x64 | `bin/release-1.0.1.121/win-x64/Workspace1C.exe` | 85 696 069 | 81.7 MB |
| Linux linux-x64 | `bin/release-1.0.1.121/linux-x64/Workspace1C` | 53 983 660 | 51.5 MB |

В каждом каталоге ровно **один** файл (всего 2 файла в `bin/release-1.0.1.121`),
никаких DLL/PDB/`Localization` рядом — single-file подтверждён.

## Версии

- Windows: `FileVersion = 1.0.1.121`, `ProductVersion = 1.0.1.121+aab9d4418a59f749e8ff72b493a22bc9b3b45861`,
  `FileDescription = Workspace 1C`, `ProductName = Workspace 1C`.
- Linux: `dotnet msbuild -p:ForceLinux=true -getProperty:FileVersion` → `1.0.1.121`
  (ELF-бинарник, магия `7F 45 4C 46`; на Windows `VersionInfo` для ELF не читается —
  проверка версии как в workflow Linux-джоба, через свойство проекта).

## Проверки

- `HasProModules=true` для обеих сборок (Windows — до publish и по бинарю; Linux — до publish).
- Windows smoke: `Workspace1C.exe --license-info --json` → exit 0,
  `"edition":"pro"`, `"proModules":true`, `"programMajor":1`, лицензия `Valid`.
- Linux: запуск на Windows невозможен (нет Linux-рантайма) — проверка ограничена
  версией, ELF-форматом и single-file структурой; `chmod +x` выполнит CI.
- Локализация `ru.json`/`en.json` встроена в сборку (внешняя папка
  `Localization/Languages` удалена скриптом как лишняя).

## Предупреждения/ошибки

Ошибок нет (exit code 0). Предупреждения компилятора — существующие nullable-предупреждения
CS8601/CS8602/CS8603/CS8604/CS8625, CS8669, CS0618, CS0649 в исходном коде; на релиз не влияют.
`NETSDK1057` — используется preview-SDK .NET 11 RC; CI собирает на `10.0.x`.

## Для T12 (публикация)

- Имена ассетов в GitHub Release (согласованы с автообновлением и workflow):
  - `Workspace1C.exe` (Windows);
  - `Workspace1C-linux-x64` (Linux; переименовать из локального `Workspace1C` при загрузке).
- Логи: `docs/_build_win_101121.log`, `docs/_build_linux_101121.log`.
