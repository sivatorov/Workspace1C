using System;
using Workspace1C.Localization;
using Workspace1C.Models;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты классификатора режима запуска процесса 1С (<see cref="RunningKindClassifier"/>)
/// и подсказки индикатора режимов (<see cref="Infobase.RunningModesTooltip"/>).
/// Индикатор «база сейчас запущена» должен различать 1С:Предприятие (Play) и
/// Конфигуратор (ключ), в том числе оба одновременно.
/// </summary>
public sealed class RunningKindClassifierTests
{
    [Theory]
    [InlineData("1cv8.exe", "ENTERPRISE /F C:\\Bases\\demo", RunningKind.Enterprise)]
    [InlineData("1cv8", "/F /home/user/bases/demo", RunningKind.Enterprise)]
    [InlineData("1cv8.exe", null, RunningKind.Enterprise)]              // пустая cmdline → Предприятие
    [InlineData("1cv8", "", RunningKind.Enterprise)]
    [InlineData("1CV8.EXE", "ENTERPRISE /S srv\\db", RunningKind.Enterprise)] // регистр имени не важен
    [InlineData("C:\\Program Files\\1cv8\\1cv8.exe", "/F C:\\Bases\\demo", RunningKind.Enterprise)] // с путём
    [InlineData("1cv8x64.exe", "/F C:\\Bases\\demo", RunningKind.Enterprise)] // старые сборки
    [InlineData("1cv8x64", "/S srv\\db", RunningKind.Enterprise)]
    public void Classify_ThickClientWithoutDesigner_ReturnsEnterprise(string processName, string? commandLine, RunningKind expected)
    {
        Assert.Equal(expected, RunningKindClassifier.Classify(processName, commandLine));
    }

    [Theory]
    [InlineData("1cv8.exe", "DESIGNER /F C:\\Bases\\demo", RunningKind.Configurator)]
    [InlineData("1cv8", "/F C:\\Bases\\demo DESIGNER", RunningKind.Configurator)]
    [InlineData("1cv8.exe", "CONFIG /S srv\\db", RunningKind.Configurator)]
    [InlineData("1cv8", "designer", RunningKind.Configurator)]          // регистр cmdline не важен
    [InlineData("1cv8x64.exe", "/F C:\\Bases\\demo CONFIG", RunningKind.Configurator)]
    public void Classify_ThickClientWithDesigner_ReturnsConfigurator(string processName, string? commandLine, RunningKind expected)
    {
        Assert.Equal(expected, RunningKindClassifier.Classify(processName, commandLine));
    }

    [Theory]
    [InlineData("1cv8c.exe", "ENTERPRISE /S srv\\db", RunningKind.Enterprise)]
    [InlineData("1cv8c", "/F C:\\Bases\\demo", RunningKind.Enterprise)]
    [InlineData("1cv8c.exe", "DESIGNER /S srv\\db", RunningKind.Enterprise)] // тонкий клиент всегда Предприятие
    [InlineData("1cv8c.exe", null, RunningKind.Enterprise)]
    public void Classify_ThinClient_AlwaysEnterprise(string processName, string? commandLine, RunningKind expected)
    {
        Assert.Equal(expected, RunningKindClassifier.Classify(processName, commandLine));
    }

    [Theory]
    [InlineData("1cv8.exe", "/Execute C:\\proc\\epf", RunningKind.Enterprise)] // служебный запуск — Предприятие
    [InlineData("1cv8.exe", "/C старт", RunningKind.Enterprise)]
    public void Classify_ServiceLaunch_ReturnsEnterprise(string processName, string? commandLine, RunningKind expected)
    {
        Assert.Equal(expected, RunningKindClassifier.Classify(processName, commandLine));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("chrome.exe", "/F C:\\Bases\\demo")]
    [InlineData("1cv8s.exe", "DESIGNER")]   // серверный процесс — не клиент
    [InlineData("ibcmd.exe", "/F C:\\Bases\\demo")]
    public void Classify_ForeignProcess_ReturnsNone(string? processName, string? commandLine)
    {
        Assert.Equal(RunningKind.None, RunningKindClassifier.Classify(processName, commandLine));
    }

    [Fact]
    public void RunningModesTooltip_NotRunning_ReturnsEmpty()
    {
        var ib = new Infobase();
        Assert.Equal(string.Empty, ib.RunningModesTooltip);
    }

