using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты issue #163 «Хранение паролей от баз» — раскладка диалогов (релиз 1.0.1.121):
/// <list type="bullet">
/// <item>WPF <c>MaterialMessageWindow</c>: высота по содержимому, ширина ограничена,
/// текст переносится и имеет собственный <c>MaxWidth</c> (окно подтверждения
/// включения шифрования больше не растягивается на весь экран);</item>
/// <item>Avalonia-версия диалога (не-<c>fitContent</c> ветка) — те же границы ширины
/// и переносы;</item>
/// <item>тексты <c>Secrets.*</c> в ru/en сокращены и разбиты на строки;</item>
/// <item><c>SecretUnlockWindow</c> (WPF/Avalonia) — узкое окно с ограничением ширины.</item>
/// </list>
/// </summary>
public sealed class Issue163PasswordDialogLayoutTests
{
    private static readonly string[] ConfirmKeys =
    {
        "Secrets.EncryptionEnableConfirm",
        "Secrets.EncryptionDisableConfirm"
    };

    private static readonly string[] ShortKeys =
    {
        "Secrets.EncryptionEnabledHint",
        "Secrets.UnlockPrompt"
    };

    // ===================== WPF MaterialMessageWindow =====================

    [Fact]
    public void WpfMessageWindow_AutoHeightOnly()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Services", "MaterialMessageWindow.xaml"));

        // Issue #163: авторазмер по обеим осям измерял текст с бесконечной шириной
        // и длинная строка растягивала окно. Теперь высота — по содержимому,
        // ширина — фиксированная с границами.
        Assert.DoesNotContain("SizeToContent=\"WidthAndHeight\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SizeToContent=\"Height\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"480\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"420\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"560\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfMessageWindow_MessageWrapsWithMaxWidth()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Services", "MaterialMessageWindow.xaml"));

        Assert.Contains("x:Name=\"MessageText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", xaml, StringComparison.Ordinal);
        // MaxWidth именно у текста сообщения (а не только у окна).
        Assert.Contains("MaxWidth=\"500\"", xaml, StringComparison.Ordinal);
    }

    // ===================== Avalonia MaterialMessageWindow =====================

    [Fact]
    public void AvaloniaMessageWindow_HasBoundedWidth()
    {
        var source = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "MaterialMessageWindow.Avalonia.cs"));

        // Issue #163: не-fitContent ветка — фиксированная ширина и границы.
        Assert.Contains("Width = 480;", source, StringComparison.Ordinal);
        Assert.Contains("MinWidth = 420;", source, StringComparison.Ordinal);
        Assert.Contains("MaxWidth = 560;", source, StringComparison.Ordinal);

        // Текст сообщения переносится и ограничен по ширине.
        Assert.Contains("TextWrapping = TextWrapping.Wrap", source, StringComparison.Ordinal);
        Assert.Contains("messageBlock.MaxWidth = 500;", source, StringComparison.Ordinal);

        // Ветка fitContent (диалог обновления, issue #139) не затронута.
        Assert.Contains("MinWidth = 680;", source, StringComparison.Ordinal);
        Assert.Contains("MaxWidth = 900;", source, StringComparison.Ordinal);
    }

    // ===================== SecretUnlockWindow =====================

    [Fact]
    public void SecretUnlockWindows_HaveBoundedWidth()
    {
        var wpf = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SecretUnlockWindow.cs"));
        // Issue #163: узкое окно запроса пароля и жёсткий потолок ширины.
        Assert.Contains("\"Secrets.UnlockTitle\", 440, 260", wpf, StringComparison.Ordinal);
        Assert.Contains("MaxWidth = 520;", wpf, StringComparison.Ordinal);

        var avalonia = ReadProjectFile(Path.Combine(
            "Workspace1C", "Views", "SecretUnlockWindow.Avalonia.cs"));
        Assert.Contains("Width = 440;", avalonia, StringComparison.Ordinal);
        Assert.Contains("MinWidth = 400;", avalonia, StringComparison.Ordinal);
        Assert.Contains("MaxWidth = 520;", avalonia, StringComparison.Ordinal);
        Assert.Contains("TextWrapping = Avalonia.Media.TextWrapping.Wrap", avalonia, StringComparison.Ordinal);
    }

    // ===================== Локализация =====================

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void Secrets_ConfirmTexts_AreShortAndMultiline(string language)
    {
        var strings = LoadLanguageStrings(language);

        foreach (var key in ConfirmKeys)
        {
            Assert.True(strings.TryGetValue(key, out var text), $"{language}.json: нет ключа {key}");
            Assert.Contains("\n", text);
            // Порог с запасом: переносы дают 2–3 короткие строки вместо «простыни».
            Assert.True(text.Length <= 200,
                $"{language}.json: {key} длиной {text.Length} символов (ожидалось ≤ 200)");
        }
    }

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void Secrets_HintAndPrompt_AreShort(string language)
    {
        var strings = LoadLanguageStrings(language);

        Assert.True(strings.TryGetValue("Secrets.EncryptionEnabledHint", out var hint));
        Assert.True(hint.Length <= 110,
            $"{language}.json: Secrets.EncryptionEnabledHint длиной {hint.Length} (ожидалось ≤ 110)");

        Assert.True(strings.TryGetValue("Secrets.UnlockPrompt", out var prompt));
        Assert.True(prompt.Length <= 130,
            $"{language}.json: Secrets.UnlockPrompt длиной {prompt.Length} (ожидалось ≤ 130)");
        // Плейсхолдер имени учётной записи сохранён.
        Assert.Contains("{0}", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Secrets_KeysParityBetweenLanguages()
    {
        var ru = LoadLanguageStrings("ru").Keys
            .Where(k => k.StartsWith("Secrets.", StringComparison.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
        var en = LoadLanguageStrings("en").Keys
            .Where(k => k.StartsWith("Secrets.", StringComparison.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ru, en);

        // Все ключи диалогов из issue #163 присутствуют в обоих словарях.
        foreach (var key in ConfirmKeys.Concat(ShortKeys))
        {
            Assert.Contains(key, ru);
            Assert.Contains(key, en);
        }
    }

    // ===================== helpers =====================

    private static Dictionary<string, string> LoadLanguageStrings(string language)
    {
        var path = Path.Combine(
            FindRepoRoot(), "Workspace1C", "Localization", "Languages", language + ".json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var strings = document.RootElement.GetProperty("strings");

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in strings.EnumerateObject())
            result[property.Name] = property.Value.GetString() ?? string.Empty;
        return result;
    }

    private static string ReadProjectFile(string relativePath)
        => File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
