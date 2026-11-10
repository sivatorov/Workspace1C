using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Workspace1C.Services.Licensing;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты-стражи окна «Как купить ПРО-версию» (v1.0.1.53):
/// - окно существует на обеих платформах и открывается кнопкой «Купить» окна «Лицензия»
///   (а не GitHub-ссылка ProFeatures.BuyUrl);
/// - кнопки контактов ведут только в Telegram-канал проекта и Issues репозитория;
/// - в текстах окна и локализации нет личных платёжных реквизитов;
/// - ключи локализации Pro.HowToBuy.* присутствуют в ru и en, шаги упоминают цену.
/// </summary>
public sealed class HowToBuyWindowV100153Tests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null &&
               !Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Views")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadSourceFile(params string[] parts)
    {
        var path = Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), "Не найден файл: " + path);
        return File.ReadAllText(path);
    }

    private static Dictionary<string, string> ReadStrings(string language)
    {
        var path = Path.Combine(FindRepoRoot(), "Workspace1C", "Localization", "Languages", language + ".json");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var strings = doc.RootElement.GetProperty("strings");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in strings.EnumerateObject())
            result[prop.Name] = prop.Value.GetString() ?? string.Empty;
        return result;
    }

    /// <summary>Все ключи окна «Как купить ПРО-версию».</summary>
    private static readonly string[] HowToBuyKeys =
    {
        "Pro.HowToBuy.Title",
        "Pro.HowToBuy.PriceHeader",
        "Pro.HowToBuy.Step1",
        "Pro.HowToBuy.Step2",
        "Pro.HowToBuy.Step3",
        "Pro.HowToBuy.Step4",
        "Pro.HowToBuy.Step5",
        "Pro.HowToBuy.ContactsHeader",
        "Pro.HowToBuy.CopyMachineCode",
        "Pro.HowToBuy.OpenTelegram",
        "Pro.HowToBuy.OpenIssues",
        "Pro.HowToBuy.OpenReadme"
    };

    private const string WpfWindowPath = "Workspace1C/Views/HowToBuyWindow.cs";
    private const string AvaloniaWindowPath = "Workspace1C/Views/HowToBuyWindow.Avalonia.cs";

    // ------------------------------------------------------------ окно на обеих платформах

    [Fact]
    public void HowToBuyWindow_Exists_ForBothPlatforms()
    {
        var wpf = ReadSourceFile(WpfWindowPath.Split('/'));
        var avalonia = ReadSourceFile(AvaloniaWindowPath.Split('/'));

        // WPF-версия собирается только под Windows и открывается модально.
        Assert.Contains("#if WINDOWS", wpf, StringComparison.Ordinal);
        // Шаг 5: кнопка «Активировать» ведёт в окно «Лицензия».
        Assert.Contains("new LicenseWindow().ShowDialog()", wpf, StringComparison.Ordinal);

        // Avalonia-версия — модальное окно по базовому классу проекта, открывается ShowDialogSync.
        Assert.Contains("#if LINUX", avalonia, StringComparison.Ordinal);
        Assert.Contains(": ModalWindowBase", avalonia, StringComparison.Ordinal);
        Assert.Contains("new LicenseWindow().ShowDialogSync(this)", avalonia, StringComparison.Ordinal);
    }

    [Fact]
    public void Buy_Button_OpensHowToBuy_NotGitHub()
    {
        // Кнопка «Купить» окна «Лицензия» больше не открывает GitHub — открывает окно инструкции.
        var wpf = ReadSourceFile("Workspace1C", "Views", "LicenseWindow.cs");
        var avalonia = ReadSourceFile("Workspace1C", "Views", "LicenseWindow.Avalonia.cs");

        Assert.DoesNotContain("OpenUrl(ProFeatures.BuyUrl)", wpf, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenUrl(ProFeatures.BuyUrl)", avalonia, StringComparison.Ordinal);
        Assert.Contains("new HowToBuyWindow().ShowDialog()", wpf, StringComparison.Ordinal);
        Assert.Contains("new HowToBuyWindow().ShowDialogSync(this)", avalonia, StringComparison.Ordinal);
    }

    [Fact]
    public void BuyUrl_StillUsedByReadmeButton()
    {
        // Справочная ссылка на README сохранена: константа + кнопка «Подробнее в README» в окне.
        var proFeature = ReadSourceFile("Workspace1C", "Services", "Licensing", "ProFeature.cs");
        Assert.Contains(
            "https://github.com/sivatorov/Workspace1C#-про-версия",
            proFeature,
            StringComparison.Ordinal);

        Assert.Contains("OpenContact(ProFeatures.BuyUrl)",
            ReadSourceFile(WpfWindowPath.Split('/')), StringComparison.Ordinal);
        Assert.Contains("OpenContact(ProFeatures.BuyUrl)",
            ReadSourceFile(AvaloniaWindowPath.Split('/')), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ контакты

    [Fact]
    public void HowToBuyWindow_UsesOnlyOfficialContactUrls()
    {
        var proFeature = ReadSourceFile("Workspace1C", "Services", "Licensing", "ProFeature.cs");

        // Подтверждённые владельцем контакты: Telegram-канал и Issues. Никаких личных реквизитов.
        Assert.Contains("public const string TelegramUrl = \"https://t.me/Workspace1C\";",
            proFeature, StringComparison.Ordinal);
        Assert.Contains("public const string IssuesUrl = \"https://github.com/sivatorov/Workspace1C/issues\";",
            proFeature, StringComparison.Ordinal);

        foreach (var path in new[] { WpfWindowPath, AvaloniaWindowPath })
        {
            var source = ReadSourceFile(path.Split('/'));
            Assert.Contains("OpenContact(ProFeatures.TelegramUrl)", source, StringComparison.Ordinal);
            Assert.Contains("OpenContact(ProFeatures.IssuesUrl)", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void HowToBuyWindow_HasCopyMachineCodeButton_OnBothPlatforms()
    {
        var wpf = ReadSourceFile(WpfWindowPath.Split('/'));
        var avalonia = ReadSourceFile(AvaloniaWindowPath.Split('/'));

        Assert.Contains("Pro.HowToBuy.CopyMachineCode", wpf, StringComparison.Ordinal);
        Assert.Contains("Pro.HowToBuy.CopyMachineCode", avalonia, StringComparison.Ordinal);
        Assert.Contains("ReportCodeCopied()", wpf, StringComparison.Ordinal);
        Assert.Contains("ReportCodeCopied()", avalonia, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ ViewModel

    [Fact]
    public void HowToBuyViewModel_MachineCode_MatchesLicenseViewModel()
    {
        MachineIdProvider.RawIdOverride = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";
        try
        {
            var vm = new HowToBuyViewModel();
            Assert.Equal(MachineIdProvider.Current, vm.MachineCode);
            Assert.Equal(MachineIdProvider.Current, vm.MachineCodeText);
            Assert.Equal(new LicenseViewModel().MachineCode, vm.MachineCode);
        }
        finally
        {
            MachineIdProvider.RawIdOverride = null;
        }
    }

    [Fact]
    public void HowToBuyViewModel_ReportCodeCopied_SetsStatusText()
    {
        var vm = new HowToBuyViewModel();
        Assert.Equal(string.Empty, vm.StatusText);
        vm.ReportCodeCopied();
        Assert.False(string.IsNullOrWhiteSpace(vm.StatusText));
        vm.ReportLinkOpenFailed();
        Assert.False(string.IsNullOrWhiteSpace(vm.StatusText));
    }

    // ------------------------------------------------------------ локализация

    [Fact]
    public void HowToBuy_Keys_PresentInBothLanguages()
    {
        foreach (var language in new[] { "ru", "en" })
        {
            var strings = ReadStrings(language);
            foreach (var key in HowToBuyKeys)
            {
                Assert.True(strings.ContainsKey(key), language + ": нет ключа " + key);
                Assert.False(string.IsNullOrWhiteSpace(strings[key]), language + ": пустой ключ " + key);
            }
        }
    }

    [Fact]
    public void HowToBuy_StepsMentionPrice()
    {
        var ru = ReadStrings("ru");
        var en = ReadStrings("en");
        Assert.Contains("2000", ru["Pro.HowToBuy.PriceHeader"], StringComparison.Ordinal);
        Assert.Contains("2000", ru["Pro.HowToBuy.Step3"], StringComparison.Ordinal);
        Assert.Contains("2,000", en["Pro.HowToBuy.PriceHeader"], StringComparison.Ordinal);
        Assert.Contains("2,000", en["Pro.HowToBuy.Step3"], StringComparison.Ordinal);
    }

    [Fact]
    public void HowToBuy_Steps_MentionOfficialContacts()
    {
        var ru = ReadStrings("ru");
        var en = ReadStrings("en");
        Assert.Contains("t.me/Workspace1C", ru["Pro.HowToBuy.Step2"], StringComparison.Ordinal);
        Assert.Contains("t.me/Workspace1C", en["Pro.HowToBuy.Step2"], StringComparison.Ordinal);
        Assert.Contains("Issue", ru["Pro.HowToBuy.Step2"], StringComparison.Ordinal);
        Assert.Contains("Issue", en["Pro.HowToBuy.Step2"], StringComparison.Ordinal);
    }

    /// <summary>
    /// Страж: в текстах окна «Как купить» и в локализации нет личных платёжных реквизитов
    /// (карта/СБП/email/@username). Оплата обсуждается в Telegram-канале проекта.
    /// </summary>
    [Fact]
    public void HowToBuy_NoPersonalPaymentDetails()
    {
        var forbidden = new[]
        {
            "@", "СБП", "mailto", "ЮMoney", "Boosty", "+7", "skype", "whatsapp"
        };

        foreach (var language in new[] { "ru", "en" })
        {
            var strings = ReadStrings(language);
            foreach (var key in HowToBuyKeys)
            foreach (var token in forbidden)
                Assert.DoesNotContain(token, strings[key], StringComparison.OrdinalIgnoreCase);
        }

        foreach (var path in new[] { WpfWindowPath, AvaloniaWindowPath })
        {
            var source = ReadSourceFile(path.Split('/'));
            foreach (var token in forbidden)
                Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase);
        }
    }
}
