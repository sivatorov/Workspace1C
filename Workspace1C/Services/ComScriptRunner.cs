using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Workspace1C.Localization;

namespace Workspace1C.Services;

/// <summary>
/// Результат операции COM-скрипта (<see cref="ComScriptRunner"/>).
/// </summary>
public sealed class ComScriptResult
{
    /// <summary>Операция выполнена, <see cref="Data"/> содержит ответ скрипта.</summary>
    public bool Success { get; init; }

    /// <summary>
    /// Код отказа: <c>noprogid</c> — COM-коннектор не зарегистрирован в этой разрядности,
    /// <c>connect</c> — не удалось подключиться к базе, <c>op</c> — ошибка операции,
    /// <c>timeout</c>, <c>cancelled</c>, <c>process</c>, <c>platform</c>, <c>protocol</c>.
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Текст ошибки (пароли из запроса в нём замаскированы).</summary>
    public string? Error { get; init; }

    /// <summary>ProgID коннектора, через который прошло подключение.</summary>
    public string? ProgId { get; init; }

    /// <summary>Разобранный JSON-ответ скрипта (корневой объект) при успехе.</summary>
    public JsonElement Data { get; init; }

    public static ComScriptResult Fail(string code, string? error) => new() { Code = code, Error = error };
}

/// <summary>
/// Выполнение операций объектной модели 1С через COM-коннектор во внешнем процессе
/// Windows PowerShell 5.1 (выпуск 1.0.0.83).
/// <para>
/// Зачем отдельный процесс. Вызов <c>Connect</c> у comcntr.dll внутри CoreCLR-процесса
/// приложения обрывает его нативным fast-fail (см. <see cref="ComReadHost"/>), а под
/// .NET Framework — среда Windows PowerShell 5.1 — тот же вызов работает штатно.
/// Каждая операция идёт в своём процессе: сбой COM или зависание базы не трогают
/// приложение, а по таймауту процесс просто завершается.
/// </para>
/// <para>
/// Протокол. Текст скрипта передаётся ключом <c>-EncodedCommand</c> (на него не
/// действует политика выполнения скриптов), а запрос со строкой подключения и паролями —
/// через stdin в Base64 (командная строка процесса видна другим процессам). Ответ —
/// строка <c>W1CRES &lt;Base64(UTF-8 JSON)&gt;</c> в stdout, поэтому кодовая страница
/// консоли значения не имеет. Члены объектов 1С вызываются через
/// <c>IDispatch</c> (<c>InvokeMember</c>): у объектов 1С нет библиотеки типов, и обычный
/// синтаксис PowerShell их свойств не видит.
/// </para>
/// <para>
/// Разрядность. Сначала используется 64-битный PowerShell; если коннектор зарегистрирован
/// только для 32-битной платформы (код <c>noprogid</c>), запрос повторяется в
/// 32-битном (SysWOW64).
/// </para>
/// </summary>
public static class ComScriptRunner
{
    /// <summary>Префикс строки ответа в stdout.</summary>
    public const string ResultPrefix = "W1CRES ";

    /// <summary>Лимит длины командной строки Windows с запасом под путь и ключи.</summary>
    public const int MaxEncodedCommandLength = 30000;

    /// <summary>
    /// Общая часть скрипта: вспомогательные функции IDispatch, чтение запроса,
    /// подключение и вызов <c>Invoke-Op</c>, которую определяет тело операции.
    /// </summary>
    public const string Prelude = @"$ErrorActionPreference='Stop'
$F=[Reflection.BindingFlags]
function G($o,$n){[System.__ComObject].InvokeMember($n,$F::GetProperty,$null,$o,$null)}
function S($o,$n,$v){[void][System.__ComObject].InvokeMember($n,$F::SetProperty,$null,$o,@(,$v))}
function M($o,$n){[System.__ComObject].InvokeMember($n,$F::InvokeMethod,$null,$o,[object[]]$args)}
function Err($e){$x=$e.Exception;while($x.InnerException){$x=$x.InnerException};[string]$x.Message}
function Out-R($o){$j=ConvertTo-Json -InputObject $o -Depth 6 -Compress;[Console]::Out.WriteLine('W1CRES '+[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($j)))}
function N($o){try{$script:opErr='';if($null -eq $o){$script:opErr='object is null';return -1};if($o -isnot [System.__ComObject]){$script:opErr=('not a COM object: '+(OC $o));return -1};return [int](M $o 'Count')}catch{$script:opErr=(Err $_);return -1}}
function Mt($o,$n){try{$script:opErr='';return (M $o $n @args)}catch{$script:opErr=(Err $_);return $null}}
function OC($o){try{if($null -eq $o){return 'null'};return [string]$o.GetType().FullName}catch{return 'unknown'}}
";

