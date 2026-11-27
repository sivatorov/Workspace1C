using System;
using System.IO;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Вид причины, по которой пакетный запуск конфигуратора не может выполнить выгрузку
/// (issue #181). Используется для выбора понятного пользователю предупреждения вместо
/// «непонятной ошибки»: при открытом конфигураторе или занятой базе/файле выгрузки
/// показывается отдельное сообщение с просьбой закрыть конфигуратор и повторить операцию.
/// </summary>
public enum DesignerFailureKind
{
    /// <summary>Причина не определена (операция не запускалась либо ошибка иного рода).</summary>
    None,

    /// <summary>Конфигуратор этой базы уже запущен (в т.ч. открыт вручную вне приложения).</summary>
    ConfiguratorRunning,

    /// <summary>Уже идёт другая выгрузка или пакетная операция конфигуратора, запущенная приложением.</summary>
    AnotherOperation,

    /// <summary>Файл информационной базы занят другим процессом (открыт конфигуратор/сеанс 1С).</summary>
    BaseLocked,

    /// <summary>Файл назначения выгрузки занят другим процессом.</summary>
    OutputFileLocked,

    /// <summary>Иная ошибка запуска/выполнения операции.</summary>
    Other
}

/// <summary>
/// Чистая логика распознавания ситуации «конфигуратор запущен / файл занят» при выгрузке
/// в <c>.cf</c>/<c>.dt</c> (issue #181). Без UI-зависимостей: используется и WPF-, и
/// Avalonia-слоем, поэтому класс собирается в обе платформенные сборки без <c>#if</c>.
/// <para>
/// Зачем нужен отдельный классификатор: <see cref="OneCLauncher.IsDesignerBlocked"/>
/// возвращает локализованную причину строкой, а ошибка 1С приходит уже после старта
/// процесса в тексте лога. Единая классификация позволяет в обоих случаях показать
/// одно и то же понятное предупреждение и предложить повторить выгрузку.
/// </para>
/// </summary>
public static class DesignerLockClassifier
{
    /// <summary>
    /// Нормализует токен подключения базы для сравнения с командной строкой процесса:
    /// снимает кавычки и префикс <c>File=</c>, приводит разделители к <c>\</c>,
    /// убирает хвостовой разделитель и приводит к нижнему регистру.
    /// </summary>
    public static string NormalizeBaseToken(string? token)
    {
        var value = (token ?? string.Empty).Trim();
        if (value.Length == 0)
            return string.Empty;

        // Формы подключения: «C:\Base», «File="C:\Base"», «Srvr="srv";Ref="base"».
        if (value.StartsWith("File=", StringComparison.OrdinalIgnoreCase))
            value = value.Substring("File=".Length);

        value = value.Trim().Trim('"').Replace('/', '\\').TrimEnd('\\');
        return value.ToLowerInvariant();
    }

    /// <summary>
    /// Проверяет, относится ли командная строка процесса конфигуратора к указанной базе.
    /// Поддерживаются файловая форма (<c>/F"C:\Base"</c>, <c>File="C:\Base"</c>) и
    /// клиент-серверная (<c>/S"server\base"</c>, <c>Srvr="server";Ref="base"</c>).
    /// </summary>
    public static bool CommandLineMatchesBase(string? commandLine, string? baseToken)
    {
        var token = NormalizeBaseToken(baseToken);
        if (token.Length < 2 || string.IsNullOrWhiteSpace(commandLine))
            return false;

        var command = commandLine.Replace('/', '\\').ToLowerInvariant();
        if (command.Contains(token, StringComparison.Ordinal))
            return true;

        // Клиент-серверная база: токен вида «server\base», в командной строке —
        // «Srvr="server";Ref="base"». Проверяем сервер и имя базы по отдельности.
        var separator = token.IndexOf('\\');
        if (separator <= 0 || separator >= token.Length - 1)
            return false;

        var server = token.Substring(0, separator);
        var database = token.Substring(separator + 1);
        if (!command.Contains(server, StringComparison.Ordinal))
            return false;

        return command.Contains("ref=\"" + database, StringComparison.Ordinal) ||
               command.Contains("ref=" + database, StringComparison.Ordinal) ||
               command.Contains("\\" + database, StringComparison.Ordinal);
    }

    /// <summary>
    /// Проверяет, занят ли файл другим процессом (эксклюзивное открытие невозможно).
    /// Отсутствующий/пустой путь не считается занятым: файл выгрузки ещё не создан.
    /// </summary>
    public static bool IsFileLocked(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Файл существует, но открыть его на запись нельзя — для выгрузки это
            // равнозначно занятости (например, файл только для чтения/чужой сеанс).
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Проверяет, занят ли файл информационной базы (для файловой ИБ — <c>1Cv8.1CD</c>).
    /// Занятость означает открытый конфигуратор или активный сеанс 1С: пакетная выгрузка
    /// требует монопольного доступа и в такой ситуации завершится ошибкой.
    /// </summary>
    public static bool IsInfobaseLocked(Infobase? infobase)
    {
        if (infobase is null)
            return false;

        var connection = infobase.Connection;
        if (connection is null || connection.Type != ConnectionType.File)
            return false;

        var path = (connection.FilePath ?? string.Empty).Trim().Trim('"');
        if (path.Length == 0)
            return false;

        string marker;
        if (File.Exists(path))
        {
            marker = path;
        }
        else
        {
            var databaseFile = Path.Combine(path, "1Cv8.1CD");
            if (!File.Exists(databaseFile))
                return false;
            marker = databaseFile;
        }

        return IsFileLocked(marker);
    }

    /// <summary>
    /// Классифицирует причину по тексту отказа запуска или сообщению 1С из лога.
    /// Понимает локализованные тексты приложения (ru/en) и типовые формулировки платформы
    /// о монопольном доступе/занятом файле.
    /// </summary>
    public static DesignerFailureKind Classify(string? errorText)
    {
        if (string.IsNullOrWhiteSpace(errorText))
            return DesignerFailureKind.None;

        var text = errorText.ToLowerInvariant();

        if (ContainsAny(text,
                "конфигуратор этой базы уже запущен",
                "configurator for this infobase is already running",
                "configurator for this database is already running"))
            return DesignerFailureKind.ConfiguratorRunning;

        if (ContainsAny(text,
                "уже запущена другая выгрузка",
                "another export or configurator operation is already running"))
            return DesignerFailureKind.AnotherOperation;

        if (ContainsAny(text,
                "монопольн",
                "exclusive access",
                "exclusive lock",
                "база занята",
                "infobase is busy",
                "database is busy",
                "не удалось заблокировать",
                "cannot lock",
                "could not lock",
                "файл базы данных занят",
                "database file is in use",
                "ib is locked",
                "infobase is locked"))
            return DesignerFailureKind.BaseLocked;

        if (ContainsAny(text,
                "файл назначения занят",
                "output file is locked",
                "file is used by another process",
                "being used by another process",
                "used by another process",
                "файл используется другим процессом",
                "используется другим процессом"))
            return DesignerFailureKind.OutputFileLocked;

        return DesignerFailureKind.Other;
    }

    /// <summary>Относится ли причина к блокировке (конфигуратор/занятая база/занятый файл).</summary>
    public static bool IsLockRelated(DesignerFailureKind kind) => kind is
        DesignerFailureKind.ConfiguratorRunning or
        DesignerFailureKind.AnotherOperation or
        DesignerFailureKind.BaseLocked or
        DesignerFailureKind.OutputFileLocked;

    private static bool ContainsAny(string text, params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            if (text.Contains(pattern, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
