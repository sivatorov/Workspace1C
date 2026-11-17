using System;
using System.Text;

namespace Workspace1C.Services;

/// <summary>
/// Чистый разбор пользователя из параметра <c>/N</c> командной строки процесса 1С
/// (issue #134): «Инспектор процессов 1С» показывает в колонке «Пользователь» имя
/// из командной строки, когда владельца процесса определить не удалось.
/// <para>
/// Поддерживаются формы <c>/N"superadmin"</c>, <c>/N superadmin</c>,
/// <c>/Nsuperadmin</c> и <c>-N</c>; регистр ключа не важен, кавычки и пробелы
/// обрабатываются (в том числе удвоенные кавычки). Ключ должен стоять отдельным
/// токеном: «/N» внутри пути или другого ключа (например «/Out/Nfoo») не считается.
/// </para>
/// Не зависит от платформы и UI — покрыта юнит-тестами.
/// </summary>
public static class ProcessCommandLineUserParser
{
    /// <summary>
    /// Извлекает имя пользователя из параметра командной строки 1С <c>/N</c>.
    /// Возвращает null, если ключ отсутствует или его значение пустое.
    /// </summary>
    public static string? ExtractUserName(string? commandLine) =>
        TryExtractUserName(commandLine, out var userName) ? userName : null;

    /// <summary>
    /// Пытается извлечь имя пользователя из параметра командной строки 1С <c>/N</c>
    /// (issue #134). <paramref name="userName"/> — найденное непустое значение;
    /// false — ключа нет либо значение пустое.
    /// </summary>
    public static bool TryExtractUserName(string? commandLine, out string? userName)
    {
        userName = null;
        var line = commandLine ?? string.Empty;

        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] != '/' && line[i] != '-')
                continue;

            // Начало токена: начало строки, пробел/табуляция или кавычка перед ключом.
            if (i > 0 && !char.IsWhiteSpace(line[i - 1]) && line[i - 1] != '"')
                continue;

            if (i + 1 >= line.Length || (line[i + 1] != 'N' && line[i + 1] != 'n'))
                continue;

            var value = ReadValue(line, i + 2);
            if (!string.IsNullOrWhiteSpace(value))
            {
                userName = value;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Значение параметра после ключа: остаток до следующего ключевого токена
    /// (пробел + «/» или «-») либо содержимое кавычек. Пусто/null — значения нет
    /// (сразу следующий ключ или конец строки).
    /// </summary>
    private static string? ReadValue(string line, int start)
    {
        var index = start;
        while (index < line.Length && char.IsWhiteSpace(line[index]))
            index++;

        if (index >= line.Length)
            return null;

        // Сразу следующий ключ — значения нет.
        if (line[index] == '/' || line[index] == '-')
            return null;

        if (line[index] == '"')
        {
            index++;
            var value = new StringBuilder();
            while (index < line.Length)
            {
                if (line[index] == '"')
                {
                    // Удвоенная кавычка внутри значения — литеральная кавычка.
                    if (index + 1 < line.Length && line[index + 1] == '"')
                    {
                        value.Append('"');
                        index += 2;
                        continue;
                    }

                    index++;
                    break;
                }

                value.Append(line[index]);
                index++;
            }

            return value.ToString().Trim();
        }

        // Без кавычек значение заканчивается перед следующим ключом («/F», «-N» и т.п.),
        // поэтому имя с пробелами («Иван Иванович») сохраняется целиком.
        var end = line.Length;
        for (var i = index + 1; i < line.Length; i++)
        {
            if ((line[i] == '/' || line[i] == '-')
                && (char.IsWhiteSpace(line[i - 1]) || line[i - 1] == '"'))
            {
                end = i;
                break;
            }
        }

        return line[index..end].Trim().Trim('"').Trim();
    }
}