    /// <summary>Запуск: подключение и диспетчеризация в <c>Invoke-Op</c>.</summary>
    public const string Main = @"
try{
$req=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([Console]::In.ReadToEnd().Trim()))|ConvertFrom-Json
$cn=$null;$le='';$used=''
foreach($p in $req.progIds){try{$cn=New-Object -ComObject $p;$used=$p;break}catch{$le=Err $_}}
if($null -eq $cn){Out-R @{ok=$false;code='noprogid';error=$le};exit 0}
try{$c=M $cn 'Connect' ([string]$req.connect)}catch{Out-R @{ok=$false;code='connect';error=(Err $_);progId=$used};exit 0}
$r=Invoke-Op $c $req
if($null -eq $r){$r=@{}}
$r.ok=$true;$r.progId=$used
Out-R $r
}catch{Out-R @{ok=$false;code='op';error=(Err $_)}}
";

    /// <summary>Полный текст скрипта из тела операции (функции <c>Invoke-Op</c> и помощников).</summary>
    public static string BuildScript(string operationBody) =>
        Prelude + (operationBody ?? string.Empty) + Main;

    /// <summary>Значение ключа <c>-EncodedCommand</c>: Base64 от UTF-16LE текста скрипта.</summary>
    public static string EncodeCommand(string script) =>
        Convert.ToBase64String(Encoding.Unicode.GetBytes(script ?? string.Empty));

    /// <summary>
    /// Тело запроса для stdin: Base64 от UTF-8 JSON со списком ProgID, строкой подключения
    /// и параметрами операции.
    /// </summary>
    public static string BuildRequest(IReadOnlyList<string> progIds, string connectString,
        IReadOnlyDictionary<string, object?> parameters)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["progIds"] = progIds ?? Array.Empty<string>(),
            ["connect"] = connectString ?? string.Empty
        };
        if (parameters is not null)
        {
            foreach (var pair in parameters)
                payload[pair.Key] = pair.Value;
        }
        var json = JsonSerializer.Serialize(payload);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Разбор stdout скрипта: ищется последняя строка <see cref="ResultPrefix"/>; ответ
    /// с <c>ok=false</c> превращается в отказ с кодом и текстом. Секреты из
    /// <paramref name="secrets"/> маскируются в тексте ошибки.
    /// </summary>
    public static ComScriptResult ParseOutput(string? stdout, IEnumerable<string?>? secrets = null)
    {
        if (string.IsNullOrEmpty(stdout))
            return ComScriptResult.Fail("protocol", LocalizationManager.T("ComScript.NoResponse"));

        string? payload = null;
        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith(ResultPrefix, StringComparison.Ordinal))
                payload = line.Substring(ResultPrefix.Length).Trim();
        }
        if (payload is null)
            return ComScriptResult.Fail("protocol", LocalizationManager.T("ComScript.NoResponse"));

        JsonElement root;
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return ComScriptResult.Fail("protocol", LocalizationManager.T("ComScript.BadResponse"));
        }

        if (root.ValueKind != JsonValueKind.Object)
            return ComScriptResult.Fail("protocol", LocalizationManager.T("ComScript.BadResponse"));

        var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
        var progId = GetString(root, "progId");
        if (ok)
            return new ComScriptResult { Success = true, Code = string.Empty, ProgId = progId, Data = root };

        var code = GetString(root, "code") ?? "op";
        var error = MaskSecrets(GetString(root, "error"), secrets);
        return new ComScriptResult { Success = false, Code = code, Error = error, ProgId = progId, Data = root };
    }

    /// <summary>Заменяет вхождения непустых секретов на «***».</summary>
    public static string? MaskSecrets(string? text, IEnumerable<string?>? secrets)
    {
        if (string.IsNullOrEmpty(text) || secrets is null)
            return text;
        foreach (var secret in secrets)
        {
            if (!string.IsNullOrEmpty(secret) && secret.Length >= 1)
                text = text.Replace(secret, "***", StringComparison.Ordinal);
        }
        return text;
    }

    /// <summary>Строковое свойство JSON-объекта или null.</summary>
    public static string? GetString(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var el)
            ? el.ValueKind switch
            {
                JsonValueKind.String => el.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => el.ToString()
            }
            : null;

    /// <summary>Логическое свойство JSON-объекта (по умолчанию <paramref name="fallback"/>).</summary>
    public static bool GetBool(JsonElement obj, string name, bool fallback = false) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var el)
            ? el.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => fallback
            }
            : fallback;

    /// <summary>Массив строк из свойства JSON-объекта (одиночное значение — как массив из одного).</summary>
    public static List<string> GetStringList(JsonElement obj, string name)
    {
        var list = new List<string>();
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var el))
            return list;
        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                var s = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                if (!string.IsNullOrEmpty(s))
                    list.Add(s);
            }
        }
        else if (el.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(el.GetString()))
        {
            list.Add(el.GetString()!);
        }
        return list;
    }

    /// <summary>Объекты массива JSON-свойства (одиночный объект — как массив из одного).</summary>
    public static List<JsonElement> GetObjectList(JsonElement obj, string name)
    {
        var list = new List<JsonElement>();
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var el))
            return list;
        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
                if (item.ValueKind == JsonValueKind.Object)
                    list.Add(item);
        }
        else if (el.ValueKind == JsonValueKind.Object)
        {
            list.Add(el);
        }
        return list;
    }

    /// <summary>Путь к Windows PowerShell 5.1 нужной разрядности.</summary>
    public static string PowerShellPath(bool x86)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrEmpty(windows))
            windows = @"C:\Windows";
        var sub = x86 && Environment.Is64BitOperatingSystem ? "SysWOW64" : "System32";
        return Path.Combine(windows, sub, "WindowsPowerShell", "v1.0", "powershell.exe");
    }

    /// <summary>
    /// Выполняет операцию: 64-битный PowerShell, при <c>noprogid</c> — 32-битный.
    /// Только Windows; на других ОС — отказ с кодом <c>platform</c>.
    /// </summary>
    public static async Task<ComScriptResult> RunAsync(string operationBody, IReadOnlyList<string> progIds,
        string connectString, IReadOnlyDictionary<string, object?> parameters, IEnumerable<string?>? secrets,
        TimeSpan timeout, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return ComScriptResult.Fail("platform", LocalizationManager.T("ComScript.WindowsOnly"));

        var script = BuildScript(operationBody);
        var encoded = EncodeCommand(script);
        if (encoded.Length > MaxEncodedCommandLength)
            return ComScriptResult.Fail("protocol", "Script too long");
        var request = BuildRequest(progIds, connectString, parameters);
        var secretList = secrets?.ToList() ?? new List<string?>();

        var first = await RunOnceAsync(PowerShellPath(false), encoded, request, secretList, timeout, ct).ConfigureAwait(false);
        if (first.Success || first.Code != "noprogid" || !Environment.Is64BitOperatingSystem)
            return first;
        var second = await RunOnceAsync(PowerShellPath(true), encoded, request, secretList, timeout, ct).ConfigureAwait(false);
        return second.Code == "noprogid"
            ? ComScriptResult.Fail("noprogid", LocalizationManager.T("ComScript.NoConnector")
                + (string.IsNullOrWhiteSpace(second.Error) ? string.Empty : " " + second.Error))
            : second;
    }

    private static async Task<ComScriptResult> RunOnceAsync(string exe, string encoded, string request,
        List<string?> secrets, TimeSpan timeout, CancellationToken ct)
    {
        if (!File.Exists(exe))
            return ComScriptResult.Fail("process", string.Format(LocalizationManager.T("ComScript.NoPowerShell"), exe));

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add("-NoLogo");
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-EncodedCommand");
        psi.ArgumentList.Add(encoded);

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null");
        }
        catch (Exception ex)
        {
            return ComScriptResult.Fail("process", ex.Message);
        }

        using (process)
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            try
            {
                await process.StandardInput.WriteAsync(request).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // Процесс завершился раньше чтения stdin — причина будет в stdout/stderr.
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); }
                catch { /* уже завершён */ }
                return ct.IsCancellationRequested
                    ? ComScriptResult.Fail("cancelled", LocalizationManager.T("ComScript.Cancelled"))
                    : ComScriptResult.Fail("timeout", LocalizationManager.T("ComScript.Timeout"));
            }

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            var result = ParseOutput(stdout, secrets);
            if (result.Code == "protocol" && !string.IsNullOrWhiteSpace(stderr))
            {
                var tail = stderr.Trim();
                if (tail.Length > 600)
                    tail = tail.Substring(tail.Length - 600);
                return ComScriptResult.Fail("process", MaskSecrets(tail, secrets));
            }
            return result;
        }
    }
}
