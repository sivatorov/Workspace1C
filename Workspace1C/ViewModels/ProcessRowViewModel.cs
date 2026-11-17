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
/// </summary>
public sealed class ProcessRowViewModel
{
    private readonly RunningOneCProcessDetails _details;

    /// <param name="details">Подробности процесса из <see cref="IRunningInfobasesService.GetRunningDetails"/>.</param>
    /// <param name="infobase">Сопоставленная база списка; null — процесс не связан с известной базой.</param>
    public ProcessRowViewModel(RunningOneCProcessDetails details, Infobase? infobase)
    {
        _details = details;
        Infobase = infobase;
        ConnectionText = ProcessCommandLineParser.ExtractConnectionString(details.CommandLine) ?? "—";
    }

    /// <summary>PID процесса.</summary>
    public int Pid => _details.Pid;

    /// <summary>Имя процесса (например «1cv8c.exe»).</summary>
    public string ProcessName => _details.ProcessName;

    /// <summary>Полная командная строка (подсказка в окне).</summary>
    public string FullCommandLine => _details.CommandLine;

    /// <summary>Токен времени старта для сверки при завершении (Linux); пусто на Windows.</summary>
    public string StartTimeToken => _details.StartTimeToken ?? string.Empty;

    /// <summary>База списка, с которой сопоставлен процесс; null — неизвестная база.</summary>
    public Infobase? Infobase { get; }

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
    public string ConnectionText { get; }

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