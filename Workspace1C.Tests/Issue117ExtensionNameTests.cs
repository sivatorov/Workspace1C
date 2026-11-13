using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #117 (расширения конфигурации, «Утилиты-Операции»): настоящее имя расширения
/// из .cfe (включая «ИР» с нестандартной раскладкой контейнера) и защита от создания
/// дубля, когда имя прочитать не удалось — требование 7OH: не устанавливать молча
/// под именем файла, если в базе уже есть расширения.
/// </summary>
public sealed class Issue117ExtensionNameTests
{
    private static string TempDir =>
        Path.Combine(Path.GetTempPath(), "ws1c_issue117_tests");

    [Fact]
    public void NameEquals_TrimsSpacesAndIgnoresCase()
    {
        Assert.True(ConfigExtensionService.NameEquals(" Доработки ", "доработки"));
        Assert.False(ConfigExtensionService.NameEquals("Доработки", "Доработки2"));
        Assert.False(ConfigExtensionService.NameEquals(null, "Доработки"));
    }

    [Fact]
    public void FindInstalledMatch_FindsByNameWithTrimAndCase()
    {
        // Сопоставление по имени (идентификатору расширения) с обрезкой пробелов:
        // имя из .cfe и имя из списка базы могут отличаться регистром/пробелами.
        Assert.Equal("Доработки",
            ConfigExtensionService.FindInstalledMatch(" доработки ", new[] { "Другое", "Доработки" }));
        Assert.Null(ConfigExtensionService.FindInstalledMatch("Новое", new[] { "Другое" }));
        Assert.Null(ConfigExtensionService.FindInstalledMatch(null, new[] { "Другое" }));
        Assert.Null(ConfigExtensionService.FindInstalledMatch("Доработки", null));
    }

    [Fact]
    public void RequiresUnknownNameConfirmation_OnlyWhenNameUnknownAndBaseHasOthers()
    {
        // Имя из .cfe прочитано — подтверждение не нужно (установка обновит расширение).
        Assert.False(ConfigExtensionService.RequiresUnknownNameConfirmation(
            "НастоящееИмя", "НастоящееИмя", new[] { "Другое" }));

        // Имя не прочитано, но в базе ничего нет — дубля не будет.
        Assert.False(ConfigExtensionService.RequiresUnknownNameConfirmation(
            null, "ИмяФайла", System.Array.Empty<string>()));

        // Имя не прочитано, а имя файла совпало с установленным — это обновление.
        Assert.False(ConfigExtensionService.RequiresUnknownNameConfirmation(
            null, " ИмяФайла ", new[] { "имяфайла" }));

        // Имя не прочитано, в базе есть расширения с другими именами — нужен вопрос.
        Assert.True(ConfigExtensionService.RequiresUnknownNameConfirmation(
            null, "ИмяФайла", new[] { "Другое" }));
        Assert.True(ConfigExtensionService.RequiresUnknownNameConfirmation(
            "   ", "ИмяФайла", new[] { "Другое", "Третье" }));
    }

    [Fact]
    public void ViewModel_UnknownCfeName_KeepsFileNameAndRequiresConfirmation()
    {
        // Файла нет/имя не читается — в поле остаётся имя файла, но установка
        // без подтверждения при непустом списке расширений базы не выполняется.
        var vm = new ExtensionsViewModel(System.Array.Empty<Infobase>(), null);
        vm.CfePath = "/x/НовоеРасширение.cfe";

        Assert.False(vm.HasCfeExtensionName);
        Assert.Null(vm.CfeExtensionName);
        Assert.Equal("НовоеРасширение", vm.ExtensionName);
        Assert.True(vm.RequiresNameConfirmation(new[] { "ДругоеРасширение" }));
        Assert.False(vm.RequiresNameConfirmation(System.Array.Empty<string>()));
        Assert.False(vm.RequiresNameConfirmation(new[] { "НовоеРасширение" }));
    }

