using System.IO;
using System.Text.Json;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>Итог загрузки одного файла .epf/.erf в одну базу.</summary>
public sealed class ExternalProcessingFileResult
{
    /// <summary>Путь к файлу.</summary>
    public string File { get; init; } = string.Empty;

    /// <summary>created — добавлен, updated — обновлён, skipped — уже есть (режим «только новые»), пусто — ошибка.</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>Имя объекта (метаданные внешней обработки).</summary>
    public string ObjectName { get; init; } = string.Empty;

    /// <summary>Наименование из регистрационных данных.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Версия из регистрационных данных.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>Предупреждения (не удалось заполнить вид, назначение и т.п.).</summary>
    public List<string> Warnings { get; init; } = new();

    /// <summary>Ошибка загрузки файла; null — успешно.</summary>
    public string? Error { get; init; }

    public bool Success => Error is null && Action.Length > 0;
}

/// <summary>Итог загрузки набора файлов в одну базу.</summary>
public sealed class ExternalProcessingsBaseResult
{
    public bool Success { get; init; }

    /// <summary>Ошибка подключения/выполнения для всей базы.</summary>
    public string? Error { get; init; }

    /// <summary>В конфигурации нет справочника «Дополнительные отчёты и обработки» (нет БСП).</summary>
    public bool NoBsp { get; init; }

    public List<ExternalProcessingFileResult> Files { get; init; } = new();

    public static ExternalProcessingsBaseResult Fail(string? error) => new() { Error = error };
}

/// <summary>
/// Загрузка внешних отчётов (.erf) и обработок (.epf) в справочник «Дополнительные отчёты
/// и обработки» (БСП) по списку баз (issue #91, п.3; docs/plans/plan-v2-free-features.md).
/// Через COM-соединение (<see cref="ComScriptRunner"/>, только Windows):
/// файл подключается во временное хранилище, создаётся объект, читаются регистрационные
/// данные <c>СведенияОВнешнейОбработке()</c>, элемент справочника ищется по имени объекта
/// и создаётся или обновляется (хранилище, версия, информация, команды). Для нового
/// элемента заполняется назначение (через <c>ОбщегоНазначения.ИдентификаторОбъектаМетаданных</c>);
/// у существующего назначение, разделы и публикация не меняются.
/// </summary>
public static class ExternalProcessingsService
{
    public static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Поддерживаемые расширения файлов.</summary>
    public static readonly IReadOnlyList<string> Extensions = new[] { ".epf", ".erf" };

