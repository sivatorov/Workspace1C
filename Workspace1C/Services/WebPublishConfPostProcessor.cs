using System;
using System.Collections.Generic;
using System.Text;

namespace Workspace1C.Services;

/// <summary>
/// Пост-обработка конфигурации Apache после успешной публикации webinst
/// (issue #63, комментарии 7OH от 2026-10-30). Чистые функции без I/O:
/// <list type="bullet">
/// <item><see cref="DeduplicateLoadModule"/> — схлопывание дубликатов строки
/// <c>LoadModule _1cws_module</c> (webinst дописывает её в <c>-confpath</c>, хотя в
/// основном конфиге она уже есть: «Не надо писать в файл эту строку… или можно
/// проверить минимум - есть ли "LoadModule _1cws_module"»);</item>
/// <item><see cref="RewriteAliasBlockWithVariables"/> — переписывание путей нового
/// блока алиаса через <c>${…}</c>-переменные «как тут заведено» — только при
/// ОДНОЗНАЧНОМ совпадении значения переменной с префиксом физического пути и при
/// наличии существующих записей с этой переменной (иначе конфиг не трогаем —
/// защита от поломки Apache).</item>
/// </list>
/// </summary>
public static class WebPublishConfPostProcessor
{
    /// <summary>Префикс строки подключения модуля web-сервера 1С (issue #63).</summary>
    public const string LoadModuleMarker = "LoadModule _1cws_module";

    /// <summary>
    /// Схлопывает дубликаты строк <c>LoadModule _1cws_module …</c>: оставляет ПЕРВОЕ
    /// вхождение, последующие удаляет (вместе со своими переводами строк). Возвращает
    /// новый текст и число удалённых дубликатов. Если строки нет или она одна — текст
    /// возвращается без изменений. Internal/сущностно чистая — юнит-тесты.
    /// </summary>
    public static (string Text, int RemovedDuplicates) DeduplicateLoadModule(string? confText)
    {
        if (string.IsNullOrEmpty(confText))
            return (confText ?? string.Empty, 0);

        var sb = new StringBuilder(confText.Length);
        var seen = false;
        var removed = 0;

        foreach (var (line, terminator) in SplitLinesWithTerminators(confText))
        {
            if (IsLoadModule1CwsLine(line))
            {
                if (seen)
                {
                    removed++;
                    continue; // дубликат — строка и её перевод строки не пишутся.
                }
                seen = true;
            }
            sb.Append(line).Append(terminator);
        }

        return removed > 0 ? (sb.ToString(), removed) : (confText, 0);
    }

    /// <summary>
    /// Переписывает пути НОВОГО блока алиаса (<paramref name="virtualDirectory"/>) через
    /// <c>${…}</c>-переменную (issue #63, «ТАК ТУТ ЗАВЕДЕНО - с ПЕРЕМЕННОЙ»). Замена
    /// выполняется только при выполнении ВСЕХ условий:
    /// <list type="bullet">
    /// <item>в конфиге найден блок <c>Alias "/<wsdir>" "<физический путь>"</c> …
    /// <c></Directory></c>;</item>
    /// <item>существует РОВНО ОДНА переменная <c>Define</c>, развёрнутое значение которой —
    /// точный префикс физического пути нового блока;</item>
    /// <item>в КОНФИГЕ уже есть другие записи (Alias/Directory и т.п.) с
    /// <c>${эта переменная}</c> — то есть «в файле так заведено».</item>
    /// </list>
    /// В нового блока переписываются пути в строках Alias, <Directory> и
    /// ManagedApplicationDescriptor (везде, где встречается развернутое значение
    /// переменной). Иначе текст возвращается без изменений (<paramref name="changed"/> =
    /// false). Регистр и вид разделителей пути ( «/» и «\») не влияют на совпадение.
    /// </summary>
    public static (string Text, bool Changed) RewriteAliasBlockWithVariables(
        string? confText,
        string virtualDirectory,
        IReadOnlyDictionary<string, string>? defines)
    {
        if (string.IsNullOrEmpty(confText) ||
            string.IsNullOrWhiteSpace(virtualDirectory))
            return (confText ?? string.Empty, false);

        var vars = defines ?? ApacheConfIncludeParser.ParseDefines(confText);
        if (vars.Count == 0)
            return (confText, false);

        var aliasName = "/" + virtualDirectory.Trim().Trim('/');
        var lines = SplitLinesWithTerminators(confText);

        // Последнее вхождение Alias "<wsdir>" — свежий блок, добавленный webinst.
        var aliasIndex = -1;
        var physicalInConf = string.Empty;
        for (var i = 0; i < lines.Count; i++)
        {
            var path = TryGetAliasPhysicalPath(lines[i].Line, aliasName);
            if (path is not null)
            {
                aliasIndex = i;
                physicalInConf = path;
            }
        }
        if (aliasIndex < 0 || physicalInConf.Length == 0)
            return (confText, false);

        var endIndex = FindBlockEnd(lines, aliasIndex);

        // Переменная-префикс физического пути, которая УЖЕ используется в существующих
        // записях публикации (Alias/Directory/ManagedApplicationDescriptor) вне нового
        // блока — «как тут заведено». Ровно одна такая переменная — иначе (0 или >1)
        // конфиг не трогаем (защита от неоднозначной подстановки и поломки Apache).
        var variableName = FindUniqueUsedPrefixVariable(lines, aliasIndex, endIndex, physicalInConf, vars);
        if (variableName is null)
            return (confText, false);

        var expanded = NormalizeSlashes(ApacheConfIncludeParser.ExpandVariables(vars[variableName], vars));

        var sb = new StringBuilder(confText.Length);
        for (var i = 0; i < lines.Count; i++)
        {
            var (line, terminator) = lines[i];
            if (i >= aliasIndex && i <= endIndex)
                line = ReplaceIgnoreCase(line, expanded, "${" + variableName + "}");
            sb.Append(line).Append(terminator);
        }

        var result = sb.ToString();
        return (result, !string.Equals(result, confText, StringComparison.Ordinal));
    }

