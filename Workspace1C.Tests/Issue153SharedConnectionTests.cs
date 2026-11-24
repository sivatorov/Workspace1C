using System;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #153: одна и та же база заведена в списке несколько раз под разными
/// учётными записями (авторизация ОС и имя/пароль). Процесс 1С должен привязываться
/// ровно к одной записи — той, под чьими учётными данными он запущен. Иначе
/// индикатор запуска появляется сразу у двух строк, двойной щелчок активирует чужое
/// окно, а вкладка «Запущенные» показывает дубли.
/// </summary>
public sealed class Issue153SharedConnectionTests
{
    private const string ConnectionCommand =
        @"""C:\Program Files\1cv8\8.3.27.2214\bin\1cv8.exe"" ENTERPRISE /S ""srvc-1c-doc\doc3""";

    private static Infobase ServerBase(
        string id,
        string name,
        AuthenticationMode mode = AuthenticationMode.Prompt,
        string user = "",
        string database = "doc3") => new()
    {
        Id = id,
        Name = name,
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.ClientServer,
            Server = "srvc-1c-doc",
            DatabaseName = database,
            AuthenticationMode = mode,
            User = user
        }
    };

    private static Infobase OsAuthBase(string id, string name) =>
        ServerBase(id, name, AuthenticationMode.Windows);

    private static Infobase AdminBase(string id = "id-admin", string name = "1С ДО 3.0 - РАБОЧАЯ БАЗА - АДМИН") =>
        ServerBase(id, name, AuthenticationMode.Credentials, "Салихов Администратор");

    private static RunningOneCProcess Process(string? rawUser = null)
    {
        var command = ConnectionCommand;
        if (!string.IsNullOrEmpty(rawUser))
            command += " /N " + rawUser;
        return new RunningOneCProcess("1cv8.exe", command);
    }

    [Fact]
    public void MatchProcesses_ProcessWithoutUser_OnlyOsAuthenticationEntry()
    {
        var osAuth = OsAuthBase("id-os", "1С ДО 3.0 - РАБОЧАЯ БАЗА");
        var admin = AdminBase();

        var result = RunningInfobaseMatcher.MatchProcesses(
            new[] { osAuth, admin }, new[] { Process() });

        Assert.True(result.ContainsKey("id-os"));
        Assert.False(result.ContainsKey("id-admin"));
        Assert.Single(result["id-os"]);
    }

    [Fact]
    public void MatchProcesses_ProcessWithUser_OnlyCredentialsEntry()
    {
        var osAuth = OsAuthBase("id-os", "1С ДО 3.0 - РАБОЧАЯ БАЗА");
        var admin = AdminBase();

        var result = RunningInfobaseMatcher.MatchProcesses(
            new[] { osAuth, admin }, new[] { Process("Салихов Администратор") });

        Assert.False(result.ContainsKey("id-os"));
        Assert.True(result.ContainsKey("id-admin"));
        Assert.Single(result["id-admin"]);
    }

    [Fact]
    public void MatchProcesses_TwoProcessesDifferentAccounts_EachEntryGetsOwnProcess()
    {
        var osAuth = OsAuthBase("id-os", "1С ДО 3.0 - РАБОЧАЯ БАЗА");
        var admin = AdminBase();
        var processes = new[]
        {
            Process(),
            Process("Салихов Администратор")
        };

        var result = RunningInfobaseMatcher.MatchProcesses(
            new[] { osAuth, admin }, processes);

        // У каждой записи ровно один процесс, всего процессов — два.
        Assert.Single(result["id-os"]);
        Assert.Single(result["id-admin"]);
        Assert.Equal(2, result.Values.Sum(list => list.Count));

        Assert.DoesNotContain("/N", result["id-os"][0].CommandLine, StringComparison.Ordinal);
        Assert.Contains("/N", result["id-admin"][0].CommandLine, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchProcesses_DuplicateEntryWithSameId_IsNotDuplicatedInResult()
    {
        // Одна и та же запись (тот же Id) в списке дважды не должна дать
        // два одинаковых совпадения в словаре.
        var first = OsAuthBase("id-same", "База");
        var second = OsAuthBase("id-same", "База (копия)");

        var result = RunningInfobaseMatcher.MatchProcesses(
            new[] { first, second }, new[] { Process() });

        Assert.Single(result);
        Assert.Single(result["id-same"]);
    }

    [Fact]
    public void MatchProcesses_DifferentConnections_NotMixed()
    {
        var doc3 = OsAuthBase("id-doc3", "doc3");
        var other = ServerBase("id-other", "other", AuthenticationMode.Windows, database: "other");

        var result = RunningInfobaseMatcher.MatchProcesses(
            new[] { doc3, other }, new[] { Process() });

        Assert.True(result.ContainsKey("id-doc3"));
        Assert.False(result.ContainsKey("id-other"));
    }

    [Fact]
    public void MatchProcesses_UserWithSpacesQuotesAndCase_MatchesCredentialsEntry()
    {
        var osAuth = OsAuthBase("id-os", "1С ДО 3.0 - РАБОЧАЯ БАЗА");
        var admin = AdminBase();

        var result = RunningInfobaseMatcher.MatchProcesses(
            new[] { osAuth, admin },
            new[] { Process("\"  салихов администратор  \"") });

        Assert.False(result.ContainsKey("id-os"));
        Assert.True(result.ContainsKey("id-admin"));
        Assert.Single(result["id-admin"]);
    }

    [Fact]
    public void SelectOwnerForProcess_UnknownUser_PrefersPromptEntryDeterministically()
    {
        // /N есть, но ни одна запись не задаёт такого пользователя явно: имя
        // введено в окне «Запрос». Выбор детерминирован — запись без явных
        // учётных данных, независимо от порядка кандидатов.
        var prompt = ServerBase("id-prompt", "База", AuthenticationMode.Prompt);
        var admin = AdminBase();
        var commandLine = Process("Неизвестный").CommandLine;

        var first = RunningInfobaseMatcher.SelectOwnerForProcess(
            new[] { admin, prompt }, commandLine);
        var second = RunningInfobaseMatcher.SelectOwnerForProcess(
            new[] { prompt, admin }, commandLine);

        Assert.NotNull(first);
        Assert.Equal("id-prompt", first!.Id);
        Assert.Same(first, second);
    }

    [Fact]
    public void MatchProcesses_OneProcess_AssignedToAtMostOneEntry()
    {
        // Ключевой регресс issue #153: один процесс не может «зажечь» индикатор
        // у нескольких записей с общим подключением.
        var osAuth = OsAuthBase("id-os", "1С ДО 3.0 - РАБОЧАЯ БАЗА");
        var admin = AdminBase();
        var prompt = ServerBase("id-prompt", "1С ДО 3.0 - РАБОЧАЯ БАЗА (запрос)", AuthenticationMode.Prompt);

        var result = RunningInfobaseMatcher.MatchProcesses(
            new[] { osAuth, admin, prompt }, new[] { Process() });

        Assert.Single(result);
        Assert.Equal(1, result.Values.Sum(list => list.Count));
        Assert.True(result.ContainsKey("id-os"));
    }

    [Fact]
    public void FindDirectOwners_StillReturnsAllEntriesWithSameConnection_ForDiagnostics()
    {
        // Функция оставлена только для диагностики (инспектор процессов) и
        // по-прежнему показывает все записи с общим подключением.
        var osAuth = OsAuthBase("id-os", "1С ДО 3.0 - РАБОЧАЯ БАЗА");
        var admin = AdminBase();

        var owners = RunningInfobaseMatcher.FindDirectOwners(
            new[] { osAuth, admin }, Process().CommandLine);

        Assert.Equal(2, owners.Count);
        Assert.Contains(owners, ib => ib.Id == "id-os");
        Assert.Contains(owners, ib => ib.Id == "id-admin");
    }

    // ===================== Source-гварды =====================

    [Fact]
    public void ApplyRunningProcesses_BeforeAfterComparison_UsesBaseKey()
    {
        // Issue #153: у баз без Id сравнение по Id ломало пересборку списка
        // «Только запущенные». Сравнение обязано идти по ключу базы.
        var code = ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.Running.cs");
        var method = ExtractMethod(code, "private void ApplyRunningProcesses(");

        Assert.Contains("Select(RunningInfobaseMatcher.GetBaseKey)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("Select(i => i.Id)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void DoubleClick_ActivatesWindow_OnlyForCurrentRecordEnterpriseFlag()
    {
        // Issue #153: активация существующего окна — только по флагу текущей
        // записи (IsEnterpriseRunning), иначе вторая запись с тем же подключением
        // открывала бы чужое окно вместо запуска под своими учётными данными.
        var code = ReadProjectFile("Workspace1C", "Views", "MainWindow.Events.cs");
        var method = ExtractMethod(code, "private void ActivateInfobaseByDoubleClickAction(");

        var flagIndex = method.IndexOf("if (infobase.IsEnterpriseRunning)", StringComparison.Ordinal);
        var activateIndex = method.IndexOf("ActivateRunningEnterpriseOrLaunch(infobase)", StringComparison.Ordinal);

        Assert.True(flagIndex >= 0, "нет проверки infobase.IsEnterpriseRunning");
        Assert.True(activateIndex > flagIndex,
            "ActivateRunningEnterpriseOrLaunch должен вызываться только внутри проверки флага записи");
        Assert.Contains("_viewModel.LaunchEnterpriseCommand.Execute(null);", method, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchProcesses_SelectsSingleOwner_AndFindDirectOwnersMarkedDiagnostics()
    {
        var code = ReadProjectFile("Workspace1C", "Services", "RunningInfobaseMatcher.cs");

        // Issue #153 (1.0.1.115): владелец выбирается через SelectOwnerForProcess
        // с резолвером стабильного индекса записи, а результат ключуется GetRecordKey.
        Assert.Contains("SelectOwnerForProcess(", code, StringComparison.Ordinal);
        Assert.Contains("resolveRecordIndex", code, StringComparison.Ordinal);
        Assert.Contains("GetRecordKey(owner, ownerIndex)", code, StringComparison.Ordinal);
        Assert.Contains("ТОЛЬКО ДЛЯ ДИАГНОСТИКИ", code, StringComparison.Ordinal);
    }

    // ===================== Вспомогательное =====================

    /// <summary>Вырезает тело метода по сигнатуре (до парной закрывающей скобки).</summary>
    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Метод не найден: {signature}");

        var brace = source.IndexOf('{', start);
        Assert.True(brace > start, $"Тело метода не найдено: {signature}");

        var depth = 0;
        for (var i = brace; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source.Substring(start, i - start + 1);
            }
        }

        Assert.Fail($"Не найдена закрывающая скобка метода: {signature}");
        return string.Empty;
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var relativePath = Path.Combine(parts);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var fullPath = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(fullPath))
                return File.ReadAllText(fullPath);
            dir = dir.Parent;
        }

        Assert.Fail($"Файл проекта не найден: {relativePath}");
        return string.Empty;
    }
}