    [Fact]
    public void ViewModel_KnownCfeName_DoesNotRequireConfirmation()
    {
        Directory.CreateDirectory(TempDir);
        var path = Path.Combine(TempDir, "ИмяФайла_СовсемДругое.cfe");
        File.WriteAllBytes(path, ConfigExtensionServiceTests.BuildCfeContainer("НастоящееИмя_Расширения"));
        try
        {
            var vm = new ExtensionsViewModel(System.Array.Empty<Infobase>(), null);
            vm.CfePath = path;

            Assert.True(vm.HasCfeExtensionName);
            Assert.Equal("НастоящееИмя_Расширения", vm.CfeExtensionName);
            Assert.Equal("НастоящееИмя_Расширения", vm.ExtensionName);
            Assert.False(vm.RequiresNameConfirmation(new[] { "ДругоеРасширение" }));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ViewModel_ApplyCfeName_UnknownName_LogsWarning()
    {
        var vm = new ExtensionsViewModel(System.Array.Empty<Infobase>(), null);
        vm.CfePath = "/x/Неизвестное.cfe";

        vm.ApplyCfeName();

        // Предупреждение о том, что настоящее имя прочитать не удалось
        // (в тестовой среде LocalizationManager.T возвращает ключ).
        Assert.Contains(LocalizationManager.T("Extensions.LogNameUnknown"), vm.LogText);
    }

    [Fact]
    public void ViewModel_ApplyCfeName_ManualConflict_LogsDuplicateWarning()
    {
        Directory.CreateDirectory(TempDir);
        var path = Path.Combine(TempDir, "manual_conflict.cfe");
        File.WriteAllBytes(path, ConfigExtensionServiceTests.BuildCfeContainer("НастоящееИмя"));
        try
        {
            var vm = new ExtensionsViewModel(System.Array.Empty<Infobase>(), null);
            vm.CfePath = path;
            Assert.Equal("НастоящееИмя", vm.ExtensionName);

            // Ручной ввод другого имени — в журнале явное предупреждение о дубле.
            vm.ExtensionName = "СвоёИмя";
            vm.ApplyCfeName();

            // Явное предупреждение о создании дубля (в тестовой среде — ключ строки).
            Assert.Contains(LocalizationManager.T("Extensions.LogNameConflict"), vm.LogText);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ViewModel_ConfirmUnknownName_InvokesHandlerAndCanCancel()
    {
        var vm = new ExtensionsViewModel(System.Array.Empty<Infobase>(), null);
        vm.CfePath = "/x/Неизвестное.cfe";
        IReadOnlyCollection<string>? captured = null;
        vm.ConfirmUnknownNameAsync = installed =>
        {
            captured = installed;
            return Task.FromResult(false);
        };

        var confirmed = await vm.ConfirmUnknownExtensionNameAsync(
            new[] { "РасширениеА", "РасширениеБ" }, CancellationToken.None);

        Assert.False(confirmed);
        Assert.NotNull(captured);
        Assert.Contains("РасширениеА", captured);
        // В журнале — предупреждение о необходимости подтверждения (ключ строки).
        Assert.Contains(LocalizationManager.T("Extensions.LogNameUnknownInstalled"), vm.LogText);
    }

    [Fact]
    public async Task ViewModel_ConfirmUnknownName_WithoutHandler_CancelsToAvoidDuplicate()
    {
        var vm = new ExtensionsViewModel(System.Array.Empty<Infobase>(), null);
        vm.CfePath = "/x/Неизвестное.cfe";
        vm.ConfirmUnknownNameAsync = null;

        var confirmed = await vm.ConfirmUnknownExtensionNameAsync(
            new[] { "ДругоеРасширение" }, CancellationToken.None);

        // Без UI-обработчика установка не выполняется — дубль не создаётся молча.
        Assert.False(confirmed);
        Assert.Contains(LocalizationManager.T("Extensions.LogNameUnknownInstalled"), vm.LogText);
    }
}
