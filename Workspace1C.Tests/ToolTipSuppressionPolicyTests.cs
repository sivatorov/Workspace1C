using System;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #131: чистая политика подавления повторного открытия подсказок.
/// Проверяются сценарии «наведение», «ESC», «деактивация окна», «повторное наведение»
/// и отсутствие «вечного» подавления (прежний дефект <c>IsPointerOver</c>).
/// </summary>
public sealed class ToolTipSuppressionPolicyTests
{
    [Fact]
    public void DefaultWindow_IsEightHundredMilliseconds()
    {
        var policy = new ToolTipSuppressionPolicy();
        Assert.Equal(800, policy.WindowMs);
    }

    [Fact]
    public void Hover_WithoutSuppressionRecord_IsNotSuppressed()
    {
        var policy = new ToolTipSuppressionPolicy();

        // Обычное наведение не создаёт записи о подавлении — вето не срабатывает.
        Assert.False(policy.ShouldSuppress(1000, ToolTipSuppressionPolicy.NoSuppression, pointerOver: true));
        Assert.True(policy.ShouldRelease(1000, ToolTipSuppressionPolicy.NoSuppression, pointerOver: true));
    }

    [Fact]
    public void Esc_WithinWindow_SuppressesReopen()
    {
        var policy = new ToolTipSuppressionPolicy(windowMs: 800);
        const long suppressedTick = 10_000;

        Assert.True(policy.ShouldSuppress(suppressedTick, suppressedTick, pointerOver: true));
        Assert.True(policy.ShouldSuppress(suppressedTick + 799, suppressedTick, pointerOver: true));
        Assert.False(policy.ShouldRelease(suppressedTick + 400, suppressedTick, pointerOver: true));
    }

    [Fact]
    public void Esc_PointerOver_DoesNotSuppressForever()
    {
        var policy = new ToolTipSuppressionPolicy(windowMs: 800);
        const long suppressedTick = 10_000;

        // Ключевой сценарий issue #131: курсор остаётся над владельцем, но окно истекло —
        // запрета больше нет, подсказка должна открываться снова.
        var afterWindow = suppressedTick + 800;
        Assert.False(policy.ShouldSuppress(afterWindow, suppressedTick, pointerOver: true));
        Assert.True(policy.ShouldRelease(afterWindow, suppressedTick, pointerOver: true));
    }

    [Fact]
    public void Esc_PointerLeft_ReleasesImmediately()
    {
        var policy = new ToolTipSuppressionPolicy(windowMs: 800);
        const long suppressedTick = 10_000;

        // Курсор ушёл с владельца — цикл закрытия завершён, ждать конца окна не нужно.
        Assert.True(policy.ShouldRelease(suppressedTick + 50, suppressedTick, pointerOver: false));
    }

    [Fact]
    public void WindowExpired_ReleasesRegardlessOfPointer()
    {
        var policy = new ToolTipSuppressionPolicy(windowMs: 500);
        const long suppressedTick = 1_000;

        Assert.True(policy.ShouldRelease(suppressedTick + 500, suppressedTick, pointerOver: false));
        Assert.True(policy.ShouldRelease(suppressedTick + 900, suppressedTick, pointerOver: true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void ZeroOrNegativeWindow_NeverSuppresses(long windowMs)
    {
        var policy = new ToolTipSuppressionPolicy(windowMs);

        Assert.Equal(0, policy.WindowMs);
        Assert.False(policy.ShouldSuppress(5_000, 5_000, pointerOver: true));
        Assert.True(policy.ShouldRelease(5_000, 5_000, pointerOver: true));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    public void FallbackClose_AllowedOnlyWhenForeignForegroundAndPointerOutside(
        bool ourProcessForeground, bool pointerOverOurWindow, bool expected)
    {
        Assert.Equal(expected,
            ToolTipSuppressionPolicy.IsFallbackCloseAllowed(ourProcessForeground, pointerOverOurWindow));
    }

    [Fact]
    public void TickGoingBackwards_DoesNotCreateEternalSuppression()
    {
        var policy = new ToolTipSuppressionPolicy(windowMs: 800);

        // Отрицательная разница (например, сброс счётчика) не должна считаться подавлением.
        Assert.False(policy.ShouldSuppress(nowTick: 500, suppressedTick: 1_000, pointerOver: true));
        Assert.True(policy.ShouldRelease(nowTick: 500, suppressedTick: 1_000, pointerOver: true));
    }
}
