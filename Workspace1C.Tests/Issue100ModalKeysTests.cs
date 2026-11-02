using System.IO;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #100 «Линукс — пробел» (репорт 7OH от 2026-11-02): в модальном диалоге
/// с одной кнопкой «ОК» (проверка обновлений) на Linux пробел не нажимал кнопку,
/// а пробрасывался в перекрытое окно. Фикс: фокус на кнопку по умолчанию при
/// открытии диалога сообщений + перехват Space/Enter на уровне окна
/// (<see cref="MaterialMessageWindowAvalonia"/>, чистая логика —
/// <see cref="ModalKeysResolver"/>).
/// </summary>
public sealed class Issue100ModalKeysTests
{
    // ===================== Чистая логика ModalKeysResolver =====================

    [Theory]
    [InlineData("Space")]
    [InlineData("Enter")]
    [InlineData("Return")]
    public void IsActivationKey_SpaceEnterWithoutModifiers_IsTrue(string key)
    {
        Assert.True(ModalKeysResolver.IsActivationKey(key, hasModifiers: false));
    }

    [Theory]
    [InlineData("Space")]
    [InlineData("Enter")]
    public void IsActivationKey_WithModifiers_IsFalse(string key)
    {
        Assert.False(ModalKeysResolver.IsActivationKey(key, hasModifiers: true));
    }

    [Theory]
    [InlineData("Escape")]
    [InlineData("Tab")]
    [InlineData("D1")]
    [InlineData(null)]
    [InlineData("")]
    public void IsActivationKey_OtherKeys_IsFalse(string? key)
    {
        Assert.False(ModalKeysResolver.IsActivationKey(key, hasModifiers: false));
    }

    [Fact]
    public void ShouldActivateDefaultButton_FocusOnButton_IsFalse()
    {
        // Фокус на кнопке: Space/Enter обрабатывает сама кнопка — перехват задваивал бы.
        Assert.False(ModalKeysResolver.ShouldActivateDefaultButton(
            focusedElementIsButton: true, "Space", hasModifiers: false));
    }

    [Fact]
    public void ShouldActivateDefaultButton_FocusOutsideButton_IsTrue()
    {
        Assert.True(ModalKeysResolver.ShouldActivateDefaultButton(
            focusedElementIsButton: false, "Space", hasModifiers: false));
        Assert.True(ModalKeysResolver.ShouldActivateDefaultButton(
            focusedElementIsButton: false, "Enter", hasModifiers: false));
    }

    // ===================== Файловые стражи платформенных окон =====================

    private static string ReadProjectFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var fullPath = Path.Combine(dir!.FullName, relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    [Fact]
    public void AvaloniaMessageWindow_FocusesDefaultButtonOnOpen()
    {
        // issue #100: диалог сообщений при открытии забирает фокус на кнопку «ОК» —
        // иначе клавиатура остаётся у владельца и Space «нажимает» перекрытое окно.
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Services", "MaterialMessageWindow.Avalonia.cs"));
        Assert.Contains("Opened += (_, _) => Dispatcher.UIThread.Post(() => _okButton.Focus());",
            source, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaMessageWindow_HandlesSpaceEnterViaModalKeysResolver()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Services", "MaterialMessageWindow.Avalonia.cs"));
        // Перехват Space/Enter на уровне окна (когда фокус не на кнопке).
        Assert.Contains("ModalKeysResolver.ShouldActivateDefaultButton", source, StringComparison.Ordinal);
        Assert.Contains("TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Button",
            source, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfMessageWindow_FocusesOkButtonOnLoaded()
    {
        // Регресс Windows (issue #100: «на винде работает отлично») — сохранён:
        // WPF-версия ставит фокус на кнопку «ОК» при загрузке, Space/Enter нажимают её.
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Services", "MaterialMessageWindow.xaml.cs"));
        Assert.Contains("OkButton.Focus()", source, StringComparison.Ordinal);
    }
}
