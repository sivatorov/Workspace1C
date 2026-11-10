using System.Text.Json;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>Результат операции над пользователями ИБ.</summary>
public sealed class InfobaseUsersResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    /// <summary>Пользователи базы (операция «список»).</summary>
    public List<InfobaseUserInfo> Users { get; init; } = new();

    /// <summary>Роли конфигурации (операция «список») или роли пользователя (операция «роли»).</summary>
    public List<string> Roles { get; init; } = new();

    /// <summary>Пользователь был создан (а не изменён) — операция «создать/изменить».</summary>
    public bool Created { get; init; }

    /// <summary>Пользователь найден (операции изменения существующего).</summary>
    public bool Found { get; init; } = true;

    /// <summary>Роли из запроса, которых нет в конфигурации.</summary>
    public List<string> MissingRoles { get; init; } = new();

    public static InfobaseUsersResult Fail(string? error) => new() { Error = error };
}

/// <summary>
/// Управление пользователями информационных баз 1С через COM-коннектор
/// (выпуск 1.0.0.83): список пользователей и ролей, создание и изменение, смена
/// пароля, отключение входа и удаление. Выполняется в отдельном процессе
/// Windows PowerShell (<see cref="ComScriptRunner"/>), подключение — с учётными
/// данными «Конфигуратора» базы (у них обычно есть административные права).
/// На Linux COM-коннектора нет — операции возвращают понятный отказ.
/// </summary>
public static class InfobaseUsersService
{
    /// <summary>Таймаут одной операции: подключение к большой серверной базе бывает долгим.</summary>
    public static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Тело скрипта PowerShell: функция <c>Invoke-Op</c> с операциями
    /// <c>list</c>, <c>roles</c>, <c>upsert</c>, <c>password</c>, <c>disable</c>, <c>delete</c>.
    /// Имена объектной модели — английские синонимы (InfoBaseUsers, FindByName …).
    /// </summary>
    public const string OperationBody = @"
function UInfo($u){@{name=[string](G $u 'Name');fullName=[string](G $u 'FullName');std=[bool](G $u 'StandardAuthentication');os=[bool](G $u 'OSAuthentication');osUser=[string](G $u 'OSUser');show=[bool](G $u 'ShowInList');pwd=[bool](G $u 'PasswordIsSet');noChange=[bool](G $u 'CannotChangePassword')}}
function FindU($ibu,$name){$u=M $ibu 'FindByName' ([string]$name);if($null -eq $u -or $u -isnot [System.__ComObject]){return $null};return $u}
function Invoke-Op($c,$q){
$ibu=G $c 'InfoBaseUsers'
$md=G $c 'Metadata';$rs=G $md 'Roles'
switch([string]$q.op){
'list'{
$arr=M $ibu 'GetUsers';$n=N $arr;$list=New-Object System.Collections.ArrayList
if($n -lt 0){return @{users=@();roles=@();warn='nocount';what='InfoBaseUsers.GetUsers';cls=(OC $arr);err=$script:opErr}}
for($i=0;$i -lt $n;$i++){[void]$list.Add((UInfo (M $arr 'Get' $i)))}
$rn=N $rs;$roles=New-Object System.Collections.ArrayList
if($rn -lt 0){return @{users=$list;roles=@();warn='nocount';what='Metadata.Roles';cls=(OC $rs);err=$script:opErr}}
for($i=0;$i -lt $rn;$i++){[void]$roles.Add([string](G (M $rs 'Get' $i) 'Name'))}
return @{users=$list;roles=$roles}}
'roles'{
$u=FindU $ibu $q.name;if($null -eq $u){return @{found=$false;roles=@()}}
$ur=G $u 'Roles';$rn=N $rs;$roles=New-Object System.Collections.ArrayList
if($rn -lt 0){return @{found=$true;roles=@();warn='nocount';what='Metadata.Roles';cls=(OC $rs);err=$script:opErr}}
for($i=0;$i -lt $rn;$i++){$r=M $rs 'Get' $i;$has=$false;try{$has=[bool](M $ur 'Contains' $r)}catch{$has=$false};if($has){[void]$roles.Add([string](G $r 'Name'))}}
return @{found=$true;roles=$roles}}
'upsert'{
$u=FindU $ibu $q.name;$created=$false
if($null -eq $u){$u=M $ibu 'CreateUser';S $u 'Name' ([string]$q.name);$created=$true}
if(-not [string]::IsNullOrEmpty([string]$q.fullName)){S $u 'FullName' ([string]$q.fullName)}elseif($created){S $u 'FullName' ([string]$q.name)}
if($q.setPassword){S $u 'Password' ([string]$q.password)}
S $u 'StandardAuthentication' ([bool]$q.std)
S $u 'OSAuthentication' ([bool]$q.os)
if($q.os -and -not [string]::IsNullOrEmpty([string]$q.osUser)){S $u 'OSUser' ([string]$q.osUser)}
S $u 'ShowInList' ([bool]$q.show)
$ur=G $u 'Roles'
if($q.replaceRoles){[void](M $ur 'Clear')}
$missing=New-Object System.Collections.ArrayList
foreach($name in @($q.roles)){if([string]::IsNullOrEmpty([string]$name)){continue};$r=M $rs 'Find' ([string]$name);if($null -eq $r -or $r -isnot [System.__ComObject]){[void]$missing.Add([string]$name)}else{$has=$false;try{$has=[bool](M $ur 'Contains' $r)}catch{$has=$false};if(-not $has){[void](M $ur 'Add' $r)}}}
[void](M $u 'Write')
return @{created=$created;missingRoles=$missing}}
'password'{
$u=FindU $ibu $q.name;if($null -eq $u){return @{found=$false}}
S $u 'Password' ([string]$q.password);[void](M $u 'Write');return @{found=$true}}
'disable'{
$u=FindU $ibu $q.name;if($null -eq $u){return @{found=$false}}
S $u 'StandardAuthentication' $false;S $u 'OSAuthentication' $false;S $u 'ShowInList' $false;[void](M $u 'Write');return @{found=$true}}
'delete'{
$u=FindU $ibu $q.name;if($null -eq $u){return @{found=$false}}
[void](M $u 'Delete');return @{found=$true}}
default{throw ('Unknown operation: '+[string]$q.op)}
}}
";

