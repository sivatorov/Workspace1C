# Сводка сборки и проверок 1.0.1.124 (2026-11-26)

Версия: **1.0.1.124** (Windows/WPF и Linux/Avalonia из одной кодовой базы).

## Тесты

- Полный прогон `Workspace1C.Tests`: **8326 тестов, 0 падений, 0 пропущено**
  (3 m 57 s). Лог: `_t4_full_test_124.log`.
- Целевые:
  - `Issue167PlatformDownloadSectionTests` — 30/30 (лог `_t1_test_167.log`).
  - `Issue159UtilitiesContextMenuTests` + `UiGlyphBanTests` — 49/49
    (лог `_t2_test_159.log`).

## Сборка Windows (WPF, win-x64)

```
powershell -ExecutionPolicy Bypass -File Workspace1C\Workspace1C\build-windows-single-file.ps1 -Configuration Release
```

- Результат: `Workspace1C/Workspace1C/dist/win-x64/Workspace1C.exe`
- Размер: **85 701 705 байт (81.7 MB)**
- `FileVersion` = `1.0.1.124`, `proModules=true`.
- Один self-contained single-file исполняемый файл (без .dll/.pdb рядом).
- Лог: `_t5_build_win_124.log`.

## Сборка Linux (Avalonia, linux-x64, кросс-компиляция из Windows)

```
powershell -ExecutionPolicy Bypass -File Workspace1C\Workspace1C\build-linux-single-file.ps1 -Configuration Release
```

- Результат: `Workspace1C/Workspace1C/dist/linux-x64/Workspace1C`
- Размер: **53 989 036 байт (51.5 MB)**
- Один self-contained single-file исполняемый файл.
- Лог: `_t5_build_linux_124.log`.

## Изменения версии

- `Workspace1C/Workspace1C/Workspace1C.csproj`: `Version`, `AssemblyVersion`,
  `FileVersion`, `InformationalVersion` = `1.0.1.124`.
- `CHANGELOG.md`: раздел `[1.0.1.124]`.
- `README.md`: бейдж, актуальная версия, таблица сборок, тесты, «Выпуск 1.0.1.124».
- `rel_notes_1.0.1.124.md`: заметки к релизу.
