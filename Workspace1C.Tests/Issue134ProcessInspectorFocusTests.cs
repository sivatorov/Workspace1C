using System;
using System.IO;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #134 (1.0.1.106): при открытии инспектора процессов и после завершения
/// процесса первое нажатие «вниз» оставляло фокус на кнопке «Колонки».
/// Причина — обработчик пропускал навигацию при пустом списке и до активации окна.
/// Политика <see cref="ProcessInspectorFocusPolicy"/> всегда перенаправляет
/// навигационную клавишу в таблицу, а перемещение выполняет только при наличии строк.
/// </summary>
public sealed class Issue134ProcessInspectorFocusTests
{
    [Theory]
    [InlineData(InspectorNavigationKey.Up)]
    [InlineData(InspectorNavigationKey.Down)]
    [InlineData(InspectorNavigationKey.PageUp)]
    [InlineData(InspectorNavigationKey.PageDown)]
    [InlineData(InspectorNavigationKey.Home)]
    [InlineData(InspectorNavigationKey.End)]
    public void ShouldRedirect_WhenGridDoesNotHaveFocus(InspectorNavigationKey key)
    {
        Assert.True(ProcessInspectorFocusPolicy.ShouldRedirectNavigation(
            gridHasKeyboardFocus: false,
            key,
            focusedElementIsTextInput: false));
    }

    [Fact]
    public void ShouldNotRedirect_WhenGridHasFocus()
    {
        Assert.False(ProcessInspectorFocusPolicy.ShouldRedirectNavigation(
            gridHasKeyboardFocus: true,
            InspectorNavigationKey.Down,
            focusedElementIsTextInput: false));
    }

    [Fact]
    public void ShouldNotRedirect_ForNonNavigationKey()
    {
        Assert.False(ProcessInspectorFocusPolicy.ShouldRedirectNavigation(
            gridHasKeyboardFocus: false,
            InspectorNavigationKey.None,
            focusedElementIsTextInput: false));
    }

    [Fact]
    public void ShouldNotRedirect_WhenTextInputFocused()
    {
        Assert.False(ProcessInspectorFocusPolicy.ShouldRedirectNavigation(
            gridHasKeyboardFocus: false,
            InspectorNavigationKey.Down,
            focusedElementIsTextInput: true));
    }

    [Theory]
    [InlineData(-1, 5, InspectorNavigationKey.Down, 0)]
    [InlineData(-1, 5, InspectorNavigationKey.Up, 0)]
    [InlineData(0, 5, InspectorNavigationKey.Down, 1)]
    [InlineData(4, 5, InspectorNavigationKey.Down, 4)]
    [InlineData(0, 5, InspectorNavigationKey.Up, 0)]
    [InlineData(3, 5, InspectorNavigationKey.Home, 0)]
    [InlineData(1, 5, InspectorNavigationKey.End, 4)]
    [InlineData(4, 20, InspectorNavigationKey.PageUp, 0)]
    [InlineData(0, 20, InspectorNavigationKey.PageDown, 10)]
    public void ResolveTargetIndex_MovesWithinBounds(
        int current, int count, InspectorNavigationKey key, int expected)
    {
        Assert.Equal(expected, ProcessInspectorFocusPolicy.ResolveTargetIndex(current, count, key));
    }

    [Fact]
    public void ResolveTargetIndex_EmptyList_ReturnsMinusOne()
    {
        Assert.Equal(-1, ProcessInspectorFocusPolicy.ResolveTargetIndex(
            -1, 0, InspectorNavigationKey.Down));
    }

    [Fact]
    public void ShouldRetryFocus_WhenGridHasNoFocus_WithinLimit()
    {
        Assert.True(ProcessInspectorFocusPolicy.ShouldRetryFocus(
            gridHasKeyboardFocus: false, attempt: 0));
        Assert.True(ProcessInspectorFocusPolicy.ShouldRetryFocus(
            gridHasKeyboardFocus: false,
            attempt: ProcessInspectorFocusPolicy.MaxFocusAttempts - 1));
    }

    [Fact]
    public void ShouldRetryFocus_WhenGridHasFocus_NoRetry()
    {
        Assert.False(ProcessInspectorFocusPolicy.ShouldRetryFocus(
            gridHasKeyboardFocus: true, attempt: 0));
        Assert.False(ProcessInspectorFocusPolicy.ShouldRetryFocus(
            gridHasKeyboardFocus: true, attempt: 3));
    }

