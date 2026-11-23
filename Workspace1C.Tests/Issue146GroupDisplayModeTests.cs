using System;
using System.IO;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #146 (комментарий 7OH 2026-11-23): в правке структуры меню «Утилиты»
/// не было поля «Отображение» для папки верхнего уровня, а вложенные группы
/// при миграции показывались только иконками. Настройка «Иконка» / «С надписью»
/// должна действовать только на группы верхнего уровня; вложенные — всегда с подписью.
/// </summary>
public sealed class Issue146GroupDisplayModeTests
{
    [Fact]
    public void EditParams_DefaultDisplayMode_IsIconWithLabel()
    {
        var parameters = new UtilitiesGroupEditParams("Группа", null);

        Assert.Equal(UtilitiesMenuGroupDisplay.IconWithLabel, parameters.DisplayMode);
    }

    [Fact]
    public void Presentation_NestedGroup_AlwaysShowsLabel()
    {
        var node = new UtilitiesMenuNode
        {
            Kind = "group",
            DisplayMode = UtilitiesMenuGroupDisplay.Icon
        };

        var presentation = UtilitiesMenuGroupPresentation.Build(node, "Вложенная", isTopLevel: false);

        Assert.True(presentation.ShowLabel);
        Assert.Equal("Вложенная", presentation.Header);
    }

    [Fact]
    public void Presentation_TopLevelGroup_CanHideLabel()
    {
        var node = new UtilitiesMenuNode
        {
            Kind = "group",
            DisplayMode = UtilitiesMenuGroupDisplay.Icon
        };

        var presentation = UtilitiesMenuGroupPresentation.Build(node, "Верхняя", isTopLevel: true);

        Assert.False(presentation.ShowLabel);
        Assert.Null(presentation.Header);
    }

    [Fact]
    public void Presentation_TopLevelGroup_WithLabel_ShowsHeader()
    {
        var node = new UtilitiesMenuNode
        {
            Kind = "group",
            DisplayMode = UtilitiesMenuGroupDisplay.IconWithLabel
        };

        var presentation = UtilitiesMenuGroupPresentation.Build(node, "Верхняя", isTopLevel: true);

        Assert.True(presentation.ShowLabel);
        Assert.Equal("Верхняя", presentation.Header);
    }

    [Fact]
    public void Editor_ForcesLabelForNestedGroup()
    {
        var source = ReadProjectFile("Workspace1C", "ViewModels", "UtilitiesMenuEditorViewModel.cs");

        Assert.Contains("item.Parent is null", source, StringComparison.Ordinal);
        Assert.Contains("UtilitiesMenuGroupDisplay.IconWithLabel", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("UtilitiesGroupEditWindow.xaml.cs")]
    [InlineData("UtilitiesGroupEditWindow.Avalonia.cs")]
    public void GroupDialog_HasDisplayModeSelector(string file)
    {
        var source = ReadProjectFile("Workspace1C", "Views", file);

        Assert.Contains("GroupDisplayOptions", source, StringComparison.Ordinal);
        Assert.Contains("showDisplayMode", source, StringComparison.Ordinal);
        Assert.Contains("DisplayIconWithLabel", ReadProjectFile("Workspace1C", "ViewModels", "UtilitiesMenuEditorViewModel.cs"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ru.json")]
    [InlineData("en.json")]
    public void Localization_HasDisplayModeKeys(string file)
    {
        var json = ReadProjectFile("Workspace1C", "Localization", "Languages", file);

        Assert.Contains("\"Utilities.Editor.DisplayHeader\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Utilities.Editor.DisplayIcon\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Utilities.Editor.DisplayIconWithLabel\"", json, StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var relativePath = Path.Combine(parts);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var fullPath = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(fullPath))
                return File.ReadAllText(fullPath);
            dir = dir.Parent;
        }

        Assert.Fail($"Файл проекта не найден: {relativePath}");
        return string.Empty;
    }
}
