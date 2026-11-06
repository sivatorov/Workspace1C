#if WINDOWS
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using Workspace1C.Collections;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>Main ViewModel (partial class split by feature blocks, see MainViewModel.*.cs).</summary>
public partial class MainViewModel : ViewModelBase
{

    /// <summary>
    /// Сохраняет выбранную тему оформления в настройках.
    /// </summary>
    public void SaveTheme(string theme)
    {
        _savedTheme = theme;
        // Идентификатор полноценной темы следует за выбором базового варианта.
        _fullTheme = string.Equals(theme, Themes.ThemeManager.DarkThemeName, StringComparison.OrdinalIgnoreCase)
            ? "dark" : "light";
        SaveSettings();
    }

    /// <summary>
    /// Применяет встроенную тему оформления по имени («Light»/«Dark»), обновляет
    /// активную схему и сохраняет настройки.
    /// </summary>
    public void ApplyTheme(string theme)
    {
        var scheme = Themes.ThemeManager.GetBuiltInScheme(theme) ?? Models.ColorScheme.CreateLight();
        ApplyColorScheme(scheme);
    }

    /// <summary>
    /// Применяет полноценную тему по идентификатору (Светлая/Тёмная/Синяя/Зелёная):
    /// задаёт базовый вариант, схему, стиль кнопок окна и набор иконок, делает схему
    /// темы активной. При <paramref name="persist"/> выбор сохраняется в настройках
    /// (AppSettings.FullTheme); при false тема применяется без записи — режим
    /// предпросмотра (issue #378). Используется вкладкой «Темы» окна настроек.
    /// </summary>
    public void ApplyFullTheme(string id, bool persist = true)
    {
        Themes.ThemeManager.ApplyFullTheme(id);
        var theme = Themes.ThemeManager.CurrentFullTheme;
        if (persist)
        {
            _fullTheme = theme?.Id ?? _fullTheme;
            _savedTheme = Themes.ThemeManager.CurrentTheme;
        }
        // Активная схема перезаписывается схемой темы ТОЛЬКО при подтверждённом выборе
        // (persist:true). В режиме предпросмотра (persist:false, issue #378) её трогать
        // нельзя: иначе правки встроенной темы затирались ещё до «ОК» (issue #26).
        // Редактор палитры всё равно показывает схему предпросмотренной темы через
        // AdoptFullThemeScheme, а подтверждение выбора фиксируется при «ОК»/«Применить».
        if (persist && theme is not null && theme.Scheme is not null)
        {
            var clone = theme.Scheme.Clone();
            clone.Normalize();
            _activeColorScheme = clone;
        }
        // Общий цвет папок хранится в схеме и применяется при построении дерева.
        RebuildGroupTree();
        if (persist)
            SaveSettings();
        LogTheme($"ApplyFullTheme('{id}', persist:{persist}) -> active='{_activeColorScheme?.Name}'");
    }

    // ---- Режим «предпросмотр → Применить/Отмена» выбора темы (issue #378) ----

    private readonly Themes.ThemePreviewState _themePreview = new();

    /// <summary>true, если есть тема в предпросмотре (не закоммичена и не отменена).</summary>
    public bool HasThemePreview => _themePreview.HasPreview;

    /// <summary>
    /// Начинает режим предпросмотра темы: запоминает текущую тему и последний
    /// сохранённый FullTheme. Вызывается при открытии вкладки «Темы».
    /// </summary>
    public void BeginThemePreview()
    {
        var current = Themes.ThemeManager.CurrentFullTheme?.Id ?? "light";
        _themePreview.Begin(current, _fullTheme);
        LogTheme($"BeginThemePreview -> before='{_themePreview.ThemeIdBeforePreview}', saved='{_themePreview.SavedFullThemeId}'");
    }

    /// <summary>
    /// Переводит тему в предпросмотр: применяет её без сохранения в настройках.
    /// Редактор палитры и предпросмотр показывают цвета выбранной темы.
    /// </summary>
    public void PreviewFullTheme(string id)
    {
        var resolved = _themePreview.Preview(id);
        ApplyFullTheme(resolved, persist: false);
        LogTheme($"PreviewFullTheme('{id}') -> applied='{resolved}' (не сохранено)");
    }

    /// <summary>
    /// Отменяет предпросмотр: возвращает тему, которая была до входа в галерею,
    /// без сохранения. Используется кнопкой «Отмена» и при закрытии окна настроек.
    /// </summary>
    public void RevertFullThemePreview()
    {
        var target = _themePreview.Revert();
        if (string.IsNullOrEmpty(target))
            return;
        ApplyFullTheme(target, persist: false);
        LogTheme($"RevertFullThemePreview -> applied='{target}' (не сохранено)");
    }

    /// <summary>
    /// Подтверждает предпросмотр: сохраняет текущую тему в настройках
    /// (AppSettings.FullTheme). Используется кнопкой «Применить» и при «ОК» окна настроек.
    /// </summary>
    public void CommitFullThemePreview()
    {
        var id = _themePreview.Commit();
        if (string.IsNullOrEmpty(id))
            return;
        var theme = Themes.ThemeManager.CurrentFullTheme;
        _fullTheme = theme?.Id ?? id;
        _savedTheme = Themes.ThemeManager.CurrentTheme;
        SaveSettings();
        LogTheme($"CommitFullThemePreview -> saved='{_fullTheme}'");
    }

    /// <summary>
    /// Переопределяет набор иконок и стиль кнопок окна активной темы (блок «Темы»,
    /// 0.3.12.62). Пустое значение — «по теме». Цветовая схема не меняется; выбор
    /// применяется сразу и сохраняется в настройках.
    /// </summary>
    public void ApplyThemeOverrides(string? iconSet, string? buttonStyle)
    {
        Themes.ThemeManager.SetOverrides(iconSet, buttonStyle);
        Themes.ThemeManager.ReapplyThemeFeatures();
        SaveSettings();
        LogTheme($"ApplyThemeOverrides(icons='{iconSet}', buttons='{buttonStyle}')");
    }