    public const string OperationBody = @"
function P($s,$n){try{if(M $s 'Property' $n){return (G $s $n)}}catch{};return $null}
function EnumV($c,$en,$v){if([string]::IsNullOrEmpty([string]$v)){return $null};try{return G (G (G $c 'Перечисления') $en) ([string]$v)}catch{return $null}}
function Invoke-Op($c,$q){
$cats=G (G $c 'Metadata') 'Catalogs'
$found=M $cats 'Find' 'ДополнительныеОтчетыИОбработки'
if($null -eq $found -or $found -isnot [System.__ComObject]){return @{noBsp=$true;files=@()}}
$mgr=G (G $c 'Справочники') 'ДополнительныеОтчетыИОбработки'
$cm=$null;try{$cm=G $c 'ОбщегоНазначения'}catch{}
$out=New-Object System.Collections.ArrayList
foreach($f in @($q.files)){
$w=New-Object System.Collections.ArrayList
$r=@{file=[string]$f.path;action='';objectName='';title='';version='';warnings=$w}
try{
$bin=M $c 'NewObject' 'BinaryData' ([string]$f.path)
if($f.report){$em=G $c 'ExternalReports'}else{$em=G $c 'ExternalDataProcessors'}
$obj=$null
try{$addr=M $c 'PutToTempStorage' $bin;$nm=M $em 'Connect' $addr '' $false;$obj=M $em 'Create' $nm $false}catch{$obj=M $em 'Create' ([string]$f.path) $false}
$meta=M $obj 'Metadata'
$objName=[string](G $meta 'Name');$r.objectName=$objName
$info=$null;try{$info=M $obj 'СведенияОВнешнейОбработке'}catch{throw ('noinfo: '+(Err $_))}
$kind=[string](P $info 'Вид')
$title=[string](P $info 'Наименование');if([string]::IsNullOrEmpty($title)){$title=[string](G $meta 'Synonym')};if([string]::IsNullOrEmpty($title)){$title=$objName}
$r.title=$title;$r.version=[string](P $info 'Версия')
$qr=M $c 'NewObject' 'Query' 'ВЫБРАТЬ ПЕРВЫЕ 1 Т.Ссылка КАК Ссылка ИЗ Справочник.ДополнительныеОтчетыИОбработки КАК Т ГДЕ Т.ИмяОбъекта = &Имя И НЕ Т.ПометкаУдаления'
[void](M $qr 'SetParameter' 'Имя' $objName)
$sel=M (M $qr 'Execute') 'Select'
if(M $sel 'Next'){
if(-not $q.update){$r.action='skipped';[void]$out.Add($r);continue}
$o=M (G $sel 'Ссылка') 'GetObject';$r.action='updated'
}else{$o=M $mgr 'CreateItem';$r.action='created'}
S $o 'Наименование' $title
S $o 'ИмяОбъекта' $objName
if(-not [string]::IsNullOrEmpty($r.version)){S $o 'Версия' $r.version}
$inf=P $info 'Информация';if($null -ne $inf){S $o 'Информация' ([string]$inf)}
$sm=P $info 'БезопасныйРежим';if($null -ne $sm){S $o 'БезопасныйРежим' ([bool]$sm)}
$ev=EnumV $c 'ВидыДополнительныхОтчетовИОбработок' $kind;if($null -ne $ev){S $o 'Вид' $ev}elseif($r.action -eq 'created'){[void]$w.Add('kind:'+$kind)}
try{S $o 'ИмяФайла' ([IO.Path]::GetFileName([string]$f.path))}catch{}
if($r.action -eq 'created'){$pub=EnumV $c 'ВариантыПубликацииДополнительныхОтчетовИОбработок' 'Используется';if($null -ne $pub){try{S $o 'Публикация' $pub}catch{}}}
S $o 'ХранилищеОбработки' (M $c 'NewObject' 'ValueStorage' $bin)
$cmds=P $info 'Команды'
if($null -ne $cmds){
$ts=G $o 'Команды';[void](M $ts 'Clear')
$n=M $cmds 'Count'
for($k=0;$k -lt $n;$k++){$row=M $cmds 'Get' $k;$nr=M $ts 'Add'
foreach($col in @('Представление','Идентификатор','ПоказыватьОповещение','Модификатор','Скрыть')){try{S $nr $col (G $row $col)}catch{}}
try{$cv=EnumV $c 'СпособыВызоваДополнительныхОбработок' ([string](G $row 'Использование'));if($null -ne $cv){S $nr 'ВариантЗапуска' $cv}}catch{}}
}
if($r.action -eq 'created'){
$dest=P $info 'Назначение'
if($null -ne $dest){$ts2=G $o 'Назначение';$dn=M $dest 'Count'
for($k=0;$k -lt $dn;$k++){$full=[string](M $dest 'Get' $k)
try{$id=M $cm 'ИдентификаторОбъектаМетаданных' $full;$nr=M $ts2 'Add';S $nr 'ОбъектНазначения' $id}catch{[void]$w.Add('dest:'+$full)}}}
if($kind -eq 'ДополнительнаяОбработка' -or $kind -eq 'ДополнительныйОтчет'){[void]$w.Add('sections')}
}
[void](M $o 'Write')
}catch{$r.action='';$r.error=(Err $_)}
[void]$out.Add($r)
}
return @{files=$out}
}
";

