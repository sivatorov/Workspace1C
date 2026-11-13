using System.IO;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #117, релиз 1.0.1.62: регрессия на РЕАЛЬНОМ файле расширения «ИР»
/// (<c>ir-8.23.1e.cfe</c>, Инструменты разработчика Tormozit, платформа 8.23.1e).
/// Файл весит ~17 МБ и НЕ коммитится: он лежит в <c>relnotes_tmp/fixtures/</c>
/// (каталог вне репозитория сборки). Если файла нет — тест НЕ падает, а
/// пропускается: синтетические раскладки контейнера проверяются в
/// <see cref="Issue117ExtensionNameTests"/> и <see cref="ConfigExtensionServiceTests"/>.
/// </summary>
public sealed class Issue117CfeRealFileRegressionTests
{
    /// <summary>Имя расширения «ИР» в метаданных контейнера (как в списке расширений базы).</summary>
    internal const string ExpectedExtensionName = "ИнструментыРазработчикаTormozit";

    /// <summary>Имя файла фикстуры из комментария @RafaIS к issue #117.</summary>
    internal const string FixtureFileName = "ir-8.23.1e.cfe";

    /// <summary>
    /// Ищет фикстуру: переменная окружения <c>WS1C_CFE_FIXTURE</c>, затем
    /// <c>relnotes_tmp/fixtures/</c> и <c>Workspace1C.Tests/Fixtures/Extensions/</c>
    /// вверх по дереву каталогов от каталога сборки тестов.
    /// </summary>
    internal static string? FindFixture()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("WS1C_CFE_FIXTURE");
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && File.Exists(fromEnvironment))
            return fromEnvironment;

        var candidates = new[]
        {
            Path.Combine("relnotes_tmp", "fixtures", FixtureFileName),
            Path.Combine("Workspace1C.Tests", "Fixtures", "Extensions", FixtureFileName),
        };

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var level = 0; level < 8 && dir is not null; level++, dir = dir.Parent)
        {
            foreach (var relative in candidates)
            {
                var path = Path.Combine(dir.FullName, relative);
                if (File.Exists(path))
                    return path;
            }
        }
        return null;
    }

    [Fact]
    public void RealIrExtension_ReadsMetadataName_NotFileName()
    {
        var path = FindFixture();
        if (path is null)
            return; // фикстуры нет — регрессия пропускается (синтетические тесты остаются)

        // Файл называется «ir-8.23.1e.cfe», а настоящее имя расширения — из метаданных
        // контейнера. Именно это имя показано в списке расширений базы, и под ним
        // установка выполняется как ОБНОВЛЕНИЕ, а не как дубль под именем файла.
        Assert.Equal(ExpectedExtensionName, ConfigExtensionService.TryReadExtensionName(path));
        Assert.Equal(ExpectedExtensionName, ConfigExtensionService.ResolveName(path));
    }

    [Fact]
    public void RealIrExtension_InstallNameAndInstalledMatchUseMetadataName()
    {
        var path = FindFixture();
        if (path is null)
            return; // фикстуры нет — регрессия пропускается

        var realName = ConfigExtensionService.TryReadExtensionName(path);
        Assert.Equal(ExpectedExtensionName, realName);

        // Имя файла («ir-8.23.1e») в базе отсутствует, а настоящее имя есть —
        // установка должна идти под настоящим именем (обновление существующего).
        var installed = new[] { ExpectedExtensionName, "ДругоеРасширение" };
        Assert.Equal(ExpectedExtensionName,
            ConfigExtensionService.ResolveInstallName(realName, ConfigExtensionService.SuggestName(path),
                nameEdited: false));
        Assert.Equal(ExpectedExtensionName,
            ConfigExtensionService.FindInstalledMatch(realName, installed));
        Assert.False(ConfigExtensionService.RequiresUnknownNameConfirmation(realName, realName, installed));
    }
}
