#if LINUX
using Avalonia.Threading;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Themes;

namespace Workspace1C.ViewModels;

/// <summary>Main ViewModel (Avalonia/Linux): тема и цветовые схемы (partial).</summary>
public partial class MainViewModel : ViewModelBase
{
    public string ThemeName
    {
        get => _themeName;
        set => SetProperty(ref _themeName, value);
    }

    /// <summary>
    /// Запоминает выбранную цветовую схему как активную и сохраняет её в слот
    /// соответствующей базовой темы (светлой/тёмной), чтобы переключение тем
    /// не сбрасывало настроенное оформление.
    /// </summary>
    public void ApplyColorScheme(Models.ColorScheme scheme)
    {
        if (scheme is null)
            return;
        var clone = scheme.Clone();
        clone.Normalize();
        _settings.ActiveColorScheme = clone;
        // Устаревшие раздельные слоты больше не ведутся: схема едина и несёт обе палитры.
        _settings.LightColorScheme = null;
        _settings.DarkColorScheme = null;
        // Применяем палитру по текущему варианту темы; сам вариант не меняем.
        ThemeManager.ApplyScheme(clone);
        // Общий цвет папок хранится в схеме и применяется при построении дерева —
        // пересобираем его, чтобы папки сразу перекрасились.
        RebuildTree();
        SaveSettingsSilently();
        OnPropertyChanged(nameof(ThemeName));
    }

    /// <summary>true, если имя соответствует встроенной теме («Светлая»/«Тёмная»).</summary>
    private static bool IsBuiltInSchemeName(string? name)
        => string.Equals(name, "Светлая", StringComparison.OrdinalIgnoreCase)
           || string.Equals(name, "Тёмная", StringComparison.OrdinalIgnoreCase);

    /// <summary>Сохранённая цветовая схема (две палитры): окно настроек открывает редактор
    /// с неё, а не с той, что применена предпросмотром.</summary>
    public Models.ColorScheme ActiveColorScheme
    {
        get
        {
            var saved = _settings.ActiveColorScheme;
            if (saved is not null && (saved.LightColors.Count > 0 || saved.DarkColors.Count > 0))
            {
                saved.Normalize();
                return saved;
            }
            return ThemeManager.GetBuiltInScheme(_settings.Theme) ?? Models.ColorScheme.CreateLight();
        }
    }

    private void ToggleTheme()
    {
        // Схема одна (несёт обе палитры): переключение темы лишь выбирает палитру.
        ApplySchemeForTheme(ThemeManager.CurrentTheme != ThemeManager.DarkThemeName);
    }

    // ---- Синхронизация темы с режимом ОС (issue #108) ----
    // Опция действует на Windows; на Linux OsThemeWatcher — заглушка (режим приложений
    // рабочего стола не определяется), галочка сохраняется, но не влияет на тему.

    /// <summary>
    /// «Тема приложения следует за режимом ОС»: при смене тёмного/светлого режима
    /// приложений в ОС переключать базовый вариант темы. Ручная смена остаётся.
    /// </summary>
    public bool ThemeFollowsOs
    {
        get => _settings.ThemeFollowsOs;
        set
        {
            if (_settings.ThemeFollowsOs == value)
                return;
            _settings.ThemeFollowsOs = value;
            OnPropertyChanged(nameof(ThemeFollowsOs));
            UpdateOsThemeWatcher();
            SaveSettingsSilently();
        }
    }

    /// <summary>
    /// Включает/выключает наблюдатель режима ОС в соответствии с опцией. Повторные
    /// вызовы безопасны: двойная подписка исключена.
    /// </summary>
    private void UpdateOsThemeWatcher()
    {
        Services.OsThemeWatcher.AppsThemeChanged -= OnOsAppsThemeChanged;
        if (_settings.ThemeFollowsOs)
        {
            Services.OsThemeWatcher.Start();
            Services.OsThemeWatcher.AppsThemeChanged += OnOsAppsThemeChanged;
        }
        else
        {
            Services.OsThemeWatcher.Stop();
        }
    }

    /// <summary>Обработчик смены режима приложений ОС: переключает вариант темы.</summary>
    private void OnOsAppsThemeChanged(bool dark)
    {
        // Тяжёлое переключение темы — через диспетчер UI-потока. На Linux событие
        // не приходит (OsThemeWatcher — заглушка), обработчик оставлен для симметрии.
        Dispatcher.UIThread.Post(() =>
        {
            if ((ThemeManager.CurrentTheme == ThemeManager.DarkThemeName) == dark)
                return;
            ApplySchemeForTheme(dark);
        });
    }

