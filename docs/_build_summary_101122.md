# Сборка релиза Workspace 1C 1.0.1.122 (T4)

Дата сборки: 2026-11-25. Локальная машина: Windows 11, .NET SDK 10.0.403
(предупреждение NETSDK1057 о preview-SDK в этом прогоне не появлялось — используется стабильный SDK 10).
Модули ПРО: клон `g:/h/Workspace1C/Workspace1C.Pro/src/` найден, `HasProModules=true` для обеих сборок.

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

Скрипты собирают в `Workspace1C/Workspace1C/dist/<rid>/`; после сборки оба артефакта
скопированы в каталог релиза `bin/release-1.0.1.122/`.

## Артефакты

| Платформа | Путь | Размер (байт) | Размер |
|---|---|---|---|
| Windows win-x64 | `bin/release-1.0.1.122/win-x64/Workspace1C.exe` | 85 697 016 | 81.7 MB |
| Linux linux-x64 | `bin/release-1.0.1.122/linux-x64/Workspace1C` | 53 983 085 | 51.5 MB |

В каталоге `bin/release-1.0.1.122` ровно **два** файла (по одному на платформу),
никаких DLL/PDB/`Localization` рядом — single-file подтверждён. Внешняя папка
`Localization/Languages` (`ru.json`, `en.json`) удалена скриптами как лишняя: языки
встроены в ресурсы сборки.

## Версии

- Windows: `FileVersion = 1.0.1.122`,
  `ProductVersion = 1.0.1.122+a3bc8053a158b5f65801f4dda36d25f7544a0ba5`,
  `FileDescription = Workspace 1C`, `ProductName = Workspace 1C`.
  (Хэш в `ProductVersion` — базовый коммит `a3bc8053`, коммитов для 1.0.1.122 ещё нет — это Задача 5.)
- Linux: `dotnet msbuild -p:ForceLinux=true -getProperty:FileVersion` → `1.0.1.122`
  (ELF-бинарник, магия `7F 45 4C 46`; на Windows `VersionInfo` для ELF не читается —
  проверка версии как в workflow Linux-джоба, через свойство проекта).

## Проверки

- `HasProModules=true` для обеих сборок (Windows — до publish и по бинарю; Linux — до publish).
- Windows smoke (`docs/_smoke_win_101122.txt`):
  - `Workspace1C.exe --license-info --json` → exit 0, `"ok":true`, `"edition":"pro"`,
    `"status":"Valid"`, `"programMajor":1`, `"proModules":true`, все 13 features = true;
  - `Workspace1C.exe --list` → exit 0, выведен список информационных баз профиля.
- Linux: запуск на Windows невозможен (нет Linux-рантайма) — проверка ограничена
  версией (`1.0.1.122`), ELF-форматом (`7F 45 4C 46`) и single-file структурой; `chmod +x` выполнит CI.
- Полный прогон тестов: `dotnet test Workspace1C.slnx -c Release` →
  **8277 пройдено, 0 не пройдено, 0 пропущено** (длительность 5 m 8 s), лог `docs/_full_test_101122.log`.

## Предупреждения/ошибки

Ошибок нет (exit code 0 у обеих сборок и у тестов). Предупреждения компилятора —
существующие nullable/анализаторные предупреждения CS8601/CS8602/CS8603/CS8604/CS8625,
CS8669, CS0618, CS0649 и xUnit1031/xUnit2017/xUnit2031 в исходном коде и тестах; на релиз не влияют.

## Для следующей задачи (публикация)

- Имена ассетов в GitHub Release (согласованы с автообновлением и workflow):
  - `Workspace1C.exe` (Windows, из `bin/release-1.0.1.122/win-x64/Workspace1C.exe`);
  - `Workspace1C-linux-x64` (Linux; переименовать из локального
    `bin/release-1.0.1.122/linux-x64/Workspace1C` при загрузке).
- Текст релиза: `rel_notes_1.0.1.122.md`; анонс: `docs/_telegram_msg_v1.0.1.122.txt`
  (канал `-1004425443528`).
- Логи:
  - Windows: `docs/_build_win_101122.log`;
  - Linux: `docs/_build_linux_101122.log`;
  - тесты: `docs/_full_test_101122.log`;
  - smoke: `docs/_smoke_win_101122.txt`.
- Коммит/пуш/тег/релиз в этой задаче НЕ выполнялись (Задача 5).
