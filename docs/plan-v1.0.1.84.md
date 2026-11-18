# План исправлений Workspace 1C 1.0.1.84

Репозиторий: `g:/h/Workspace1C` (https://github.com/sivatorov/Workspace1C)
Текущая версия: **1.0.1.83** (`Workspace1C/Workspace1C.csproj:62-65`).
Итоговая версия: **1.0.1.84** — увеличивается **только последнее число и только один раз**, после выполнения всех правок.

В работе только issues: **#140, #139, #138, #134**. Остальные открытые issues не трогаем.
**Issues не закрывать.** После релиза в каждый из четырёх issues добавляется комментарий о том, что исправлено и в какой версии, со ссылкой на релиз.

> Примечание: исходно план требовалось положить в `plans/plan-v1.0.1.84.md`, но каталог `plans/` исключён в `.codeassistantignore` и запись туда заблокирована. По согласованию план сохранён в `docs/plan-v1.0.1.84.md`.

---

## 0. Общие правила для всех задач

### 0.1. Две оболочки

Проект собирается в двух конфигурациях:

| Оболочка | TFM | Символ | UI-файлы |
|---|---|---|---|
| Windows/WPF | `net10.0-windows` | `WINDOWS` | `*.xaml`, `*.xaml.cs` |
| Linux/Avalonia | `net10.0` | `LINUX` | `*.Avalonia.cs`, `*.axaml` |

Общая (платформенно-нейтральная) логика живёт в `Workspace1C/ViewModels`, `Workspace1C/Models`, `Workspace1C/Services` без `#if` и покрывается юнит-тестами. Правило: **сначала чистая логика + тесты, затем тонкая обвязка в каждой оболочке**.

### 0.2. Команды сборки и тестов

Запускать из корня `g:/h/Workspace1C`:

```powershell
# Windows/WPF: сборка
dotnet build Workspace1C/Workspace1C.csproj -c Debug

# Windows/WPF: тесты
dotnet test Workspace1C.Tests/Workspace1C.Tests.csproj -c Debug

# Linux/Avalonia: проверка компиляции (кросс-сборка из Windows)
dotnet build Workspace1C/Workspace1C.csproj -c Debug -p:ForceLinux=true

# Linux/Avalonia: тесты (в тестовом csproj флаг называется BuildLinux)
dotnet test Workspace1C.Tests/Workspace1C.Tests.csproj -c Debug -p:BuildLinux=true
```

Публикация single-file:

```powershell
Workspace1C/build-windows-single-file.ps1 -Configuration Release   # Workspace1C/dist/win-x64/Workspace1C.exe
Workspace1C/build-linux-single-file.ps1   -Configuration Release   # Workspace1C/dist/linux-x64/Workspace1C
Workspace1C/build.ps1                                              # обычная сборка; -Publish — self-contained
```

### 0.3. Локализация

Ключи добавляются одновременно в:
- `Workspace1C/Localization/Languages/ru.json`
- `Workspace1C/Localization/Languages/en.json`

Существующие ключи не переименовывать (на них завязаны тесты и сохранённые настройки).

### 0.4. Стиль кода

- Комментарии — по-русски, со ссылкой на issue и версию (`issue #140 (1.0.1.84)`), как уже принято в проекте.
- Не менять поведение, не относящееся к issue.
- Не удалять исторические записи `CHANGELOG.md` и старые версии в `README.md`.

### 0.5. Общая схема выполнения

```mermaid
flowchart LR
  A[140 Подсветка и сопоставление] --> B[139 Размер окна обновления]
  B --> C[138 Информация о подключении]
  C --> D[134 Панель, Утилиты, чекбоксы]
  D --> E[Версия 1.0.1.84 + CHANGELOG + README]
  E --> F[Сборка WPF и Linux]
  F --> G[Тег и GitHub Release]
  G --> H[Комментарии в issues]
  G --> I[Telegram]
```

---

## 1. Задача #140 — «Инспектор процессов»: подсветка строк и «Неизвестная база»

### 1.1. Подтверждённые причины

#### Причина A (WPF, «не выделяется каждая вторая строка») — подтверждена

- В разметке [`ProcessInspectorWindow.xaml`](Workspace1C/Views/ProcessInspectorWindow.xaml:52) задан локальный `AlternatingRowBackground="{DynamicResource CardBackgroundBrush}"`.
- Глобальный стиль `DataGrid` тоже задаёт зебру: [`LightTheme.xaml:1260`](Workspace1C/Themes/LightTheme.xaml:1260), [`DarkTheme.xaml:1114`](Workspace1C/Themes/DarkTheme.xaml:1114) (`ZebraRowBrush`).
- Строка `DataGridRow` получает фон чередования как **локальное значение** `Background` от `DataGrid`; локальное значение имеет приоритет над триггером стиля. Триггер `IsSelected` в стиле строки ([`LightTheme.xaml:1340`](Workspace1C/Themes/LightTheme.xaml:1340), [`DarkTheme.xaml:1195`](Workspace1C/Themes/DarkTheme.xaml:1195)) перекрывается локальным фоном чётной строки → выделение не видно.
- Ячейки прозрачные ([`LightTheme.xaml:1347-1358`](Workspace1C/Themes/LightTheme.xaml:1347)), поэтому фон строки — единственный индикатор.

**Вывод:** нужно убрать `AlternatingRowBackground` с конкретного `DataGrid` и задавать зебру триггером в локальном `DataGrid.RowStyle`, где триггер `IsSelected` стоит последним.

#### Причина B (Avalonia) — требует явной фиксации

- Строки инспектора — `ListBox` + `ModernListBoxItem` ([`ProcessInspectorWindow.Avalonia.cs:98-115`](Workspace1C/Views/ProcessInspectorWindow.Avalonia.cs:98)).
- Тема `ModernListBoxItem` ([`Controls.axaml:396-416`](Workspace1C/Themes/Controls.axaml:396)) задаёт `:selected` только через `ContentPresenter#PART_ContentPresenter`, а фон самого `ListBoxItem` — `Transparent`.
- В сочетании с `SelectionMode.Multiple` и пересозданием контейнеров при `RebuildTable()`/автообновлении это даёт нестабильную видимость выделения. Нужно продублировать фон на самом `ListBoxItem` в состоянии `:selected` и явно назначить `ItemContainerTheme`.

#### Причина C («Неизвестная база» для всех баз) — подтверждена

1. **Инспектор не использует полный матчер.** [`ProcessInspectorViewModel.BuildRows`](Workspace1C/ViewModels/ProcessInspectorViewModel.cs:430-452) перебирает базы и вызывает только `RunningInfobaseMatcher.MatchesCommandLine` (прямые `/F`, `/S`, `/IBConnectionString`). Второй проход по `/IBName` и уникальности имён из [`RunningInfobaseMatcher.MatchProcesses`](Workspace1C/Services/RunningInfobaseMatcher.cs:302-393) **не применяется**. Поэтому при запуске через `/IBName` (отладка из Конфигуратора, часть сценариев `1cestart`) индикатор в главном окне базу находит, а инспектор показывает «Неизвестная база» — ровно та несогласованность, о которой сообщает автор.
2. **Нормализация путей не покрывает типичные различия между машинами.** [`NormalizePath`](Workspace1C/Services/RunningInfobaseMatcher.cs:655-666) делает только `Path.GetFullPath` + обрезку хвостовых разделителей:
   - не раскрывает переменные окружения (`%USERPROFILE%`, `%APPDATA%`) — карточка базы может хранить их, а командная строка — абсолютный путь;
   - не приводит короткие имена 8.3 (`C:\BASES~1\IB`) к длинным;
   - не сопоставляет UNC-путь (`\\server\share\base`) и путь через смонтированный диск (`Z:\base`) — на другой машине (Windows 10) буква диска/способ доступа может отличаться.
3. **Веб-базы не сопоставляются вообще.** В [`MatchesCommandLine`](Workspace1C/Services/RunningInfobaseMatcher.cs:32-50) ветка `ConnectionType.WebServer` попадает в `_ => false`. Если у коллеги базы — веб-публикации, все процессы будут «неизвестными».
4. **Нет диагностики в инспекторе.** `RunningMatchDebug` логирует только `MatchProcesses` ([`RunningInfobaseMatcher.cs:317-326`](Workspace1C/Services/RunningInfobaseMatcher.cs:317)), а `BuildRows` — нет. Без лога причину на чужой машине не подтвердить.
5. **Windows-сервис может вернуть пустой `CommandLine`.** [`RunningInfobasesService.Windows.cs:115`](Workspace1C/Services/RunningInfobasesService.Windows.cs:115) при отказе WMI отдаёт `""`, и процесс гарантированно «неизвестен». Нужна диагностическая запись (сколько процессов без командной строки).

#### Проверка `Infobase.Id` и `NormalizeInfobases` — причина НЕ в Id

- Начиная с 1.0.1.80 пустые `Id` восстанавливаются при загрузке списка: [`InfobaseRepository.Load`](Workspace1C/Services/InfobaseRepository.cs:97-114) вызывает [`NormalizeInfobases`](Workspace1C/Services/InfobaseRepository.cs:183-209), которая генерирует GUID и устраняет дубликаты.
- Матчер процессов уже не требует непустого `Id`: ключ базы строится в [`RunningInfobaseMatcher.GetBaseKey`](Workspace1C/Services/RunningInfobaseMatcher.cs:61-83) (Id → подпись подключения).
- Инспектор (`ProcessRowViewModel`/`BuildRows`) **вообще не использует `Id`** — он сопоставляет `Infobase` по командной строке. Поэтому «Неизвестная база» не может быть следствием пустого `Id`; искать нужно в матчинге путей/ключей (причины C1-C5).
- Проверить стоит только одно: что в инспектор передаётся **полный** список баз (`Infobases.ToList()` в [`MainViewModel.Tools.cs:3299-3301`](Workspace1C/ViewModels/MainViewModel.Tools.cs:3299)), а не отфильтрованный по профилю/приватности. Если приватные базы скрыты — это ожидаемое поведение, а не баг #140.

### 1.2. Изменяемые файлы

- [`Workspace1C/Views/ProcessInspectorWindow.xaml`](Workspace1C/Views/ProcessInspectorWindow.xaml)
- [`Workspace1C/Views/ProcessInspectorWindow.Avalonia.cs`](Workspace1C/Views/ProcessInspectorWindow.Avalonia.cs)
- [`Workspace1C/ViewModels/ProcessInspectorViewModel.cs`](Workspace1C/ViewModels/ProcessInspectorViewModel.cs)
- [`Workspace1C/Services/RunningInfobaseMatcher.cs`](Workspace1C/Services/RunningInfobaseMatcher.cs)
- [`Workspace1C/Services/RunningInfobasesService.Windows.cs`](Workspace1C/Services/RunningInfobasesService.Windows.cs) — только диагностика
- [`Workspace1C/Themes/Controls.axaml`](Workspace1C/Themes/Controls.axaml) — при необходимости усилить `:selected`
- Новый тест: `Workspace1C.Tests/Issue140ProcessInspectorTests.cs`

### 1.3. Пошаговый подход

#### Шаг 1. WPF: видимое выделение всех строк

В [`ProcessInspectorWindow.xaml`](Workspace1C/Views/ProcessInspectorWindow.xaml:44):

1. Удалить `AlternatingRowBackground="{DynamicResource CardBackgroundBrush}"` (строка 52).
2. Добавить `AlternationCount="2"` и локальный `RowStyle`:

```xml
<DataGrid x:Name="ProcessesGrid" ...
          AlternationCount="2"
          SelectionMode="Extended" SelectionUnit="FullRow" ...>
    <DataGrid.RowStyle>
        <Style TargetType="DataGridRow" BasedOn="{StaticResource {x:Type DataGridRow}}">
            <Style.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                    <Setter Property="Background" Value="{DynamicResource ItemHoverBrush}"/>
                </Trigger>
                <Trigger Property="ItemsControl.AlternationIndex" Value="1">
                    <Setter Property="Background" Value="{DynamicResource ZebraRowBrush}"/>
                </Trigger>
                <!-- Последним: выделение всегда перекрывает зебру и hover. -->
                <Trigger Property="IsSelected" Value="True">
                    <Setter Property="Background" Value="{DynamicResource ItemSelectedBrush}"/>
                    <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}"/>
                </Trigger>
            </Style.Triggers>
        </Style>
    </DataGrid.RowStyle>
```

3. Оставить `SelectionMode="Extended"`, `SelectionUnit="FullRow"`, `SelectedItem` и `SelectionChanged`.
4. Проверить, что `DataGridCell` не перекрывает фон (в теме уже `Transparent`, менять не нужно).

#### Шаг 2. Avalonia: видимое выделение всех строк

1. В [`Controls.axaml`](Workspace1C/Themes/Controls.axaml:396) для `ModernListBoxItem` добавить фон на сам элемент и состояние фокуса:

```xml
<Style Selector="^:selected">
    <Setter Property="Background" Value="{DynamicResource ItemSelectedBrush}"/>
</Style>
<Style Selector="^:selected:pointerover">
    <Setter Property="Background" Value="{DynamicResource ItemSelectedBrush}"/>
</Style>
```

2. В [`ProcessInspectorWindow.Avalonia.cs`](Workspace1C/Views/ProcessInspectorWindow.Avalonia.cs:98) явно назначить тему контейнера, чтобы окно не зависело от неявного стиля:

```csharp
_grid.ItemContainerTheme = (ControlTheme?)Application.Current?.FindResource("ModernListBoxItem");
```

3. Убедиться, что `RebuildTable()` и `RestoreSelectedRows()` не сбрасывают выделение (логика уже есть, строки 225-260, 413-430).

#### Шаг 3. Инспектор использует полный матчер

В [`RunningInfobaseMatcher`](Workspace1C/Services/RunningInfobaseMatcher.cs) добавить публичный метод поиска владельца для одной командной строки, который включает все проходы:

```csharp
/// <summary>
/// Владелец командной строки: прямые ключи /F, /S, /IBConnectionString, затем /IBName
/// (только если имя уникально в списке), затем файловый fallback по уникальному
/// имени каталога. null — база не определена.
/// </summary>
public static Infobase? FindOwner(IEnumerable<Infobase> infobases, string? commandLine)
```

- `MatchProcesses` переписать через `FindOwner` (поведение сохранить, тесты `RunningInfobaseMatcherTests` должны остаться зелёными).
- В [`ProcessInspectorViewModel.BuildRows`](Workspace1C/ViewModels/ProcessInspectorViewModel.cs:430) заменить внутренний цикл на `RunningInfobaseMatcher.FindOwner(_infobases, p.CommandLine)`.
- Добавить диагностику в `BuildRows`:

```csharp
if (RunningMatchDebug.IsEnabled)
{
    RunningMatchDebug.Log($"inspector: pid={p.Pid} exe=\"{p.ProcessName}\" cmdline=\"{p.CommandLine}\"");
    RunningMatchDebug.Log($"inspector: matched={(matched is null ? "none" : matched.Name)}");
}
```

#### Шаг 4. Устойчивая нормализация путей

В [`RunningInfobaseMatcher`](Workspace1C/Services/RunningInfobaseMatcher.cs:644-666):

1. `NormalizePath`:
   - `Trim().Trim('"').Trim()`;
   - `Environment.ExpandEnvironmentVariables(...)`;
   - убрать префикс `\\?\` (и `\\?\UNC\` → `\\`);
   - заменить `/` на `Path.DirectorySeparatorChar` (на Windows — `\`);
   - `Path.GetFullPath` в `try/catch`;
   - обрезать хвостовые разделители.
2. `PathsEqual` оставить регистронезависимым (`OrdinalIgnoreCase`).
3. Добавить `PathLeafEquals(a, b)` — сравнение последнего сегмента пути без регистра.
4. Добавить fallback в `FindOwner` для **файловых** баз:
   - если прямой матч не дал результата, собрать файловые базы, у которых `Path.GetFileName(NormalizePath(FilePath))` совпадает с листом пути из `/F`;
   - применять fallback **только если такой лист уникален среди файловых баз** (защита от ложных совпадений, как в проходе `/IBName`);
   - это покрывает UNC ↔ смонтированный диск и короткие имена промежуточных каталогов.

#### Шаг 5. Веб-базы

Добавить сопоставление `ConnectionType.WebServer`:

- извлечь `/WS` или `WS=` из `/IBConnectionString`;
- нормализовать URL: нижний регистр схемы и хоста, убрать хвостовой `/`;
- сравнивать с `Connection.WebUrl`; если `WebUrl` пуст — с `Connection.DatabaseName` (как в [`ServerDatabaseDisplay`](Workspace1C/Models/Infobase.cs:1053));
- пустое ожидаемое значение не сопоставлять никогда.

#### Шаг 6. Диагностика Windows-сервиса

В [`RunningInfobasesService.Windows.cs`](Workspace1C/Services/RunningInfobasesService.Windows.cs:95) при пустом `CommandLine` писать в `RunningMatchDebug`:

```csharp
if (string.IsNullOrWhiteSpace(commandLine) && RunningMatchDebug.IsEnabled)
    RunningMatchDebug.Log($"wmi: empty CommandLine pid={pid} name=\"{name}\" (нет прав/WMI)");
```

Это позволит на машине коллеги с `CM_DEBUG_RUNNING_MATCH=1` увидеть, пустая ли командная строка или не совпадают пути.

### 1.4. Тесты

Новый файл `Workspace1C.Tests/Issue140ProcessInspectorTests.cs` (xUnit, стиль как в `Issue134ProcessInspectorTests`):

| Тест | Что проверяет |
|---|---|
| `Wpf_InspectorGrid_HasNoLocalAlternatingRowBackground` | в `ProcessInspectorWindow.xaml` нет `AlternatingRowBackground=`, есть `DataGrid.RowStyle` |
| `Wpf_RowStyle_SelectedTrigger_ComesAfterAlternation` | индекс `IsSelected` больше индекса `AlternationIndex` внутри `RowStyle` |
| `Avalonia_ModernListBoxItem_SelectedHasItemBackground` | в `Controls.axaml` есть `Selector="^:selected"` с `Background` |
| `Avalonia_Inspector_SetsItemContainerTheme` | в `ProcessInspectorWindow.Avalonia.cs` есть `ItemContainerTheme` |
| `Inspector_BuildRows_UsesFullMatcher_ForIbName` | VM с фейковым сервисом и базой; процесс с `/IBName` → `IsKnown == true` |
| `Matcher_FileBase_ExpandsEnvironmentVariables` | `%TEMP%\ib` в карточке ↔ абсолютный путь в cmdline |
| `Matcher_FileBase_MatchesUniqueLeafName_WhenPathsDiffer` | `\\srv\share\base` ↔ `Z:\base` при уникальном листе |
| `Matcher_FileBase_DoesNotMatchAmbiguousLeafName` | две базы с одинаковым листом → не сопоставляется |
| `Matcher_WebBase_MatchesWsUrl` | `/WS https://srv/base/` ↔ `WebUrl=https://srv/base` |
| `Matcher_FileBase_WholeArgumentQuoted` | `"ENTERPRISE /F""C:\base"""` |
| `Matcher_ServerBase_WithPortAndSlash` | `/S"srv/base"` ↔ `Server=srv:1541`, `DatabaseName=base` |

Существующие тесты, которые должны остаться зелёными:
`RunningInfobaseMatcherTests`, `ProcessInspectorSelectionTests`, `Issue134ProcessInspectorTests`, `ProcessInspectorParsingTests`.

### 1.5. Риски и проверка

- **Риск ложных совпадений** из-за fallback по листу каталога → строгое условие уникальности и только файловые базы.
- **Риск изменения поведения `MatchProcesses`** → переписать через `FindOwner`, но сохранить двухпроходность и защиту от неоднозначных имён; прогнать `RunningInfobaseMatcherTests`.
- **Проверка вручную:** запустить инспектор, включить `CM_DEBUG_RUNNING_MATCH=1`, убедиться, что в `running-match-debug.log` есть строки `inspector:`; проверить Ctrl/Shift-выделение и сохранение выделения после 5-секундного автообновления.

### 1.6. Definition of Done

- [ ] В WPF выделяются все выбранные строки (одиночное и мультивыделение), зебра сохранена.
- [ ] В Avalonia выделение видно для всех выбранных строк.
- [ ] Инспектор использует полный матчер (`/IBName`, `/IBConnectionString`, `/F`, `/S`, веб).
- [ ] Пути нормализуются с переменными окружения и fallback по уникальному листу.
- [ ] Добавлены тесты из 1.4; `dotnet test` (WPF) и `-p:BuildLinux=true` — зелёные.
- [ ] `dotnet build -p:ForceLinux=true` без ошибок.

---

## 2. Задача #139 — обрезание окна «Доступна новая версия приложения для загрузки»

### 2.1. Подтверждённая причина

- Окно: [`UpdateAvailableWindow.xaml:8-13`](Workspace1C/Services/UpdateAvailableWindow.xaml:8) — `SizeToContent="WidthAndHeight"`, `MaxWidth="760"`, `MaxHeight="640"`, `MinWidth="440"`, `ResizeMode="NoResize"`.
- Внутри [`OfferPanel`](Workspace1C/Services/UpdateAvailableWindow.xaml:71) кнопки лежат в горизонтальном `StackPanel` с фиксированными ширинами: `InstallButton 150` + `DownloadButton 150` + `SkipVersionButton MinWidth 170` + `CancelButton 100` + отступы ≈ 600 DIP, плюс иконка 44+16 и поля 20+20 → минимальная ширина контента ≈ 700 DIP.
- `MinWidth="440"` разрешает окну стать уже контента. При `SizeToContent` + `ResizeMode="NoResize"` WPF на части тем/DPI всё равно отдаёт `HTLEFT/HTRIGHT` при попадании на границу; перетаскивание уменьшает ширину, `SizeToContent` во время drag не пересчитывается → контент обрезается справа (видна только «Перезапустить»).
- `MaxWidth="760"`/`MaxHeight="640"` не учитывают рабочую область и масштаб (Windows 10 + нестандартный DPI).
- Avalonia-аналог: [`MaterialMessageWindow.Avalonia.cs:62-81`](Workspace1C/Services/MaterialMessageWindow.Avalonia.cs:62) — та же схема `MinWidth=440`, `MaxWidth=760`, `SizeToContent.WidthAndHeight`, `CanResize=false`; оконный менеджер Linux тоже может изменить ширину, контент обрежется.

### 2.2. Изменяемые файлы

- [`Workspace1C/Services/UpdateAvailableWindow.xaml`](Workspace1C/Services/UpdateAvailableWindow.xaml)
- [`Workspace1C/Services/UpdateAvailableWindow.xaml.cs`](Workspace1C/Services/UpdateAvailableWindow.xaml.cs)
- [`Workspace1C/Services/MaterialMessageWindow.Avalonia.cs`](Workspace1C/Services/MaterialMessageWindow.Avalonia.cs)
- [`Workspace1C/Services/UpdateService.Avalonia.cs`](Workspace1C/Services/UpdateService.Avalonia.cs) — проверить вызов диалога обновления (`fitContent: true`) и передачу размеров/текста
- [`Workspace1C/Services/UpdateProgressWindow.Avalonia.cs`](Workspace1C/Services/UpdateProgressWindow.Avalonia.cs) — проверить на ту же схему и при необходимости применить те же правила
- Новый чистый helper: `Workspace1C/Services/UpdateDialogSizePolicy.cs`
- Новый тест: `Workspace1C.Tests/Issue139UpdateDialogSizeTests.cs`

### 2.3. Пошаговый подход

#### Шаг 1. Чистая политика размеров

Создать `UpdateDialogSizePolicy` (без WPF/Avalonia):

```csharp
public readonly record struct UpdateDialogSize(double Width, double Height);

public static class UpdateDialogSizePolicy
{
    public const double MinWidth = 440;
    public const double MaxWidth = 760;
    public const double MaxHeight = 640;
    public const double WorkAreaMargin = 24;

    /// <summary>Начальный размер: по содержимому, но не больше рабочей области и MaxWidth/MaxHeight.</summary>
    public static UpdateDialogSize Clamp(double desiredWidth, double desiredHeight,
        double workAreaWidth, double workAreaHeight);

    /// <summary>True — точку попадания на границу нужно трактовать как клиентскую (запрет resize).</summary>
    public static bool ShouldBlockResize(int hitTestCode, bool sizeToContent, bool noResize);

    /// <summary>Минимальная ширина контента по ширинам кнопок и отступам.</summary>
    public static double ContentMinWidth(IEnumerable<double> buttonWidths, double spacing, double chrome);
}
```

Константы `WM_NCHITTEST` можно держать в WPF-файле, а в политику передавать числовой код (`HTLEFT=10`, `HTRIGHT=11`, `HTTOP=12`, `HTTOPLEFT=13`, `HTTOPRIGHT=14`, `HTBOTTOM=15`, `HTBOTTOMLEFT=16`, `HTBOTTOMRIGHT=17`).

#### Шаг 2. WPF: запрет изменения ширины и перенос кнопок

1. В XAML кнопочные `StackPanel` заменить на `WrapPanel`:
   - блок кнопок `OfferPanel` (строки 71-110);
   - блок `RestartPanel` (строки 128-141).
   Это гарантирует, что при узкой ширине кнопки переносятся, а не обрезаются.
2. Сохранить `SizeToContent="WidthAndHeight"`, `ResizeMode="NoResize"`, `MinWidth="440"`, `MaxWidth="760"` (на них завязаны существующие тесты `Issue116*`).
3. В [`UpdateAvailableWindow.xaml.cs`](Workspace1C/Services/UpdateAvailableWindow.xaml.cs:111) добавить:
   - в конструкторе `SourceInitialized += OnSourceInitialized;`
   - `OnSourceInitialized`: получить `HwndSource` через `WindowInteropHelper`, добавить `AddHook(OnWindowMessage)`, и выставить `MaxWidth`/`MaxHeight` = `Math.Min(760/640, WorkArea - 2*WorkAreaMargin)`.
   - `OnWindowMessage`: для `WM_NCHITTEST` вызвать `UpdateDialogSizePolicy.ShouldBlockResize(...)`; если true — вернуть `HTCLIENT` и `handled = true`.
   - **Не использовать** идентификаторы `WndProc`, `WmGetMinMaxInfo`, `SizeToContent = SizeToContent.Manual`, `CaptureFixedSize` — тест [`Issue116ManualUpdateDialogTests`](Workspace1C.Tests/Issue116ManualUpdateDialogTests.cs:89-108) прямо их запрещает.
4. `RefreshWindowSize()` оставить как есть (`UpdateLayout`).

#### Шаг 3. Avalonia: те же правила

В [`MaterialMessageWindow.Avalonia.cs`](Workspace1C/Services/MaterialMessageWindow.Avalonia.cs:62):

1. Сохранить литералы `MinWidth = 440;`, `MaxWidth = 760;`, `SizeToContent = SizeToContent.WidthAndHeight;`, `CanResize = false;` (тесты `Issue116ManualUpdateNoNotificationTests`/`Issue116UpdateIntervalTests` их проверяют).
2. После установки размеров добавить `Opened += (_, _) => ClampToScreen();`.
3. `ClampToScreen()`: взять `Screens.ScreenFromWindow(this)?.WorkingArea`, привести к DIP через `RenderScaling`, вызвать `UpdateDialogSizePolicy.Clamp(...)` и присвоить `MaxWidth`/`MaxHeight` (и при необходимости `Width`/`Height`).
4. Кнопки диалога собрать в `WrapPanel`, а не в горизонтальный `StackPanel`.

#### Шаг 4. Проверка `UpdateService.Avalonia.cs` и `UpdateProgressWindow.Avalonia.cs`

- В [`UpdateService.Avalonia.cs`](Workspace1C/Services/UpdateService.Avalonia.cs) проверить, что диалог предложения обновления создаётся с `fitContent: true` (как в [`AvaloniaDialogService.ConfirmUpdateWithSkip`](Workspace1C/Services/AvaloniaDialogService.cs)) и что в него передаётся полный текст без предварительной обрезки.
- В [`UpdateProgressWindow.Avalonia.cs`](Workspace1C/Services/UpdateProgressWindow.Avalonia.cs) сейчас `Width = 440`, `SizeToContent = SizeToContent.Height`, `CanResize = false` — горизонтального обрезания нет; менять не нужно, если не появится `SizeToContent.WidthAndHeight`.
- Если в любом из окон используется схема `SizeToContent` + `CanResize=false` + фиксированные ширины кнопок — применить `WrapPanel` и `ClampToScreen`.

### 2.4. Тесты

Новый `Workspace1C.Tests/Issue139UpdateDialogSizeTests.cs`:

| Тест | Что проверяет |
|---|---|
| `Clamp_RespectsMaxWidthAndMaxHeight` | 1000×900 при рабочей области 1920×1080 → 760×640 |
| `Clamp_RespectsSmallWorkArea` | рабочая область 700×500 → ширина/высота меньше рабочей области на 2×margin |
| `Clamp_NeverBelowMinWidth` | желаемая ширина 300 → 440 |
| `ShouldBlockResize_BlocksAllBorderCodes_WhenNoResizeAndSizeToContent` | коды 10-17 → true |
| `ShouldBlockResize_AllowsClientAndCaption` | `HTCLIENT=1`, `HTCAPTION=2` → false |
| `ContentMinWidth_ButtonRow` | 150+150+170+100+3×10 + chrome |
| `Wpf_Xaml_ButtonRowsUseWrapPanel` | в `UpdateAvailableWindow.xaml` два `WrapPanel` для кнопок |
| `Wpf_Code_BlocksBorderResizeWithoutForbiddenIdentifiers` | есть `AddHook`, `HTCLIENT`; нет `WndProc`/`WmGetMinMaxInfo` |
| `Avalonia_FitContent_ClampsToScreen` | в `MaterialMessageWindow.Avalonia.cs` есть `WorkingArea`/`ClampToScreen` |

Существующие тесты обновления:
- `Issue116ManualUpdateDialogTests` — `MinWidth="440"`, `SizeToContent`, отсутствие `WndProc`/`WmGetMinMaxInfo` сохраняются.
- `Issue116UpdateIntervalTests` — `ResizeMode="NoResize"` сохраняется.
- `Issue116ManualUpdateNoNotificationTests` — `MaxWidth = 760;`, `CanResize = false;`, `SizeToContent.WidthAndHeight` сохраняются.
Если какой-то литерал всё же меняется — обновить соответствующий тест осознанно, с комментарием про issue #139.

### 2.5. Риски и проверка

- **Риск:** хук `WM_NCHITTEST` может заблокировать системное меню/перетаскивание за заголовок. Решение: блокировать только коды границ (10-17), `HTCAPTION` пропускать.
- **Риск:** `SizeToContent` + `MaxWidth` на многомониторных конфигурациях. Решение: считать по `WorkArea` монитора владельца.
- **Проверка вручную:** открыть диалог, потянуть за правую границу (окно не должно менять ширину), уменьшить рабочую область/масштаб 150%, проверить, что кнопки переносятся и текст не обрезается.

### 2.6. Definition of Done

- [ ] Клик/перетаскивание правой границы не уменьшает ширину окна и не обрезает контент.
- [ ] Кнопки переносятся (`WrapPanel`), длинные строки переносятся (`TextWrapping` уже есть).
- [ ] Размер ограничен рабочей областью и `MaxWidth/MaxHeight`.
- [ ] Тесты 2.4 зелёные; существующие тесты обновления не сломаны.
- [ ] WPF и Avalonia проверены.

---

## 3. Задача #138 — «Информация о подключении»: заголовки по типу базы, копирование, открытие каталога

### 3.1. Подтверждённая причина / текущее состояние

- WPF: блок «Информация о подключении» — фиксированная сетка из 12 строк [`MainWindow.xaml:2945-2992`](Workspace1C/Views/MainWindow.xaml:2945). Для файловой базы одновременно показываются:
  - `Main.ServerPath` → [`ConnectionPathDisplay`](Workspace1C/Models/Infobase.cs:866) = `"C:\base"`;
  - `Column.ServerBase` → [`ServerDatabaseDisplay`](Workspace1C/Models/Infobase.cs:1048) = `C:\base`;
  - `Main.ConnectionString` → `File="C:\base"`.
  Три представления одного пути → «громоздко из-за дублирования».
- Заголовки универсальные (`Сервер / путь`, `Сервер/База`), не учитывают тип базы.
- Нет кликабельных значений; открытие каталога — только через контекстное меню (`ContextMenuCommandKind.OpenInfobaseFolder` → [`OpenInfobaseFolderCommand`](Workspace1C/ViewModels/MainViewModel.Display.cs:990)).
- Avalonia: та же карточка [`MainWindow.Avalonia.Tree.cs:1499-1511`](Workspace1C/Views/MainWindow.Avalonia.Tree.cs:1499), `DetailRow` — [`2520-2554`](Workspace1C/Views/MainWindow.Avalonia.Tree.cs:2520).

### 3.2. Изменяемые файлы

- Новый `Workspace1C/ViewModels/ConnectionInfoPresenter.cs` — чистая логика состава строк.
- [`Workspace1C/ViewModels/MainViewModel.cs`](Workspace1C/ViewModels/MainViewModel.cs:1085) — WPF-сеттер `SelectedInfobase`, свойства `ConnectionInfoFields`, `ShowOpenFolderLink`.
- [`Workspace1C/ViewModels/MainViewModel.Avalonia.cs`](Workspace1C/ViewModels/MainViewModel.Avalonia.cs:681) — Avalonia-сеттер, те же свойства.
- [`Workspace1C/Views/MainWindow.xaml`](Workspace1C/Views/MainWindow.xaml:2940) — замена фиксированной сетки на `ItemsControl`.
- [`Workspace1C/Views/MainWindow.xaml.cs`](Workspace1C/Views/MainWindow.xaml.cs) — обработчик копирования.
- [`Workspace1C/Views/MainWindow.Avalonia.Tree.cs`](Workspace1C/Views/MainWindow.Avalonia.Tree.cs:1495) — построение строк из `ConnectionInfoFields`.
- `Workspace1C/Localization/Languages/ru.json`, `en.json` — новые ключи.
- Новый тест: `Workspace1C.Tests/Issue138ConnectionInfoPanelTests.cs`.

### 3.3. Правила состава строк

Новый helper:

```csharp
public enum ConnectionInfoField
{
    Type, Directory, Address, Server, DatabaseName, ConnectionString,
    Platform, LaunchMode, Client, Bitness, Parameters, LastLaunch, CacheSize, Configuration
}

public sealed record ConnectionInfoFieldInfo(
    ConnectionInfoField Field,
    string LabelKey,
    string Label,      // LocalizationManager.T(LabelKey)
    string Value,
    bool Copyable,
    bool IsFileDirectory);

public static class ConnectionInfoPresenter
{
    public static IReadOnlyList<ConnectionInfoFieldInfo> Build(Infobase? infobase);
    public static bool CanOpenFolder(Infobase? infobase);
}
```

Состав по типу:

| Тип базы | Строки (в порядке отображения) |
|---|---|
| **Файловая** (`ConnectionType.File`) | `Тип`; **`Каталог`** (`FilePath`, copyable, `IsFileDirectory=true`); `Платформа`; `Режим запуска`; `Клиент`; `Разрядность`; `Параметры`; `Последний запуск`; `Размер кэша`; `Конфигурация` |
| **Веб-сервер** (`WebServer`) | `Тип`; **`Адрес`** (`WebUrl`, fallback `DatabaseName`, copyable); `Платформа`; `Режим запуска`; `Клиент`; `Разрядность`; `Параметры`; `Последний запуск`; `Размер кэша`; `Конфигурация` |
| **Серверная** (`ClientServer`) | `Тип`; **`Сервер`** (`Server`, copyable); **`Имя базы`** (`DatabaseName`, copyable); **`Строка`** (`ConnectionStringDisplay`, copyable); `Платформа`; `Режим запуска`; `Клиент`; `Разрядность`; `Параметры`; `Последний запуск`; `Размер кэша`; `Конфигурация` |

Важно: для файловых и веб-баз **не выводить** `Main.ServerPath`, `Column.ServerBase` и `Main.ConnectionString` — именно они дают дублирование. Пустые значения: строка не добавляется (кроме `Тип`), чтобы блок не разрастался.

Новые ключи локализации:

```json
"Main.ConnectionInfo.Directory": "Каталог",
"Main.ConnectionInfo.Address": "Адрес",
"Main.ConnectionInfo.Server": "Сервер",
"Main.ConnectionInfo.DatabaseName": "Имя базы",
"Main.ConnectionInfo.CopyTooltip": "Копировать значение",
"Main.ConnectionInfo.OpenFolder": "Открыть каталог",
"Main.ConnectionInfo.Copied": "Скопировано"
```

`en.json`: `Directory`, `Address`, `Server`, `Database name`, `Copy value`, `Open folder`, `Copied`.

### 3.4. Пошаговый подход

#### Шаг 1. Helper + VM

1. Создать `ConnectionInfoPresenter` (см. 3.3).
2. В `MainViewModel` добавить:

```csharp
public ObservableCollection<ConnectionInfoFieldInfo> ConnectionInfoFields { get; } = new();

public bool ShowOpenFolderLink =>
    SelectedInfobase is { } ib && ConnectionInfoPresenter.CanOpenFolder(ib);

private void RebuildConnectionInfoFields()
{
    ConnectionInfoFields.Clear();
    foreach (var field in ConnectionInfoPresenter.Build(SelectedInfobase))
        ConnectionInfoFields.Add(field);
    OnPropertyChanged(nameof(ShowOpenFolderLink));
}
```

3. Вызвать `RebuildConnectionInfoFields()`:
   - в WPF-сеттере [`SelectedInfobase`](Workspace1C/ViewModels/MainViewModel.cs:1085) после `SetProperty`;
   - в Avalonia-сеттере [`SelectedInfobase`](Workspace1C/ViewModels/MainViewModel.Avalonia.cs:681) после `SetPropertyWithRelated`;
   - после редактирования базы (там, где вызывается `NotifyAllPropertiesChanged`/`Save`), если такой путь есть в VM.
4. `ShowOpenFolderLink` добавить в набор related-свойств при смене `SelectedInfobase` (в Avalonia — в `SetPropertyWithRelated`).

#### Шаг 2. WPF: динамические строки + копирование + открытие каталога

1. Заменить `Grid` из строк 2948-2991 на:

```xml
<ItemsControl ItemsSource="{Binding ConnectionInfoFields}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <Grid Margin="0,0,0,8">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="110"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                <TextBlock Text="{Binding Label}" FontSize="12"
                           Foreground="{DynamicResource TextSecondaryBrush}"/>
                <TextBlock Grid.Column="1" Text="{Binding Value}" Tag="{Binding Value}"
                           FontSize="12" FontWeight="SemiBold" TextWrapping="Wrap"
                           MouseLeftButtonUp="OnConnectionInfoValueClick">
                    <TextBlock.Style>
                        <Style TargetType="TextBlock">
                            <Setter Property="Cursor" Value="Arrow"/>
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding Copyable}" Value="True">
                                    <Setter Property="Cursor" Value="Hand"/>
                                    <Setter Property="ToolTip"
                                            Value="{loc:Loc Main.ConnectionInfo.CopyTooltip}"/>
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </TextBlock.Style>
                </TextBlock>
            </Grid>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

2. Рядом с заголовком `Main.SectionConnInfo` (строка 2941) добавить кнопку/ссылку «Открыть каталог»:

```xml
<Button Content="{loc:Loc Main.ConnectionInfo.OpenFolder}"
        Command="{Binding OpenInfobaseFolderCommand}"
        Visibility="{Binding ShowOpenFolderLink, Converter={StaticResource BoolToVis}}"
        Style="{DynamicResource LinkButton}" ... />
```

Если стиля `LinkButton` нет — использовать `SecondaryButton`/`ModernButton` с компактными полями.

3. В `MainWindow.xaml.cs`:

```csharp
private void OnConnectionInfoValueClick(object sender, MouseButtonEventArgs e)
{
    if (sender is FrameworkElement { Tag: string value } && !string.IsNullOrWhiteSpace(value))
    {
        try { Clipboard.SetText(value); }
        catch { /* буфер обмена недоступен — не роняем окно */ }
    }
}
```

Копировать только `Copyable`-строки: проверку можно сделать через `DataContext is ConnectionInfoFieldInfo { Copyable: true }`.

#### Шаг 3. Avalonia: те же строки

1. В [`MainWindow.Avalonia.Tree.cs`](Workspace1C/Views/MainWindow.Avalonia.Tree.cs:1499) заменить `PlainCard(DetailRow(...) × 12)` на `ItemsControl`, привязанный к `ConnectionInfoFields`:

```csharp
var connectionCard = PlainCard(new ItemsControl
{
    ItemsSource = _vm?.ConnectionInfoFields,
    ItemTemplate = new FuncDataTemplate<ConnectionInfoFieldInfo>((row, _) => BuildConnectionInfoRow(row))
});
```

2. `BuildConnectionInfoRow(ConnectionInfoFieldInfo row)` — Grid 110/*, label + value; для `Copyable` выставить `Cursor = new Cursor(StandardCursorType.Hand)`, `ToolTip.SetTip(valueBlock, T("Main.ConnectionInfo.CopyTooltip"))`, `valueBlock.PointerPressed += async (_, _) => await CopyConnectionInfoValue(row.Value);`.
3. `CopyConnectionInfoValue`: `if (this.Clipboard is { } cb) await cb.SetTextAsync(value);`.
4. Рядом с `connectionLabel` добавить кнопку «Открыть каталог», `IsVisible` привязать к `ShowOpenFolderLink`, `Command` — к `OpenInfobaseFolderCommand`.
5. Старый `DetailRow(string, Binding)` можно оставить для других мест (MetadataExplorer), но из правой панели убрать.

#### Шаг 4. Локализация и тесты

- Добавить ключи из 3.3 в `ru.json`/`en.json`.
- Убедиться, что `LocalizationManager.T` возвращает ключ при отсутствии перевода (для тестов), либо тесты используют только `LabelKey`.

### 3.5. Тесты

Новый `Workspace1C.Tests/Issue138ConnectionInfoPanelTests.cs`:

| Тест | Что проверяет |
|---|---|
| `FileBase_HasDirectoryRow_AndNoDuplicatePathRows` | ровно одна строка с путём; нет `Main.ServerPath`/`Column.ServerBase`/`Main.ConnectionString` |
| `FileBase_DirectoryRow_IsCopyableAndOpenFolder` | `Copyable=true`, `CanOpenFolder=true` |
| `WebBase_HasAddressRow` | label `Main.ConnectionInfo.Address`, значение `WebUrl` |
| `WebBase_AddressFallsBackToDatabaseName` | пустой `WebUrl` → `DatabaseName` |
| `ServerBase_HasServerAndDatabaseRows` | labels `Server`, `DatabaseName`, значения из `Connection` |
| `ServerBase_ConnectionStringRow_IsCopyable` | `ConnectionString` copyable |
| `EmptyValues_AreOmitted` | пустые `Server`/`DatabaseName` не создают строк |
| `Wpf_Panel_UsesItemsControlAndCopyHandler` | XAML: `ItemsSource="{Binding ConnectionInfoFields}"`, `OnConnectionInfoValueClick`, `OpenInfobaseFolderCommand` |
| `Avalonia_Panel_UsesConnectionInfoFieldsAndClipboard` | Tree.cs: `ConnectionInfoFields`, `Clipboard`, `OpenInfobaseFolderCommand` |
| `Localization_HasConnectionInfoKeys` | ключи в ru/en |

Существующие тесты `CopyConnectionStringTests`, `InfobaseDisplayTests` не зависят от разметки панели и должны остаться зелёными.

### 3.6. Риски и проверка

- **Риск:** `ItemsControl` в WPF внутри `ScrollViewer` правой панели — проверить, что нет двойной прокрутки; `ItemsControl` не скроллит сам.
- **Риск:** копирование по клику может мешать выделению текста. Решение: клик по значению — копирование (как просил автор), `TextBlock` не редактируемый.
- **Проверка вручную:** файловая, серверная и веб-база; клик по каждой строке → значение в буфере; кнопка «Открыть каталог» для файловой базы.

### 3.7. Definition of Done

- [ ] Для файловой базы — «Каталог», для веб — «Адрес», для серверной — «Сервер» и «Имя базы».
- [ ] Дублирования пути/строки для файловых баз нет.
- [ ] Клик по значению копирует его в буфер в WPF и Avalonia.
- [ ] Кнопка «Открыть каталог» работает для файловых баз из панели.
- [ ] Тесты 3.5 зелёные, локализация ru/en добавлена.

---

## 4. Задача #134 — «Завершение процессов 1С» (последние комментарии 7OH/ArtyoZ)

### 4.1. Перенос иконки «Инспектор процессов» на панель

Требование: иконка должна стоять **после кнопки «Проверить доступность всех баз», перед разделителем** (набор инструментов).

- WPF: сейчас кнопка [`ProcessInspectorButton`](Workspace1C/Views/MainWindow.xaml:694) стоит после «Утилит» (692-701). Перенести блок в [`MainWindow.xaml`](Workspace1C/Views/MainWindow.xaml:658) сразу после кнопки `CheckAvailabilityCommand` (658-663) и **до** разделителя (664-666).
- Avalonia: сейчас [`processInspectorBtn`](Workspace1C/Views/MainWindow.Avalonia.cs:1096) добавляется после `utilitiesBtn` (1092). Перенести создание/добавление на позицию после `checkAvailBtn` (1046-1056) и **до** `panel.Children.Add(CommandPanelSeparator())` (1058).

### 4.2. Убрать пункт «Инспектор процессов 1С» из меню «Утилиты»

- [`UtilitiesMenuCatalog`](Workspace1C/Services/UtilitiesMenuCatalog.cs:673) — удалить элемент каталога `new("ProcessInspector", ...)`.
- [`UtilitiesMenuLayouts.Default()`](Workspace1C/Services/UtilitiesMenuCatalog.cs:780) — удалить `Item("ProcessInspector")` из группы «Операции».
- [`UtilitiesMenuCommandKind`](Workspace1C/Services/UtilitiesMenuCatalog.cs:120) — удалить значение `ProcessInspector` (или оставить, если решим не ломать старые сборки; рекомендуется удалить и почистить switch).
- [`MainWindow.UtilitiesMenu.cs:321-322`](Workspace1C/Views/MainWindow.UtilitiesMenu.cs:321) — удалить `case UtilitiesMenuCommandKind.ProcessInspector`.
- [`MainWindow.Avalonia.Tree.cs:2857-2858`](Workspace1C/Views/MainWindow.Avalonia.Tree.cs:2857) — удалить тот же case.
- Проверить [`UtilitiesMenuLayoutStore.NormalizeNode`](Workspace1C/Services/UtilitiesMenuLayoutStore.cs) — по CHANGELOG (строка 6024) узлы с неизвестным `ItemId` уже отбрасываются; если нет — добавить.
- Обновить тест [`UtilitiesMenuLayoutTests.DefaultLayout_MatchesCurrentStaticMenu`](Workspace1C.Tests/UtilitiesMenuLayoutTests.cs:88-112): в ожидаемом массиве убрать `"ProcessInspector"`, оставить `"KillOneCProcesses"`. Количество узлов группы уменьшится на 1; корневое количество (18) не меняется.
- Добавить тест `ProcessInspector_IsNotInUtilitiesMenu`: `UtilitiesMenuCatalog.Find("ProcessInspector") == null` и в `UtilitiesMenuLayouts.Default()` нет `ItemId == "ProcessInspector"`.

### 4.3. Меню «Колонки»: настоящие чекбоксы

- WPF уже использует `IsCheckable = true` / `IsChecked` ([`ProcessInspectorWindow.xaml.cs:103-110`](Workspace1C/Views/ProcessInspectorWindow.xaml.cs:103)) — оставить и покрыть тестом.
- Avalonia сейчас формирует текстовый префикс `"✓ "` ([`ProcessInspectorWindow.Avalonia.cs:394`](Workspace1C/Views/ProcessInspectorWindow.Avalonia.cs:394)). Заменить на реальный чекбокс:

```csharp
var item = new MenuItem
{
    Header = LocalizationManager.T(info.TitleKey),
    ToggleType = MenuItemToggleType.CheckBox,
    IsChecked = visible
};
item.Styled(ControlThemes.ModernMenuItem);
item.Click += (_, _) => _vm.SetColumnVisible(column, item.IsChecked);
```

- Проверить, что `ModernMenuItem` (Avalonia) поддерживает индикатор `ToggleType` (тема `ControlThemes.ModernMenuItem` в [`Controls.axaml`](Workspace1C/Themes/Controls.axaml)). Если кастомный шаблон не рисует галочку — добавить в шаблон `Path`/`TextBlock` с видимостью по `IsChecked`, но оставить `ToggleType=CheckBox` и `IsChecked` (это и есть «реальный чекбокс», а не текстовый префикс).
- Убедиться, что клик по пункту не закрывает меню до применения (`ContextMenu` закроется — это нормально; состояние сохраняется в настройках).

### 4.4. Мультивыбор и подсветка

- WPF: та же правка, что в задаче #140 (шаг 1) — убрать `AlternatingRowBackground`, локальный `RowStyle` с `IsSelected` после зебры.
- Avalonia: правка темы из #140 (шаг 2) — `:selected` фон на `ListBoxItem`.
- Проверить `RestoreSelectedRows` после автообновления: WPF [`ProcessInspectorWindow.xaml.cs:147-163`](Workspace1C/Views/ProcessInspectorWindow.xaml.cs:147), Avalonia [`413-430`](Workspace1C/Views/ProcessInspectorWindow.Avalonia.cs:413). Логика уже восстанавливает все строки; добавить регрессионный тест.
- Убедиться, что Ctrl/Shift-мультивыбор работает: `SelectionMode="Extended"` (WPF) и `SelectionMode.Multiple` (Avalonia).

### 4.5. Тесты

Обновить/добавить в `Workspace1C.Tests/Issue134ProcessInspectorTests.cs`:

| Тест | Что проверяет |
|---|---|
| `Wpf_ProcessInspectorButton_IsAfterCheckAvailability_BeforeSeparator` | индексы в `MainWindow.xaml` |
| `Avalonia_ProcessInspectorButton_IsAfterCheckAvailability_BeforeSeparator` | индексы в `MainWindow.Avalonia.cs` |
| `ProcessInspector_IsRemovedFromUtilitiesCatalogAndLayout` | каталог/компоновка |
| `Wpf_ColumnsMenu_UsesIsCheckableIsChecked` | XAML/код |
| `Avalonia_ColumnsMenu_UsesMenuItemToggleTypeCheckBox` | нет `"✓ "`, есть `MenuItemToggleType.CheckBox`/`IsChecked` |
| `Wpf_Inspector_SelectionTriggerOverridesZebra` | см. #140 |
| `RestoreSelectedRows_RestoresAllSelectedRows` | ViewModel: 2 выбранные строки после `ApplyRows` |

Обновить `UtilitiesMenuLayoutTests` (см. 4.2). Остальные тесты каталога (`Catalog_ContainsAllDefaultLayoutItems`, `ClusterMigration_IsSeparateItem_WithoutHotkey`) не должны сломаться.

### 4.6. Риски и проверка

- **Риск:** удаление `ProcessInspector` из каталога ломает старые пользовательские компоновки → `NormalizeNode` отбрасывает неизвестный `ItemId`, меню не падает. Проверить тестом.
- **Риск:** `MenuItemToggleType` в Avalonia требует `Avalonia.Controls` — using уже есть.
- **Проверка вручную:** панель — иконка на новом месте; меню «Утилиты» без пункта; «Колонки» — галочки; Ctrl/Shift-выделение и подсветка чётных строк.

### 4.7. Definition of Done

- [ ] Иконка инспектора на панели после «Проверить доступность», перед разделителем (WPF и Avalonia).
- [ ] Пункта «Инспектор процессов 1С» в «Утилитах» нет; тесты каталога/компоновки обновлены.
- [ ] В «Колонках» реальные чекбоксы (`IsChecked`), без текстового префикса.
- [ ] Выделение видно для всех строк, мультивыбор и восстановление после автообновления работают.
- [ ] Все тесты зелёные.

---

## 5. Задача — версия 1.0.1.84, CHANGELOG, README

Выполняется **после** задач #140, #139, #138, #134 и только один раз.

### 5.1. Версия

В [`Workspace1C/Workspace1C.csproj:62-65`](Workspace1C/Workspace1C.csproj:62) заменить **только последнее число**:

```xml
<Version>1.0.1.84</Version>
<AssemblyVersion>1.0.1.84</AssemblyVersion>
<FileVersion>1.0.1.84</FileVersion>
<InformationalVersion>1.0.1.84</InformationalVersion>
```

Проверить, что других мест с `1.0.1.83`, влияющих на версию, нет (`VersionInfo.cs` читает сборку). Исторические записи `CHANGELOG.md`/`README.md` со старыми версиями не менять.

### 5.2. CHANGELOG.md

Добавить **первым** разделом после шапки (перед `## [1.0.1.83]`, строка 14):

```markdown
## [1.0.1.84] — 2026-11-18

### Исправлено

- **Инспектор процессов: выделение строк и «Неизвестная база» (issue #140).** ...
  (подробно: AlternatingRowBackground, RowStyle, полный матчер, нормализация путей, веб-базы; тесты `Issue140ProcessInspectorTests`)
- **Окно «Доступна новая версия» больше не обрезается (issue #139).** ...
  (`UpdateDialogSizePolicy`, запрет resize по границе, WrapPanel; тесты `Issue139UpdateDialogSizeTests`)
- **Информация о подключении: заголовки по типу базы, копирование, открытие каталога (issue #138).** ...
  (`ConnectionInfoPresenter`, `ConnectionInfoFields`, `OpenInfobaseFolderCommand`; тесты `Issue138ConnectionInfoPanelTests`)
- **Инспектор процессов: панель, меню «Утилиты», чекбоксы колонок, мультивыбор (issue #134).** ...
  (перенос иконки, удаление пункта из «Утилит», `MenuItemToggleType.CheckBox`; обновлён `Issue134ProcessInspectorTests`, `UtilitiesMenuLayoutTests`)
```

Формат — как в существующих разделах: ссылки на файлы, ключи локализации, имена тестов.

### 5.3. README.md

- Бейдж версии (строка 3): `Версия-1.0.1.84`.
- Текст «актуальная версия — **1.0.1.84**» (строка 11).
- В начало блока «Что нового» (перед строкой 208) добавить 4 пункта по issues с пометкой `(1.0.1.84, Windows и Linux)` и ссылкой на `CHANGELOG.md`.

### 5.4. DoD

- [ ] Все четыре поля csproj = `1.0.1.84`.
- [ ] `CHANGELOG.md` содержит раздел `## [1.0.1.84]` с перечнем #140/#139/#138/#134.
- [ ] `README.md` — бейдж и актуальная версия `1.0.1.84`, новые пункты.
- [ ] Нет случайных изменений версии в других файлах.

---

## 6. Задача — сборка

1. `dotnet build Workspace1C/Workspace1C.csproj -c Release`
2. `dotnet build Workspace1C/Workspace1C.csproj -c Release -p:ForceLinux=true`
3. `dotnet test Workspace1C.Tests/Workspace1C.Tests.csproj -c Release`
4. `dotnet test Workspace1C.Tests/Workspace1C.Tests.csproj -c Release -p:BuildLinux=true`
5. `Workspace1C/build-windows-single-file.ps1 -Configuration Release`
6. `Workspace1C/build-linux-single-file.ps1 -Configuration Release`
7. Проверить артефакты:
   - `Workspace1C/dist/win-x64/Workspace1C.exe`
   - `Workspace1C/dist/linux-x64/Workspace1C`

### DoD

- [ ] Обе конфигурации компилируются без ошибок.
- [ ] Все тесты зелёные (WPF и Linux-кросс).
- [ ] Оба single-file артефакта собраны, размеры адекватны.
- [ ] `Workspace1C/build.ps1` проходит.

---

## 7. Задача — релиз

Релиз идёт по тегу. Workflow: [`.github/workflows/release.yml`](.github/workflows/release.yml).

1. Закоммитить все правки (файл `docs/plan-v1.0.1.84.md` при необходимости не коммитить — это план, а не код).
2. Создать и запушить тег:

```powershell
git tag v1.0.1.84
git push origin v1.0.1.84
```

3. Workflow `Публикация релиза`:
   - job `windows` собирает `Workspace1C.exe` (`build-windows-single-file.ps1`);
   - job `linux` собирает `Workspace1C-linux-x64` (`build-linux-single-file.sh`), затем смок-тест под `xvfb`;
   - job `release` берёт текст из раздела `## [1.0.1.84]` файла `CHANGELOG.md` ([`release.yml:200-225`](.github/workflows/release.yml:200)) и публикует GitHub Release с двумя asset'ами;
   - шаг «Новость в Telegram» ([`release.yml:247-284`](.github/workflows/release.yml:247)) отправляет сообщение в канал `-1004425443528`, если задан секрет `TELEGRAM_BOT_TOKEN` (чат переопределяется `TELEGRAM_CHAT_ID`).
4. Если автоматический запуск не сработал — `workflow_dispatch` с `tag: v1.0.1.84`.
5. Проверить:
   - релиз `v1.0.1.84` опубликован, не prerelease, `latest`;
   - asset'ы `Workspace1C.exe` и `Workspace1C-linux-x64` прикреплены;
   - текст релиза содержит исправления #140/#139/#138/#134.

### DoD

- [ ] Тег `v1.0.1.84` запушен.
- [ ] GitHub Release создан, asset'ы на месте, текст из CHANGELOG.
- [ ] Workflow завершился успешно (оба job'а сборки и release).

---

## 8. Задача — комментарии в issues (НЕ закрывать)

После успешного релиза добавить в каждый issue комментарий. Шаблон:

```text
Исправлено в [v1.0.1.84](https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.84).

Что сделано:
- ...
```

Команды:

```powershell
gh issue comment 140 --repo sivatorov/Workspace1C --body "<текст>"
gh issue comment 139 --repo sivatorov/Workspace1C --body "<текст>"
gh issue comment 138 --repo sivatorov/Workspace1C --body "<текст>"
gh issue comment 134 --repo sivatorov/Workspace1C --body "<текст>"
```

Содержание по issues:
- **#140** — видимое выделение всех строк (WPF `RowStyle`, Avalonia `:selected`), полный матчер в инспекторе, нормализация путей (переменные окружения, UNC/диск, уникальный лист каталога), сопоставление веб-баз, диагностика `CM_DEBUG_RUNNING_MATCH`.
- **#139** — окно обновления не обрезается: `UpdateDialogSizePolicy`, запрет resize по границе, `WrapPanel`, ограничение рабочей областью.
- **#138** — заголовки по типу базы («Каталог»/«Адрес»/«Сервер»+«Имя базы»), копирование по клику, кнопка «Открыть каталог» для файловых баз, устранено дублирование.
- **#134** — иконка инспектора перенесена после «Проверить доступность», пункт убран из «Утилит», реальные чекбоксы в «Колонках», исправлена подсветка чётных строк и мультивыбор.

**Issues не закрывать** (`gh issue close` не вызывать).

### DoD

- [ ] Во все четыре issues добавлены комментарии со ссылкой на релиз.
- [ ] Issues остаются открытыми.

---

## 9. Задача — Telegram

Основной путь — автоматический шаг workflow (см. 7). Если секрет `TELEGRAM_BOT_TOKEN` не задан:

1. Ручная отправка через Bot API:

```powershell
$env:TG_TOKEN = "<токен>"
$text = @"
🚀 Workspace 1C 1.0.1.84

Исправлены #140, #139, #138, #134.
https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.84
"@
curl.exe -sS -X POST "https://api.telegram.org/bot$env:TG_TOKEN/sendMessage" `
  --data-urlencode "chat_id=-1004425443528" `
  --data-urlencode "text=$text"
```

2. Либо через CLI приложения (если настроен Telegram-мост):

```powershell
Workspace1C/dist/win-x64/Workspace1C.exe --telegram-send --text "Workspace 1C 1.0.1.84 — https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.84"
```

Примечание: workflow ищет файл `_telegram_msg_v101084.txt` (шаблон имени `_telegram_msg_v<head3 без точек>0<последнее число>.txt`); файлы `_telegram_*.txt` в `.gitignore`, поэтому при отсутствии файла текст берётся из заметок релиза. Это ожидаемо.

### DoD

- [ ] Новость о релизе отправлена в канал `-1004425443528` (workflow или вручную).
- [ ] В сообщении есть ссылка на релиз.

---

## 10. Чеклист релиза (сводный)

- [ ] #140: подсветка всех строк (WPF + Avalonia), «Неизвестная база» исправлена, тесты.
- [ ] #139: окно обновления не обрезается, тесты.
- [ ] #138: заголовки по типу, копирование, открытие каталога, тесты.
- [ ] #134: иконка на панели, пункт убран из «Утилит», чекбоксы колонок, мультивыбор.
- [ ] Версия `1.0.1.84` во всех четырёх полях csproj (только последнее число).
- [ ] `CHANGELOG.md` — раздел `## [1.0.1.84]`.
- [ ] `README.md` — бейдж и актуальная версия.
- [ ] Сборка WPF и Linux, все тесты зелёные.
- [ ] Single-file артефакты собраны.
- [ ] Тег `v1.0.1.84` и GitHub Release с двумя asset'ами.
- [ ] Комментарии в #140/#139/#138/#134 со ссылкой на релиз; issues не закрыты.
- [ ] Новость в Telegram `-1004425443528`.

---

## 11. Матрица влияния на существующие тесты

| Тест | Влияние | Действие |
|---|---|---|
| `RunningInfobaseMatcherTests` | меняется `MatchProcesses`/`NormalizePath` | прогнать, при необходимости дополнить кейсами #140 |
| `ProcessInspectorSelectionTests` | меняется выделение | прогнать, добавить кейс всех строк |
| `Issue134ProcessInspectorTests` | перенос кнопки, удаление пункта меню, чекбоксы | обновить ожидания/добавить тесты |
| `UtilitiesMenuLayoutTests` | удалён `ProcessInspector` из компоновки | убрать из ожидаемого массива |
| `Issue116ManualUpdateDialogTests` | #139 | сохранить литералы `SizeToContent`, `MinWidth="440"`, отсутствие `WndProc`/`WmGetMinMaxInfo` |
| `Issue116ManualUpdateNoNotificationTests` | #139 | сохранить `MaxWidth = 760;`, `CanResize = false;`, `SizeToContent.WidthAndHeight` |
| `Issue116UpdateIntervalTests` | #139 | сохранить `ResizeMode="NoResize"` |
| `Issue26ContrastToggleZebraGuardTests` | зебра/кнопки | не ломать `ModernButton` и отсутствие `#16A34A` |
| `CopyConnectionStringTests`, `InfobaseDisplayTests` | #138 | не менять `ConnectionPathDisplay`/`ServerDatabaseDisplay` |
| `EscCloseWindowsTests` | окна | не менять `IsCancel`/Esc |
| `Issue131LongOperationNotificationTests` | правая панель Avalonia | `BuildRightPanel` не должен потерять переключатель длительных операций |

---

## 12. Порядок выполнения задач (для отдельных агентов Code)

1. **#140** — подсветка + полный матчер + тесты.
2. **#139** — размеры окна обновления + тесты.
3. **#138** — панель информации о подключении + тесты.
4. **#134** — панель, «Утилиты», чекбоксы, мультивыбор + тесты.
5. **Версия/CHANGELOG/README** — только после всех правок, один раз.
6. **Сборка** — WPF + Linux + тесты + single-file.
7. **Релиз** — тег, workflow, проверка ассетов.
8. **Комментарии в issues** — не закрывать.
9. **Telegram** — проверить/дополнить.

Каждая задача самодостаточна: в ней указаны файлы, точные места, ключи локализации, имена тестов и команды проверки.
