using System;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #178 «Мои программы — запуск от администратора»: у программы появился флаг
/// <see cref="CustomProgram.RunAsAdministrator"/> и необязательная учётная запись.
/// Пароль приложение не хранит — при указанной учётной записи используется
/// <c>runas /savecred</c> (Windows сохраняет пароль после первого ввода), иначе —
/// обычный UAC-запрос (Verb = "runas").
/// </summary>
public sealed class Issue178RunAsAdministratorTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var path = Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"Файл проекта не найден: {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Model_DefaultsToNoElevation()
    {
        var program = new CustomProgram();

        Assert.False(program.RunAsAdministrator);
        Assert.Equal(string.Empty, program.RunAsUserName);
    }

    [Fact]
    public void BuildRunAsArguments_UsesSaveCredAndQuotesPath()
    {
        var program = new CustomProgram
        {
            FilePath = @"C:\Program Files\pgAdmin 4\bin\pgAdmin4.exe",
            Arguments = "--verbose",
            RunAsUserName = @"DOMAIN\admin",
            RunAsAdministrator = true,
        };

        var args = CustomProgramLauncher.BuildRunAsArguments(program);

        Assert.StartsWith(@"/savecred /user:DOMAIN\admin ", args);
        Assert.Contains(@"""C:\Program Files\pgAdmin 4\bin\pgAdmin4.exe""", args);
        Assert.EndsWith("--verbose", args);
    }

    [Fact]
    public void RequiresElevation_RequiresFlag()
    {
        Assert.False(CustomProgramLauncher.RequiresElevation(null));
        Assert.False(CustomProgramLauncher.RequiresElevation(new CustomProgram { RunAsAdministrator = false }));

        var elevated = CustomProgramLauncher.RequiresElevation(
            new CustomProgram { RunAsAdministrator = true });
        // Флаг учитывается только на Windows; тест не зависит от платформы запуска.
        Assert.Equal(OperatingSystem.IsWindows(), elevated);
    }

    [Theory]
    [InlineData("ru.json")]
    [InlineData("en.json")]
    public void Localization_HasRunAsKeys(string file)
    {
        var json = ReadProjectFile("Workspace1C", "Localization", "Languages", file);
        foreach (var key in new[]
                 {
                     "Launch.CustomPrograms.RunAsAdministrator",
                     "Launch.CustomPrograms.RunAsAdministratorTooltip",
                     "Launch.CustomPrograms.RunAsUserName",
                     "Launch.CustomPrograms.RunAsUserNameTooltip",
                 })
        {
            Assert.Contains($"\"{key}\"", json);
        }
    }

    [Fact]
    public void Editor_HasRunAsControlsOnBothPlatforms()
    {
        var wpf = ReadProjectFile("Workspace1C", "Views", "CustomProgramsWindow.xaml");
        var avalonia = ReadProjectFile("Workspace1C", "Views", "CustomProgramsWindow.Avalonia.cs");

        Assert.Contains("SelectedProgram.RunAsAdministrator", wpf);
        Assert.Contains("SelectedProgram.RunAsUserName", wpf);
        Assert.Contains("SelectedProgram.RunAsAdministrator", avalonia);
        Assert.Contains("SelectedProgram.RunAsUserName", avalonia);
    }
}
