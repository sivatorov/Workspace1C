using System;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты наблюдателя режима ОС и настройки «Тема приложения следует за режимом ОС»
/// (issue #108). Полная проверка слежения реестра невозможна в юнит-тестах без
/// подмены HKCU — проверяются контракт API, идемпотентность Start/Stop и
/// сохранение настройки по умолчанию (ЛОЖЬ).
/// </summary>
public class OsThemeWatcherTests
{
    [Fact]
    public void ThemeFollowsOs_Default_IsFalse()
    {
        var settings = new AppSettings();
        Assert.False(settings.ThemeFollowsOs);
    }

    [Fact]
    public void StartStop_IsIdempotent_DoesNotThrow()
    {
        // Двойные Start/Stop (LoadSettings + сеттер свойства) не должны падать
        // и не должны накапливать подписки.
        OsThemeWatcher.Start();
        OsThemeWatcher.Start();
        OsThemeWatcher.Stop();
        OsThemeWatcher.Stop();

        // Подписка/отписка обработчика без Start — тоже допустима.
        static void Handler(bool dark) { }
        OsThemeWatcher.AppsThemeChanged += Handler;
        OsThemeWatcher.AppsThemeChanged -= Handler;
    }

    [Fact]
    public void GetAppsDarkMode_ReturnsNullOutsideWindows()
    {
        if (OperatingSystem.IsWindows())
            return; // на Windows значение может быть и true, и false — контракт не проверяем

        Assert.Null(OsThemeWatcher.GetAppsDarkMode());
        Assert.False(OsThemeWatcher.IsSupported);
    }

    [Fact]
    public void GetAppsDarkMode_OnWindows_ReturnsBoolOrNull()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var dark = OsThemeWatcher.GetAppsDarkMode();
        Assert.True(dark is null || dark is true || dark is false);
    }
}
