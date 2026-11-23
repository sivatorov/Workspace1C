#if WINDOWS
using System;
using System.Collections.Generic;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Безопасное чтение сведений о конфигурации 1С через Windows PowerShell 5.1
/// (.NET Framework) — запасной путь для issue #154.
/// <para>
/// Зачем. <see cref="ComReadHost"/> запускает COM-агента как дочерний экземпляр этого же
/// CoreCLR-приложения. На вызове <c>Connect</c> у <c>comcntr.dll</c> (в частности, у
/// <c>V85.COMConnector</c>) агент может оборваться нативным fast-fail
/// <c>0xC0000409</c>, поэтому изоляция в том же рантайме не спасает: родитель видит лишь
/// код возврата. Под .NET Framework тот же вызов работает штатно, поэтому при таком
/// отказе чтение повторяется в отдельном процессе Windows PowerShell 5.1.
/// </para>
/// <para>
/// Протокол полностью совпадает с <see cref="ComScriptRunner"/>: скрипт передаётся через
/// <c>-EncodedCommand</c>, запрос (список ProgID и строка подключения с паролем) — через
/// stdin в Base64, ответ — строка <c>W1CRES <Base64(UTF-8 JSON)></c>. Пароль не
/// попадает в командную строку процесса. При <c>noprogid</c> <see cref="ComScriptRunner"/>
/// автоматически повторяет запуск в 32-битном PowerShell (SysWOW64), поэтому коннектор,
/// зарегистрированный только для 32-битной платформы, тоже находится.
/// </para>
/// </summary>
internal static class ComConfigInfoPowerShell
{
    /// <summary>
    /// Тело операции <c>Invoke-Op</c>: чтение <c>Metadata.Name</c>/<c>Metadata.Version</c>
    /// подключённой базы. Ошибки не глотаются — они превращаются в отказ протокола
    /// (<c>code='op'</c>), чтобы родитель показал причину, а не пустые поля.
    /// </summary>
    public const string OperationBody = @"
function Invoke-Op($c,$q){
$md=G $c 'Metadata'
if($null -eq $md -or $md -isnot [System.__ComObject]){throw 'Metadata is not available'}
$name=''
try{$name=[string](G $md 'Name')}catch{try{$name=[string](G $md 'Synonym')}catch{$name=''}}
$ver=''
try{$ver=[string](G $md 'Version')}catch{$ver=''}
if([string]::IsNullOrWhiteSpace($name) -and [string]::IsNullOrWhiteSpace($ver)){throw 'Configuration name and version are empty'}
return @{name=$name;version=$ver}
}
";

    /// <summary>
    /// Коды нативного fast-fail, при которых COM-вызов в CoreCLR-процессе не даёт
    /// управляемого исключения и повтор в PowerShell обязателен. <c>0xC0000409</c>
    /// (STATUS_STACK_BUFFER_OVERRUN / <c>__fastfail</c>) — тот самый дефект из issue #154.
    /// Остальные добавлены как заведомо «нативные» обрывы, а не ошибки 1С.
    /// </summary>
    private static readonly string[] NativeFastFailCodes =
    {
        "0xC0000409", // STATUS_STACK_BUFFER_OVERRUN / __fastfail
        "0xC00000FD", // STATUS_STACK_OVERFLOW
        "0xC0000374", // STATUS_HEAP_CORRUPTION
        "0xC000041D", // STATUS_FATAL_USER_CALLBACK_EXCEPTION
        "0xC0000005", // STATUS_ACCESS_VIOLATION
        "0x80000003"  // STATUS_BREAKPOINT
    };

