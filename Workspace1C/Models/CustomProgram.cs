using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Workspace1C.Models;

/// <summary>
/// Пользовательская программа группы «Мои программы» под кнопками запуска (issue #91):
/// произвольный исполняемый файл с аргументами, рабочей папкой и иконкой из файла.
/// Хранится в JSON-файле <c>custom_programs.json</c> (см.
/// <see cref="Workspace1C.Services.CustomProgramsStore"/>), порядок задаётся полем
/// <see cref="Order"/>. Реализует <see cref="INotifyPropertyChanged"/> — редактор
/// обновляет поля без пересоздания списка.
/// </summary>
public class CustomProgram : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = "";
    private string _filePath = "";
    private string _arguments = "";
    private string _workingDirectory = "";
    private string _iconPath = "";
    private bool _useCustomIcon;
    private bool _useCustomIconExplicit;
    private bool _enabled = true;
    private int _order;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Идентификатор программы (GUID); при повреждённом файле восстанавливается.</summary>
    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    /// <summary>Отображаемое имя кнопки на панели запуска.</summary>
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>Путь к исполняемому файлу (полный или относительный).</summary>
    public string FilePath
    {
        get => _filePath;
        set => SetProperty(ref _filePath, value);
    }

    /// <summary>Аргументы запуска — передаются процессу как есть (без разбора).</summary>
    public string Arguments
    {
        get => _arguments;
        set => SetProperty(ref _arguments, value);
    }

    /// <summary>Рабочая папка процесса (опционально); пусто — каталог исполняемого файла.</summary>
    public string WorkingDirectory
    {
        get => _workingDirectory;
        set => SetProperty(ref _workingDirectory, value);
    }

    /// <summary>
    /// Путь к иконке из файла (<c>.ico</c>/<c>.png</c>). Используется только при
    /// включённой галочке <see cref="UseCustomIcon"/>; иначе показывается основная
    /// иконка исполняемого файла (issue #91).
    /// </summary>
    public string IconPath
    {
        get => _iconPath;
        set
        {
            var wasUsingCustomIcon = UseCustomIcon;
            if (!SetProperty(ref _iconPath, value))
                return;
            // Пока флаг не задан явно, UseCustomIcon выводится из пути: при
            // появлении/очистке пути меняется и отображаемая иконка.
            if (wasUsingCustomIcon != UseCustomIcon)
                OnPropertyChanged(nameof(UseCustomIcon));
        }
    }

    /// <summary>
    /// Использовать иконку из файла <see cref="IconPath"/> (галочка «Иконка из файла»,
    /// issue #91). Если галочка снята — показывается основная иконка исполняемого файла
    /// (как в проводнике). Обратная совместимость: в старых записях
    /// <c>custom_programs.json</c> поля не было, и заполненный <see cref="IconPath"/>
    /// считался включённым — пока значение не задано явно, флаг выводится из пути.
    /// </summary>
    public bool UseCustomIcon
    {
        get => _useCustomIconExplicit ? _useCustomIcon : !string.IsNullOrWhiteSpace(_iconPath);
        set
        {
            var wasUsingCustomIcon = UseCustomIcon;
            _useCustomIconExplicit = true;
            _useCustomIcon = value;
            if (wasUsingCustomIcon != value)
                OnPropertyChanged();
        }
    }

    /// <summary>
    /// Запускать программу от имени администратора (issue #178). На Windows:
    /// без <see cref="RunAsUserName"/> — через UAC-запрос (Verb = "runas"),
    /// с указанной учётной записью — через <c>runas /savecred</c> (пароль
    /// запрашивается один раз и сохраняется в диспетчере учётных данных Windows).
    /// На других платформах флаг игнорируется.
    /// </summary>
    public bool RunAsAdministrator { get; set; }

    /// <summary>
    /// Учётная запись для запуска от администратора (issue #178), например
    /// <c>Administrator</c> или <c>DOMAIN\admin</c>. Пусто — обычный UAC-запрос
    /// от текущего пользователя. Пароль в приложении не хранится: его сохраняет
    /// сама Windows при первом запуске через <c>runas /savecred</c>.
    /// </summary>
    public string RunAsUserName { get; set; } = "";

    /// <summary>Показывать программу на панели запуска.</summary>
    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    /// <summary>Порядок в группе (меньше — выше); пересчитывается при перемещении.</summary>
    public int Order
    {
        get => _order;
        set => SetProperty(ref _order, value);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

/// <summary>Режим отображения группы «Мои программы» на панели запуска (issue #91).</summary>
public enum CustomProgramsViewMode
{
    /// <summary>Вертикальный список кнопок «иконка + подпись» (как родной стартер 1С).</summary>
    List,

    /// <summary>Кнопки-иконки в строку с переносом, подсказка — имя программы.</summary>
    Icons
}

/// <summary>
/// Канонические строковые значения режима отображения группы «Мои программы» (issue #91)
/// и вспомогательные методы разбора. Строки стабильны и не зависят от локали — они
/// хранятся в <c>settings.json</c> (<see cref="AppSettings.CustomProgramsViewMode"/>),
/// поэтому смена языка интерфейса не ломает сохранённый режим.
/// </summary>
public static class CustomProgramsViewModes
{
    /// <summary>Каноническое значение режима «Списком».</summary>
    public const string List = "List";

    /// <summary>Каноническое значение режима «Иконками».</summary>
    public const string Icons = "Icons";

    /// <summary>Режим по умолчанию — «Списком» (как родной стартер 1С).</summary>
    public const string Default = List;

    /// <summary>
    /// Приводит произвольное значение к каноническому режиму. Пустые и неизвестные
    /// значения (в том числе старые профили без поля) возвращают
    /// <see cref="Default"/> («Списком»).
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Default;
        var v = value.Trim();
        if (string.Equals(v, Icons, StringComparison.OrdinalIgnoreCase))
            return Icons;
        return List;
    }

    /// <summary>Разбирает строковое значение в <see cref="CustomProgramsViewMode"/>.</summary>
    public static CustomProgramsViewMode Parse(string? value)
        => Normalize(value) == Icons ? CustomProgramsViewMode.Icons : CustomProgramsViewMode.List;

    /// <summary>Строковое представление режима для сохранения в настройках.</summary>
    public static string ToSettingString(CustomProgramsViewMode mode)
        => mode == CustomProgramsViewMode.Icons ? Icons : List;
}
