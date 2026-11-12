using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Операции с расширениями конфигурации (.cfe) через пакетный режим конфигуратора
/// (выпуск 1.0.0.69): список расширений базы, загрузка/обновление расширения
/// (<c>/LoadCfg -Extension</c> + <c>/UpdateDBCfg -Extension</c>), выгрузка в файл
/// (<c>/DumpCfg -Extension</c>) и удаление (<c>/DeleteCfg -Extension</c>).
/// Построение ключей — чистые функции (покрыты тестами), запуск —
/// <see cref="DesignerCommandRunner"/>.
/// </summary>
public static class ConfigExtensionService
{
    /// <summary>
    /// Ключи загрузки расширения из файла и применения его к базе данных.
    /// Issue #87 («Загрузка расширения конфигурации в клиент-серверную базу»):
    /// у <c>/UpdateDBCfg</c> ключа <c>-Extension</c> НЕТ (он определен для
    /// <c>/LoadCfg</c>/<c>/DumpCfg</c>) — повторный <c>-Extension</c> у операции
    /// применения даёт «Error in command-line options» при запуске конфигуратора
    /// на клиент-серверной базе. Расширение, загруженное через
    /// <c>/LoadCfg "файл.cfe" -Extension "имя"</c>, применяется к базе данных
    /// последующим <c>/UpdateDBCfg</c> (без повторения ключа).
    /// </summary>
    public static string? BuildInstallArguments(string cfePath, string extensionName)
    {
        if (!DesignerCommandRunner.IsSafeValue(cfePath) || !DesignerCommandRunner.IsSafeValue(extensionName))
            return null;
        var name = extensionName.Trim();
        return $"/LoadCfg\"{cfePath}\" -Extension \"{name}\" /UpdateDBCfg";
    }

    /// <summary>Ключи выгрузки расширения в файл .cfe.</summary>
    public static string? BuildDumpArguments(string cfePath, string extensionName)
    {
        if (!DesignerCommandRunner.IsSafeValue(cfePath) || !DesignerCommandRunner.IsSafeValue(extensionName))
            return null;
        return $"/DumpCfg\"{cfePath}\" -Extension \"{extensionName.Trim()}\"";
    }

    /// <summary>Ключи удаления расширения из базы.</summary>
    public static string? BuildDeleteArguments(string extensionName)
    {
        if (!DesignerCommandRunner.IsSafeValue(extensionName))
            return null;
        return $"/DeleteCfg -Extension \"{extensionName.Trim()}\"";
    }

    /// <summary>Ключи вывода списка расширений базы данных в файл /Out.</summary>
    public const string ListArguments = "/DumpDBCfgList -AllExtensions";

    /// <summary>
    /// Имя расширения по умолчанию из имени файла: «Доработки_1.2.cfe» → «Доработки_1.2».
    /// </summary>
    public static string SuggestName(string? cfePath)
    {
        if (string.IsNullOrWhiteSpace(cfePath))
            return string.Empty;
        return Path.GetFileNameWithoutExtension(cfePath.Trim());
    }

    /// <summary>
    /// Автоподстановка имени по файлу: сначала НАСТОЯЩЕЕ имя расширения из .cfe
    /// (issue #117), затем, если прочитать его не удалось, имя файла без расширения.
    /// </summary>
    public static string ResolveName(string? cfePath) =>
        TryReadExtensionName(cfePath) ?? SuggestName(cfePath);

    /// <summary>
    /// Issue #117 (защита от дубля): имя, под которым выполняется установка расширения.
    /// По умолчанию — НАСТОЯЩЕЕ имя расширения из .cfe (тогда <c>/LoadCfg -Extension</c>
    /// обновит уже установленное расширение вместо создания копии под именем файла).
    /// Если пользователь ввёл имя вручную — остаётся введённое имя (отдельное расширение,
    /// в журнале выводится предупреждение). Если настоящее имя прочитать не удалось —
    /// введённое/автоподставленное имя файла. Чистая функция — для юнит-тестов.
    /// </summary>
    public static string ResolveInstallName(string? cfeRealName, string enteredName, bool nameEdited)
    {
        if (string.IsNullOrWhiteSpace(cfeRealName) || nameEdited ||
            string.Equals(cfeRealName, enteredName, StringComparison.OrdinalIgnoreCase))
            return enteredName;
        return cfeRealName;
    }

