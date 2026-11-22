#if WINDOWS
using System.Collections.Generic;
using System.Linq;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;

namespace Workspace1C.ViewModels;

/// <summary>
/// Модель представления окна настроек (Windows/WPF). Инкапсулирует состояние вкладок
/// «Цветовое оформление» (текущая схема, рабочие копии правок, набор «грязных» тем),
/// «ibases.v8i» (рабочее состояние синхронизации) и «Шрифт» (рабочие копии настроек
/// шрифтов областей) и бизнес-операции над ними: разрешение/валидацию/локализацию
/// имён схем, загрузку рабочих копий, персист изменённых тем и CRUD пользовательских
/// тем, построение статус-текста синхронизации.
/// Класс не ссылается на конкретные WPF-контролы и не трогает визуальное дерево —
/// вся работа с интерфейсом остаётся в <c>SettingsWindow</c>, которая вызывает методы
/// этой модели для получения данных и применения изменений.
/// </summary>
public sealed class SettingsViewModel
{
    /// <summary>Отображаемое имя встроенной светлой темы.</summary>
    private const string BuiltInLightName = "Светлая";

    /// <summary>Отображаемое имя встроенной тёмной темы.</summary>
    private const string BuiltInDarkName = "Тёмная";

    private readonly MainViewModel _viewModel;

    /// <summary>Рабочие копии схем по идентификатору темы (встроенной «Светлая»/«Тёмная»
    /// или пользовательской). Хранят незаконченные правки каждой темы отдельно, поэтому
    /// переключение между темами не сбрасывает внесённые изменения.</summary>
    private readonly Dictionary<string, ColorScheme> _editingSchemes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Идентификаторы тем, реально изменённых в ходе редактирования (сохраняются при «ОК»).</summary>
    private readonly HashSet<string> _dirtySchemes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Создаёт модель представления окна настроек поверх главной модели.
    /// </summary>
    /// <param name="viewModel">Главная модель представления приложения (источник данных).</param>
    public SettingsViewModel(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        // Активная тема регистрируется в карте правок, чтобы её настройки сохранялись
        // при переключении на другие темы и обратно.
        CurrentColorScheme = _viewModel.ActiveColorScheme.Clone();
        _editingSchemes[CurrentColorScheme.Name] = CurrentColorScheme;
    }

    /// <summary>Текущая редактируемая цветовая схема вкладки «Цветовое оформление».</summary>
    public ColorScheme CurrentColorScheme { get; private set; }

    /// <summary>
    /// Доля цвета группы в фоне строки-папки, проценты 0–100 (issue #145).
    /// Проксирует главную модель: изменение сразу применяется к ThemeManager.
    /// </summary>
    public int GroupColorTintPercent
    {
        get => _viewModel.GroupColorTintPercent;
        set => _viewModel.GroupColorTintPercent = value;
    }

    /// <summary>
    /// Доля цвета группы в фоне строки-папки при наведении, проценты 0–100 (issue #145).
    /// </summary>
    public int GroupColorHoverTintPercent
    {
        get => _viewModel.GroupColorHoverTintPercent;
        set => _viewModel.GroupColorHoverTintPercent = value;
    }

    /// <summary>Режим редактора: true — редактируется тёмная палитра, false — светлая.</summary>
    public bool EditingDarkPalette { get; private set; }

    /// <summary>Задаёт, какую палитру (светлую/тёмную) сейчас редактирует редактор цветов.</summary>
    public void SetPaletteMode(bool dark)
    {
        EditingDarkPalette = dark;
    }

    /// <summary>Список доступных схем: встроенные (Светлая/Тёмная) и пользовательские.</summary>
    public IReadOnlyList<ColorScheme> AvailableColorSchemes() => _viewModel.AvailableColorSchemes();

    // ---- Полноценные темы (Задача 3) ----