    /// <summary>
    /// Переключает базовую тему (светлую/тёмную), сохраняя пользовательские схемы каждой темы:
    /// применяется схема целевой темы (сохранённая пользовательская или встроенная по умолчанию),
    /// ни одна из сохранённых схем не затирается.
    /// </summary>
    public void ToggleTheme()
    {
        // Схема одна (несёт обе палитры): переключение темы лишь выбирает палитру.
        var targetDark = Themes.ThemeManager.CurrentTheme != Themes.ThemeManager.DarkThemeName;
        ApplySchemeForTheme(targetDark);
        SaveSettings();
        LogTheme($"ToggleTheme -> dark={targetDark}, scheme='{_activeColorScheme?.Name}' (colors light={_activeColorScheme?.LightColors.Count}, dark={_activeColorScheme?.DarkColors.Count})");
    }

    /// <summary>
    /// Задаёт вариант темы (светлый/тёмный) и применяет активную схему с палитрой этого
    /// варианта. Схема одна и несёт обе палитры, поэтому вариант её не меняет.
    /// </summary>
    private void ApplySchemeForTheme(bool dark)
    {
        _savedTheme = dark ? Themes.ThemeManager.DarkThemeName : Themes.ThemeManager.LightThemeName;
        // Убеждаемся, что активная схема применена, затем задаёт вариант темы.
        Themes.ThemeManager.ApplyScheme(_activeColorScheme ?? Models.ColorScheme.CreateLight());
        Themes.ThemeManager.ApplyTheme(dark);
        // Переключение базового варианта не меняет полноценную тему (её Id остаётся),
        // но выбор должен сохраниться в настройках.
        _fullTheme = Themes.ThemeManager.CurrentFullTheme?.Id ?? _fullTheme;
        LogTheme($"ApplySchemeForTheme(dark={dark}) -> active='{_activeColorScheme?.Name}'");
    }

    // ---- Синхронизация темы с режимом ОС (issue #108, Windows) ----

    /// <summary>
    /// «Тема приложения следует за режимом ОС»: при смене тёмного/светлого режима
    /// приложений в Windows переключать базовый вариант темы. Ручная смена темы
    /// остаётся доступной; опция управляется галочкой «Поведение и язык».
    /// </summary>
    public bool ThemeFollowsOs
    {
        get => _themeFollowsOs;
        set
        {
            if (SetProperty(ref _themeFollowsOs, value))
            {
                UpdateOsThemeWatcher();
                ScheduleSaveSettings();
            }
        }
    }

    /// <summary>
    /// Включает/выключает наблюдатель режима ОС в соответствии с опцией. Повторные
    /// вызовы (в том числе из LoadSettings) безопасны: двойная подписка исключена.
    /// </summary>
    private void UpdateOsThemeWatcher()
    {
        Services.OsThemeWatcher.AppsThemeChanged -= OnOsAppsThemeChanged;
        if (_themeFollowsOs)
        {
            Services.OsThemeWatcher.Start();
            Services.OsThemeWatcher.AppsThemeChanged += OnOsAppsThemeChanged;
            _osThemeWatcherActive = true;
        }
        else if (_osThemeWatcherActive)
        {
            Services.OsThemeWatcher.Stop();
            _osThemeWatcherActive = false;
        }
    }

    /// <summary>Обработчик смены режима приложений ОС: переключает вариант темы.</summary>
    private void OnOsAppsThemeChanged(bool dark)
    {
        try
        {
            // SystemEvents поднимает событие на UI-потоке (подписка установлена из UI),
            // но тяжёлое переключение темы отдаём диспетчеру, чтобы не блокировать поток.
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
            {
                // Применяем только при реальной смене варианта (без лишней перерисовки).
                if (IsDarkTheme(Themes.ThemeManager.CurrentTheme) == dark)
                    return;
                ApplySchemeForTheme(dark);
                SaveSettings();
                LogTheme($"OsAppsThemeChanged -> dark={dark}");
            }));
        }
        catch (Exception ex)
        {
            LogTheme($"OsAppsThemeChanged error: {ex.Message}");
        }
    }

    /// <summary>
    /// Возвращает схему (с двумя палитрами) для указанной базовой темы. Вариант темы
    /// определяет, какая палитра показывается; сама схема от него не зависит.
    /// Если активной является встроенная схема той же базовой темы — возвращается она
    /// (сохраняя правки пользователя базовой темы). Если же активна пользовательская или
    /// чужая встроенная схема — для встроенной базовой темы возвращаются её цвета
    /// по умолчанию (issue #136): раньше здесь всегда возвращался активный пользовательский
    /// набор цветов, поэтому выбор «Светлой» после пользовательской темы не менял цвета.
    /// </summary>
    public Models.ColorScheme GetSchemeForTheme(string theme)
    {
        var active = _activeColorScheme;
        if (active is not null)
        {
            var isLight = !IsDarkTheme(theme);
            // Сравниваем с каноническими именами («Светлая»/«Тёмная»), а не с
            // локализованными подписями (issue #26): в не-русской локали активная
            // встроенная схема не опознавалась и редактор показывал правки ТОЛЬКО
            // до тех пор, пока они совпадали с активной схемой.
            var activeLight = string.Equals(active.Name,
                Models.ColorScheme.CreateLight().Name, StringComparison.OrdinalIgnoreCase);
            var activeDark = string.Equals(active.Name,
                Models.ColorScheme.CreateDark().Name, StringComparison.OrdinalIgnoreCase);
            // Активная схема — та же встроенная базовая тема: возвращаем её правки.
            if ((isLight && activeLight) || (!isLight && activeDark))
                return active.Clone();
        }

        // Пользовательская/чужая активная схема: берём встроенные цвета по умолчанию.
        var builtIn = Themes.ThemeManager.GetBuiltInScheme(theme) ?? Models.ColorScheme.CreateLight();
        return builtIn.Clone();
    }

    private static bool IsDarkTheme(string? theme)
        => string.Equals(theme, Themes.ThemeManager.DarkThemeName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Применяет цветовую схему, сохраняет её как активную и записывает настройки.
    /// Схема едина и несёт обе палитры; вариант темы она не меняет.
    /// </summary>
    public void ApplyColorScheme(ColorScheme scheme)
    {
        if (scheme is null)
            return;
        var clone = scheme.Clone();
        clone.Normalize();
        _activeColorScheme = clone;
        // Схема применяется по текущему варианту темы (палитра выбирается им).
        Themes.ThemeManager.ApplyScheme(clone);
        // Общий цвет папок хранится в схеме и применяется при построении дерева —
        // пересобираем его, чтобы папки сразу перекрасились.
        RebuildGroupTree();
        SaveSettings();
        LogTheme($"ApplyColorScheme('{clone.Name}', colors light={clone.LightColors.Count}, dark={clone.DarkColors.Count}) -> active='{_activeColorScheme.Name}'");
    }

    /// <summary>
    /// Сохраняет правки схемы как активную (единая схема с двумя палитрами),
    /// не меняя активную тему и не трогая интерфейс. Используется редактором цветов
    /// во вкладке «Цветовое оформление».
    /// </summary>
    public void SaveColorSchemeSlot(ColorScheme scheme)
    {
        if (scheme is null)
            return;
        var clone = scheme.Clone();
        clone.Normalize();
        _activeColorScheme = clone;
        SaveSettings();
        LogTheme($"SaveColorSchemeSlot('{clone.Name}') -> active='{_activeColorScheme?.Name}'");
    }

    /// <summary>Диагностика переключения/применения темы (пишет в лог и во временный файл).</summary>
    private void LogTheme(string message)
    {
        // issue #80 «Настройка логирования»: применение тем — направление CM_THEMES
        // (INFO пишется при включённых категории и общем выключателе CM_ON,
        // по умолчанию выключено; WARN/ERROR не фильтруются).
        try { Workspace1C.Services.Log.Info(Workspace1C.Services.LogCategory.Themes, "[theme-debug] " + message); } catch { /* не критично */ }
#if DEBUG
        try
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cm_theme_debug.log"),
                "[theme-debug] " + message + Environment.NewLine);
        }
        catch { /* не критично */ }