    /// <summary>
    /// Возвращает физический путь из строки <c>Alias "<имя>" "<путь>"</c>, если
    /// имя совпадает с <paramref name="aliasName"/> (регистронезависимо); иначе null.
    /// </summary>
    private static string? TryGetAliasPhysicalPath(string line, string aliasName)
    {
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith("Alias", StringComparison.OrdinalIgnoreCase))
            return null;
        var rest = trimmed["Alias".Length..].Trim();
        if (!rest.StartsWith("\"", StringComparison.Ordinal))
            return null;

        var nameEnd = rest.IndexOf('"', 1);
        if (nameEnd < 0)
            return null;
        var name = rest[1..nameEnd];
        if (!string.Equals(name.Trim(), aliasName, StringComparison.OrdinalIgnoreCase))
            return null;

        var remainder = rest[(nameEnd + 1)..].Trim();
        if (!remainder.StartsWith("\"", StringComparison.Ordinal))
            return null;
        var pathEnd = remainder.IndexOf('"', 1);
        if (pathEnd < 0)
            return null;
        var path = remainder[1..pathEnd];
        return path.Trim().Length > 0 ? path.Trim() : null;
    }

    /// <summary>
    /// Единственная переменная, которая (а) развёрнутым значением — точный префикс
    /// <paramref name="physicalPath"/> (не весь путь и не корень; сразу после префикса —
    /// разделитель пути) и (б) УЖЕ используется в существующих записях публикации
    /// (Alias/Directory/ManagedApplicationDescriptor) вне нового блока. 0 или больше
    /// одной такой переменной → null (защита от неоднозначной подстановки). Отбор по
    /// фактическому использованию отсекает корневые переменные вроде SRVROOT, которые
    /// формально префиксуют любой путь, но в записях публикации не применяются.
    /// </summary>
    private static string? FindUniqueUsedPrefixVariable(
        IReadOnlyList<(string Line, string Terminator)> lines,
        int blockStart, int blockEnd,
        string physicalPath, IReadOnlyDictionary<string, string> vars)
    {
        var normalizedPhysical = NormalizeSlashes(physicalPath);
        string? found = null;
        foreach (var pair in vars)
        {
            var expanded = NormalizeSlashes(
                ApacheConfIncludeParser.ExpandVariables(pair.Value ?? string.Empty, vars));
            if (expanded.Length == 0 ||
                expanded == "/" ||
                expanded == normalizedPhysical || // переменная должна быть только префиксом
                !normalizedPhysical.StartsWith(expanded, StringComparison.OrdinalIgnoreCase))
                continue;

            // Сразу после префикса должен идти разделитель пути — иначе это совпадение
            // «по строке», а не по каталогу («htdocs/27» не должен совпасть с «htdocs/270»).
            var next = normalizedPhysical[expanded.Length];
            if (next is not '/' and not '\\')
                continue;

            if (!VariableUsedInPublicationLines(lines, blockStart, blockEnd, pair.Key))
                continue;

            if (found is not null)
                return null; // неоднозначно — две переменные подходят под все условия.
            found = pair.Key;
        }
        return found;
    }

    /// <summary>
    /// Используется ли <c>${name}</c> в существующих записях публикации (строки Alias,
    /// <Directory>, ManagedApplicationDescriptor) вне диапазона нового блока.
    /// </summary>
    private static bool VariableUsedInPublicationLines(
        IReadOnlyList<(string Line, string Terminator)> lines, int blockStart, int blockEnd, string name)
    {
        var marker = "${" + name + "}";
        for (var i = 0; i < lines.Count; i++)
        {
            if (i >= blockStart && i <= blockEnd)
                continue;
            var line = lines[i].Line;
            if (!line.Contains(marker, StringComparison.OrdinalIgnoreCase))
                continue;
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("Alias", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("<Directory", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("ManagedApplicationDescriptor", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Строка <c></Directory></c>, закрывающая блок, начавшийся на <paramref name="startIndex"/>.</summary>
    private static int FindBlockEnd(IReadOnlyList<(string Line, string Terminator)> lines, int startIndex)
    {
        var opened = false;
        for (var i = startIndex; i < lines.Count; i++)
        {
            var trimmed = lines[i].Line.TrimStart();
            if (!opened && trimmed.StartsWith("<Directory", StringComparison.OrdinalIgnoreCase))
                opened = true;
            if (opened && trimmed.StartsWith("</Directory", StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return lines.Count - 1; // незакрытый блок — до конца файла.
    }

    /// <summary>Замена всех вхождений <paramref name="oldValue"/> без учёта регистра; если не
    /// нашлось — повторная попытка с противоположным видом разделителей пути.</summary>
    private static string ReplaceIgnoreCase(string text, string oldValue, string newValue)
    {
        if (oldValue.Length == 0)
            return text;

        var result = ReplaceAllIgnoreCase(text, oldValue, newValue);
        if (!ReferenceEquals(result, text))
            return result;

        // Попытка с другим видом разделителей («c:/Apache/...» vs «c:\Apache\...»).
        var swapped = oldValue.Replace('/', '\\');
        if (string.Equals(swapped, oldValue, StringComparison.Ordinal))
            swapped = oldValue.Replace('\\', '/');
        return string.Equals(swapped, oldValue, StringComparison.OrdinalIgnoreCase)
            ? text
            : ReplaceAllIgnoreCase(text, swapped, newValue);
    }

    /// <summary>Замена всех вхождений без учёта регистра; если вхождений нет — возвращает тот же экземпляр строки.</summary>
    private static string ReplaceAllIgnoreCase(string text, string oldValue, string newValue)
    {
        var sb = new StringBuilder(text.Length);
        var index = 0;
        var replaced = false;
        while (index < text.Length)
        {
            var found = text.IndexOf(oldValue, index, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                sb.Append(text, index, text.Length - index);
                break;
            }
            sb.Append(text, index, found - index).Append(newValue);
            index = found + oldValue.Length;
            replaced = true;
        }
        return replaced ? sb.ToString() : text;
    }

    /// <summary>Нормализация разделителей пути для сравнения: «\» → «/».</summary>
    private static string NormalizeSlashes(string value) =>
        (value ?? string.Empty).Trim().Replace('\\', '/').TrimEnd('/');

    /// <summary>Является ли строка записью <c>LoadModule _1cws_module …</c> (регистр не важен).</summary>
    private static bool IsLoadModule1CwsLine(string line)
    {
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith("LoadModule", StringComparison.OrdinalIgnoreCase))
            return false;
        var rest = trimmed["LoadModule".Length..];
        if (!rest.StartsWith(" ", StringComparison.Ordinal) && !rest.StartsWith("\t", StringComparison.Ordinal))
            return false;
        // Второй токен — имя модуля: именно _1cws_module (другие LoadModule не трогаем).
        rest = rest.TrimStart();
        if (!rest.StartsWith("_1cws_module", StringComparison.OrdinalIgnoreCase))
            return false;
        var tail = rest["_1cws_module".Length..];
        return tail.Length == 0 || char.IsWhiteSpace(tail[0]);
    }

    /// <summary>
    /// Разбивает текст на строки с СОХРАНЕНИЕМ переводов строк (CRLF/LF/CR — как есть),
    /// чтобы пост-обработка не меняла стиль конца строк исходного файла.
    /// </summary>
    private static List<(string Line, string Terminator)> SplitLinesWithTerminators(string text)
    {
        var result = new List<(string, string)>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not '\r' and not '\n')
                continue;
            var end = i;
            var terminatorLength = text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
            result.Add((text[start..end], text.Substring(i, terminatorLength)));
            i += terminatorLength - 1;
            start = i + 1;
        }
        if (start < text.Length)
            result.Add((text[start..], string.Empty));
        return result;
    }
}