    /// <summary>Встроенные полноценные темы (Светлая/Тёмная/Синяя/Зелёная) для выбора в настройках.</summary>
    public static IReadOnlyList<ThemeDefinition> FullThemes => ThemeCatalog.BuiltIn;

    /// <summary>
    /// Идентификатор активной полноценной темы (из <see cref="ThemeManager.CurrentFullTheme"/>);
    /// до первого применения — светлая тема по умолчанию.
    /// </summary>
    public string CurrentFullThemeId => ThemeManager.CurrentFullTheme?.Id ?? ThemeCatalog.BuiltIn[0].Id;

    /// <summary>
    /// true, если имя совпадает с именем встроенной полноценной темы
    /// (Светлая/Тёмная/Синяя/Зелёная). Такие схемы нельзя переименовывать/удалять
    /// в списке цветовых схем.
    /// </summary>
    public static bool IsBuiltInFullThemeName(string name)
        => ThemeCatalog.BuiltIn.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Делает схему выбранной полноценной темы текущей редактируемой (без пометки
    /// «изменена»): редактор палитры и предпросмотр показывают цвета темы. Пока
    /// пользователь не тронет цвета, схема не сохраняется как пользовательская тема.
    /// </summary>
    public void AdoptFullThemeScheme(ThemeDefinition theme)
    {
        if (theme?.Scheme is null)
            return;
        var scheme = theme.Scheme.Clone();
        scheme.Normalize();
        CurrentColorScheme = scheme;
        _editingSchemes[scheme.Name] = scheme;
    }

    /// <summary>true, если имя соответствует встроенной теме («Светлая»/«Тёмная»).</summary>
    public static bool IsBuiltInName(string name)
        => string.Equals(name, BuiltInLightName, StringComparison.OrdinalIgnoreCase)
           || string.Equals(name, BuiltInDarkName, StringComparison.OrdinalIgnoreCase);

    /// <summary>true, если имя зарезервировано (встроенная тема) — нельзя использовать для пользовательской.</summary>
    public static bool IsReservedName(string name) => IsBuiltInName(name);

    /// <summary>
    /// Возвращает локализованное отображаемое имя встроенной темы.
    /// Идентификатор схемы (<c>Name</c>) остаётся неизменным, поэтому сохранение/загрузка и сравнения не ломаются.
    /// </summary>
    public static string LocalizedBuiltInName(string name)
    {
        if (string.Equals(name, BuiltInLightName, StringComparison.OrdinalIgnoreCase))
            return LocalizationManager.T("Theme.Light");
        if (string.Equals(name, BuiltInDarkName, StringComparison.OrdinalIgnoreCase))
            return LocalizationManager.T("Theme.Dark");
        // Встроенные полноценные темы «Синяя»/«Зелёная» (Задача 3).
        var full = ThemeCatalog.BuiltIn.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (full is not null)
            return LocalizationManager.T(ThemeFeatureMap.ThemeNameKey(full.Id));
        return name;
    }

    /// <summary>Возвращает схему по имени из доступных (встроенных или пользовательских).</summary>
    public ColorScheme? ResolveScheme(string name)
    {
        return AvailableColorSchemes()
            .FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))?.Clone();
    }

    /// <summary>
    /// Возвращает сохранённые правки темы из активной схемы, если активная схема —
    /// та же тема (по каноническому имени), иначе null — тогда нужно загружать
    /// «чистую» встроенную схему. Чистая функция для юнит-тестов (issue #26):
    /// правки встроенных «Светлая»/«Тёмная» живут в активной схеме/слоте, а не в
    /// <see cref="EnumerateAllSchemes"/>, где встроенные возвращаются дефолтными.
    /// </summary>
    public static ColorScheme? TryGetActiveEditsFor(Models.ColorScheme? active, string name)
        => active is not null
           && string.Equals(active.Name, name, StringComparison.OrdinalIgnoreCase)
               ? active.Clone()
               : null;

    /// <summary>
    /// Возвращает рабочую копию схемы для редактирования по идентификатору темы.
    /// Если тема уже открыта в редакторе (есть незаконченные правки) — возвращает её,
    /// иначе загружает сохранённое состояние (слот базовой темы для встроенных,
    /// JSON-файл для пользовательских). Так каждая тема хранит собственные настройки
    /// во время редактирования, и переключение между темами не теряет правки.
    /// </summary>
    public ColorScheme LoadEditableScheme(string name)
    {
        if (_editingSchemes.TryGetValue(name, out var cached))
            return cached;

        // Единый источник правок встроенных тем (issue #26): правки живут в активной
        // схеме/слоте (AppSettings.ActiveColorScheme), а не в EnumerateAllSchemes,
        // где встроенные «Светлая»/«Тёмная» возвращаются «чистыми». Если активная
        // схема — та же тема, возвращаем её сохранённые правки (акцент и пр.), иначе
        // редактор снова показал бы дефолтные #FDBF00/#E0A800.
        var withEdits = TryGetActiveEditsFor(_viewModel.ActiveColorScheme, name);
        if (withEdits is not null)
        {
            withEdits.Normalize();
            _editingSchemes[name] = withEdits;
            return withEdits;
        }

        ColorScheme? source;
        if (IsBuiltInName(name))
        {
            var dark = string.Equals(name, BuiltInDarkName, StringComparison.OrdinalIgnoreCase);
            source = _viewModel.GetSchemeForTheme(
                dark ? ThemeManager.DarkThemeName : ThemeManager.LightThemeName).Clone();
            source.Name = dark ? BuiltInDarkName : BuiltInLightName;
        }
        else if (ThemeCatalog.BuiltIn.FirstOrDefault(t =>
                     string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) is { } fullTheme)
        {
            // Встроенная полноценная тема «Синяя»/«Зелёная»: берём её схему из каталога.
            source = fullTheme.Scheme.Clone();
            source.Name = fullTheme.Name;
        }
        else
        {
            source = ResolveScheme(name);
        }

        if (source is null)
            source = ColorScheme.Create(name, false);

        _editingSchemes[name] = source;
        return source;
    }

    /// <summary>Делает указанную тему текущей редактируемой, загружая её рабочую копию.</summary>
    public void SetCurrentScheme(string name)
    {
        CurrentColorScheme = LoadEditableScheme(name);
    }

    /// <summary>
    /// Сохраняет все темы, изменённые во время редактирования вкладки «Цветовое оформление».
    /// Встроенные темы сохраняются в слот соответствующей базовой темы (светлой/тёмной),
    /// пользовательские — в их JSON-файл. Правки одной темы не затрагивают остальные.
    /// </summary>
    public void PersistEditedSchemes()
    {
        foreach (var name in _dirtySchemes.ToList())
        {
            if (!_editingSchemes.TryGetValue(name, out var scheme))
                continue;
            if (IsBuiltInName(name))
            {
                _viewModel.SaveColorSchemeSlot(scheme);
            }
            else
            {
                _viewModel.SaveCustomColorScheme(scheme);
                // Имя могло измениться при конфликте со встроенной темой (issue #376):
                // переносим рабочую копию на новое имя.
                if (!string.Equals(scheme.Name, name, StringComparison.Ordinal))
                {
                    _editingSchemes.Remove(name);
                    _editingSchemes[scheme.Name] = scheme;
                }
            }
        }
        _dirtySchemes.Clear();
    }

    /// <summary>true, если тема была изменена в ходе редактирования.</summary>
    public bool IsDirty(string name) => _dirtySchemes.Contains(name);

    // ---- Редактирование отдельных цветов ----

    /// <summary>
    /// Возвращает упорядоченный список редактируемых цветов текущей схемы:
    /// ключ ресурса, локализованная подпись и текущее значение HEX.
    /// </summary>
    public IReadOnlyList<(string Key, string Label, string Hex)> GetEditableColors()
    {
        var result = new List<(string Key, string Label, string Hex)>();
        foreach (var (key, label) in ColorScheme.Definitions)
            result.Add((key, label, CurrentColorScheme.PaletteValue(EditingDarkPalette, key)));
        return result;
    }

    /// <summary>Устанавливает значение цвета активной палитры текущей схемы и фиксирует её как изменённую.</summary>
    public void SetColor(string key, string hex)
    {
        CurrentColorScheme.Palette(EditingDarkPalette)[key] = hex;
        MarkCurrentDirty();
    }

    /// <summary>
    /// Генерирует обе палитры (светлую и тёмную) текущей схемы из одного базового цвета
    /// (issue #271) и помечает схему изменённой. Заменяет редактируемые цвета, но не имя
    /// схемы — результат можно править в редакторе перед сохранением.
    /// </summary>
    public void ApplyGeneratedScheme(string baseHex)
    {
        var generated = ColorScheme.GenerateFromColor(baseHex);

        CurrentColorScheme.LightColors.Clear();
        foreach (var (key, value) in generated.LightColors)
            CurrentColorScheme.LightColors[key] = value;

        CurrentColorScheme.DarkColors.Clear();
        foreach (var (key, value) in generated.DarkColors)
            CurrentColorScheme.DarkColors[key] = value;

        MarkCurrentDirty();
    }

    /// <summary>Помечает текущую схему как изменённую (будет сохранена при «ОК»).</summary>
    public void MarkCurrentDirty()
    {
        _editingSchemes[CurrentColorScheme.Name] = CurrentColorScheme;
        _dirtySchemes.Add(CurrentColorScheme.Name);
    }

    // ---- CRUD пользовательских тем ----

    /// <summary>
    /// Создаёт собственную тему на основе текущих цветов, сохраняет её и делает текущей.
    /// Возвращает новую схему или null, если имя зарезервировано (валидация выполняется
    /// вызывающей стороной через <see cref="IsReservedName"/>).
    /// </summary>
    public ColorScheme CreateCustomScheme(string name)
    {
        var copy = CurrentColorScheme.Clone();
        copy.Name = name;
        _viewModel.SaveCustomColorScheme(copy);
        CurrentColorScheme = copy;
        // Регистрируем новую тему в карте правок для дальнейшего редактирования.
        // Имя берём после сохранения: при конфликте со встроенной темой оно
        // могло быть изменено (issue #376).
        _editingSchemes[copy.Name] = copy;
        return copy;
    }

    /// <summary>
    /// Переименовывает выбранную пользовательскую тему: сохраняет под новым именем и удаляет
    /// старый файл. Если тема уже открыта в редакторе с незаконченными правками — переносит
    /// её рабочую копию на новое имя. Возвращает null, если тема встроенная или не найдена.
    /// </summary>
    public ColorScheme? RenameCustomScheme(string oldName, string newName)
    {
        if (IsBuiltInName(oldName))
            return null;

        // Сначала сохраняем файл под новым именем и только после успешной записи удаляем
        // старый файл — если запись не пройдёт, прежняя тема не пропадёт. Если тема уже
        // открыта в редакторе с незаконченными правками — переносим её рабочую копию на новое имя.
        var toSave = _editingSchemes.TryGetValue(oldName, out var working) ? working : ResolveScheme(oldName);
        if (toSave is null)
            return null;

        toSave.Name = newName;
        _viewModel.SaveCustomColorScheme(toSave);
        _viewModel.DeleteCustomColorScheme(oldName);
        _editingSchemes.Remove(oldName);
        // Имя берём после сохранения: при конфликте со встроенной темой оно
        // могло быть изменено (issue #376).
        _editingSchemes[toSave.Name] = toSave;
        if (_dirtySchemes.Remove(oldName))
            _dirtySchemes.Add(toSave.Name);

        if (string.Equals(CurrentColorScheme.Name, oldName, StringComparison.OrdinalIgnoreCase))
            CurrentColorScheme.Name = toSave.Name;

        return toSave;
    }

    /// <summary>
    /// Удаляет пользовательскую тему по имени. Если удаляемая тема является активной —
    /// переключается на базовую встроенную тему. Возвращает false для встроенных тем.
    /// </summary>
    public bool DeleteCustomScheme(string name)
    {
        if (IsBuiltInName(name))
            return false;

        _viewModel.DeleteCustomColorScheme(name);
        _editingSchemes.Remove(name);
        _dirtySchemes.Remove(name);

        // Если удалили активную — переключаемся на базовую встроенную светлую схему.
        if (string.Equals(CurrentColorScheme.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            CurrentColorScheme = ColorScheme.CreateLight();
            _editingSchemes[CurrentColorScheme.Name] = CurrentColorScheme;
        }

        return true;
    }

    /// <summary>Сбрасывает обе палитры ТОЛЬКО выбранной темы на значения по умолчанию (остальные не затрагиваются).</summary>
    public void ResetCurrentSchemeColors()
    {
        var name = CurrentColorScheme.Name;
        CurrentColorScheme = ColorScheme.Create(name, false);
        _editingSchemes[name] = CurrentColorScheme;
        _dirtySchemes.Add(name);
    }

    /// <summary>Принимает импортированную схему: сохраняет в пользовательские темы и делает текущей.</summary>
    public void AdoptImportedScheme(ColorScheme scheme)
    {
        _viewModel.SaveCustomColorScheme(scheme);
        CurrentColorScheme = scheme;
        _editingSchemes[scheme.Name] = scheme;
    }

    // ---- Рабочее состояние синхронизации с ibases.v8i ----

    /// <summary>
    /// Рабочее состояние настроек синхронизации с файлом ibases.v8i до нажатия «ОК».
    /// Инкапсулирует режим/путь/момент синхронизации и их чистые преобразования
    /// (разрешение отображаемого пути, построение статус-текста, разбор интервала).
    /// </summary>
    public sealed class IbasesSyncSettings
    {
        /// <summary>Режим синхронизации («Нет»/«Импорт»/«Экспорт»/«Оба»).</summary>
        public IbasesSyncMode Mode { get; set; }

        /// <summary>Пользовательский путь к файлу ibases.v8i (может быть пустым).</summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>Момент автоматической синхронизации.</summary>
        public IbasesSyncTrigger Trigger { get; set; } = IbasesSyncTrigger.OnStartup;

        /// <summary>Интервал синхронизации в минутах.</summary>
        public int IntervalMinutes { get; set; } = 30;

        /// <summary>Время синхронизации по расписанию (HH:mm).</summary>
        public string ScheduleTime { get; set; } = "09:00";

        /// <summary>«Сохранять после правки» (issue #269): записывать изменения в ibases.v8i сразу.</summary>
        public bool SaveAfterEdit { get; set; } = true;

        /// <summary>true, если синхронизация включена (режим не «Нет»).</summary>
        public bool IsEnabled => Mode != IbasesSyncMode.None;

        /// <summary>
        /// Путь к файлу ibases.v8i для отображения: пользовательский путь или
        /// стандартный путь 1С, если пользовательский не задан.
        /// </summary>
        public string? ResolveDisplayPath()
            => string.IsNullOrWhiteSpace(FilePath)
                ? IbasesV8iImporter.FindDefaultPath()
                : FilePath;

        /// <summary>
        /// Разбирает введённое значение интервала в минуты. Невалидные значения
        /// (не число, ноль или отрицательные) заменяются значением по умолчанию 30.
        /// </summary>
        public static int ParseInterval(string? text)
            => int.TryParse(text, out var minutes) && minutes > 0 ? minutes : 30;

        /// <summary>
        /// Строит локализованный статус-текст блока синхронизации в зависимости от
        /// состояния, режима, пути и момента автоматической синхронизации.
        /// </summary>
        public string BuildStatusText()
        {
            if (!IsEnabled)
                return LocalizationManager.T("Settings.Ibases.StatusDisabled");

            var path = ResolveDisplayPath();
            if (string.IsNullOrWhiteSpace(path))
                return LocalizationManager.T("Settings.Ibases.StatusFileNotFound");

            var modeText = Mode switch
            {
                IbasesSyncMode.Import => LocalizationManager.T("Settings.Ibases.ModeImportShort"),
                IbasesSyncMode.Export => LocalizationManager.T("Settings.Ibases.ModeExportShort"),
                _ => LocalizationManager.T("Settings.Ibases.ModeBothShort")
            };
            var triggerText = Trigger switch
            {
                IbasesSyncTrigger.Interval => string.Format(LocalizationManager.T("Settings.Ibases.TriggerIntervalShort"), IntervalMinutes),
                IbasesSyncTrigger.Schedule => string.Format(LocalizationManager.T("Settings.Ibases.TriggerScheduleShort"), ScheduleTime),
                _ => LocalizationManager.T("Settings.Ibases.TriggerStartupShort")
            };
            return string.Format(LocalizationManager.T("Settings.Ibases.StatusFormat"), path, modeText, triggerText);
        }
    }

    /// <summary>Рабочее состояние настроек синхронизации с ibases.v8i (заполняется вкладкой «ibases.v8i»).</summary>
    public IbasesSyncSettings Sync { get; } = new();

    // ---- Рабочие копии настроек шрифтов областей интерфейса ----

    private readonly Dictionary<string, ElementFontSettings> _editingElementFonts = new();

    /// <summary>Рабочие копии настроек шрифта областей интерфейса (не сохранены до «ОК»).</summary>
    public Dictionary<string, ElementFontSettings> ElementFonts => _editingElementFonts;

    /// <summary>
    /// Загружает рабочие копии настроек шрифтов элементов из главной модели: каждое
    /// значение клонируется, чтобы правки в окне не влияли на главную модель до «ОК».
    /// Область «По умолчанию» всегда присутствует и берёт значения общих настроек шрифта.
    /// </summary>
    public void LoadElementFontWorkingCopies(MainViewModel vm)
    {
        _editingElementFonts.Clear();
        foreach (var kvp in vm.ElementFonts)
            _editingElementFonts[kvp.Key] = kvp.Value?.Clone() ?? new ElementFontSettings();
        if (!_editingElementFonts.ContainsKey(ThemeManager.FontDefault))
        {
            _editingElementFonts[ThemeManager.FontDefault] = new ElementFontSettings
            {
                FontFamily = vm.FontFamily,
                FontSize = vm.FontSize,
                FontWeight = vm.FontWeight,
                FontStyle = vm.FontStyle
            };
        }
    }

    /// <summary>Возвращает рабочую копию шрифта области, создавая её при необходимости.</summary>
    public ElementFontSettings EnsureElementFont(string key)
    {
        if (_editingElementFonts.TryGetValue(key, out var fs) && fs is not null)
            return fs;
        fs = new ElementFontSettings();
        _editingElementFonts[key] = fs;
        return fs;
    }

    // ---- Чистое маппинг-преобразование для горячих клавиш (используется при сохранении) ----

    /// <summary>
    /// Группирует назначения горячих клавиш по клавише (без учёта регистра) и возвращает
    /// только те клавиши, которые назначены более чем одному действию. Пустые назначения
    /// («Нет») не учитываются.
    /// </summary>
    public static IEnumerable<IGrouping<string, (string Name, string Key)>> FindDuplicateHotkeys(
        IEnumerable<(string Name, string Key)> assignments)
        => assignments
            .Where(a => !string.IsNullOrEmpty(a.Key))
            .GroupBy(a => a.Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();
}
#endif