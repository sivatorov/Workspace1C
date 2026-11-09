using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #107 «Неисправленные огрехи в тёмной теме» (v1.0.1.48): стражи по исходникам.
/// (a) Avalonia/Linux стартовая страница: компактные кнопки «1С:Стартер», «По ссылке»,
/// «Выход» (<c>CompactActionButton</c>/<c>CompactActionButtonBound</c>) строятся на
/// фоне карточки <c>CardBackgroundBrush</c> с hover <c>ItemHoverBrush</c>, а не на
/// светлых кистях SecondaryButton* (#FFF3CD в тёмной теме — нечитаемая подложка);
/// (b) окно «Заметки базы» (WPF): «Отмена» — вторичная SecondaryButton, а не акцентная
/// ModernButton.
/// </summary>
public sealed class Issue107StartPageButtonsTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Views")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Workspace1C.Tests")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Корень репозитория не найден из тестового каталога.");
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    [Fact]
    public void CompactActionButton_UsesCardBackgroundInsteadOfSecondary()
    {
        // (a) CompactActionButton/CompactActionButtonBound: фон карточки + hover,
        // светлые кисти SecondaryButton* в тёмной теме не читались (issue #107).
        var source = ReadProjectFile(
            Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));

        foreach (var method in new[] { "CompactActionButton(", "CompactActionButtonBound(" })
        {
            var start = source.IndexOf("private static Control " + method, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Метод {method} не найден в MainWindow.Avalonia.Tree.cs");
            var end = source.IndexOf("CompactIconAndText(", start, StringComparison.Ordinal);
            Assert.True(end > start, $"Не найдено тело метода {method}");

            var body = source[start..end];
            Assert.Contains("\"CardBackgroundBrush\"", body);
            Assert.Contains("\"ItemHoverBrush\"", body);
            Assert.DoesNotContain("SecondaryButtonBackgroundBrush", body);
            Assert.DoesNotContain("SecondaryButtonHoverBrush", body);
            Assert.DoesNotContain("SecondaryButtonPressedBrush", body);
        }
    }

    [Fact]
    public void BaseNotesWindow_CancelButton_UsesSecondaryButton()
    {
        // (b) «Заметки базы» (WPF): «Отмена» — вторичная SecondaryButton,
        // а не акцентная ModernButton.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "BaseNotesWindow.xaml"));
        var index = xaml.IndexOf("x:Name=\"CancelButton\"", StringComparison.Ordinal);
        Assert.True(index > 0, "Кнопка CancelButton не найдена в BaseNotesWindow.xaml");
        var start = xaml.LastIndexOf("<Button", index, StringComparison.Ordinal);
        Assert.True(start >= 0, "Тег кнопки CancelButton не найден");
        var tag = xaml[start..(index + 200)];

        Assert.Contains("SecondaryButton", tag);
        Assert.DoesNotContain("ModernButton", tag);
    }

    [Fact]
    public void BaseNotesWindow_SaveButton_StaysAccent()
    {
        // «Сохранить» остаётся акцентной (primary) — это норма, менять не нужно.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "BaseNotesWindow.xaml"));
        var index = xaml.IndexOf("x:Name=\"SaveButton\"", StringComparison.Ordinal);
        Assert.True(index > 0, "Кнопка SaveButton не найдена в BaseNotesWindow.xaml");
        var start = xaml.LastIndexOf("<Button", index, StringComparison.Ordinal);
        Assert.True(start >= 0, "Тег кнопки SaveButton не найден");
        var tag = xaml[start..(index + 200)];

        Assert.Contains("ModernButton", tag);
    }
}
