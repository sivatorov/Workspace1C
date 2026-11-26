# Сводка сборки и проверок 1.0.1.125 (2026-11-26)

Версия: **1.0.1.125** (Windows/WPF и Linux/Avalonia из одной кодовой базы).

## Тесты

- Полный прогон `Workspace1C.Tests`: **8404 теста, 0 падений, 0 пропущено**
  (4 m 33 s). Лог: `docs/_t9_full_test_125.log`.
- Первый прогон выявил один устаревший source-тест
  `Issue71SettingsWindowLayoutTests.MainWindow_Avalonia_RestoreClampsToMinSizes`:
  после правки T3 (#171) сохранённые границы собираются в `WindowLayoutBounds`,
  и зажим к минимуму идёт по `saved.Width`/`saved.Height`. Проверка обновлена под
  фактический код (логика приложения не менялась), повторный прогон — 0 падений.

## Сборка Windows (WPF, win-x64)

```
powershell -ExecutionPolicy Bypass -File Workspace1C\Workspace1C\build-windows-single-file.ps1 -Configuration Release
```

- Результат: `Workspace1C/Workspace1C/dist/win-x64/Workspace1C.exe`
- Размер: **85 708 123 байт (81.7 MB)**
- `FileVersion` = `1.0.1.125`, `ProductVersion` = `1.0.1.125+603e1fa18fbf3a50817152f83624d345ce8f3f4d`.
- `proModules=true` (модули ПРО вкомпилированы, открываются лицензией).
- Один self-contained single-file исполняемый файл (без .dll/.pdb рядом).
- Лог: `docs/_t9_build_win_125.log`.

### Smoke Windows

Вывод сохранён в `docs/_t9_smoke_win_125.txt`:

- `Workspace1C.exe --list` — exit 0, список информационных баз выведен.
- `Workspace1C.exe --license-info --json` — exit 0, `"proModules": true`,
  `"edition": "pro"`, `"status": "Valid"`.
- `Workspace1C.exe --version` — exit 0 (отдельный вывод версии не предусмотрен;
  версия подтверждена свойствами файла и `--license-info --json`).

## Сборка Linux (Avalonia, linux-x64, кросс-компиляция из Windows)

```
powershell -ExecutionPolicy Bypass -File Workspace1C\Workspace1C\build-linux-single-file.ps1 -Configuration Release
```

- Результат: `Workspace1C/Workspace1C/dist/linux-x64/Workspace1C`
- Размер: **53 996 012 байт (51.5 MB)**
- Один self-contained single-file исполняемый файл (`-p:ForceLinux=true`, RID `linux-x64`,
  `HasProModules=true`).
- Проверка формата на Windows-хосте: сигнатура `7F 45 4C 46` (ELF), размер ненулевой.
- Функциональный smoke Linux на Windows невозможен — выполняется в CI на `ubuntu-latest`.
- Лог: `docs/_t9_build_linux_125.log`.

## Артефакты релиза

Разложены в те же пути, что и для 1.0.1.121/1.0.1.124:

- `bin/release-1.0.1.125/win-x64/Workspace1C.exe` — 85 708 123 байт (81.7 MB),
  `FileVersion` = `1.0.1.125`.
- `bin/release-1.0.1.125/linux-x64/Workspace1C` — 53 996 012 байт (51.5 MB), ELF.

> Примечание. В GitHub Release `v1.0.1.125` загружена пересборка из коммита релиза
> `009e77e1`: ассеты `Workspace1C.exe` — 85 599 766 байт и `Workspace1C-linux-x64` —
> 53 889 319 байт. `FileVersion` скачанного `Workspace1C.exe` = `1.0.1.125`,
> `ProductVersion` = `1.0.1.125+009e77e1…`. Побайтовое совпадение single-file сборок
> между запусками не гарантируется; версия в релизных ассетах корректна.

Абсолютные пути на машине сборки:

- `G:\h\Workspace1C\bin\release-1.0.1.125\win-x64\Workspace1C.exe`
- `G:\h\Workspace1C\bin\release-1.0.1.125\linux-x64\Workspace1C`

## Изменения версии

- `Workspace1C/Workspace1C/Workspace1C.csproj`: `Version`, `AssemblyVersion`,
  `FileVersion`, `InformationalVersion` = `1.0.1.125`.
- `CHANGELOG.md`: раздел `[1.0.1.125]`.
- `README.md`: бейдж, актуальная версия, таблица сборок (размеры и `FileVersion`),
  итог тестов и время, ссылка на эту сводку.
- `rel_notes_1.0.1.125.md`: заметки к релизу.

## Логи

- Полный прогон тестов: `docs/_t9_full_test_125.log`.
- Сборка Windows: `docs/_t9_build_win_125.log`.
- Smoke Windows: `docs/_t9_smoke_win_125.txt`.
- Сборка Linux и проверка ELF: `docs/_t9_build_linux_125.log`.
