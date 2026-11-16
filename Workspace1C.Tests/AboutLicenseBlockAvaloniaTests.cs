using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Пункт 3 плана ПРО: блок «Лицензия» на вкладке «О программе» Avalonia-версии
/// (Linux). Повторяет состав и поведение WPF-блока: редакция, владелец, данные
/// ключа, код компьютера, статус, напоминание о сборке без модулей ПРО и кнопки
/// «Лицензия…»/«Купить ПРО». Блок всегда виден и расположен строго ВЫШЕ кнопок
/// «Скопировать техническую информацию» / «Собрать диагностику (ZIP)».
/// Тесты читают исходник из репозитория (как AboutLicenseBlockTests), поэтому
/// не требуют Avalonia-сборки и UI.
/// </summary>
public sealed class AboutLicenseBlockAvaloniaTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadAvaloniaSettingsWindow()
    {
        var relativePath = Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    private static int IndexOf(string text, string needle)
    {
        var index = text.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(index >= 0, $"В исходнике не найдено: {needle}");
        return index;
    }

    // ===================== Состав блока лицензии =====================

    [Fact]
    public void AvaloniaAboutTab_HasLicenseBlockWithNamedElements()
    {
        var source = ReadAvaloniaSettingsWindow();

        Assert.Contains("BuildAboutLicenseBlock()", source, StringComparison.Ordinal);
        // Строки блока: редакция, владелец, данные ключа, код компьютера, ошибка
        // ключа и напоминание о сборке без модулей ПРО.
        Assert.Contains("_aboutLicenseEditionText", source, StringComparison.Ordinal);
        Assert.Contains("_aboutLicenseOwnerText", source, StringComparison.Ordinal);
        Assert.Contains("_aboutLicenseKeyInfoText", source, StringComparison.Ordinal);
        Assert.Contains("_aboutLicenseMachineCodeLabel", source, StringComparison.Ordinal);
        Assert.Contains("_aboutLicenseMachineCodeText", source, StringComparison.Ordinal);
        Assert.Contains("_aboutLicenseStatusText", source, StringComparison.Ordinal);
        Assert.Contains("_aboutLicenseCommunityText", source, StringComparison.Ordinal);
        Assert.Contains("_aboutLicenseProMark", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaAboutTab_HasLicenseAndBuyProButtons()
    {
        var source = ReadAvaloniaSettingsWindow();

        Assert.Contains("_aboutBuyProButton", source, StringComparison.Ordinal);
        Assert.Contains("new LicenseWindow().ShowDialogSync(this);", source, StringComparison.Ordinal);
        Assert.Contains("new HowToBuyWindow().ShowDialogSync(this);", source, StringComparison.Ordinal);
        // Кнопки оформлены вторичным стилем темы, как в WPF-версии.
        Assert.Contains("ControlThemes.SecondaryButton", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaAboutTab_LicenseBlock_AboveTechInfoAndDiagnosticsButtons()
    {
        var source = ReadAvaloniaSettingsWindow();

        var block = IndexOf(source, "panel.Children.Add(BuildAboutLicenseBlock());");
        var copyButton = IndexOf(source, "panel.Children.Add(copyButton);");
        var diagButton = IndexOf(source, "panel.Children.Add(diagButton);");

        // Блок лицензии строго ВЫШЕ существующих кнопок техинформации/диагностики —
        // их порядок не меняется.
        Assert.True(block < copyButton, "Блок лицензии должен быть выше кнопки копирования техинформации.");
        Assert.True(block < diagButton, "Блок лицензии должен быть выше кнопки сбора диагностики.");
        Assert.True(copyButton < diagButton, "Порядок кнопок техинформации/диагностики не должен меняться.");
    }

    [Fact]
    public void AvaloniaAboutTab_LicenseBlock_UsesSharedSummaryAndLocalizationKeys()
    {
        var source = ReadAvaloniaSettingsWindow();

        // Данные берутся из общей сводки, логика ключа не дублируется.
        Assert.Contains("LicenseSummary.Current()", source, StringComparison.Ordinal);
        // Метка ПРО берётся из LicenseSummary.ProMark, а не дублируется строкой.
        Assert.Contains("LicenseSummary.ProMark", source, StringComparison.Ordinal);
        // Код компьютера — моноширинный, как в окне «Лицензия».
        Assert.Contains("new FontFamily(\"monospace\")", source, StringComparison.Ordinal);
        // Локализация — те же ключи, что в WPF-версии.
        Assert.Contains("Settings.About.LicenseHeader", source, StringComparison.Ordinal);
        Assert.Contains("Settings.About.LicenseMachineCode", source, StringComparison.Ordinal);
        Assert.Contains("Pro.MenuItem", source, StringComparison.Ordinal);
        Assert.Contains("Settings.About.BuyPro", source, StringComparison.Ordinal);
        Assert.Contains("Pro.CommunityBuild", source, StringComparison.Ordinal);
    }

    // ===================== Поведение блока =====================

    [Fact]
    public void AvaloniaSettingsWindow_RefreshesLicenseBlockOnOpenAndEditionChange()
    {
        var source = ReadAvaloniaSettingsWindow();

        // Первичное заполнение при открытии окна.
        Assert.Contains("InitializeAboutLicense();", source, StringComparison.Ordinal);
        // Подписка на смену редакции и обязательная отписка при закрытии окна.
        Assert.Contains("EditionService.Changed += OnEditionServiceChanged;", source, StringComparison.Ordinal);
        Assert.Contains("EditionService.Changed -= OnEditionServiceChanged;", source, StringComparison.Ordinal);
        Assert.Contains("Closed += OnSettingsWindowClosedForLicense;", source, StringComparison.Ordinal);
        Assert.Contains("Closed -= OnSettingsWindowClosedForLicense;", source, StringComparison.Ordinal);
        // Обновление в UI-потоке.
        Assert.Contains("Dispatcher.UIThread.Post", source, StringComparison.Ordinal);
        // Пустые строки скрываются, «Купить ПРО» видна только в бесплатной редакции.
        Assert.Contains("IsVisible = false", source, StringComparison.Ordinal);
        Assert.Contains("_aboutBuyProButton.IsVisible = !isPro", source, StringComparison.Ordinal);
        // Повреждённый ключ не роняет окно — безопасный текст ошибки.
        Assert.Contains("catch (Exception ex)", source, StringComparison.Ordinal);
        Assert.Contains("LocalizationManager.T(\"Common.Error\")", source, StringComparison.Ordinal);
        // Обновление блока после возврата из окон «Лицензия»/«Как купить ПРО».
        Assert.Contains("RefreshAboutLicenseUi();", source, StringComparison.Ordinal);
    }
}