    // ---------------------------------------------------------------------------
    // Чтение настоящего имени расширения из .cfe (issue #117).
    //
    // Файл .cfe — НЕ zip-архив, а нативный контейнер 1С (как .cf): страница-заголовок
    // с сигнатурой 0x7FFFFFFF, дальше блоки с ASCII-заголовками вида «0d0a + hex-размеры».
    // Элементы контейнера (таблица адресов, заголовки и данные элементов) сжаты deflate;
    // корневой элемент метаданных содержит текстовое дерево, в котором после GUID-класса
    // «Конфигурация» (9cd510cd-abfc-11d4-9434-004095e12fc7) идёт первое строковое
    // свойство — имя расширения: «{1,0,<guid>},"НастоящееИмя"». Формат проверен на
    // реальных .cfe (платформа 8.3.x); служебный индекс контейнера называется
    // «configinfo» и содержит только список GUID-файлов с их хэшами.
    // ---------------------------------------------------------------------------

    /// <summary>GUID-класс «Конфигурация» в текстовом дереве корневого элемента .cfe.</summary>
    private const string ConfigurationClassGuid = "9cd510cd-abfc-11d4-9434-004095e12fc7";

    /// <summary>Сигнатура нативного контейнера 1С (первое LE-слово файла .cfe).</summary>
    private const uint ContainerSignature = 0x7FFFFFFF;

    /// <summary>Размер ASCII-заголовка блока контейнера 1С.</summary>
    private const int BlockHeaderSize = 31;

    /// <summary>Заголовок элемента контейнера: даты создания/изменения + 4 нулевых байта.</summary>
    private const int ElementHeaderSize = 20;

    /// <summary>
    /// Читает НАСТОЯЩЕЕ имя расширения из файла .cfe (issue #117): при выборе файла
    /// «Обзором» пользователь должен увидеть имя, под которым расширение установлено
    /// в базе, а не имя файла. Возвращает <c>null</c>, если файл не распознан или имя
    /// прочитать не удалось (тогда callers откатываются к имени файла).
    /// </summary>
    public static string? TryReadExtensionName(string? cfePath)
    {
        if (string.IsNullOrWhiteSpace(cfePath))
            return null;
        try
        {
            if (!File.Exists(cfePath))
                return null;
            var bytes = File.ReadAllBytes(cfePath);
            if (bytes.Length < BlockHeaderSize + 16 || BitConverter.ToUInt32(bytes, 0) != ContainerSignature)
                return null;

            var elements = new List<ContainerElement>();
            if (!ReadContainerElements(bytes, elements, maxDepth: 1))
                return null;

            // Приоритет 1: текущие версии элементов (имена без суффикса «.N») компактного
            // размера — корневой элемент метаданных всегда невелик. Приоритет 2: все
            // элементы (на случай другой раскладки контейнера).
            foreach (var candidates in new Func<ContainerElement, bool>[]
            {
                e => !IsVersionedElement(e.Name) && e.Data is { Length: > 60 } && e.Data.Length <= 1024 * 1024,
                e => e.Data is { Length: > 60 }
            })
            {
                foreach (var element in elements)
                {
                    if (!candidates(element))
                        continue;
                    var name = ExtractExtensionName(element.Data!);
                    if (name is not null)
                        return name;
                }
            }
            return null;
        }
        catch
        {
            // Любая ошибка чтения — откат к имени файла (не мешаем установке).
            return null;
        }
    }

    private sealed class ContainerElement
    {
        public string Name = string.Empty;
        public byte[]? Data;
    }

    /// <summary>Истина, если имя элемента имеет суффикс версии «.N» (старая версия файла).</summary>
    private static bool IsVersionedElement(string name)
    {
        var dot = name.LastIndexOf('.');
        if (dot < 0 || dot == name.Length - 1)
            return false;
        for (var i = dot + 1; i < name.Length; i++)
            if (!char.IsAsciiDigit(name[i]))
                return false;
        return true;
    }