    /// <summary>Разбор ответа операции «список».</summary>
    public static (List<InfobaseUserInfo> Users, List<string> Roles) ParseList(JsonElement data)
    {
        var users = new List<InfobaseUserInfo>();
        foreach (var u in ComScriptRunner.GetObjectList(data, "users"))
        {
            users.Add(new InfobaseUserInfo
            {
                Name = ComScriptRunner.GetString(u, "name") ?? string.Empty,
                FullName = ComScriptRunner.GetString(u, "fullName") ?? string.Empty,
                StandardAuthentication = ComScriptRunner.GetBool(u, "std"),
                OSAuthentication = ComScriptRunner.GetBool(u, "os"),
                OSUser = ComScriptRunner.GetString(u, "osUser") ?? string.Empty,
                ShowInList = ComScriptRunner.GetBool(u, "show"),
                PasswordIsSet = ComScriptRunner.GetBool(u, "pwd"),
                CannotChangePassword = ComScriptRunner.GetBool(u, "noChange")
            });
        }
        users.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        var roles = ComScriptRunner.GetStringList(data, "roles");
        roles.Sort(StringComparer.CurrentCultureIgnoreCase);
        return (users, roles);
    }

    /// <summary>
    /// Разбор строки ролей «ПолныеПрава, Администратор; Пользователь» в список
    /// (разделители — запятая, точка с запятой, перевод строки; повторы убираются).
    /// </summary>
    public static List<string> ParseRoles(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return result;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in text.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var name = part.Trim();
            if (name.Length > 0 && seen.Add(name))
                result.Add(name);
        }
        return result;
    }

    /// <summary>Параметры запроса «создать/изменить» (без строки подключения).</summary>
    public static Dictionary<string, object?> BuildUpsertParameters(InfobaseUserSpec spec) => new()
    {
        ["op"] = "upsert",
        ["name"] = spec.Name.Trim(),
        ["fullName"] = spec.FullName?.Trim() ?? string.Empty,
        ["setPassword"] = spec.SetPassword,
        ["password"] = spec.SetPassword ? spec.Password ?? string.Empty : string.Empty,
        ["std"] = spec.StandardAuthentication,
        ["os"] = spec.OSAuthentication,
        ["osUser"] = spec.OSUser?.Trim() ?? string.Empty,
        ["show"] = spec.ShowInList,
        ["roles"] = spec.Roles ?? new List<string>(),
        ["replaceRoles"] = spec.ReplaceRoles
    };

    /// <summary>Проверка параметров «создать/изменить»: текст ошибки или null.</summary>
    public static string? Validate(InfobaseUserSpec spec)
    {
        if (spec is null || string.IsNullOrWhiteSpace(spec.Name))
            return LocalizationManager.T("IbUsers.NameRequired");
        if (!spec.StandardAuthentication && !spec.OSAuthentication)
            return LocalizationManager.T("IbUsers.AuthRequired");
        if (spec.OSAuthentication && !string.IsNullOrWhiteSpace(spec.OSUser) && !spec.OSUser.Trim().StartsWith(@"\\", StringComparison.Ordinal))
            return LocalizationManager.T("IbUsers.OsUserFormat");
        return null;
    }

    public static Task<InfobaseUsersResult> ListAsync(Infobase infobase, CancellationToken ct = default) =>
        RunAsync(infobase, new Dictionary<string, object?> { ["op"] = "list" }, null, data =>
        {
            var (users, roles) = ParseList(data);
            return new InfobaseUsersResult { Success = true, Users = users, Roles = roles };
        }, ct);

    public static Task<InfobaseUsersResult> GetUserRolesAsync(Infobase infobase, string userName, CancellationToken ct = default) =>
        RunAsync(infobase, new Dictionary<string, object?> { ["op"] = "roles", ["name"] = userName }, null, data =>
        {
            var roles = ComScriptRunner.GetStringList(data, "roles");
            roles.Sort(StringComparer.CurrentCultureIgnoreCase);
            return new InfobaseUsersResult { Success = true, Found = ComScriptRunner.GetBool(data, "found", true), Roles = roles };
        }, ct);

    public static Task<InfobaseUsersResult> UpsertAsync(Infobase infobase, InfobaseUserSpec spec, CancellationToken ct = default)
    {
        var error = Validate(spec);
        if (error is not null)
            return Task.FromResult(InfobaseUsersResult.Fail(error));
        return RunAsync(infobase, BuildUpsertParameters(spec), spec.Password, data => new InfobaseUsersResult
        {
            Success = true,
            Created = ComScriptRunner.GetBool(data, "created"),
            MissingRoles = ComScriptRunner.GetStringList(data, "missingRoles")
        }, ct);
    }

    public static Task<InfobaseUsersResult> SetPasswordAsync(Infobase infobase, string userName, string password,
        CancellationToken ct = default) =>
        RunSimpleAsync(infobase, "password", userName, password, ct);

    public static Task<InfobaseUsersResult> DisableLoginAsync(Infobase infobase, string userName, CancellationToken ct = default) =>
        RunSimpleAsync(infobase, "disable", userName, null, ct);

    public static Task<InfobaseUsersResult> DeleteAsync(Infobase infobase, string userName, CancellationToken ct = default) =>
        RunSimpleAsync(infobase, "delete", userName, null, ct);

    private static Task<InfobaseUsersResult> RunSimpleAsync(Infobase infobase, string op, string userName,
        string? password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return Task.FromResult(InfobaseUsersResult.Fail(LocalizationManager.T("IbUsers.NameRequired")));
        var parameters = new Dictionary<string, object?> { ["op"] = op, ["name"] = userName.Trim() };
        if (password is not null)
            parameters["password"] = password;
        return RunAsync(infobase, parameters, password, data => new InfobaseUsersResult
        {
            Success = true,
            Found = ComScriptRunner.GetBool(data, "found", true)
        }, ct);
    }

    private static async Task<InfobaseUsersResult> RunAsync(Infobase infobase, Dictionary<string, object?> parameters,
        string? extraSecret, Func<JsonElement, InfobaseUsersResult> map, CancellationToken ct)
    {
        var result = await RunCoreAsync(infobase, parameters, extraSecret, map, ct).ConfigureAwait(false);
        // Журнал аудита: изменения пользователей (чтение списка и ролей не пишется).
        var op = parameters.TryGetValue("op", out var o) ? o as string : null;
        if (op is "upsert" or "password" or "disable" or "delete")
            Audit.AuditHub.Write(Audit.AuditActions.IbUserChange, infobase?.Name ?? string.Empty, result.Success,
                $"{op}: {(parameters.TryGetValue("name", out var n) ? n : null)}" + (result.Success ? string.Empty : "; " + result.Error));
        return result;
    }

    private static async Task<InfobaseUsersResult> RunCoreAsync(Infobase infobase, Dictionary<string, object?> parameters,
        string? extraSecret, Func<JsonElement, InfobaseUsersResult> map, CancellationToken ct)
    {
        if (infobase is null)
            return InfobaseUsersResult.Fail(LocalizationManager.T("IbUsers.NoBase"));
        var connection = ComConnectionInfo.Prepare(infobase);
        if (connection.Error is not null)
            return InfobaseUsersResult.Fail(connection.Error);

        var result = await ComScriptRunner.RunAsync(OperationBody, connection.ProgIds, connection.ConnectString,
            parameters, new[] { connection.Password, extraSecret }, OperationTimeout, ct).ConfigureAwait(false);
        if (!result.Success)
            return InfobaseUsersResult.Fail(DescribeFailure(result));
        return map(result.Data);
    }

    /// <summary>Понятный текст отказа по коду результата.</summary>
    public static string DescribeFailure(ComScriptResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.Error) ? string.Empty : result.Error!.Trim();
        var text = result.Code switch
        {
            "connect" => string.Format(LocalizationManager.T("ComScript.ConnectFailed"), detail),
            "op" => string.Format(LocalizationManager.T("ComScript.OperationFailed"), detail),
            _ => detail.Length > 0 ? detail : LocalizationManager.T("ComScript.NoResponse")
        };
        // Без загруженного словаря шаблон может прийти без {0} — деталь не должна теряться.
        return detail.Length > 0 && !text.Contains(detail, StringComparison.Ordinal) ? text + ": " + detail : text;
    }
}

