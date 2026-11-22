using System.ComponentModel;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Строка «Инспектора процессов» (0.3.9.93): запущенный процесс платформы 1С
/// с распознанным режимом, пользователем, временем старта, PID и краткой строкой
/// подключения. Если командную строку процесса удалось сопоставить с базой списка
/// (<see cref="RunningInfobaseMatcher"/>), строка показывает имя базы и позволяет
/// перейти к ней в главном окне. Чистый .NET без платформенных зависимостей —
/// подключается в Linux-сборку явно.
/// <para>
/// issue #134 (1.0.1.102): строка умеет обновляться «на месте»
/// (<see cref="UpdateFrom"/>) и уведомляет об изменениях. Инкрементальное
/// автообновление переиспользует тот же экземпляр, поэтому контейнеры таблицы
/// не пересоздаются и клавиатурный фокус/текущая строка не теряются.
/// </para>
/// </summary>
public sealed class ProcessRowViewModel : INotifyPropertyChanged
{
    private RunningOneCProcessDetails _details;
    private string _connectionText;

    /// <param name="details">Подробности процесса из <see cref="IRunningInfobasesService.GetRunningDetails"/>.</param>
    /// <param name="infobase">Сопоставленная база списка; null — процесс не связан с известной базой.</param>
    public ProcessRowViewModel(RunningOneCProcessDetails details, Infobase? infobase)
    {
        _details = details ?? throw new ArgumentNullException(nameof(details));
        Infobase = infobase;
        _connectionText = ProcessCommandLineParser.ExtractConnectionString(details.CommandLine) ?? "—";
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Подробности процесса, по которым построена строка.</summary>
    public RunningOneCProcessDetails Details => _details;

    /// <summary>
    /// Строка описывает тот же запуск процесса (PID + время старта + командная
    /// строка). Используется инкрементальным обновлением: совпадение ключа —
    /// повод переиспользовать экземпляр и не пересоздавать контейнер таблицы.
    /// </summary>
    public bool HasSameIdentity(RunningOneCProcessDetails details) =>
        details is not null &&
        details.Pid == _details.Pid &&
        string.Equals(details.CommandLine ?? string.Empty, _details.CommandLine ?? string.Empty,
            StringComparison.Ordinal) &&
        string.Equals(details.StartTimeToken ?? string.Empty, _details.StartTimeToken ?? string.Empty,
            StringComparison.Ordinal);

    /// <summary>
    /// Обновляет строку данными нового опроса, сохраняя экземпляр (issue #134,
    /// 1.0.1.102). Так автообновление не «выдёргивает» таблицу из-под пользователя:
    /// фокус, текущая строка и выделение остаются на месте.
    /// </summary>
    public void UpdateFrom(RunningOneCProcessDetails details, Infobase? infobase)
    {
        if (details is null)
            return;

        _details = details;
        Infobase = infobase;
        _connectionText = ProcessCommandLineParser.ExtractConnectionString(details.CommandLine) ?? "—";

        // Все свойства вычисляемые от _details/Infobase — уведомляем обо всех.
        foreach (var name in ChangedProperties)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Свойства строки, зависящие от подробностей процесса и сопоставленной базы.</summary>
    private static readonly string[] ChangedProperties =
    {
        nameof(Details),
        nameof(Pid),
        nameof(ProcessName),
        nameof(FullCommandLine),
        nameof(StartTimeToken),
        nameof(Infobase),
        nameof(IsKnown),
        nameof(BaseName),
        nameof(Subtitle),
        nameof(ModeText),
        nameof(UserName),
        nameof(StartTimeText),
        nameof(ConnectionText),
        nameof(KnownSymbol),
        nameof(CanOpenBase)
    };

    /// <summary>PID процесса.</summary>
    public int Pid => _details.Pid;

    /// <summary>Имя процесса (например «1cv8c.exe»).</summary>
    public string ProcessName => _details.ProcessName;

    /// <summary>Полная командная строка (подсказка в окне).</summary>
    public string FullCommandLine => _details.CommandLine;

    /// <summary>Токен времени старта для сверки при завершении (Linux); пусто на Windows.</summary>
    public string StartTimeToken => _details.StartTimeToken ?? string.Empty;

    /// <summary>База списка, с которой сопоставлен процесс; null — неизвестная база.</summary>
    public Infobase? Infobase { get; private set; }

    /// <summary>Процесс сопоставлен с известной базой (значок-флаг в таблице).</summary>
    public bool IsKnown => Infobase is not null;

    /// <summary>Колонка «База»: имя базы либо «Неизвестная база».</summary>
    public string BaseName => IsKnown
        ? Infobase!.Name
        : LocalizationManager.T("ProcessInspector.UnknownBase");

    /// <summary>Пояснение под именем: сервер/база для известной, иначе — строка подключения из командной строки.</summary>
    public string Subtitle => IsKnown
        ? Infobase!.ServerDatabaseDisplay
        : (string.IsNullOrWhiteSpace(_details.CommandLine) ? string.Empty : _details.CommandLine);

    /// <summary>Колонка «Режим»: 1С:Предприятие / Конфигуратор / Служебный.</summary>
    public string ModeText => DetectModeText(_details.CommandLine);

    /// <summary>
    /// Колонка «Пользователь»: владелец процесса, а если он не определён — имя из
    /// параметра <c>/N</c> командной строки (issue #134, формы <c>/N"superadmin"</c>,
    /// <c>/N superadmin</c>, <c>/Nsuperadmin</c>, <c>-N</c>). Пусто — оба источника пусты.
    /// </summary>
    public string UserName
    {
        get
        {
            var owner = _details.UserName?.Trim();
            if (!string.IsNullOrWhiteSpace(owner))
                return owner;

            return ProcessCommandLineUserParser.ExtractUserName(_details.CommandLine) ?? string.Empty;
        }
    }

    /// <summary>Колонка «Время запуска» (локальное).</summary>
    public string StartTimeText => _details.StartTime.HasValue
        ? _details.StartTime.Value.ToString("dd.MM.yyyy HH:mm:ss")
        : "—";

    /// <summary>Колонка «Строка подключения» (кратко: /F <путь> или /S <сервер>\<база>).</summary>
    public string ConnectionText => _connectionText;

    /// <summary>Значок-флаг сопоставления с базой: «●» — известная, «○» — неизвестная.</summary>
    public string KnownSymbol => IsKnown ? "●" : "○";

    /// <summary>Двойной клик возможен только по строке с известной базой.</summary>
    public bool CanOpenBase => IsKnown;

    private static string DetectModeText(string? commandLine) =>
        ProcessCommandLineParser.DetectMode(commandLine) switch
        {
            OneCProcessLaunchMode.Configurator => LocalizationManager.T("ProcessInspector.Mode.Configurator"),
            OneCProcessLaunchMode.Service => LocalizationManager.T("ProcessInspector.Mode.Service"),
            _ => LocalizationManager.T("ProcessInspector.Mode.Enterprise")
        };
}