    /// <summary>Применяет выбранный вариант темы (светлую/тёмную), не меняя схему.</summary>
    public void ApplyTheme(string theme)
    {
        ApplySchemeForTheme(theme == ThemeManager.DarkThemeName);
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
        ThemeManager.ApplyFullTheme(id);
        var theme = ThemeManager.CurrentFullTheme;
        if (persist)
        {
            _settings.FullTheme = theme?.Id ?? _settings.FullTheme;
            _settings.Theme = ThemeManager.CurrentTheme;
            _themeName = _settings.Theme;
        }
        // Активная схема перезаписывается схемой темы ТОЛЬКО при подтверждённом выборе
        // (persist:true). В режиме предпросмотра (persist:false, issue #378) её трогать
        // нельзя: иначе правки встроенной темы затирались ещё до «ОК» (issue #26).
        if (persist && theme is not null && theme.Scheme is not null)
        {
            var clone = theme.Scheme.Clone();
            clone.Normalize();
            _settings.ActiveColorScheme = clone;
            // Устаревшие раздельные слоты больше не ведутся: схема едина.
            _settings.LightColorScheme = null;
            _settings.DarkColorScheme = null;
        }
        // Общий цвет папок берётся из активной палитры схемы при построении дерева.
        RebuildTree();
        if (persist)
            SaveSettingsSilently();
        OnPropertyChanged(nameof(ThemeName));
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
        var current = ThemeManager.CurrentFullTheme?.Id ?? "light";
        _themePreview.Begin(current, _settings.FullTheme);
    }

    /// <summary>
    /// Переводит тему в предпросмотр: применяет её без сохранения в настройках.
    /// Редактор палитры и предпросмотр показывают цвета выбранной темы.
    /// </summary>
    public void PreviewFullTheme(string id)
    {
        var resolved = _themePreview.Preview(id);
        ApplyFullTheme(resolved, persist: false);
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
        var theme = ThemeManager.CurrentFullTheme;
        _settings.FullTheme = theme?.Id ?? id;
        _settings.Theme = ThemeManager.CurrentTheme;
        _themeName = _settings.Theme;
        SaveSettingsSilently();
        OnPropertyChanged(nameof(ThemeName));
    }

    /// <summary>
    /// Переопределяет набор иконок и стиль кнопок окна активной темы (блок «Темы»,
    /// 0.3.12.62). Пустое значение — «по теме». Цветовая схема не меняется; выбор
    /// применяется сразу и сохраняется в настройках.
    /// </summary>
    public void ApplyThemeOverrides(string? iconSet, string? buttonStyle)
    {
        ThemeManager.SetOverrides(iconSet, buttonStyle);
        ThemeManager.ReapplyThemeFeatures();
        _settings.IconSetOverride = ThemeManager.IconSetOverride is { } iconSetOverride
            ? ThemeFeatureMap.IconSetId(iconSetOverride)
            : string.Empty;
        _settings.WindowButtonStyleOverride = ThemeManager.WindowButtonStyleOverride is { } buttonStyleOverride
            ? ThemeFeatureMap.ButtonStyleId(buttonStyleOverride)
            : string.Empty;
        SaveSettingsSilently();
    }

    /// <summary>
    /// Возвращает активную схему (с двумя палитрами). Вариант темы определяет,
    /// какая палитра показывается; сама схема от него не зависит.
    /// </summary>
    public Models.ColorScheme GetSchemeForTheme(string theme)
        => ActiveColorScheme.Clone();

    /// <summary>
    /// Задаёт вариант темы (светлую/тёмную) и применяет активную схему с палитрой
    /// этого варианта.
    /// </summary>
    private void ApplySchemeForTheme(bool dark)
    {
        ThemeManager.ApplyTheme(dark);
        // Общий цвет папок берётся из активной палитры схемы при построении дерева.
        RebuildTree();
        _settings.Theme = dark ? ThemeManager.DarkThemeName : ThemeManager.LightThemeName;
        _themeName = _settings.Theme;
        // Переключение базового варианта не меняет полноценную тему (её Id остаётся),
        // но выбор должен сохраниться в настройках.
        _settings.FullTheme = ThemeManager.CurrentFullTheme?.Id ?? _settings.FullTheme;
        SaveSettingsSilently();
        OnPropertyChanged(nameof(ThemeName));
    }

    private static bool IsDarkTheme(string? theme)
        => string.Equals(theme, ThemeManager.DarkThemeName, StringComparison.OrdinalIgnoreCase);
}
#endif