#endif
    }

    /// <summary>
    /// Применяет активную цветовую схему к интерфейсу (без сохранения настроек).
    /// Используется при запуске, чтобы применить сохранённую тему.
    /// </summary>
    public void ApplyActiveColorSchemeToUi()
    {
        Themes.ThemeManager.ApplyScheme(_activeColorScheme ?? Models.ColorScheme.CreateLight());
    }

    /// <summary>Список доступных схем: встроенные (Светлая/Тёмная) и пользовательские.</summary>
    public IReadOnlyList<Models.ColorScheme> AvailableColorSchemes()
    {
        return Themes.ThemeManager.EnumerateAllSchemes();
    }

    /// <summary>Сохраняет пользовательскую цветовую схему в каталог пользователя.</summary>
    public void SaveCustomColorScheme(Models.ColorScheme scheme)
    {
        Themes.ThemeManager.SaveCustomScheme(scheme);
    }

    /// <summary>Удаляет пользовательскую цветовую схему по имени.</summary>
    public bool DeleteCustomColorScheme(string name)
    {
        return Themes.ThemeManager.DeleteCustomScheme(name);
    }

    /// <summary>
    /// Применяет цветовую схему к интерфейсу для предпросмотра (без сохранения настроек).
    /// </summary>
    public void PreviewColorScheme(Models.ColorScheme scheme)
    {
        if (scheme is null)
            return;
        Themes.ThemeManager.ApplyScheme(scheme);
        // Общий цвет папок пересчитывается при построении дерева.
        RebuildGroupTree();
    }

    /// <summary>
    /// Применяет настройки шрифта интерфейса (семейство, размер, начертание, стиль),
    /// обновляет главное окно и сохраняет настройки.
    /// </summary>
    public void ApplyFontSettings(string fontFamily, double fontSize, string fontWeight, string fontStyle)
    {
        _fontFamily = string.IsNullOrWhiteSpace(fontFamily)
            ? Themes.ThemeManager.DefaultFontFamily : fontFamily;
        _fontSize = fontSize > 0 ? fontSize : Themes.ThemeManager.DefaultFontSize;
        _fontWeight = string.Equals(fontWeight, "Bold", StringComparison.OrdinalIgnoreCase)
            ? "Bold" : Themes.ThemeManager.DefaultFontWeight;
        _fontStyle = string.Equals(fontStyle, "Italic", StringComparison.OrdinalIgnoreCase)
            ? "Italic" : Themes.ThemeManager.DefaultFontStyle;

        Themes.ThemeManager.ApplyFontToAllWindows(_fontFamily, _fontSize, _fontWeight, _fontStyle);

        SaveSettings();
    }

    /// <summary>
    /// Применяет индивидуальные настройки шрифта областей к главному окну.
    /// Используется при запуске и для предпросмотра при «Применить».
    /// </summary>
    public void ApplyElementFonts()
    {
        var window = Application.Current?.MainWindow;
        if (window is not MainWindow mw)
            return;
        Themes.ThemeManager.ApplyElementFonts(mw, _elementFonts);
        // Размеры строк списка зависят и от области «Список баз»/«По умолчанию»,
        // уведомляем их привязки (issue #303).
        OnListFontSizeChanged();
    }

    // ---- Масштаб строк списка баз (issue #303) ----

    private ICommand? _zoomInCommand;
    private ICommand? _zoomOutCommand;
    private ICommand? _zoomResetCommand;
    private double _listZoomFactor = 1.0;
    private string _hotkeyZoomIn = "Ctrl+OemPlus";
    private string _hotkeyZoomOut = "Ctrl+OemMinus";
    private string _hotkeyZoomReset = "Ctrl+D0";

    /// <summary>
    /// Масштаб строк списка баз (issue #303): 1.0 — обычный размер, допустимо
    /// 0.5–3.0. Меняется Ctrl+колесом мыши над списком, горячими клавишами
    /// (как в редакторах: Ctrl+«+» / Ctrl+«-» / Ctrl+0) и сохраняется между запусками.
    /// </summary>
    public double ListZoomFactor
    {
        get => _listZoomFactor;
        set
        {
            var clamped = Math.Clamp(Math.Round(value * 10) / 10.0, 0.5, 3.0);
            if (SetProperty(ref _listZoomFactor, clamped))
            {
                OnListFontSizeChanged();
                ScheduleSaveSettings();
            }
        }
    }

    /// <summary>Горячая клавиша увеличения масштаба строк списка (issue #303).</summary>
    public string HotkeyZoomIn
    {
        get => _hotkeyZoomIn;
        set { if (SetProperty(ref _hotkeyZoomIn, NormalizeHotkey(value, "Ctrl+OemPlus"))) ScheduleSaveSettings(); }
    }

    /// <summary>Горячая клавиша уменьшения масштаба строк списка (issue #303).</summary>
    public string HotkeyZoomOut
    {
        get => _hotkeyZoomOut;
        set { if (SetProperty(ref _hotkeyZoomOut, NormalizeHotkey(value, "Ctrl+OemMinus"))) ScheduleSaveSettings(); }
    }

    /// <summary>Горячая клавиша сброса масштаба строк списка (issue #303).</summary>
    public string HotkeyZoomReset
    {
        get => _hotkeyZoomReset;
        set { if (SetProperty(ref _hotkeyZoomReset, NormalizeHotkey(value, "Ctrl+D0"))) ScheduleSaveSettings(); }
    }

    /// <summary>Команда увеличения масштаба строк списка (Ctrl++ / Ctrl+колесо, issue #303).</summary>
    public ICommand ZoomInCommand => _zoomInCommand ??= new RelayCommand(() => ListZoomFactor += 0.1);

    /// <summary>Команда уменьшения масштаба строк списка (Ctrl+-, issue #303).</summary>
    public ICommand ZoomOutCommand => _zoomOutCommand ??= new RelayCommand(() => ListZoomFactor -= 0.1);

    /// <summary>Команда сброса масштаба строк списка (Ctrl+0, issue #303).</summary>
    public ICommand ZoomResetCommand => _zoomResetCommand ??= new RelayCommand(() => ListZoomFactor = 1.0);

    /// <summary>Меняет масштаб строк списка на один шаг (Ctrl+колесо мыши, issue #303).</summary>
    public void ZoomListBy(double delta) => ListZoomFactor += delta;

    /// <summary>
    /// Итоговый коэффициент размеров строк списка: база области («Список баз»,
    /// иначе «По умолчанию», эталон 13) отнесённая к 13 и умноженная на масштаб
    /// Ctrl+колеса/хоткеев (issue #303).
    /// </summary>
    private double ListFontScale
    {
        get
        {
            double baseSize = 13.0;
            if (_elementFonts is not null)
            {
                if (_elementFonts.TryGetValue(Themes.ThemeManager.FontList, out var list) && list is { FontSize: > 0 })
                    baseSize = list.FontSize;
                else if (_elementFonts.TryGetValue(Themes.ThemeManager.FontDefault, out var def) && def is { FontSize: > 0 })
                    baseSize = def.FontSize;
            }
            return baseSize / 13.0 * _listZoomFactor;
        }
    }

    /// <summary>Размер названия базы в строке списка (эталон 13, issue #303).</summary>
    public double RowNameFontSize => 13.0 * ListFontScale * (_compactMode ? 0.9 : 1.0);

    /// <summary>Размер второстепенных ячеек строки (эталон 12, issue #303).</summary>
    public double RowSecondaryFontSize => 12.0 * ListFontScale * (_compactMode ? 0.9 : 1.0);

    /// <summary>Размер плашек-тегов строки (эталон 11, issue #303).</summary>
    public double RowBadgeFontSize => 11.0 * ListFontScale * (_compactMode ? 0.9 : 1.0);

    /// <summary>Размер номера слота Alt+N в строке (эталон 10, issue #303).</summary>
    public double RowHotkeyFontSize => 10.0 * ListFontScale * (_compactMode ? 0.9 : 1.0);

    /// <summary>Размер названия группы (15, в компактном режиме 12, issue #303).</summary>
    public double GroupNameFontSize => (_compactMode ? 12.0 : 15.0) * ListFontScale;

    /// <summary>Размер счётчика баз у группы (13, в компактном режиме 11, issue #303).</summary>
    public double GroupCountFontSize => (_compactMode ? 11.0 : 13.0) * ListFontScale;

    /// <summary>Уведомляет привязки размеров строк списка об изменении масштаба/шрифта.</summary>
    private void OnListFontSizeChanged()
    {
        OnPropertyChanged(nameof(RowNameFontSize));
        OnPropertyChanged(nameof(RowSecondaryFontSize));
        OnPropertyChanged(nameof(RowBadgeFontSize));
        OnPropertyChanged(nameof(RowHotkeyFontSize));
        OnPropertyChanged(nameof(GroupNameFontSize));
        OnPropertyChanged(nameof(GroupCountFontSize));
    }

    /// <summary>
    /// Сохраняет индивидуальные настройки шрифта областей, применяет их и записывает в настройки.
    /// </summary>
    public void SaveElementFonts(IDictionary<string, Models.ElementFontSettings> fonts)
    {
        _elementFonts = fonts is null
            ? new Dictionary<string, Models.ElementFontSettings>()
            : new Dictionary<string, Models.ElementFontSettings>(fonts);

        // «По умолчанию» также обновляет глобальные настройки шрифта (для всех окон и при запуске).
        if (_elementFonts.TryGetValue(Themes.ThemeManager.FontDefault, out var def) && def is not null && def.FontSize > 0)
        {
            _fontFamily = string.IsNullOrWhiteSpace(def.FontFamily) ? Themes.ThemeManager.DefaultFontFamily : def.FontFamily;
            _fontSize = def.FontSize;
            _fontWeight = string.Equals(def.FontWeight, "Bold", StringComparison.OrdinalIgnoreCase)
                ? "Bold" : Themes.ThemeManager.DefaultFontWeight;
            _fontStyle = string.Equals(def.FontStyle, "Italic", StringComparison.OrdinalIgnoreCase)
                ? "Italic" : Themes.ThemeManager.DefaultFontStyle;
        }

        Themes.ThemeManager.ApplyFontToAllWindows(_fontFamily, _fontSize, _fontWeight, _fontStyle);
        ApplyElementFonts();
        SaveSettings();
    }

    /// <summary>
    /// Применяет индивидуальные настройки шрифта областей для предпросмотра (без сохранения).
    /// </summary>
    public void PreviewElementFonts(IReadOnlyDictionary<string, Models.ElementFontSettings> fonts)
    {
        var window = Application.Current?.MainWindow;
        if (window is not MainWindow mw)
            return;
        Themes.ThemeManager.ApplyElementFonts(mw, fonts);
        // Предпросмотр тоже меняет размер строк списка (issue #303).
        OnListFontSizeChanged();
    }

    /// <summary>Выгружает схему в JSON-файл.</summary>
    public void ExportColorScheme(Models.ColorScheme scheme, string filePath)
    {
        Themes.ThemeManager.ExportScheme(scheme, filePath);
    }

    /// <summary>Загружает схему из JSON-файла.</summary>
    public Models.ColorScheme? ImportColorScheme(string filePath)
    {
        return Themes.ThemeManager.ImportScheme(filePath);
    }

    /// <summary>
    /// Возвращает true, если указанная группа свёрнута в списке баз.
    /// </summary>
    public bool IsGroupCollapsed(string groupName)
    {
        return _collapsedGroups.Contains(groupName);
    }

    /// <summary>
    /// Устанавливает состояние свёрнутости группы и сохраняет его в настройках.
    /// </summary>
    public void SetGroupCollapsed(string groupName, bool collapsed)
    {
        if (collapsed)
        {
            _collapsedGroups.Add(groupName);
        }
        else
        {
            _collapsedGroups.Remove(groupName);
        }
        MarkListStateDirty();
        SaveSettings();
    }

    /// <summary>
    /// Сворачивает/разворачивает группу по кнопке «свернуть»/«развернуть» на узле дерева.
    /// Принимает узел <see cref="GroupNodeViewModel"/> в качестве параметра команды.
    /// Явно переключает состояние развёрнутости узла и сохраняет результат в настройках,
    /// чтобы не зависеть от порядка срабатывания двухсторонней привязки.
    /// </summary>
    private void ToggleGroupExpanded(object? parameter)
    {
        if (parameter is not GroupNodeViewModel node)
            return;

        node.IsExpanded = !node.IsExpanded;
        // Для реальных групп ключом служит полный путь, для служебных узлов —
        // внутренний маркер (не зависит от языка), т.к. пути у них нет.
        var key = node.NodeKey;
        SetGroupCollapsed(key, !node.IsExpanded);
    }

    /// <summary>
    /// Сворачивает все группы в дереве баз.
    /// </summary>
    private void CollapseAllGroups(object? parameter)
    {
        SetExpandedDeep(_groupNodes, expanded: false);
        _collapsedGroups.Clear();
        CollectGroupPaths(_groupNodes, _collapsedGroups);
        MarkListStateDirty();
        ScheduleSaveSettings();

        // Контейнеры обновляются сами из OneWay-привязки IsExpanded (issue #160).
        // Layout нужен лишь для того, чтобы виртуализация завершила пересборку.
        if (Application.Current?.MainWindow is global::Workspace1C.MainWindow window)
            window.ApplyGroupExpandedState(expand: false);
    }

    /// <summary>
    /// Разворачивает все группы в дереве баз.
    /// </summary>
    private void ExpandAllGroups(object? parameter)
    {
        SetExpandedDeep(_groupNodes, expanded: true);
        _collapsedGroups.Clear();
        MarkListStateDirty();
        ScheduleSaveSettings();

        if (Application.Current?.MainWindow is global::Workspace1C.MainWindow window)
            window.ApplyGroupExpandedState(expand: true);
    }

    // ===================== Ветки дерева групп (issue #341) =====================

    /// <summary>
    /// Узел «под курсором» для команд ветки: выбранная группа, либо группа, в которой
    /// находится выбранная база. null — ветки нет.
    /// </summary>
    private GroupNodeViewModel? CurrentBranchNode()
    {
        if (SelectedGroupNode is { } group)
            return group;
        if (SelectedInfobase is { } ib && !string.IsNullOrWhiteSpace(ib.Group))
            return FindGroupNodeByPath(ib.Group);
        return null;
    }

    /// <summary>
    /// Разворачивает ветку: сам узел и рекурсивно всех его потомков. Соседние ветки
    /// не трогаются (issue #341).
    /// </summary>
    public void ExpandBranchOf(GroupNodeViewModel? node)
    {
        if (node is null)
            return;

        node.IsExpanded = true;
        SetExpandedDeep(node.Children, expanded: true);
        RecollectCollapsedGroups();
        MarkListStateDirty();
        ScheduleSaveSettings();

        if (Application.Current?.MainWindow is global::Workspace1C.MainWindow window)
            window.ApplyGroupExpandedState(expand: true);
    }

    /// <summary>
    /// Сворачивает ветку: сам узел и рекурсивно всех его потомков. Соседние ветки
    /// не трогаются (issue #341).
    /// </summary>
    public void CollapseBranchOf(GroupNodeViewModel? node)
    {
        if (node is null)
            return;

        node.IsExpanded = false;
        SetExpandedDeep(node.Children, expanded: false);
        RecollectCollapsedGroups();
        MarkListStateDirty();
        ScheduleSaveSettings();

        if (Application.Current?.MainWindow is global::Workspace1C.MainWindow window)
            window.ApplyGroupExpandedState(expand: false);
    }

    /// <summary>Переключает ветку: свёрнутая — разворачивается, развёрнутая — сворачивается.</summary>
    public void ToggleGroupBranch(GroupNodeViewModel? node)
    {
        if (node is null)
            return;
        if (node.IsExpanded)
            CollapseBranchOf(node);
        else
            ExpandBranchOf(node);
    }

    /// <summary>
    /// Пересобирает набор свёрнутых групп из текущего состояния дерева: используется
    /// после массовых операций над веткой, когда PropertyChanged-отслеживание не ведётся
    /// (WPF, в отличие от Avalonia, не подписан на IsExpanded узлов).
    /// </summary>
    private void RecollectCollapsedGroups()
    {
        _collapsedGroups.Clear();
        Walk(_groupNodes);

        void Walk(IEnumerable<GroupNodeViewModel> nodes)
        {
            foreach (var n in nodes)
            {
                if (!n.IsExpanded && !string.IsNullOrEmpty(n.NodeKey))
                    _collapsedGroups.Add(n.NodeKey!);
                Walk(n.Children);
            }
        }
    }

    /// <summary>
    /// Сортирует группы по имени (А→Я или Я→А): корневые группы и рекурсивно все подгруппы.
    /// Направление запоминается и применяется при последующих перестройках дерева.
    /// </summary>
    private void SortGroups(bool ascending)
    {
        _groupSortAscending = ascending;
        RebuildGroupTree();
    }

    /// <summary>
    /// Рекурсивно задаёт IsExpanded всем узлам через свойство (с PropertyChanged).
    /// Существующие контейнеры TreeView обновляются из OneWay-привязки автоматически,
    /// а виртуализация догенерирует контейнеры вновь развёрнутых веток.
    /// Прямая установка TreeViewItem.IsExpanded (local value) здесь намеренно НЕ
    /// используется: у local value выше приоритет, чем у OneWay-привязки из DataTrigger,
    /// поэтому после «развернуть/свернуть всё» мышь перестаёт сворачивать/разворачивать
    /// отдельные папки (issue #160). Уведомление же затрагивает только реализованные
    /// контейнеры, поэтому PropertyChanged-лавины (как при пересборке ItemsSource) нет.
    /// </summary>
    private static void SetExpandedDeep(IEnumerable<GroupNodeViewModel> nodes, bool expanded)
    {
        foreach (var node in nodes)
        {
            node.IsExpanded = expanded;
            SetExpandedDeep(node.Children, expanded);
        }
    }

    /// <summary>
    /// Рекурсивно устанавливает состояние развёрнутости всех узлов дерева групп.
    /// </summary>
    private static void CollapseAllNodes(IEnumerable<GroupNodeViewModel> nodes, bool collapse)
    {
        foreach (var node in nodes)
        {
            node.SetExpandedSilent(!collapse);
            CollapseAllNodes(node.Children, collapse);
        }
    }

    /// <summary>
    /// Рекурсивно собирает полные пути всех реальных групп в указанный набор.
    /// </summary>
    private static void CollectGroupPaths(IEnumerable<GroupNodeViewModel> nodes, HashSet<string> target)
    {
        foreach (var node in nodes)
        {
            // Для реальных групп ключом служит полный путь, для служебных узлов —
            // внутренний маркер (не зависит от языка; единый формат с ToggleGroupExpanded).
            var key = node.NodeKey;
            target.Add(key);
            CollectGroupPaths(node.Children, target);
        }
    }

    /// <summary>
    /// true, если база должна показываться в списках с учётом приватности (0.3.9.85):
    /// не приватная — всегда; приватная — только когда активный профиль разблокирован
    /// паролем (или у профиля нет пароля). Сбой получения сервиса (тестовый контекст)
    /// не скрывает базы — приватность там не задействована.
    /// </summary>
    private bool IsVisibleForPrivateFilter(Infobase infobase)
    {
        if (!infobase.IsPrivate)
            return true;
        try
        {
            return AppServices.GetRequiredService<Services.IProfileService>().CanShowPrivateBases;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Перебирает базы с учётом фильтров (приватность, избранное, поиск, тег)
    /// без ICollectionView.Refresh.
    /// </summary>
    private IEnumerable<Infobase> EnumerateFilteredInfobases()
    {
        var mode = _listViewMode;
        IEnumerable<Infobase> source = EnumerateSearchTagFilteredInfobases();
        if (mode == ListViewMode.Favorites)
            source = source.Where(i => i.IsFavorite);
        else if (mode == ListViewMode.Recent)
            source = source.Where(i => i.LastLaunchDate.HasValue)
                           .OrderByDescending(i => i.LastLaunchDate);
        else if (mode == ListViewMode.Running)
            // Отбор «Только запущенные» (issue #339): флаги обновляет монитор
            // процессов 1С (MainViewModel.Running).
            source = source.Where(i => i.IsRunning);
        return source;
    }

    /// <summary>
    /// Перебирает базы с учётом приватности, поиска и тегов (БЕЗ режима списка):
    /// общая основа для <see cref="EnumerateFilteredInfobases"/> и провайдеров кэша
    /// «Избранное»/«Недавние» (issue #21).
    /// </summary>
    private IEnumerable<Infobase> EnumerateSearchTagFilteredInfobases()
    {
        var search = SearchText?.Trim() ?? string.Empty;
        var hasSearch = search.Length > 0;
        var hasTags = _activeTagFilterSet.Count > 0;

        foreach (var infobase in Infobases.Where(IsVisibleForPrivateFilter))
        {
            // Несколько тегов: база подходит, если содержит все выбранные (AND),
            // сравнение без учёта регистра (синхронизировано с Avalonia).
            if (hasTags
                && !_activeTagFilterSet.All(t =>
                    infobase.Tags.Any(bt => string.Equals(bt, t, StringComparison.OrdinalIgnoreCase))))
                continue;

            // Поиск по имени/описанию/пути/серверу работает вместе с тегами (AND).
            if (hasSearch)
            {
                if (!(infobase.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                      || (infobase.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                      || (infobase.Group?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                      || (infobase.PlatformVersion?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                      || (infobase.ServerDatabaseDisplay?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                      || (infobase.ConnectionStringDisplay?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                      || infobase.Tags.Any(t => t.Contains(search, StringComparison.OrdinalIgnoreCase))))
                    continue;
            }

            yield return infobase;
        }
    }

    /// <summary>
    /// Возвращает видимые базы для текущего режима списка. Для «Избранное»/«Недавние»
    /// берётся готовый кэш (не пересчитывается при переключении вкладок без изменения
    /// данных, issue #21); для остальных режимов — фильтрация+сортировка на лету.
    /// </summary>
    private IReadOnlyList<Infobase> GetVisibleForCurrentMode()
    {
        switch (_listViewMode)
        {
            case ListViewMode.Favorites:
                return _listFilter.GetFavorites();
            case ListViewMode.Recent:
                return _listFilter.GetRecent();
            default:
                // «Все» и «Только запущенные»: режимный фильтр внутри EnumerateFilteredInfobases.
                return ApplyCurrentSort(EnumerateFilteredInfobases()).ToList();
        }
    }

    /// <summary>
    /// Перестраивает дерево групп на основе текущего списка групп и баз.
    /// Закреплённые базы попадают в корневой узел «Закреплённые», базы без группы — в «Без группы».
    /// Группы и подгруппы, не содержащие баз (в том числе при активном фильтре «Только избранные»),
    /// в дерево не попадают.
    /// </summary>
    /// <summary>
    /// Обработчик смены языка интерфейса (Windows): пересчитывает все привязки к VM
    /// и пересобирает дерево групп, чтобы локализованные свойства и служебные узлы
    /// («Все базы», «Без группы», «Избранное») обновились без перезапуска.
    /// Работает для любого направления (ru ↔ en и внешние языки).
    /// </summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // Событие поднимается на UI-потоке (SettingsWindow), но добавляем страховку:
        // если вызов пришёл с другого потока — маршализуем на диспетчер приложения.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            HandleLanguageChanged();
            return;
        }

        dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.DataBind,
            new Action(HandleLanguageChanged));
    }

    /// <summary>
    /// Непосредственно применяет смену языка: уведомляет об изменении всех свойств VM
    /// (WPF пересчитает StatusBarInfo, ExportIndicatorTooltip, RightPanelToggleTooltip,
    /// GroupByGroupText, SyncMessage и пр.) и пересобирает дерево групп.
    /// </summary>
    private void HandleLanguageChanged()
    {
        // Пустое имя свойства означает «изменились все свойства» — WPF пересчитает
        // все активные привязки к VM.
        OnPropertyChanged(string.Empty);
        // Дерево перестраиваем целиком: узлы со спецмаркерами возвращают
        // LocalizationManager.T(...) на лету (см. GroupNodeViewModel.DisplayName).
        RebuildGroupTree();
    }

    /// <summary>
    /// Отписывается от события смены языка. Вызывается при полном закрытии главного
    /// окна (MainWindow.OnClosing), чтобы избежать утечек и дублирования подписки.
    /// </summary>
    public void UnsubscribeLanguageChanged()
    {
        if (!_languageChangedSubscribed)
            return;
        LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
        _languageChangedSubscribed = false;
    }

    public void RebuildGroupTree()
    {
        // Любой вызов пересборки, кроме чистого переключения вкладки (ListViewMode setter
        // идёт через RebuildGroupTreeCore), означает изменение данных/фильтров — кэш
        // «Избранное»/«Недавние» И кэш дерева «Все» сбрасываем (issue #21).
        _listFilter.Invalidate();
        // Узлы кэша «Все» уничтожаются пересборкой (issue #21, эксперимент A 1.0.0.34):
        // отписываем их от собственных коллекций Infobases/Children, чтобы удаляемые
        // узлы не удерживались обработчиками. Постоянный _flatNode в кэше не участвует.
        if (_groupedTreeCacheValid && _groupedTreeCache is { } oldCache)
        {
            foreach (var root in oldCache)
                root.Detach();
        }
        _groupedTreeCacheValid = false;
        RebuildGroupTreeCore();
    }

    /// <summary>
    /// Пересобирает дерево БЕЗ сброса кэша списков «Избранное»/«Недавние»: используется
    /// при чистом переключении вкладок, когда данные не менялись (issue #21).
    /// </summary>
    private void RebuildGroupTreeCore()
    {
        // События подняты здесь, а не в ReplaceGroupNodes (issue #21, Этап F): стабильный
        // набор корней не меняет состав на переключении вкладки, но диагностике
        // CM_TREESWITCH и окну нужны события смены вкладки (замер vmMs/allocMb, запоминание
        // прокрутки).
        TreeRebuilding?.Invoke();
        try
        {
            // Один проход по базам без CollectionView.Refresh (он дорогой на больших списках).
            // Учитываем выбранное поле сортировки (_sortField / _sortAscending).
            var visible = GetVisibleForCurrentMode();
            var flatMode = !_groupByGroup || IsFilterModeActive();

            // Когда группировка отключена или активен временный фильтр
            // (Избранное / Недавние / отбор по тегу / поиск) — показываем плоский список
            // в одном узле без групп и закреплений. Так избегаем дублей: закреплённая база
            // иначе попадает и в «Закреплённые», и в свою группу, а при фильтрации
            // группировка по группам теряет смысл.
            if (flatMode)
            {
                var filterActive = IsFilterModeActive();
                // Постоянный плоский узел (issue #21, Этап C): переиспользуем один узел и
                // обновляем только его содержимое (маркер/имя + Items одним Reset) — контейнер
                // корневого узла в TreeView не пересоздаётся при каждом переключении вкладки.
                _flatNode ??= new GroupNodeViewModel(null);
                _flatNode.ResetFlatContent(
                    visible,
                    displayName: filterActive ? LocalizationManager.T("Main.FlatFound") : null,
                    marker: filterActive ? null : GroupNodeViewModel.AllBasesMarker);
                _flatNode.IsExpanded = true;
            }
            else if (!(_groupedTreeCacheValid && _groupedTreeCache is not null))
            {
                // Кэш группированного дерева режима «Все» (issue #21, Этап C): при чистом
                // переключении вкладок узлы дерева переиспользуются, а не пересоздаются
                // тысячи объектов GroupNodeViewModel и обёрток PinnedInfobaseItem (источник
                // роста CPU и памяти с каждым переключением). Кэш инвалидируется в
                // RebuildGroupTree при любом изменении данных/фильтров.
                BuildGroupedTree(visible);
            }
            else
            {
                // Состояние развёрнутости может отличаться от момента кэширования
                // (раскрытие/сворачивание на вкладках) — применяем актуальное.
                ApplyExpandedState(_groupedTreeCache);
            }

            // Стабильный набор корней (issue #21, Этап F): GroupNodes всегда содержит
            // плоский узел и все корни кэша «Все»; переключение вкладки меняет только
            // видимость узлов, а не состав коллекции (нет Reset GroupNodes, collReset=0
            // в CM_TREESWITCH).
            ApplyStableRoots(flatMode);

            // Узлы групп пересоздаются при пересборке: ссылка SelectedGroupNode иначе
            // осталась бы на старый (выброшенный) узел, и после правки настроек группы её
            // выделение не восстановилось бы. Перепривязываем выбранную группу по её
            // идентификатору модели.
            var selectedGroupId = SelectedGroupNode?.Group?.Id;
            if (!string.IsNullOrEmpty(selectedGroupId)
                && _groupedTreeCache is not null
                && FindGroupNodeById(_groupedTreeCache, selectedGroupId) is { } remapped)
                SelectedGroupNode = remapped;

            // Выбранный узел мог стать скрытым (плоский режим, пустая группа): снимаем
            // выбор, иначе правая панель продолжила бы показывать группу, которой в дереве нет.
            if (SelectedGroupNode is { } selected && !selected.IsVisible)
                SelectedGroupNode = null;

            // Панель тегов обновляем только если набор тегов мог измениться
            // (не на каждый символ поиска — там уже есть ранний выход, но лишний проход лишний).
            if (!flatMode)
                RefreshTagFilterItems();
        }
        finally
        {
            TreeRebuilt?.Invoke();
        }
    }

    /// <summary>
    /// Строит группированное дерево режима «Все» и заполняет кэш (issue #21, Этап C).
    /// Узлы переиспользуются при чистом переключении вкладок, а не пересоздаются.
    /// </summary>
    private void BuildGroupedTree(IReadOnlyList<Infobase> visible)
    {
        var roots = GroupNodeViewModel.BuildTree(Groups);

        // Сортируем корневые группы и все подгруппы по имени согласно сохранённому направлению.
        var groupComparer = StringComparer.OrdinalIgnoreCase;
        roots.Sort(_groupSortAscending
            ? (a, b) => groupComparer.Compare(a.DisplayName, b.DisplayName)
            : (a, b) => groupComparer.Compare(b.DisplayName, a.DisplayName));
        foreach (var root in roots)
            root.SortChildrenRecursive(_groupSortAscending);

        var pinnedNode = new GroupNodeViewModel(
            null,
            marker: GroupNodeViewModel.PinnedMarker,
            defaultColor: _pinnedColor,
            defaultIconColor: _pinnedIconColor,
            defaultIcon: _pinnedIcon);
        var noGroupNode = new GroupNodeViewModel(
            null,
            marker: GroupNodeViewModel.NoGroupMarker,
            defaultColor: _noGroupColor,
            defaultIconColor: _noGroupIconColor,
            defaultIcon: _noGroupIcon);

        // Индексация по каноническому пути (GetFullPath) и по FullPath узла —
        // после DnD группы пути в памяти и в UI должны совпасть сразу, без перезапуска.
        var pathToNode = new Dictionary<string, GroupNodeViewModel>(StringComparer.OrdinalIgnoreCase);
        void IndexNode(GroupNodeViewModel node)
        {
            if (node.Group is not null)
            {
                // FullPath уже кэшируется в узле — не вызываем GetFullPath по всему списку групп.
                var path = node.FullPath;
                if (!string.IsNullOrEmpty(path))
                {
                    pathToNode[path] = node;
                    var normalized = NormalizeGroupPath(path);
                    if (!string.IsNullOrEmpty(normalized) &&
                        !string.Equals(normalized, path, StringComparison.OrdinalIgnoreCase))
                        pathToNode[normalized] = node;
                }
            }
            foreach (var child in node.Children)
                IndexNode(child);
        }
        foreach (var root in roots)
            IndexNode(root);

        // Без NotifyCountChanged на каждое добавление базы (лавина PropertyChanged).
        foreach (var root in roots)
            root.SetNotificationsSuppressed(true);
        pinnedNode.SetNotificationsSuppressed(true);
        noGroupNode.SetNotificationsSuppressed(true);

        foreach (var infobase in visible)
        {
            if (infobase.IsPinned)
                pinnedNode.Infobases.Add(infobase);

            var groupPath = infobase.Group?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(groupPath))
            {
                noGroupNode.Infobases.Add(infobase);
                continue;
            }

            if (pathToNode.TryGetValue(groupPath, out var node)
                || pathToNode.TryGetValue(NormalizeGroupPath(groupPath), out node))
            {
                node.Infobases.Add(infobase);
                continue;
            }

            noGroupNode.Infobases.Add(infobase);
        }

        foreach (var root in roots)
            root.PopulateItems(_showEmptyGroups);
        pinnedNode.PopulateItems(_showEmptyGroups);
        noGroupNode.PopulateItems(_showEmptyGroups);

        foreach (var root in roots)
            root.SetNotificationsSuppressed(false);
        pinnedNode.SetNotificationsSuppressed(false);
        noGroupNode.SetNotificationsSuppressed(false);

        var next = new List<GroupNodeViewModel>();
        if (pinnedNode.ContainsInfobases)
            next.Add(pinnedNode);
        if (noGroupNode.ContainsInfobases)
            next.Add(noGroupNode);
        foreach (var root in roots)
        {
            if (_showEmptyGroups || root.ContainsInfobases)
                next.Add(root);
        }

        _groupNodes = next;
        // Заполняем кэш дерева «Все» (issue #21, Этап C): валидным он становится только
        // после успешного построения.
        _groupedTreeCache = next;
        _groupedTreeCacheValid = true;

        // Expand/collapse до Replace — один проход построения TreeView.
        if (ShouldAutoExpandGroups())
            ExpandAllNodesWithContent(next);
        else
            ApplyExpandedState(next);
    }

    /// <summary>
    /// Рекурсивно ищет узел группы по идентификатору модели в новом дереве.
    /// Нужен для восстановления выбранной группы после пересборки, когда узлы
    /// GroupNodeViewModel пересоздаются, а модель группы остаётся той же.
    /// Спец-узлы («Без группы», «Закреплённые») имеют Group == null и пропускаются.
    /// </summary>
    private static GroupNodeViewModel? FindGroupNodeById(
        IEnumerable<GroupNodeViewModel> roots, string groupId)
    {
        // Скрытые корни (issue #21, Этап F) остаются в GroupNodes, но не отображаются:
        // восстановление выделения ищет только среди видимых узлов.
        foreach (var root in roots)
        {
            if (!root.IsVisible)
                continue;
            if (root.Group is not null
                && string.Equals(root.Group.Id, groupId, StringComparison.OrdinalIgnoreCase))
                return root;
            if (root.Children.Count > 0 && FindGroupNodeById(root.Children, groupId) is { } found)
                return found;
        }
        return null;
    }
}
#endif