/// <summary>
/// Подготовка COM-подключения к базе: строка подключения (с учётными данными
/// «Конфигуратора»), кандидаты ProgID и пароль для маскирования в ошибках.
/// </summary>
public sealed class ComConnectionInfo
{
    public string ConnectString { get; init; } = string.Empty;

    public IReadOnlyList<string> ProgIds { get; init; } = Array.Empty<string>();

    public string? Password { get; init; }

    public string? Error { get; init; }

    public static ComConnectionInfo Prepare(Infobase infobase)
    {
#if WINDOWS
        var connect = OneCComConnector.BuildComConnectString(infobase, OneCLaunchMode.Configurator, out var hasSecret);
        if (string.IsNullOrWhiteSpace(connect))
            return new ComConnectionInfo { Error = LocalizationManager.T("ComScript.NoConnectString") };
        string? template = null;
        try { template = AppServices.TryGetService<IInfobaseRepository>()?.LoadSettings()?.ComConnectorNameTemplate; }
        catch { /* настройки недоступны — стандартные ProgID */ }
        var progIds = OneCComConnector.BuildProgIdCandidates(template, infobase.PlatformVersion);
        string? password = null;
        if (hasSecret)
        {
            InfobaseAuthResolver.Resolve(infobase, OneCLaunchMode.Configurator, out _, out _, out var pwd,
                OneCLauncher.CommonEnterpriseAuthorization,
                OneCLauncher.CommonConfiguratorAuthorization,
                OneCLauncher.CommonAuthorization);
            password = pwd;
        }
        return new ComConnectionInfo { ConnectString = connect, ProgIds = progIds, Password = password };
#else
        return new ComConnectionInfo { Error = LocalizationManager.T("ComScript.WindowsOnly") };
#endif
    }
}