    [Fact]
    public void RunningModesTooltip_EnterpriseOnly_ShowsEnterprise()
    {
        var ib = new Infobase { IsEnterpriseRunning = true };

        Assert.True(ib.IsRunning);
        Assert.False(ib.IsConfiguratorRunning);
        Assert.Equal(LocalizationManager.T("Infobase.RunningEnterpriseTooltip"), ib.RunningModesTooltip);
    }

    [Fact]
    public void RunningModesTooltip_ConfiguratorOnly_ShowsConfigurator()
    {
        var ib = new Infobase { IsConfiguratorRunning = true };

        Assert.True(ib.IsRunning);
        Assert.False(ib.IsEnterpriseRunning);
        Assert.Equal(LocalizationManager.T("Infobase.RunningConfiguratorTooltip"), ib.RunningModesTooltip);
    }

    [Fact]
    public void RunningModesTooltip_BothModes_ShowsBoth()
    {
        var ib = new Infobase { IsEnterpriseRunning = true, IsConfiguratorRunning = true };

        Assert.True(ib.IsRunning);
        Assert.NotEqual(LocalizationManager.T("Infobase.RunningEnterpriseTooltip"), ib.RunningModesTooltip);
        Assert.NotEqual(LocalizationManager.T("Infobase.RunningConfiguratorTooltip"), ib.RunningModesTooltip);
        Assert.Equal(LocalizationManager.T("Infobase.RunningBothTooltip"), ib.RunningModesTooltip);
    }

    [Fact]
    public void Localization_KeysPresentInRuAndEnDictionaries()
    {
        // Подсказки индикатора режимов должны быть локализованы (не fallback на ключ):
        // новые ключи объявлены и в ru.json, и в en.json.
        var root = RepoRoot();
        foreach (var (file, mustContain) in new[]
                 {
                     ("ru.json", new[] { "Предприятие", "Конфигуратор" }),
                     ("en.json", new[] { "Enterprise", "Designer" })
                 })
        {
            var json = File.ReadAllText(Path.Combine(root, "Workspace1C", "Localization", "Languages", file));
            foreach (var key in new[]
                     {
                         "Infobase.RunningEnterpriseTooltip",
                         "Infobase.RunningConfiguratorTooltip",
                         "Infobase.RunningBothTooltip",
                         "Infobase.NotRespondingSuffix"
                     })
                Assert.Contains($"\"{key}\"", json, StringComparison.Ordinal);

            Assert.Contains(mustContain[0], json, StringComparison.Ordinal);
            Assert.Contains(mustContain[1], json, StringComparison.Ordinal);
        }
    }

    /// <summary>Корень репозитория (как в SemanticPaletteAuditTests).</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent!;
        return dir!.FullName;
    }

    [Fact]
    public void RunningModesTooltip_NotResponding_AppendsSuffix()
    {
        var ib = new Infobase { IsEnterpriseRunning = true, IsConfiguratorRunning = true };
        Assert.DoesNotContain(
            LocalizationManager.T("Infobase.NotRespondingSuffix"),
            ib.RunningModesTooltip,
            StringComparison.Ordinal);

        ib.IsNotResponding = true;

        Assert.EndsWith(
            ", " + LocalizationManager.T("Infobase.NotRespondingSuffix"),
            ib.RunningModesTooltip,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RunningModesTooltip_AllModesStopped_ReturnsEmpty()
    {
        // Снятие флагов возвращает пустую подсказку (индикаторы скрыты).
        var ib = new Infobase { IsEnterpriseRunning = true, IsConfiguratorRunning = true };
        ib.IsEnterpriseRunning = false;
        ib.IsConfiguratorRunning = false;

        Assert.False(ib.IsRunning);
        Assert.Equal(string.Empty, ib.RunningModesTooltip);
    }

    [Fact]
    public void RunningStatus_MixedModes_KeepsIssue310Classification()
    {
        // Механика issue #310 сохраняется: зависший процесс при любом наборе режимов
        // даёт Hung/Critical по RunningDotStatusClassifier.
        var ib = new Infobase { IsEnterpriseRunning = true };
        Assert.Equal(RunningDotStatus.Responding, ib.RunningStatus);

        ib.IsConfiguratorRunning = true;
        ib.IsNotResponding = true;
        ib.NotRespondingStreak = 1;
        Assert.Equal(RunningDotStatus.Hung, ib.RunningStatus);

        ib.NotRespondingStreak = 2;
        Assert.Equal(RunningDotStatus.Critical, ib.RunningStatus);

        ib.IsEnterpriseRunning = false;
        ib.IsConfiguratorRunning = false;
        Assert.Equal(RunningDotStatus.NotRunning, ib.RunningStatus);
    }
}
