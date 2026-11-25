using System;
using System.IO;
using System.Linq;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #164 «Не спрашивает пароль при старте» (план 1.0.1.121, T6, часть 2 — окно
/// блокировки): 7OH: «При блокировке — куча пустого места под кнопками».
/// <para>
/// Окно имело фиксированную высоту 340 DIP, а в режиме смены пароля — жёстко
/// <c>Height = 430</c>; содержимое (<c>StackPanel</c>) меньше окна, поэтому под кнопками
/// оставалась пустота. Проверяется: профили <see cref="DialogWorkAreaPolicy.AppLock"/> и
/// <see cref="DialogWorkAreaPolicy.AppLockChange"/> согласованы и клампятся по рабочей
/// области; WPF-окно растягивается по содержимому (<c>SizeToContent="Height"</c>,
/// <c>MinHeight 240</c>) и берёт границы из политики; Avalonia-окно использует
/// <c>Height = NaN</c> и тот же кламп по экрану. Окно входа (T5) не затрагивается.
/// </para>
/// </summary>
public sealed class Issue164AppLockWindowSizeTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadProjectFile(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray()));

    private static string ReadAppLockXaml() =>
        ReadProjectFile("Workspace1C", "Views", "AppLockWindow.xaml");

    private static string ReadAppLockWpfCode() =>
        ReadProjectFile("Workspace1C", "Views", "AppLockWindow.xaml.cs");

    private static string ReadAppLockAvaloniaCode() =>
        ReadProjectFile("Workspace1C", "Views", "AppLockWindow.Avalonia.cs");

    [Fact]
    public void AppLockProfiles_AreConsistent()
    {
        var appLock = DialogWorkAreaPolicy.AppLock;

        // План 1.0.1.121, §2.2: AppLock = (340, 240, MaxHeight 520).
        Assert.Equal(340, appLock.DefaultHeight, 3);
        Assert.Equal(240, appLock.MinHeight, 3);
        Assert.Equal(340, appLock.ContentHeight, 3);
        Assert.Equal(520, appLock.MaxHeight, 3);

        // Согласованность: Min ≤ Default ≤ Max, содержимое помещается при Default.
        Assert.True(appLock.MinHeight <= appLock.DefaultHeight);
        Assert.True(appLock.DefaultHeight <= appLock.MaxHeight);
        Assert.True(appLock.ContentHeight <= appLock.MaxHeight);

        var change = DialogWorkAreaPolicy.AppLockChange;

        // Смена пароля: три поля — 430 DIP по содержимому, минимум 340, максимум 560.
        Assert.Equal(430, change.DefaultHeight, 3);
        Assert.Equal(340, change.MinHeight, 3);
        Assert.Equal(430, change.ContentHeight, 3);
        Assert.Equal(560, change.MaxHeight, 3);

        Assert.True(change.MinHeight <= change.DefaultHeight);
        Assert.True(change.DefaultHeight <= change.MaxHeight);
        Assert.True(change.ContentHeight <= change.MaxHeight);

        // Режим смены пароля выше обычной блокировки, иначе три поля не помещаются.
        Assert.True(change.DefaultHeight > appLock.DefaultHeight);
        Assert.True(change.MinHeight > appLock.MinHeight);
        Assert.True(change.MaxHeight > appLock.MaxHeight);
    }

    [Fact]
    public void AppLockProfiles_Recommend_ClampsToWorkAreaAndCap()
    {
        // Обычный экран: высота по содержимому, верхняя граница — из профиля (520),
        // а не 1032 (1080 − 48), поэтому окно не растянется на весь экран.
        var big = DialogWorkAreaPolicy.Recommend(DialogWorkAreaPolicy.AppLock, 1080);
        Assert.Equal(340, big.Height, 3);
        Assert.Equal(240, big.MinHeight, 3);
        Assert.Equal(520, big.MaxHeight, 3);
        Assert.False(big.RequiresScrolling);

        var bigChange = DialogWorkAreaPolicy.Recommend(DialogWorkAreaPolicy.AppLockChange, 1080);
        Assert.Equal(430, bigChange.Height, 3);
        Assert.Equal(340, bigChange.MinHeight, 3);
        Assert.Equal(560, bigChange.MaxHeight, 3);
        Assert.False(bigChange.RequiresScrolling);

        // Экран 768 DIP: 768 − 48 = 720 — граница профиля побеждает.
        var laptop = DialogWorkAreaPolicy.Recommend(DialogWorkAreaPolicy.AppLock, 768);
        Assert.Equal(340, laptop.Height, 3);
        Assert.Equal(520, laptop.MaxHeight, 3);

        // Маленькая рабочая область 500 DIP: 500 − 48 = 452 ограничивает максимум.
        var small = DialogWorkAreaPolicy.Recommend(DialogWorkAreaPolicy.AppLock, 500);
        Assert.Equal(340, small.Height, 3);
        Assert.Equal(452, small.MaxHeight, 3);
        Assert.Equal(240, small.MinHeight, 3);

        var smallChange = DialogWorkAreaPolicy.Recommend(DialogWorkAreaPolicy.AppLockChange, 500);
        Assert.Equal(430, smallChange.Height, 3);
        Assert.Equal(452, smallChange.MaxHeight, 3);
        Assert.Equal(340, smallChange.MinHeight, 3);

        // Совсем маленький экран 400 DIP: 400 − 48 = 352 — минимум не превышает
        // максимум, высота упирается в рабочую область, нужна прокрутка содержимого.
        var tinyChange = DialogWorkAreaPolicy.Recommend(DialogWorkAreaPolicy.AppLockChange, 400);
        Assert.Equal(352, tinyChange.Height, 3);
        Assert.Equal(352, tinyChange.MaxHeight, 3);
        Assert.Equal(340, tinyChange.MinHeight, 3);
        Assert.True(tinyChange.RequiresScrolling);
    }

    [Fact]
    public void WpfAppLockWindow_AutoHeight_NoFixedHeight()
    {
        var xaml = ReadAppLockXaml();

        // Issue #164: фиксированная высота 340 DIP убрана — окно по содержимому.
        Assert.DoesNotContain("Height=\"340\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SizeToContent=\"Height\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"240\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"460\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfAppLockWindow_Code_UsesProfilesAndWorkArea()
    {
        var code = ReadAppLockWpfCode();

        // Профили политики вместо жёстких высот 340/430.
        Assert.Contains("DialogWorkAreaPolicy.AppLock", code, StringComparison.Ordinal);
        Assert.Contains("DialogWorkAreaPolicy.AppLockChange", code, StringComparison.Ordinal);
        Assert.Contains("DialogWorkAreaPolicy.Recommend(", code, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.WorkArea", code, StringComparison.Ordinal);

        // Старое жёсткое значение не вернулось.
        Assert.DoesNotContain("Height = 430", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Height = 340", code, StringComparison.Ordinal);

        // Геометрия обёрнута в try/catch: сбой не блокирует блокировку/разблокировку.
        Assert.Contains("try", code, StringComparison.Ordinal);
        Assert.Contains("catch", code, StringComparison.Ordinal);
        Assert.Contains("MinHeight = size.MinHeight;", code, StringComparison.Ordinal);
        Assert.Contains("MaxHeight = size.MaxHeight;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaAppLockWindow_UsesProfilesAndClamp()
    {
        var code = ReadAppLockAvaloniaCode();

        // Авторазмер по содержимому вместо фиксированных 340/430.
        Assert.Contains("Height = double.NaN;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Height = changeMode ? 430 : 340;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight = changeMode ? 400 : 320;", code, StringComparison.Ordinal);

        // Границы — из профилей политики.
        Assert.Contains("DialogWorkAreaPolicy.AppLockChange", code, StringComparison.Ordinal);
        Assert.Contains("DialogWorkAreaPolicy.AppLock", code, StringComparison.Ordinal);
        Assert.Contains("MinHeight = profile.MinHeight;", code, StringComparison.Ordinal);
        Assert.Contains("MaxHeight = profile.MaxHeight;", code, StringComparison.Ordinal);

        // Кламп по рабочей области текущего монитора (образец — окно входа после T5).
        Assert.Contains("Screens.ScreenFromWindow(this) ?? Screens.Primary", code, StringComparison.Ordinal);
        Assert.Contains("screen.WorkingArea.Height / screen.Scaling", code, StringComparison.Ordinal);
        Assert.Contains("DialogWorkAreaPolicy.Recommend(profile, dipHeight)", code, StringComparison.Ordinal);
        Assert.Contains("MaxHeight = size.MaxHeight;", code, StringComparison.Ordinal);
    }
}
