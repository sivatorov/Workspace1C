using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты выпуска v1.0.1.49 (issues #118, #117, #116, #107, #103):
/// <list type="bullet">
/// <item>#118 — безопасные COM-помощники N/Mt в <see cref="ComScriptRunner.Prelude"/>
/// и безусловные «Count» заменены в телах операций пользователей/регламентных заданий;</item>
/// <item>#117 п.2 — имя расширения перезаполняется при повторном выборе файла
/// (<see cref="ExtensionsViewModel.CfePath"/>);</item>
/// <item>#116 п.2 — разбор/кламп периодичности проверки обновлений
/// (<see cref="AppSettings.NormalizeUpdateInterval"/>);</item>
/// <item>#103 — чистая функция <see cref="RunningInfobaseMatcher.HasDebugKey"/>
/// (ключ /DEBUG с защитой от ложных срабатываний).</item>
/// </list>
/// </summary>
public sealed class V100149IssuesTests
{
    // ---- Issue #118: безопасные COM-помощники ----

    [Fact]
    public void ComScriptRunner_Prelude_ContainsSafeCountHelpers()
    {
        // Помощники N (безопасный Count) и Mt (безопасный метод с диагностикой в opErr),
        // плюс OC — имя класса объекта для диагностики; класс вычисляется из OC-хвоста.
        var prelude = ComScriptRunner.Prelude;
        Assert.Contains("function N(", prelude);
        Assert.Contains("function Mt(", prelude);
        Assert.Contains("function OC(", prelude);
        // Помощник N возвращает -1 при недоступном члене Count (не роняет скрипт).
        Assert.Contains("return -1", prelude);
    }

    [Fact]
    public void ComScriptRunner_BuildScript_KeepsPreludeAndMain()
    {
        var script = ComScriptRunner.BuildScript(InfobaseUsersService.OperationBody);
        Assert.StartsWith(ComScriptRunner.Prelude, script);
        Assert.EndsWith(ComScriptRunner.Main, script);
        // Тело «list» пользователей больше не вызывает безусловный M $arr 'Count'.
        Assert.DoesNotContain("M $arr 'Count'", script);
        // …а использует безопасный N.
        Assert.Contains("$n=N $arr", script);
    }

    [Fact]
    public void ComScriptRunner_BuildScript_ScheduledJobs_NoUnconditionalCount()
    {
        var script = ComScriptRunner.BuildScript(ScheduledJobsService.OperationBody);
        Assert.StartsWith(ComScriptRunner.Prelude, script);
        Assert.DoesNotContain("M $arr 'Count'", script);
        Assert.Contains("$n=N $arr", script);
        // Ошибки чтения отдельного задания (issue #79) не сломаны: setall/verify сохранены.
        Assert.Contains("'setall'", script);
        Assert.Contains("'verify'", script);
    }

    [Fact]
    public void ComScriptRunner_ParseOutput_WarnField_DoesNotBreakParsing()
    {
        // Регресс: ответ операции с warn-полем («nocount», #118) разбирается штатно.
        var stdout = ComScriptRunner.ResultPrefix + Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(
                "{\"ok\":true,\"progId\":\"V83.COMConnector\",\"users\":[],\"roles\":[],\"warn\":\"nocount\",\"cls\":\"System.__ComObject\",\"err\":\"not a collection\"}"));
        var result = ComScriptRunner.ParseOutput(stdout);
        Assert.True(result.Success);
        Assert.Equal("nocount", result.Data.GetProperty("warn").GetString());
        // Разбор «списка» с warn-полем не возвращает пользователей.
        var (users, roles) = InfobaseUsersService.ParseList(result.Data);
        Assert.Empty(users);
        Assert.Empty(roles);
    }

    // ---- Issue #117 п.2: перезаполнение имени расширения при выборе файла ----

    [Fact]
    public void ExtensionsViewModel_CfePath_RewritesAutoSuggestedName()
    {
        var vm = new ExtensionsViewModel(new[] { MakeBase("База") }, MakeBase("База"));

        vm.CfePath = @"C:\Ext\Доработки_1.2.cfe";
        Assert.Equal("Доработки_1.2", vm.ExtensionName);

        // Повторный выбор файла перезаполняет имя (фикс #117 п.2), даже если
        // поле уже заполнено автоподстановкой от прошлого файла.
        vm.CfePath = @"C:\Ext\ПлатёжныйМодуль.cfe";
        Assert.Equal("ПлатёжныйМодуль", vm.ExtensionName);
    }

    [Fact]
    public void ExtensionsViewModel_ManualName_IsNotOverwrittenByFileChoice()
    {
        var vm = new ExtensionsViewModel(new[] { MakeBase("База") }, MakeBase("База"));

        vm.CfePath = @"C:\Ext\Доработки.cfe";
        vm.ExtensionName = "СвоёИмя";

        // Пользователь ввёл имя вручную — выбор нового файла его не затирает.
        vm.CfePath = @"C:\Ext\ДругойФайл.cfe";
        Assert.Equal("СвоёИмя", vm.ExtensionName);

        // Очистка поля пользователем снимает признак ручного редактирования:
        // следующий выбор файла снова автоподставляет имя.
        vm.ExtensionName = "";
        vm.CfePath = @"C:\Ext\ДругойФайл.cfe";
        Assert.Equal("ДругойФайл", vm.ExtensionName);
    }

    private static Infobase MakeBase(string name) => new()
    {
        Id = name,
        Name = name,
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\Bases\" + name }
    };

    // ---- Issue #116 п.2: периодичность проверки обновлений ----

    // Issue #116 v1.0.1.54: интервал теперь произвольный «значение + единица»,
    // старые фиксированные значения (0/60/360/1440) остаются валидными.
    [Fact]
    public void AppSettings_NormalizeUpdateInterval_AcceptsKnownValuesAndClamps()
    {
        Assert.Equal(0, AppSettings.NormalizeUpdateInterval(0));
        Assert.Equal(60, AppSettings.NormalizeUpdateInterval(60));
        Assert.Equal(360, AppSettings.NormalizeUpdateInterval(360));
        Assert.Equal(1440, AppSettings.NormalizeUpdateInterval(1440));
        // Произвольные значения проходят (интервал задаётся числом + единицей),
        // отрицательные — 0, слишком большие клампятся к 30 суткам.
        Assert.Equal(15, AppSettings.NormalizeUpdateInterval(15));
        Assert.Equal(999, AppSettings.NormalizeUpdateInterval(999));
        Assert.Equal(0, AppSettings.NormalizeUpdateInterval(-5));
        Assert.Equal(AppSettings.MaxUpdateCheckIntervalMinutes,
            AppSettings.NormalizeUpdateInterval(1_000_000));
    }

    [Fact]
    public void AppSettings_LoadKeepsKnownUpdateIntervals()
    {
        var settings = new AppSettings { UpdateCheckIntervalMinutes = 777 };
        settings.NormalizeForLoad();
        Assert.Equal(777, settings.UpdateCheckIntervalMinutes);

        var valid = new AppSettings { UpdateCheckIntervalMinutes = 360 };
        valid.NormalizeForLoad();
        Assert.Equal(360, valid.UpdateCheckIntervalMinutes);

        var zero = new AppSettings { UpdateCheckIntervalMinutes = -1 };
        zero.NormalizeForLoad();
        Assert.Equal(0, zero.UpdateCheckIntervalMinutes);
    }

    // ---- Issue #103: ключ отладки /DEBUG ----

    [Theory]
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /F ""C:\Bases\ib"" /DEBUG", true)]          // в конце
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /DEBUG /F ""C:\Bases\ib""", true)]          // в середине
    [InlineData(@"1cv8c.exe /DEBUG", true)]                                            // в начале
    [InlineData(@"/debug /F ""C:\Bases\ib""", true)]                                   // нижний регистр
    [InlineData(@"""1cv8c.exe"" ENTERPRISE ""/DEBUG"" /S srv\base", true)]             // в кавычках
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /DEBUGKEY /F ""C:\Bases\ib""", false)]      // /DEBUGKEY — не /DEBUG
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /DEBUG-KEY /F ""C:\Bases\ib""", false)]     // продолжение ключа
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /DEBUGGER", false)]                         // продолжение ключа
    [InlineData(@"DEBUG /F ""C:\Bases\ib""", false)]                                   // без префикса
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /DEBUGKEY2 /DEBUGX", false)]                // только продолжения
    [InlineData("", false)]
    public void HasDebugKey_DetectsStandaloneDebugToken(string commandLine, bool expected)
    {
        Assert.Equal(expected, RunningInfobaseMatcher.HasDebugKey(commandLine));
    }

    [Fact]
    public void HasDebugKey_NullCommandLine_ReturnsFalse()
    {
        Assert.False(RunningInfobaseMatcher.HasDebugKey(null));
    }

    // v1.0.1.54 (issue #103): константа Infobase.EnterpriseDebugMarkerHex и
    // отдельный янтарный маркер удалены — цвет значка Предприятия теперь сам
    // кодирует отладку (синий при /DEBUG, оранжевый без); проверки — в
    // Issue103EnterpriseGlyphTests.
}
