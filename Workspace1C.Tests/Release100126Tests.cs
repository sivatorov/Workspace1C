using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Tests.TestSupport;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Релиз 1.0.1.26 (issues #79, #83, #92, #101):
/// <list type="bullet">
/// <item>#79 — креды ИБ в pre-валидации мастера (TestDeployService), диагностика rac-команды
/// с маскированием паролей (RacClient.MaskSecrets), fallback на кластерные креды
/// (ClusterInfobaseCredentialsResolver);</item>
/// <item>#92 — прокрутка журнала массовых операций (без IsHitTestVisible), поле «Пароль
/// пользователя СУБД» (SqlPassword), понятное сообщение при отсутствии пароля;</item>
/// <item>#101 — раздельная индикация зависания Предприятие/Конфигуратор
/// (EnterpriseRunningStatus / ConfiguratorRunningStatus);</item>
/// <item>#83 — разбор файла *.lic (ParseLicFile), колонка «Владелец», диагностика
/// «занято 0 из N».</item>
/// </list>
/// </summary>
public sealed class Release100126Tests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    private static JsonElement ReadLanguageStrings(string fileName)
    {
        var json = ReadProjectFile($"Workspace1C/Localization/Languages/{fileName}");
        return JsonDocument.Parse(json).RootElement.GetProperty("strings");
    }

    private static Infobase ServerBase(string name = "Demo") => new()
    {
        Name = name,
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.ClientServer,
            Server = "srv",
            DatabaseName = "demo_db"
        }
    };

    // ===================== #101: пер-режимные статусы =====================

    [Fact]
    public void PerModeStatus_ConfiguratorHung_EnterpriseKeepsOwnColor()
    {
        var ib = new Infobase
        {
            IsEnterpriseRunning = true,
            IsConfiguratorRunning = true
        };
        // Завис только Конфигуратор (первый опрос — Hung).
        ib.IsConfiguratorNotResponding = true;
        ib.ConfiguratorNotRespondingStreak = 1;
        // Общий флаг (точка у имени) — как ставит монитор: любой процесс не отвечает.
        ib.IsNotResponding = true;
        ib.NotRespondingStreak = 1;

        Assert.Equal(RunningDotStatus.Responding, ib.EnterpriseRunningStatus);
        Assert.Equal(RunningDotStatus.Hung, ib.ConfiguratorRunningStatus);
        // Общий статус сохраняет прежнее поведение (точка у имени базы красится).
        Assert.Equal(RunningDotStatus.Hung, ib.RunningStatus);
    }

    [Fact]
    public void PerModeStatus_EnterpriseHungTwoStreaks_CriticalOnlyForEnterprise()
    {
        var ib = new Infobase
        {
            IsEnterpriseRunning = true,
            IsConfiguratorRunning = true
        };
        ib.IsEnterpriseNotResponding = true;
        ib.EnterpriseNotRespondingStreak = 2;

        Assert.Equal(RunningDotStatus.Critical, ib.EnterpriseRunningStatus);
        // Конфигуратор отвечает — его чип в собственном цвете.
        Assert.Equal(RunningDotStatus.Responding, ib.ConfiguratorRunningStatus);
    }

    [Fact]
    public void PerModeStatus_ModeNotRunning_NotRunning()
    {
        var ib = new Infobase { IsEnterpriseRunning = true };
        ib.IsEnterpriseNotResponding = true;
        ib.EnterpriseNotRespondingStreak = 3;

        Assert.Equal(RunningDotStatus.NotRunning, ib.ConfiguratorRunningStatus);
        Assert.Equal(RunningDotStatus.Critical, ib.EnterpriseRunningStatus);
    }

    [Fact]
    public void RunningModesTooltip_NamesOnlyHungMode()
    {
        var ib = new Infobase
        {
            IsEnterpriseRunning = true,
            IsConfiguratorRunning = true
        };
        // Локализация в тестовом процессе не загружена — T() возвращает ключ строки.
        ib.IsConfiguratorNotResponding = true;
        Assert.Contains("Infobase.ConfiguratorNotRespondingSuffix", ib.RunningModesTooltip, StringComparison.Ordinal);
        Assert.DoesNotContain("Infobase.EnterpriseNotRespondingSuffix", ib.RunningModesTooltip, StringComparison.Ordinal);

        ib.IsConfiguratorNotResponding = false;
        ib.IsEnterpriseNotResponding = true;
        Assert.Contains("Infobase.EnterpriseNotRespondingSuffix", ib.RunningModesTooltip, StringComparison.Ordinal);

        // Оба зависли — общий суффикс.
        ib.IsConfiguratorNotResponding = true;
        Assert.DoesNotContain("Infobase.EnterpriseNotRespondingSuffix", ib.RunningModesTooltip, StringComparison.Ordinal);
        Assert.DoesNotContain("Infobase.ConfiguratorNotRespondingSuffix", ib.RunningModesTooltip, StringComparison.Ordinal);
        Assert.Contains("Infobase.NotRespondingSuffix", ib.RunningModesTooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_PerModeNotRespondingSuffix_KeysPresent()
    {
        var ru = ReadLanguageStrings("ru.json");
        var en = ReadLanguageStrings("en.json");
        Assert.True(ru.TryGetProperty("Infobase.EnterpriseNotRespondingSuffix", out _));
        Assert.True(ru.TryGetProperty("Infobase.ConfiguratorNotRespondingSuffix", out _));
        Assert.True(en.TryGetProperty("Infobase.EnterpriseNotRespondingSuffix", out _));
        Assert.True(en.TryGetProperty("Infobase.ConfiguratorNotRespondingSuffix", out _));
    }

    [Fact]
    public void LayoutAudit_WpfChips_BindPerModeStatuses()
    {
        var xaml = ReadProjectFile("Workspace1C/Views/MainWindow.xaml");
        // Триггеры чипа «Предприятие» — на EnterpriseRunningStatus, чипа «Конфигуратор» —
        // на ConfiguratorRunningStatus (issue #101: зависший Конфигуратор не красит чип
        // Предприятия).
        Assert.Contains("\"{Binding EnterpriseRunningStatus}\" Value=\"Hung\"", xaml, StringComparison.Ordinal);
        Assert.Contains("\"{Binding EnterpriseRunningStatus}\" Value=\"Critical\"", xaml, StringComparison.Ordinal);
        Assert.Contains("\"{Binding ConfiguratorRunningStatus}\" Value=\"Hung\"", xaml, StringComparison.Ordinal);
        Assert.Contains("\"{Binding ConfiguratorRunningStatus}\" Value=\"Critical\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void LayoutAudit_AvaloniaChips_BindPerModeStatuses()
    {
        var source = ReadProjectFile("Workspace1C/Views/MainWindow.Avalonia.Tree.cs");
        Assert.Contains("nameof(Infobase.EnterpriseRunningStatus)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(Infobase.ConfiguratorRunningStatus)", source, StringComparison.Ordinal);
    }

    // ===================== #92а: прокрутка журнала =====================

    [Fact]
    public void LayoutAudit_WpfLogList_NoHitTestBlocking()
    {
        var xaml = ReadProjectFile("Workspace1C/Views/BatchOperationsWindow.xaml");
        // Issue #92 (v1.0.1.26): IsHitTestVisible="False" у LogList снимает все события
        // мыши — прокрутка журнала была невозможна. Внутри элемента LogList атрибута
        // быть не должно (в других местах окна он допустим — например, у иконки поиска).
        var logListStart = xaml.IndexOf("x:Name=\"LogList\"", StringComparison.Ordinal);
        Assert.True(logListStart >= 0, "LogList не найден в BatchOperationsWindow.xaml");
        var logListEnd = xaml.IndexOf("</ListBox>", logListStart, StringComparison.Ordinal);
        Assert.True(logListEnd > logListStart, "Закрывающий тег LogList не найден");
        Assert.DoesNotContain("IsHitTestVisible", xaml.Substring(logListStart, logListEnd - logListStart),
            StringComparison.Ordinal);
        // Выделение строк запрещено контейнером без фокуса.
        Assert.Contains("TargetType=\"ListBoxItem\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void LayoutAudit_WpfLogList_SmartAutoscrollPresent()
    {
        var code = ReadProjectFile("Workspace1C/Views/BatchOperationsWindow.xaml.cs");
        Assert.Contains("_logStickToBottom", code, StringComparison.Ordinal);
        Assert.Contains("ScrollChanged", code, StringComparison.Ordinal);
    }

    [Fact]
    public void LayoutAudit_AvaloniaLogList_NoHitTestBlocking_SmartAutoscrollPresent()
    {
        var code = ReadProjectFile("Workspace1C/Views/BatchOperationsWindow.Avalonia.cs");
        // Журнал Avalonia больше не блокирует hit-test (прокрутка колесом/ползунком).
        Assert.DoesNotContain("_logList = new() { IsHitTestVisible", code, StringComparison.Ordinal);
        Assert.Contains("_logStickToBottom", code, StringComparison.Ordinal);
        // Выделение строк гасится сбросом SelectedIndex.
        Assert.Contains("_logList.SelectedIndex = -1", code, StringComparison.Ordinal);
    }

    // ===================== #92б: пароль СУБД на форме =====================

    [Fact]
    public void Localization_SqlPasswordKeys_PresentInBothLanguages()
    {
        var ru = ReadLanguageStrings("ru.json");
        var en = ReadLanguageStrings("en.json");
        foreach (var key in new[]
                 {
                     "BatchOps.SqlPasswordLabel",
                     "BatchOps.SqlPasswordHint",
                     "BatchOps.SqlPasswordApplied",
                     "BatchOps.DbmsPasswordMissing"
                 })
        {
            Assert.True(ru.TryGetProperty(key, out _), $"Нет ключа {key} в ru.json");
            Assert.True(en.TryGetProperty(key, out _), $"Нет ключа {key} в en.json");
        }
    }

    [Fact]
    public void LayoutAudit_WpfWindow_HasSqlPasswordBox()
    {
        var xaml = ReadProjectFile("Workspace1C/Views/BatchOperationsWindow.xaml");
        Assert.Contains("SqlPasswordBox", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void LayoutAudit_AvaloniaWindow_HasSqlPasswordBox()
    {
        var code = ReadProjectFile("Workspace1C/Views/BatchOperationsWindow.Avalonia.cs");
        Assert.Contains("_sqlPasswordBox", code, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BatchOperationsViewModel_SqlPassword_AppliedToResolvedProfile()
    {
        // issue #92 (v1.0.1.26): rac отдал логин, но не пароль; пароль с формы
        // (SqlPassword) подставляется в профиль перед ibcmd.
        var tempDir = Path.Combine(Path.GetTempPath(), "batch-sqlpwd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var previous = FeatureDataStore.DirectoryOverride;
        FeatureDataStore.DirectoryOverride = tempDir;
        try
        {
            var ib = ServerBase();
            var vm = new BatchOperationsViewModel(new[] { ib }, BatchOperationKind.DumpDt)
            {
                PathText = tempDir,
                SqlPassword = "form-password",
                DbmsProfileResolver = (base_, _) => Task.FromResult<DbmsBackupProfile?>(
                    new DbmsBackupProfile
                    {
                        InfobaseId = base_.Id,
                        Dbms = DbmsKind.PostgreSql,
                        Host = "pg-host",
                        Database = "zup_db",
                        User = "postgres",
                        Password = string.Empty
                    })
            };

            DbmsBackupProfile? passed = null;
            vm.ExecutorWithProfileOverride = (base_, path, profile, ct) =>
            {
                passed = profile;
                return Task.FromResult(new IbcmdResult { Success = true, ExitCode = 0 });
            };

            await vm.RunAsync();

            Assert.Equal(BatchOperationsStatus.Success, vm.Items.Single(i => i.Infobase == ib).Status);
            Assert.NotNull(passed);
            Assert.Equal("form-password", passed!.Password);
        }
        finally
        {
            FeatureDataStore.DirectoryOverride = previous;
            try { Directory.Delete(tempDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task BatchOperationsViewModel_MissingDbmsPassword_LogsClearMessageOnFailure()
    {
        // issue #92 (v1.0.1.26, вопрос aepkugesi): профиль от rac есть, логин есть,
        // пароля нет (rac его не отдаёт, на форме не ввели) — при ошибке ibcmd в журнал
        // добавляется понятное объяснение вместо голого rac/ibcmd-текста.
        var tempDir = Path.Combine(Path.GetTempPath(), "batch-sqlpwd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var previous = FeatureDataStore.DirectoryOverride;
        FeatureDataStore.DirectoryOverride = tempDir;
        try
        {
            var ib = ServerBase();
            var vm = new BatchOperationsViewModel(new[] { ib }, BatchOperationKind.DumpDt)
            {
                PathText = tempDir,
                DbmsProfileResolver = (base_, _) => Task.FromResult<DbmsBackupProfile?>(
                    new DbmsBackupProfile
                    {
                        InfobaseId = base_.Id,
                        Dbms = DbmsKind.PostgreSql,
                        Host = "pg-host",
                        Database = "zup_db",
                        User = "postgres",
                        Password = string.Empty
                    })
            };

            vm.ExecutorWithProfileOverride = (base_, path, profile, ct) =>
                Task.FromResult(new IbcmdResult { Success = false, ExitCode = 1, Error = "connection failed" });

            await vm.RunAsync();

            Assert.Equal(BatchOperationsStatus.Failed, vm.Items.Single(i => i.Infobase == ib).Status);
            // Строка журнала может прийти через SynchronizationContext.Post —
            // дожидаемся её и перечисляем снимок (иначе «Collection was modified»).
            var log = AsyncLogAssert.WaitForLine(
                vm.LogLines,
                l => l.Contains("BatchOps.DbmsPasswordMissing", StringComparison.Ordinal),
                "В журнале ожидалось объяснение отсутствия пароля СУБД");
            Assert.Contains(log, l => l.Contains("BatchOps.DbmsPasswordMissing", StringComparison.Ordinal));
        }
        finally
        {
            FeatureDataStore.DirectoryOverride = previous;
            try { Directory.Delete(tempDir, recursive: true); } catch (IOException) { }
        }
    }

    // ===================== #79: креды ИБ и диагностика rac =====================

    [Theory]
    [InlineData("--infobase-pwd=s3cret", "--infobase-pwd=***")]
    [InlineData("--password=secret", "--password=***")]
    [InlineData("--db-pwd=pwd", "--db-pwd=***")]
    public void MaskSecrets_MasksPasswordArguments(string arg, string expected)
    {
        var masked = RacClient.MaskSecrets(new[] { "infobase", "info", arg });
        Assert.Contains(expected, masked, StringComparison.Ordinal);
    }

    [Fact]
    public void MaskSecrets_KeepsUsersAndNonSecrets()
    {
        var masked = RacClient.MaskSecrets(new[]
        {
            "--cluster=abc", "--infobase-user=admin", "session", "list"
        });
        Assert.Contains("--infobase-user=admin", masked, StringComparison.Ordinal);
        Assert.Contains("--cluster=abc", masked, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Недостаточно прав пользователя на информационную базу", true)]
    [InlineData("Insufficient privileges to perform operation", true)]
    [InlineData("Insufficient rights", true)]
    [InlineData("База не найдена", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void HasInsufficientRightsError_DetectsRightsErrors(string? error, bool expected)
    {
        Assert.Equal(expected, ClusterInfobaseCredentialsResolver.HasInsufficientRightsError(error));
    }

    [Fact]
    public void InfobaseAdminCredentialsWithSource_FormOverride()
    {
        var (user, password, source) = ClusterInfobaseCredentialsResolver.InfobaseAdminCredentialsWithSource(
            ServerBase(), new BackupCredential { UseInfobaseAuth = false, User = "ib-admin", Password = "ib-pwd" });

        Assert.Equal("ib-admin", user);
        Assert.Equal("ib-pwd", password);
        Assert.Equal("form", source);
    }

    [Fact]
    public void InfobaseAdminCredentialsWithSource_NullInfobase_None()
    {
        var (_, _, source) = ClusterInfobaseCredentialsResolver.InfobaseAdminCredentialsWithSource(null);
        Assert.Equal("none", source);
    }

    [Fact]
    public void ClusterInfobaseCredentials_Find_FromEnterpriseAuthOnly()
    {
        // issue #79 (v1.0.1.26): креды могут храниться только в авторизации
        // «1С:Предприятия» (EnterpriseAuth) — Find должен их найти при единственной
        // базе с таким именем SQL-БД.
        var ib = ServerBase();
        ib.EnterpriseAuth = new InfobaseAuthSettings
        {
            AuthenticationMode = AuthenticationMode.Credentials,
            User = "ent-admin",
            Password = "ent-pwd"
        };

        var found = ClusterInfobaseCredentials.Find(new[] { ib }, "unknown-host:1545", "demo_db");

        Assert.NotNull(found);
        Assert.Equal("ent-admin", found!.User);
        Assert.Equal("ent-pwd", found.Password);
    }

    [Fact]
    public void TestDeployService_PreValidation_PassesFormCredentialsToResolver()
    {
        // issue #79 (v1.0.1.26, регресс v1.0.1.25): pre-валидация обязана передавать
        // креды формы в резолвер — как и резолв перед «Выгрузкой».
        var source = ReadProjectFile("Workspace1C/Services/TestDeployService.cs");
        var methodIndex = source.IndexOf(
            "ResolveRacProfileForValidationAsync", StringComparison.Ordinal);
        Assert.True(methodIndex >= 0, "ResolveRacProfileForValidationAsync не найден");
        var methodBody = source.Substring(methodIndex);
        Assert.Contains("resolver.CredentialOverride = scenario.Credential;", methodBody, StringComparison.Ordinal);
        Assert.Contains("resolver.FallbackDbms = scenario.Dbms;", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void RacClient_LogsMaskedCommandLine()
    {
        // issue #79 (v1.0.1.26): фактическая командная строка rac пишется в CM_RAC
        // с маскированием секретов.
        var source = ReadProjectFile("Workspace1C/Services/RacClient.cs");
        Assert.Contains("команда: rac {MaskSecrets(args)}", source, StringComparison.Ordinal);
    }

    // ===================== #83: файл *.lic и занятость =====================

    [Fact]
    public void ParseLicFile_ExtractsOwnerRegNumberUserComputerDate()
    {
        const string content = """
            Файл лицензии 1С:Предприятие
            Owner: ООО «Ромашка»
            RegNumber: 1234567890
            User: ivanov
            Computer: WS-01
            IssueDate: 2026-01-15
            Лицензия действительна
            """;

        var license = RingLicenseParser.ParseLicFile(content, "20260115120000.lic");

        Assert.Equal("ООО «Ромашка»", license.Owner);
        Assert.Equal("1234567890", license.RegNumber);
        Assert.Equal("ivanov", license.User);
        Assert.Equal("WS-01", license.Computer);
        Assert.Equal(new DateTime(2026, 1, 15), license.IssueDate);
        Assert.Contains("Лицензия действительна", license.RawDetails, StringComparison.Ordinal);
        Assert.Equal("20260115120000.lic", license.RawText);
    }

    [Fact]
    public void ParseLicFile_EmptyContent_ReturnsEmptyLicense()
    {
        var license = RingLicenseParser.ParseLicFile(string.Empty);
        Assert.Equal(string.Empty, license.Owner);
        Assert.Equal(string.Empty, license.RawDetails);
    }

    [Fact]
    public void TryReadLicFile_ReadsUtf8File()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "lic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var path = Path.Combine(tempDir, "test.lic");
            File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF }
                .Concat(System.Text.Encoding.UTF8.GetBytes("Owner: Тест\nRegNumber: 42\n"))
                .ToArray());

            var license = RingLicenseService.TryReadLicFile(path, "test.lic");

            Assert.NotNull(license);
            Assert.Equal("Тест", license!.Owner);
            Assert.Equal("42", license.RegNumber);
            Assert.Equal("файл лицензии: test.lic", license.Module);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Localization_LicenseOwnerColumnAndNoLicTypeReason_PresentInBothLanguages()
    {
        var ru = ReadLanguageStrings("ru.json");
        var en = ReadLanguageStrings("en.json");
        Assert.True(ru.TryGetProperty("ServerMonitor.Licenses.Columns.Owner", out _));
        Assert.True(ru.TryGetProperty("ServerMonitor.Licenses.NoLicTypeReason", out _));
        Assert.True(en.TryGetProperty("ServerMonitor.Licenses.Columns.Owner", out _));
        Assert.True(en.TryGetProperty("ServerMonitor.Licenses.NoLicTypeReason", out _));
    }

    [Fact]
    public void LayoutAudit_LicenseTables_HaveOwnerColumnAndTooltip()
    {
        var xaml = ReadProjectFile("Workspace1C/Views/ServerMonitorWindow.xaml");
        Assert.Contains("ServerMonitor.Licenses.Columns.Owner", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding RawDetails}", xaml, StringComparison.Ordinal);

        var avalonia = ReadProjectFile("Workspace1C/Views/ServerMonitorWindow.Avalonia.cs");
        Assert.Contains("\"Owner\"", avalonia, StringComparison.Ordinal);
        Assert.Contains("rowTooltipPath", avalonia, StringComparison.Ordinal);
    }

    [Fact]
    public void RacClient_LogsLicensedSessionsDiagnostics()
    {
        // issue #83 (v1.0.1.26): сырой вывод «session list --licenses» и счётчики
        // сеансов с/без lic-type — в CM_RAC.
        var source = ReadProjectFile("Workspace1C/Services/RacClient.cs");
        Assert.Contains("session list --licenses — сеансов=", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerMonitorViewModel_ShowsNoLicTypeReason()
    {
        var source = ReadProjectFile("Workspace1C/ViewModels/ServerMonitorViewModel.cs");
        Assert.Contains("ServerMonitor.Licenses.NoLicTypeReason", source, StringComparison.Ordinal);
    }
}
