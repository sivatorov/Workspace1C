using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Пункт 2 плана ПРО: блок «Лицензия» на вкладке «О программе» WPF-версии.
/// Блок всегда виден (бесплатная редакция — «Редакция: бесплатная» и «Купить ПРО»,
/// ПРО — владелец, данные ключа, код компьютера и «Лицензия…») и расположен
/// строго ВЫШЕ кнопок «Скопировать техническую информацию» / «Собрать диагностику (ZIP)».
/// Тесты читают исходники из репозитория (как Issue71/Issue99), поэтому не требуют UI.
/// </summary>
public sealed class AboutLicenseBlockTests
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

    private static int IndexOf(string text, string needle)
    {
        var index = text.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(index >= 0, $"В разметке не найдено: {needle}");
        return index;
    }

    // ===================== Разметка вкладки «О программе» =====================

    [Fact]
    public void WpfAboutTab_HasLicenseBlockWithNamedElements()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));

        Assert.Contains("x:Name=\"AboutLicenseBlock\"", xaml, StringComparison.Ordinal);
        // Строки блока: редакция, владелец, данные ключа, код компьютера, ошибка ключа
        // и напоминание о сборке без модулей ПРО.
        Assert.Contains("x:Name=\"AboutLicenseEditionText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AboutLicenseOwnerText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AboutLicenseKeyInfoText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AboutLicenseMachineCodeRun\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AboutLicenseStatusText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AboutLicenseCommunityText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AboutLicenseProMark\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfAboutTab_HasLicenseAndBuyProButtons()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));

        Assert.Contains("x:Name=\"AboutLicenseButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnAboutLicense_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AboutBuyProButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnAboutBuyPro_Click\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfAboutTab_LicenseBlock_AboveTechInfoAndDiagnosticsButtons()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));

        var block = IndexOf(xaml, "x:Name=\"AboutLicenseBlock\"");
        var licenseButton = IndexOf(xaml, "x:Name=\"AboutLicenseButton\"");
        var buyButton = IndexOf(xaml, "x:Name=\"AboutBuyProButton\"");
        var buttonsColumn = IndexOf(xaml, "x:Name=\"AboutButtonsColumn\"");

        // Новые элементы строго ВЫШЕ существующих кнопок техинформации/диагностики —
        // их порядок не меняется.
        Assert.True(block < buttonsColumn, "Блок лицензии должен быть выше AboutButtonsColumn.");
        Assert.True(licenseButton < buttonsColumn, "Кнопка «Лицензия…» должна быть выше AboutButtonsColumn.");
        Assert.True(buyButton < buttonsColumn, "Кнопка «Купить ПРО» должна быть выше AboutButtonsColumn.");
        Assert.True(IndexOf(xaml, "OnCopyTechInfo_Click") > buttonsColumn,
            "Кнопка «Скопировать техническую информацию» остаётся внутри AboutButtonsColumn.");
        Assert.True(IndexOf(xaml, "OnCollectDiagnostics_Click") > buttonsColumn,
            "Кнопка «Собрать диагностику (ZIP)» остаётся внутри AboutButtonsColumn.");
    }

    [Fact]
    public void WpfAboutTab_LicenseBlock_UsesExistingLocalizationKeys()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));

        Assert.Contains("{loc:Loc Settings.About.LicenseHeader}", xaml, StringComparison.Ordinal);
        Assert.Contains("{loc:Loc Pro.MenuItem}", xaml, StringComparison.Ordinal);
        Assert.Contains("{loc:Loc Settings.About.BuyPro}", xaml, StringComparison.Ordinal);
        // Код компьютера — моноширинный (Consolas), как в окне «Лицензия».
        Assert.Contains("FontFamily=\"Consolas\"", xaml, StringComparison.Ordinal);
        // Метка ПРО берётся из LicenseSummary.ProMark, а не дублируется строкой.
        Assert.Contains("licensing:LicenseSummary.ProMark", xaml, StringComparison.Ordinal);
    }

    // ===================== Код окна настроек =====================

    [Fact]
    public void WpfSettingsWindow_RefreshesLicenseBlockOnOpenAndEditionChange()
    {
        var window = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml.cs"));
        Assert.Contains("InitializeAboutLicense();", window, StringComparison.Ordinal);

        var platforms = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Platforms.cs"));
        // Заполнение из общей сводки LicenseSummary (не дублируем логику ключа).
        Assert.Contains("LicenseSummary.Current()", platforms, StringComparison.Ordinal);
        // Подписка на смену редакции и обязательная отписка при закрытии окна.
        Assert.Contains("EditionService.Changed += OnEditionServiceChanged;", platforms, StringComparison.Ordinal);
        Assert.Contains("EditionService.Changed -= OnEditionServiceChanged;", platforms, StringComparison.Ordinal);
        // Обновление в UI-потоке.
        Assert.Contains("Dispatcher.BeginInvoke", platforms, StringComparison.Ordinal);
        // Пустые строки скрываются, «Купить ПРО» видна только в бесплатной редакции.
        Assert.Contains("Visibility.Collapsed", platforms, StringComparison.Ordinal);
        Assert.Contains("AboutBuyProButton.Visibility", platforms, StringComparison.Ordinal);
        // Повреждённый ключ не роняет окно — безопасный текст ошибки.
        Assert.Contains("catch (Exception ex)", platforms, StringComparison.Ordinal);
        Assert.Contains("LocalizationManager.T(\"Common.Error\")", platforms, StringComparison.Ordinal);
        // Обработчики открывают окна «Лицензия» и «Как купить ПРО».
        Assert.Contains("new LicenseWindow { Owner = this }.ShowDialog();", platforms, StringComparison.Ordinal);
        Assert.Contains("new HowToBuyWindow { Owner = this }.ShowDialog();", platforms, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void Localization_AboutLicenseKeys_Present(string language)
    {
        var json = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", language + ".json"));
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("strings", out var strings));

        foreach (var key in new[]
                 {
                     "Settings.About.LicenseHeader",
                     "Settings.About.LicenseMachineCode",
                     "Settings.About.LicenseKeyInfo",
                     "Settings.About.BuyPro",
                     "Pro.MenuItem",
                     "Pro.EditionFree",
                     "Pro.CommunityBuild"
                 })
        {
            Assert.True(
                strings.TryGetProperty(key, out var value) && value.GetString() is { Length: > 0 },
                $"{language}.json: ключ {key} отсутствует или пуст.");
        }
    }
}