    /// <summary>Извлекает имя расширения из текстового дерева корневого элемента.</summary>
    private static string? ExtractExtensionName(byte[] data)
    {
        try
        {
            var text = Encoding.UTF8.GetString(data);
            var classPos = text.IndexOf(ConfigurationClassGuid, StringComparison.OrdinalIgnoreCase);
            if (classPos < 0)
                return null;
            // После GUID-класса «Конфигурация» первое строковое свойство — имя:
            // …9cd510cd-…,{1,{79,{0,{4,{1,0,<guid>},"ИмяРасширения",{1,"ru",…
            var match = Regex.Match(text.Substring(classPos), @"\{1,0,[0-9a-f-]{36}\},""([^""\r\n]{1,100})""");
            if (!match.Success)
                return null;
            var name = match.Groups[1].Value.Trim().TrimEnd('\0');
            if (name.Length == 0 || name.IndexOf('"') >= 0)
                return null;
            return name;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Разбирает контейнер 1С (включая вложенные до <paramref name="maxDepth"/>) и
    /// наполняет <paramref name="elements"/> парами «имя элемента — распакованные данные».
    /// </summary>
    private static bool ReadContainerElements(byte[] bytes, List<ContainerElement> elements, int maxDepth)
    {
        // Заголовок контейнера: 16 байт (сигнатура, размер страницы, версия, резерв),
        // затем блок-таблица адресов элементов.
        var table = ReadBlock(bytes, 16);
        if (table is null)
            return false;
        table = TryInflate(table);

        for (var offset = 0; offset + 12 <= table.Length; offset += 12)
        {
            var signature = BitConverter.ToUInt32(table, offset + 8);
            if (signature != ContainerSignature)
                break;
            var headerAddr = BitConverter.ToUInt32(table, offset);
            var dataAddr = BitConverter.ToUInt32(table, offset + 4);
            if (headerAddr == ContainerSignature || headerAddr == 0 || dataAddr >= bytes.Length)
                break;

            var elementHeader = TryInflate(ReadBlock(bytes, (int)headerAddr) ?? Array.Empty<byte>());
            var elementData = ReadBlock(bytes, (int)dataAddr);
            if (elementData is null)
                continue;

            var element = new ContainerElement
            {
                Name = GetElementName(elementHeader),
                Data = TryInflate(elementData)
            };
            elements.Add(element);

            // Вложенный контейнер (например, «<guid>.2» — текущая версия файла объекта).
            if (maxDepth > 0 && element.Data.Length > 16 &&
                BitConverter.ToUInt32(element.Data, 0) == ContainerSignature)
            {
                ReadContainerElements(element.Data, elements, maxDepth - 1);
            }
        }
        return true;
    }

    /// <summary>Имя элемента контейнера: UTF-16LE после 20-байтового заголовка.</summary>
    private static string GetElementName(byte[] elementHeader)
    {
        if (elementHeader.Length <= ElementHeaderSize)
            return string.Empty;
        var charCount = (elementHeader.Length - ElementHeaderSize) / 2;
        if (charCount > 256)
            charCount = 256;
        return Encoding.Unicode.GetString(elementHeader, ElementHeaderSize, charCount * 2)
            .TrimEnd('\0').TrimEnd(' ');
    }

    /// <summary>
    /// Читает блок контейнера по адресу его ASCII-заголовка, поддерживая блоки,
    /// разбитые на несколько страниц (переход по адресу следующей страницы).
    /// </summary>
    private static byte[]? ReadBlock(byte[] bytes, int addr)
    {
        if (addr < 0 || addr + BlockHeaderSize > bytes.Length)
            return null;
        var header = Encoding.ASCII.GetString(bytes, addr, BlockHeaderSize);
        if (!header.StartsWith("\r\n", StringComparison.Ordinal))
            return null;
        if (!TryParseHex(header, 2, 8, out var dataSize) ||
            !TryParseHex(header, 11, 8, out var pageSize) ||
            !TryParseHex(header, 20, 8, out var nextAddr))
            return null;
        if (dataSize < 0 || dataSize > bytes.Length)
            return null;

        using var result = new MemoryStream();
        var read = 0;
        while (read < dataSize)
        {
            var take = (int)Math.Min(pageSize, dataSize - read);
            if (take <= 0 || addr + BlockHeaderSize + read + take > bytes.Length)
                return null;
            result.Write(bytes, addr + BlockHeaderSize + read, take);
            read += take;
            if (nextAddr == ContainerSignature)
                break;
            addr = (int)nextAddr;
            if (addr < 0 || addr + BlockHeaderSize > bytes.Length)
                return null;
            header = Encoding.ASCII.GetString(bytes, addr, BlockHeaderSize);
            if (!header.StartsWith("\r\n", StringComparison.Ordinal) ||
                !TryParseHex(header, 11, 8, out pageSize) ||
                !TryParseHex(header, 20, 8, out nextAddr))
                return null;
        }
        return result.ToArray();
    }

    /// <summary>Разбор 8-символьного hex-числа из ASCII-заголовка блока.</summary>
    private static bool TryParseHex(string text, int start, int length, out long value)
    {
        value = 0;
        if (start + length > text.Length)
            return false;
        var chunk = text.Substring(start, length).Trim();
        return long.TryParse(chunk, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Распаковывает deflate-данные; если данные не сжаты — возвращает как есть.</summary>
    private static byte[] TryInflate(byte[] data)
    {
        if (data.Length < 2)
            return data;
        try
        {
            using var source = new MemoryStream(data);
            using var deflate = new DeflateStream(source, CompressionMode.Decompress);
            using var result = new MemoryStream();
            deflate.CopyTo(result);
            return result.Length > 0 ? result.ToArray() : data;
        }
        catch
        {
            return data;
        }
    }

    // ---------------------------------------------------------------------------
    // Определение фактического отказа в журнале конфигуратора (issue #117): пакетный
    // конфигуратор возвращает код 0 даже при отказе применять расширение («Загрузка
    // конфигурации успешно завершена» при неуспешном /UpdateDBCfg) — поэтому журнал
    // окна не должен утверждать «успешно», если в выводе есть сообщение об отказе.
    // ---------------------------------------------------------------------------

    /// <summary>Маркеры отказа в выводе конфигуратора (русские и английские локали).</summary>
    private static readonly string[] RefusalMarkers =
    {
        "не выполнено",
        "не может быть выполнено",
        "выполнено с ошибками",
        "обнаружены ошибки",
        "ошибка обновления",
        "ошибка при обновлении",
        "has failed",
        "cannot be performed",
        "cannot be completed",
        "completed with errors",
    };

    /// <summary>
    /// Ищет в выводе конфигуратора (/Out) сообщение об отказе и возвращает его строку
    /// (или <c>null</c>, если отказ не обнаружен). Чистая функция — для юнит-тестов
    /// и журнала результатов установки расширений.
    /// </summary>
    public static string? FindRefusalMessage(string? log)
    {
        if (string.IsNullOrWhiteSpace(log))
            return null;
        foreach (var raw in log.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            foreach (var marker in RefusalMarkers)
            {
                if (line.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    return line;
            }
        }
        return null;
    }

    /// <summary>
    /// Разбор вывода <c>/DumpDBCfgList</c>: имена расширений по одному в строке.
    /// Служебные строки конфигуратора (пустые, с двоеточием в начале, сообщения
    /// о завершении) отбрасываются; повторы убираются.
    /// </summary>
    public static IReadOnlyList<string> ParseList(string? log)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(log))
            return result;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in log.Split('\n'))
        {
            var line = raw.Trim().Trim('﻿');
            if (line.Length == 0)
                continue;
            if (line.StartsWith(":", StringComparison.Ordinal) || line.EndsWith(".", StringComparison.Ordinal))
                continue;
            if (line.Contains(' ') && !line.Contains('_') && line.Split(' ').Length > 3)
                continue; // похоже на фразу-сообщение, а не на имя (имена — идентификаторы)
            if (seen.Add(line))
                result.Add(line);
        }
        return result;
    }

    public static async Task<DesignerCommandResult> ListAsync(Infobase infobase, CancellationToken ct = default) =>
        await DesignerCommandRunner.RunAsync(infobase, ListArguments, TimeSpan.FromMinutes(10), ct).ConfigureAwait(false);

    public static async Task<DesignerCommandResult> InstallAsync(Infobase infobase, string cfePath, string extensionName,
        CancellationToken ct = default)
    {
        var result = await InstallCoreAsync(infobase, cfePath, extensionName, ct).ConfigureAwait(false);
        Audit.AuditHub.Write(Audit.AuditActions.ExtensionInstall, infobase?.Name ?? string.Empty, result.Success,
            $"{extensionName} ← {cfePath}" + (result.Success ? string.Empty : "; " + result.Error));
        return result;
    }

    private static async Task<DesignerCommandResult> InstallCoreAsync(Infobase infobase, string cfePath, string extensionName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cfePath) || !File.Exists(cfePath))
            return DesignerCommandResult.Fail(LocalizationManager.T("Extensions.FileNotFound"));
        var args = BuildInstallArguments(cfePath, extensionName);
        if (args is null)
            return DesignerCommandResult.Fail(LocalizationManager.T("Extensions.BadName"));
        return await DesignerCommandRunner.RunAsync(infobase, args, null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Признак отказа конфигуратора по причине «Error in command-line options» (issue #87):
    /// конфигуратор не принял строку параметров (например, на клиент-серверной базе).
    /// Возвращает <c>true</c>, если результат неуспешен и в тексте ошибки/вывода есть
    /// признак ошибки параметров командной строки. Чистая функция — для юнит-тестов
    /// и автоматического fallback'а на ibcmd-ветку.
    /// </summary>
    public static bool IsCommandLineOptionsError(DesignerCommandResult result)
    {
        if (result is null || result.Success || result.ExitCode == 0)
            return false;
        var text = (result.Error ?? string.Empty) + "\n" + (result.Log ?? string.Empty);
        return text.IndexOf("command-line options", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("command line options", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Признак отказа конфигуратора по причине сбоя аутентификации (issue #87): база без
    /// собственных учётных данных при незаданной авторизации запуска — конфигуратор падает
    /// с «The infobase user is not authenticated». Чистая функция — для юнит-тестов
    /// и автоматического fallback'а на ibcmd-ветку.
    /// </summary>
    public static bool IsAuthenticationError(DesignerCommandResult result)
    {
        if (result is null || result.Success || result.ExitCode == 0)
            return false;
        var text = (result.Error ?? string.Empty) + "\n" + (result.Log ?? string.Empty);
        return text.IndexOf("infobase user is not authenticated", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Нужен ли автоматический переход на ibcmd-ветку после отказа конфигуратора (issue #87):
    /// либо конфигуратор не принял параметры командной строки, либо произошёл сбой
    /// аутентификации пользователя ИБ. Чистая функция — для юнит-тестов и fallback'а в
    /// <see cref="Workspace1C.ViewModels.ExtensionsViewModel"/>.
    /// </summary>
    public static bool ShouldFallbackToIbcmd(DesignerCommandResult result)
        => IsCommandLineOptionsError(result) || IsAuthenticationError(result);

    public static async Task<DesignerCommandResult> DumpAsync(Infobase infobase, string cfePath, string extensionName,
        CancellationToken ct = default)
    {
        var args = BuildDumpArguments(cfePath, extensionName);
        if (args is null)
            return DesignerCommandResult.Fail(LocalizationManager.T("Extensions.BadName"));
        var dir = Path.GetDirectoryName(cfePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var result = await DesignerCommandRunner.RunAsync(infobase, args, null, ct).ConfigureAwait(false);
        if (result.Success && (!File.Exists(cfePath) || new FileInfo(cfePath).Length == 0))
        {
            return new DesignerCommandResult
            {
                ExitCode = result.ExitCode,
                Log = result.Log,
                Error = LocalizationManager.T("Extensions.DumpEmpty")
            };
        }
        return result;
    }

    public static async Task<DesignerCommandResult> DeleteAsync(Infobase infobase, string extensionName,
        CancellationToken ct = default)
    {
        var result = await DeleteCoreAsync(infobase, extensionName, ct).ConfigureAwait(false);
        Audit.AuditHub.Write(Audit.AuditActions.ExtensionDelete, infobase?.Name ?? string.Empty, result.Success,
            extensionName + (result.Success ? string.Empty : "; " + result.Error));
        return result;
    }

    private static async Task<DesignerCommandResult> DeleteCoreAsync(Infobase infobase, string extensionName,
        CancellationToken ct)
    {
        var args = BuildDeleteArguments(extensionName);
        if (args is null)
            return DesignerCommandResult.Fail(LocalizationManager.T("Extensions.BadName"));
        return await DesignerCommandRunner.RunAsync(infobase, args, null, ct).ConfigureAwait(false);
    }
}