    /// <summary>Код возврата агента относится к нативным fast-fail.</summary>
    public static bool IsNativeFastFail(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        foreach (var known in NativeFastFailCodes)
        {
            if (string.Equals(code, known, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Агент уже показал нативный fast-fail в этой сессии. Тогда при защёлке
    /// <see cref="ComFailureKind.Disabled"/> безопасный путь всё равно пробуется: иначе
    /// пакетное определение после двух падений агента молча перестало бы читать сведения.
    /// </summary>
    private static int _preferPowerShell;

    /// <summary>В этой сессии уже наблюдался нативный fast-fail агента.</summary>
    public static bool PreferPowerShell => System.Threading.Volatile.Read(ref _preferPowerShell) != 0;

    /// <summary>Запоминает нативный fast-fail агента, чтобы не повторять его на каждой базе.</summary>
    public static void NoteAgentFailure(ComReadResult result)
    {
        if (result.Failure == ComFailureKind.AgentCrashed && IsNativeFastFail(result.Detail))
            System.Threading.Volatile.Write(ref _preferPowerShell, 1);
    }

    /// <summary>Сбрасывает сессионную память о fast-fail (используется тестами).</summary>
    internal static void ResetSessionState() => System.Threading.Volatile.Write(ref _preferPowerShell, 0);

    /// <summary>
    /// Нужно ли повторять чтение через PowerShell. Повторяем при нативном fast-fail агента
    /// (issue #154), при <see cref="ComFailureKind.NotRegistered"/> и
    /// <see cref="ComFailureKind.InstanceFailed"/>: коннектор мог быть зарегистрирован только
    /// для 32-битной платформы, и 32-битный PowerShell (SysWOW64) его найдёт. Защёлку
    /// <see cref="ComFailureKind.Disabled"/> повторяем, только если fast-fail уже наблюдался —
    /// иначе на машине без COM мы бы запускали PowerShell на каждую базу без пользы.
    /// Остальные отказы (таймаут, ошибка базы) повтором не лечатся.
    /// </summary>
    public static bool ShouldFallback(ComReadResult result) => result.Failure switch
    {
        ComFailureKind.AgentCrashed => IsNativeFastFail(result.Detail),
        ComFailureKind.NotRegistered => true,
        ComFailureKind.InstanceFailed => true,
        ComFailureKind.Disabled => PreferPowerShell,
        _ => false
    };

    /// <summary>
    /// Читает имя и версию конфигурации через Windows PowerShell 5.1. Синхронный вызов:
    /// используется из фонового потока чтения свойств. <paramref name="host"/> — описание
    /// фактически использованного хоста («PowerShell 5.1 x64»/«x86») для диагностики.
    /// </summary>
    public static ComReadResult Read(string connectString, IReadOnlyList<string> progIds,
        int timeoutMs, out string? host)
    {
        host = null;

        if (!OperatingSystem.IsWindows())
            return ComReadResult.Fail(ComFailureKind.NotRegistered);

        var timeout = TimeSpan.FromMilliseconds(Math.Max(1000, timeoutMs));
        ComScriptResult script;
        try
        {
            script = ComScriptRunner.RunAsync(
                OperationBody,
                progIds ?? OneCComConnector.KnownProgIds,
                connectString,
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["op"] = "config" },
                secrets: null,
                timeout).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return ComReadResult.Fail(ComFailureKind.Transport, ex.Message);
        }

        host = script.Host;
        return MapResult(script);
    }

    /// <summary>
    /// Переводит ответ PowerShell-скрипта в общий контракт <see cref="ComReadResult"/>,
    /// чтобы вызывающий не различал агента и безопасный путь.
    /// </summary>
    internal static ComReadResult MapResult(ComScriptResult result)
    {
        if (result.Success)
        {
            var name = ComScriptRunner.GetString(result.Data, "name") ?? string.Empty;
            var version = ComScriptRunner.GetString(result.Data, "version") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(version))
            {
                return new ComReadResult(null, ComFailureKind.MetadataRead, null, null, result.ProgId);
            }

            return ComReadResult.Ok(new OneCConfigInfo(name, version), result.ProgId);
        }

        var kind = result.Code switch
        {
            "noprogid" => ComFailureKind.NotRegistered,
            "connect" => ComFailureKind.DatabaseError,
            "timeout" => ComFailureKind.Timeout,
            "cancelled" => ComFailureKind.Transport,
            "platform" => ComFailureKind.NotRegistered,
            "protocol" => ComFailureKind.Transport,
            "process" => ComFailureKind.AgentStart,
            _ => ComFailureKind.DatabaseError
        };

        return new ComReadResult(null, kind, result.Error, null, result.ProgId);
    }
}
#endif