    /// <summary>Внешний отчёт (.erf), а не обработка.</summary>
    public static bool IsReport(string path) =>
        string.Equals(Path.GetExtension(path ?? string.Empty), ".erf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Поддерживаемый файл внешнего отчёта или обработки.</summary>
    public static bool IsSupported(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Все .epf/.erf в каталоге (без подкаталогов), по имени.</summary>
    public static IReadOnlyList<string> FindInFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return Array.Empty<string>();
        return Directory.EnumerateFiles(folder)
            .Where(IsSupported)
            .OrderBy(p => Path.GetFileName(p), StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Проверка списка файлов; текст ошибки или null.</summary>
    public static string? Validate(IReadOnlyList<string> files)
    {
        if (files is null || files.Count == 0)
            return LocalizationManager.T("ExtProc.NoFiles");
        foreach (var file in files)
        {
            if (!IsSupported(file))
                return string.Format(LocalizationManager.T("ExtProc.BadExtension"), Path.GetFileName(file));
            if (!File.Exists(file))
                return string.Format(LocalizationManager.T("ExtProc.FileNotFound"), file);
        }
        return null;
    }

    /// <summary>Параметры COM-операции.</summary>
    public static Dictionary<string, object?> BuildParameters(IReadOnlyList<string> files, bool update) => new()
    {
        ["op"] = "load",
        ["update"] = update,
        ["files"] = files.Select(f => new Dictionary<string, object?>
        {
            ["path"] = Path.GetFullPath(f),
            ["report"] = IsReport(f)
        }).ToList()
    };

    /// <summary>Разбор ответа COM-операции.</summary>
    public static ExternalProcessingsBaseResult ParseResult(JsonElement data)
    {
        if (ComScriptRunner.GetBool(data, "noBsp"))
            return new ExternalProcessingsBaseResult
            {
                NoBsp = true,
                Error = LocalizationManager.T("ExtProc.NoBsp")
            };

        var files = new List<ExternalProcessingFileResult>();
        foreach (var item in ComScriptRunner.GetObjectList(data, "files"))
        {
            var error = ComScriptRunner.GetString(item, "error");
            files.Add(new ExternalProcessingFileResult
            {
                File = ComScriptRunner.GetString(item, "file") ?? string.Empty,
                Action = ComScriptRunner.GetString(item, "action") ?? string.Empty,
                ObjectName = ComScriptRunner.GetString(item, "objectName") ?? string.Empty,
                Title = ComScriptRunner.GetString(item, "title") ?? string.Empty,
                Version = ComScriptRunner.GetString(item, "version") ?? string.Empty,
                Warnings = ComScriptRunner.GetStringList(item, "warnings"),
                Error = string.IsNullOrWhiteSpace(error) ? null : DescribeFileError(error!)
            });
        }
        return new ExternalProcessingsBaseResult { Success = true, Files = files };
    }

    /// <summary>Понятный текст ошибки файла (отдельно — обработка без регистрационных данных БСП).</summary>
    public static string DescribeFileError(string error)
    {
        var e = error.Trim();
        if (e.StartsWith("noinfo:", StringComparison.Ordinal))
            return string.Format(LocalizationManager.T("ExtProc.NoRegistrationInfo"), e["noinfo:".Length..].Trim());
        return e;
    }

    /// <summary>Понятный текст предупреждения по коду из COM-операции.</summary>
    public static string DescribeWarning(string warning)
    {
        if (warning.StartsWith("kind:", StringComparison.Ordinal))
            return string.Format(LocalizationManager.T("ExtProc.WarnKind"), warning["kind:".Length..]);
        if (warning.StartsWith("dest:", StringComparison.Ordinal))
            return string.Format(LocalizationManager.T("ExtProc.WarnDestination"), warning["dest:".Length..]);
        if (warning == "sections")
            return LocalizationManager.T("ExtProc.WarnSections");
        return warning;
    }

    /// <summary>Загружает файлы в одну базу (одно COM-соединение на базу).</summary>
    public static async Task<ExternalProcessingsBaseResult> LoadAsync(Infobase infobase, IReadOnlyList<string> files,
        bool update, CancellationToken ct = default)
    {
        if (infobase is null)
            return ExternalProcessingsBaseResult.Fail(LocalizationManager.T("IbUsers.NoBase"));
        var invalid = Validate(files);
        if (invalid is not null)
            return ExternalProcessingsBaseResult.Fail(invalid);

        var connection = ComConnectionInfo.Prepare(infobase);
        if (connection.Error is not null)
            return ExternalProcessingsBaseResult.Fail(connection.Error);

        var result = await ComScriptRunner.RunAsync(OperationBody, connection.ProgIds, connection.ConnectString,
            BuildParameters(files, update), new[] { connection.Password }, OperationTimeout, ct).ConfigureAwait(false);
        if (!result.Success)
            return ExternalProcessingsBaseResult.Fail(InfobaseUsersService.DescribeFailure(result));
        return ParseResult(result.Data);
    }
}
