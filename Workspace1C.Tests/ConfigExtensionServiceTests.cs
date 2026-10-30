using System.Linq;
using System.Text;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Расширения конфигурации (выпуск 1.0.0.69): ключи командной строки конфигуратора,
/// разбор списка расширений, безопасность значений и логика окна.
/// </summary>
public sealed class ConfigExtensionServiceTests
{
    [Fact]
    public void InstallArguments_LoadAndUpdateExtension()
    {
        // issue #87 («Загрузка расширения конфигурации в клиент-серверную базу»): у
        // /UpdateDBCfg ключа -Extension НЕТ — повторный ключ у операции применения давал
        // «Error in command-line options» (код 1) на клиент-серверной базе. Расширение
        // применяется последующим /UpdateDBCfg без повторения ключа.
        var args = ConfigExtensionService.BuildInstallArguments(@"C:\cfe\Доработки.cfe", " Доработки ");
        Assert.Equal("/LoadCfg\"C:\\cfe\\Доработки.cfe\" -Extension \"Доработки\" /UpdateDBCfg", args);
        Assert.DoesNotContain("-Extension",
            args.Substring(args.IndexOf("/UpdateDBCfg", StringComparison.Ordinal)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void IsCommandLineOptionsError_DetectsDesignerParameterFailure()
    {
        // issue #87: fallback на ibcmd при отказе конфигуратора «Error in command-line options».
        Assert.True(ConfigExtensionService.IsCommandLineOptionsError(
            new DesignerCommandResult { ExitCode = 1, Error = "Конфигуратор завершился с кодом 1: Error in command-line options" }));
        Assert.True(ConfigExtensionService.IsCommandLineOptionsError(
            new DesignerCommandResult { ExitCode = 1, Log = "… Error in command-line options …" }));
        Assert.False(ConfigExtensionService.IsCommandLineOptionsError(
            new DesignerCommandResult { Success = true, ExitCode = 0 }));
        Assert.False(ConfigExtensionService.IsCommandLineOptionsError(
            new DesignerCommandResult { ExitCode = 1, Error = "База занята" }));
        Assert.False(ConfigExtensionService.IsCommandLineOptionsError(null!));
    }

    [Fact]
    public void IsAuthenticationError_DetectsInfobaseUserNotAuthenticated()
    {
        // issue #87: база без собственных учётных данных при незаданной авторизации
        // запуска — конфигуратор падает с «The infobase user is not authenticated».
        Assert.True(ConfigExtensionService.IsAuthenticationError(
            new DesignerCommandResult { ExitCode = 1, Error = "РД_001 3.0 (агенты): Конфигуратор завершился с кодом 1: The infobase user is not authenticated" }));
        Assert.True(ConfigExtensionService.IsAuthenticationError(
            new DesignerCommandResult { ExitCode = 1, Log = "… The infobase user is not authenticated …" }));
        Assert.False(ConfigExtensionService.IsAuthenticationError(
            new DesignerCommandResult { Success = true, ExitCode = 0 }));
        Assert.False(ConfigExtensionService.IsAuthenticationError(
            new DesignerCommandResult { ExitCode = 1, Error = "Error in command-line options" }));
        Assert.False(ConfigExtensionService.IsAuthenticationError(null!));
    }

    [Fact]
    public void ShouldFallbackToIbcmd_CoversBothFailureReasons()
    {
        // issue #87: переход на ibcmd-ветку нужен и при «command-line options», и при
        // сбое аутентификации пользователя ИБ.
        Assert.True(ConfigExtensionService.ShouldFallbackToIbcmd(
            new DesignerCommandResult { ExitCode = 1, Error = "Error in command-line options" }));
        Assert.True(ConfigExtensionService.ShouldFallbackToIbcmd(
            new DesignerCommandResult { ExitCode = 1, Error = "The infobase user is not authenticated" }));
        Assert.False(ConfigExtensionService.ShouldFallbackToIbcmd(
            new DesignerCommandResult { Success = true, ExitCode = 0 }));
        Assert.False(ConfigExtensionService.ShouldFallbackToIbcmd(
            new DesignerCommandResult { ExitCode = 1, Error = "База занята" }));
        Assert.False(ConfigExtensionService.ShouldFallbackToIbcmd(null!));
    }

    [Fact]
    public void DumpAndDeleteArguments()
    {
        Assert.Equal("/DumpCfg\"/tmp/x.cfe\" -Extension \"Ext\"", ConfigExtensionService.BuildDumpArguments("/tmp/x.cfe", "Ext"));
        Assert.Equal("/DeleteCfg -Extension \"Ext\"", ConfigExtensionService.BuildDeleteArguments("Ext"));
    }

    [Theory]
    [InlineData("bad\"name")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("line\nbreak")]
    public void UnsafeValues_AreRejected(string name)
    {
        Assert.Null(ConfigExtensionService.BuildDeleteArguments(name));
        Assert.Null(ConfigExtensionService.BuildInstallArguments("/tmp/a.cfe", name));
    }

    [Fact]
    public void SuggestName_FromFileName()
    {
        Assert.Equal("Доработки_1.2", ConfigExtensionService.SuggestName("/x/Доработки_1.2.cfe"));
        Assert.Equal(string.Empty, ConfigExtensionService.SuggestName(null));
    }

    [Fact]
    public void ParseList_ReturnsNamesWithoutServiceLines()
    {
        var log = "\uFEFFДоработки\r\nИсправления_ЗУП\r\n\r\nДоработки\r\nОбновление конфигурации успешно завершено.\r\n";
        var names = ConfigExtensionService.ParseList(log);
        Assert.Equal(new[] { "Доработки", "Исправления_ЗУП" }, names.ToArray());
        Assert.Empty(ConfigExtensionService.ParseList(null));
    }

    [Fact]
    public void DesignerArguments_ContainOutAndDisableDialogs()
    {
        var args = DesignerCommandRunner.BuildArguments("/F\"C:\\db\"", " /N\"Admin\"", "/DeleteCfg -Extension \"E\"", "C:\\tmp\\out.log");
        Assert.StartsWith("DESIGNER /F\"C:\\db\" /N\"Admin\" /DeleteCfg", args);
        Assert.Contains("/DisableStartupDialogs", args);
        Assert.EndsWith("/Out\"C:\\tmp\\out.log\"", args);
    }

    [Fact]
    public void DecodeLog_Utf8AndBom()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("Привет")).ToArray();
        Assert.Equal("Привет", DesignerCommandRunner.DecodeLog(bytes));
        Assert.Equal(string.Empty, DesignerCommandRunner.DecodeLog(System.Array.Empty<byte>()));
    }

    [Fact]
    public void ViewModel_SelectedBaseIsCheckedAndFilterWorks()
    {
        var a = new Infobase { Id = "a", Name = "Бухгалтерия" };
        var b = new Infobase { Id = "b", Name = "ЗУП" };
        var vm = new ExtensionsViewModel(new[] { a, b }, b);
        Assert.Equal(2, vm.Targets.Count);
        Assert.Single(vm.CheckedTargets);
        Assert.Same(b, vm.CheckedTargets[0].Infobase);
        Assert.Same(b, vm.SelectedTarget!.Infobase);

        vm.SearchText = "бух";
        Assert.Single(vm.Targets);
        vm.SetAllChecked(true);
        Assert.Equal(2, vm.CheckedTargets.Count);
    }

    [Fact]
    public void ViewModel_CfePathSuggestsNameOnce()
    {
        var vm = new ExtensionsViewModel(System.Array.Empty<Infobase>(), null);
        vm.CfePath = "/x/Первое.cfe";
        Assert.Equal("Первое", vm.ExtensionName);
        vm.CfePath = "/x/Второе.cfe";
        Assert.Equal("Первое", vm.ExtensionName);
    }
}
