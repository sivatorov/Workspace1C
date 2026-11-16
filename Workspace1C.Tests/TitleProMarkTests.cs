using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Пункт 4 плана ПРО: метка редакции «★» перед названием в заголовке главного окна
/// (WPF и Avalonia). Проверяется, что обе версии берут метку из общего источника
/// (<see cref="Workspace1C.Services.Licensing.LicenseSummary.ProMark"/> и
/// <see cref="Workspace1C.Services.Licensing.EditionService.IsPro"/>), передают её
/// в общий компоновщик и пересобирают заголовок по событию
/// <see cref="Workspace1C.Services.Licensing.EditionService.Changed"/> без перезапуска.
/// Тесты статические (читают исходники из репозитория, как AboutLicenseBlockTests),
/// поэтому не требуют UI-потока и Avalonia-сборки.
/// </summary>
public sealed class TitleProMarkTests
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

    // ===================== WPF =====================

    [Fact]
    public void WpfTitle_UsesProMarkAndEditionService()
    {
        var language = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Language.cs"));

        // Метка и признак редакции берутся из общих сервисов, а не дублируются строкой.
        Assert.Contains("EditionService.IsPro", language, StringComparison.Ordinal);
        Assert.Contains("LicenseSummary.ProMark", language, StringComparison.Ordinal);
        // Метка передаётся в общий компоновщик заголовка.
        Assert.Contains("proMark: proMark", language, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfMainWindow_RefreshesTitleOnEditionChanged()
    {
        var window = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml.cs"));
        var language = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Language.cs"));

        // Подписка на смену лицензии и обязательная отписка при закрытии окна.
        Assert.Contains("EditionService.Changed += OnEditionServiceChanged;", window, StringComparison.Ordinal);
        Assert.Contains("EditionService.Changed -= OnEditionServiceChanged;", window, StringComparison.Ordinal);
        // Обработчик пересобирает заголовок в UI-потоке.
        Assert.Contains("OnEditionServiceChanged", language, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.CheckAccess()", language, StringComparison.Ordinal);
        Assert.Contains("UpdateWindowTitle()", language, StringComparison.Ordinal);
    }

    // ===================== Avalonia =====================

    [Fact]
    public void AvaloniaTitle_UsesProMarkAndEditionService()
    {
        var language = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Language.cs"));

        Assert.Contains("EditionService.IsPro", language, StringComparison.Ordinal);
        Assert.Contains("LicenseSummary.ProMark", language, StringComparison.Ordinal);
        Assert.Contains("proMark: proMark", language, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaMainWindow_RefreshesTitleOnEditionChanged()
    {
        var window = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));

        // Подписка на смену лицензии и отписка при закрытии окна.
        Assert.Contains("EditionService.Changed += OnEditionChanged;", window, StringComparison.Ordinal);
        Assert.Contains("EditionService.Changed -= OnEditionChanged;", window, StringComparison.Ordinal);
        // Обработчик обновляет и меню «Утилиты», и заголовок окна.
        Assert.Contains("RebuildUtilitiesMenu();", window, StringComparison.Ordinal);
        Assert.Contains("Title = ComposeWindowTitle();", window, StringComparison.Ordinal);
    }
}