    [Fact]
    public void ShouldRetryFocus_WhenAttemptsExhausted_NoRetry()
    {
        Assert.False(ProcessInspectorFocusPolicy.ShouldRetryFocus(
            gridHasKeyboardFocus: false,
            attempt: ProcessInspectorFocusPolicy.MaxFocusAttempts));
        Assert.False(ProcessInspectorFocusPolicy.ShouldRetryFocus(
            gridHasKeyboardFocus: false,
            attempt: ProcessInspectorFocusPolicy.MaxFocusAttempts + 5));
    }

    [Fact]
    public void FocusPolicy_RetryLimitsAreReasonable()
    {
        Assert.Equal(12, ProcessInspectorFocusPolicy.MaxFocusAttempts);
        Assert.Equal(120, ProcessInspectorFocusPolicy.FocusRetryDelayMs);
    }

    [Fact]
    public void WpfWindow_UsesPolicyAndActivatesBeforeFocus()
    {
        var source = ReadView("ProcessInspectorWindow.xaml.cs");

        Assert.Contains("ProcessInspectorFocusPolicy.ShouldRedirectNavigation", source, StringComparison.Ordinal);
        Assert.Contains("ProcessInspectorFocusPolicy.ResolveTargetIndex", source, StringComparison.Ordinal);
        Assert.Contains("Activate();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaWindow_UsesTunnelAndPolicy()
    {
        var source = ReadView("ProcessInspectorWindow.Avalonia.cs");

        Assert.Contains("RoutingStrategies.Tunnel", source, StringComparison.Ordinal);
        Assert.Contains("ProcessInspectorFocusPolicy.ShouldRedirectNavigation", source, StringComparison.Ordinal);
        Assert.Contains("ProcessInspectorFocusPolicy.ResolveTargetIndex", source, StringComparison.Ordinal);
        Assert.Contains("_grid.Focus(NavigationMethod.Directional)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfWindow_DefersNavigationUntilWindowActivated()
    {
        var source = ReadView("ProcessInspectorWindow.xaml.cs");

        // Навигация после Activate() выполняется отложенно, а не синхронным Focus().
        Assert.Contains("Activate();", source, StringComparison.Ordinal);
        Assert.Contains("NavigateGridFromButton(navigation)),", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Input", source, StringComparison.Ordinal);
        // Повторные попытки фокуса до фактического получения.
        Assert.Contains("FocusGridWithRetry", source, StringComparison.Ordinal);
        Assert.Contains("ProcessInspectorFocusPolicy.ShouldRetryFocus", source, StringComparison.Ordinal);
        // Таблица назначена логическим фокусом окна после Loaded.
        Assert.Contains("FocusManager.SetFocusedElement(this, ProcessesGrid)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfWindow_FocusRetryDoesNotDependOnItemCount()
    {
        var source = ReadView("ProcessInspectorWindow.xaml.cs");

        // Условие повтора опирается на фокус и номер попытки, а не на наличие строк.
        Assert.Contains("ShouldRetryFocus(focused, attempt)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("if (ProcessesGrid.Items.Count == 0)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaWindow_DefersNavigationUntilWindowActivated()
    {
        var source = ReadView("ProcessInspectorWindow.Avalonia.cs");

        Assert.Contains("Activate();", source, StringComparison.Ordinal);
        Assert.Contains("() => NavigateGridFromButton(navigation)", source, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.UIThread.Post", source, StringComparison.Ordinal);
        Assert.Contains("FocusGridAttempt", source, StringComparison.Ordinal);
        Assert.Contains("ProcessInspectorFocusPolicy.ShouldRetryFocus", source, StringComparison.Ordinal);
        Assert.Contains("if (!_grid.Focus(NavigationMethod.Directional))", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaWindow_FocusRetryDoesNotDependOnItemCount()
    {
        var source = ReadView("ProcessInspectorWindow.Avalonia.cs");

        // Условие повтора опирается на фокус и номер попытки, а не на наличие строк.
        Assert.Contains("_grid.IsKeyboardFocusWithin, attempt", source, StringComparison.Ordinal);
        Assert.DoesNotContain("if (_grid.ItemCount == 0)", source, StringComparison.Ordinal);
    }

    private static string ReadView(string fileName) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "Workspace1C", "Views", fileName));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
