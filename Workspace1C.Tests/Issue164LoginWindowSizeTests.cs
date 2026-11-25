using System;
using System.IO;
using System.Linq;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #164 «Не спрашивает пароль при старте» (план 1.0.1.121, T5, часть 1 — окно входа):
/// 7OH: «При входе — над полем ввода куча пустого места». Окно входа имело фиксированную
/// высоту 360 DIP и <c>*</c>/<c>Star</c>-строку списка профилей: при одной записи список
/// скрывался, но строка продолжала занимать всё свободное место.
/// <para>
/// Проверяется: профиль <see cref="DialogWorkAreaPolicy.Login"/> согласован и клампится по
/// рабочей области; WPF-окно растягивается по содержимому (<c>SizeToContent="Height"</c>,
/// <c>MinHeight 220</c>, строка <c>Auto</c> с именем, <c>ListBox.MaxHeight 280</c>) и
/// обнуляет строку при одной записи; Avalonia-окно берёт границы из профиля с клампом по
/// экрану, строка — <c>GridLength.Auto</c>, <c>ListBox.MaxHeight = 280</c>.
/// </para>
/// </summary>
public sealed class Issue164LoginWindowSizeTests
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

    private static string ReadLoginXaml() =>
        ReadProjectFile("Workspace1C", "Views", "LoginWindow.xaml");

    private static string ReadLoginWpfCode() =>
        ReadProjectFile("Workspace1C", "Views", "LoginWindow.xaml.cs");

    private static string ReadLoginAvaloniaCode() =>
        ReadProjectFile("Workspace1C", "Views", "LoginWindow.Avalonia.cs");

    [Fact]
    public void LoginProfile_IsConsistent()
    {
        var profile = DialogWorkAreaPolicy.Login;

        // План 1.0.1.121, §2.2: Login = (420, 220, MaxHeight 560).
        Assert.Equal(420, profile.DefaultHeight, 3);
        Assert.Equal(220, profile.MinHeight, 3);
        Assert.Equal(420, profile.ContentHeight, 3);
        Assert.Equal(560, profile.MaxHeight, 3);

        // Согласованность: Min ≤ Default ≤ Max, содержимое помещается при Default.
        Assert.True(profile.MinHeight <= profile.DefaultHeight);
        Assert.True(profile.DefaultHeight <= profile.MaxHeight);
        Assert.True(profile.ContentHeight <= profile.MaxHeight);
    }

    [Fact]
    public void LoginProfile_Recommend_ClampsToWorkAreaAndCap()
    {
        // Обычный экран: высота по содержимому, верхняя граница — из профиля (560),
        // а не 1032 (1080 − 48), поэтому окно не растянется на весь экран.
        var big = DialogWorkAreaPolicy.Recommend(DialogWorkAreaPolicy.Login, 1080);
        Assert.Equal(420, big.Height, 3);
        Assert.Equal(220, big.MinHeight, 3);
        Assert.Equal(560, big.MaxHeight, 3);
        Assert.False(big.RequiresScrolling);

        // Экран 768 DIP: 768 − 48 = 720 > 560 — граница профиля побеждает.
        var laptop = DialogWorkAreaPolicy.Recommend(DialogWorkAreaPolicy.Login, 768);
        Assert.Equal(420, laptop.Height, 3);
        Assert.Equal(560, laptop.MaxHeight, 3);

        // Маленькая рабочая область: высота по содержимому помещается (420 ≤ 452),
        // верхняя граница — экран (500 − 48), минимум не превышает максимум.
        var small = DialogWorkAreaPolicy.Recommend(DialogWorkAreaPolicy.Login, 500);
        Assert.Equal(420, small.Height, 3);
        Assert.Equal(452, small.MaxHeight, 3);
        Assert.Equal(220, small.MinHeight, 3);

        var tiny = DialogWorkAreaPolicy.Recommend(DialogWorkAreaPolicy.Login, 200);
        Assert.Equal(152, tiny.Height, 3);
        Assert.Equal(152, tiny.MaxHeight, 3);
        Assert.Equal(152, tiny.MinHeight, 3);
    }

    [Fact]
    public void WpfLoginWindow_AutoHeight_NoFixedHeight()
    {
        var xaml = ReadLoginXaml();

        // Issue #164: фиксированная высота 360 DIP убрана — окно по содержимому.
        Assert.DoesNotContain("Height=\"360\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SizeToContent=\"Height\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"220\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"460\"", xaml, StringComparison.Ordinal);

        // Строка списка профилей — Auto и именована, чтобы её можно было схлопнуть.
        Assert.Contains("x:Name=\"ProfilesListRow\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Height=\"*\"", xaml, StringComparison.Ordinal);

        // Список ограничен по высоте — прокрутка внутри списка, а не окна.
        Assert.Contains("MaxHeight=\"280\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfLoginWindow_Code_UsesLoginPolicyAndCollapsesRow()
    {
        var code = ReadLoginWpfCode();

        // Верхняя граница — из общей политики размеров по рабочей области монитора.
        Assert.Contains("DialogWorkAreaPolicy.Recommend(", code, StringComparison.Ordinal);
        Assert.Contains("DialogWorkAreaPolicy.Login", code, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.WorkArea", code, StringComparison.Ordinal);

        // Одна учётная запись: строка списка обнуляется, пустого места над паролем нет.
        Assert.Contains("ProfilesListRow.Height = new GridLength(0)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaLoginWindow_UsesLoginPolicy()
    {
        var code = ReadLoginAvaloniaCode();

        // Авторазмер по содержимому сохранён, границы — из профиля Login.
        Assert.Contains("Height = double.NaN;", code, StringComparison.Ordinal);
        Assert.Contains("MinHeight = DialogWorkAreaPolicy.Login.MinHeight;", code, StringComparison.Ordinal);
        Assert.Contains("MaxHeight = DialogWorkAreaPolicy.Login.MaxHeight;", code, StringComparison.Ordinal);

        // Старые значения не вернулись.
        Assert.DoesNotContain("MinHeight = 360;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Height = 360", code, StringComparison.Ordinal);

        // Кламп по рабочей области текущего монитора (образец — редактор «Утилит»).
        Assert.Contains("Screens.ScreenFromWindow(this) ?? Screens.Primary", code, StringComparison.Ordinal);
        Assert.Contains("screen.WorkingArea.Height / screen.Scaling", code, StringComparison.Ordinal);
        Assert.Contains("DialogWorkAreaPolicy.Recommend(", code, StringComparison.Ordinal);
        Assert.Contains("DialogWorkAreaPolicy.Login", code, StringComparison.Ordinal);
        Assert.Contains("MaxHeight = size.MaxHeight;", code, StringComparison.Ordinal);

        // Строка списка — Auto (Star резервировала пустое место), список ограничен 280.
        Assert.Contains("new RowDefinition(GridLength.Auto)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GridUnitType.Star", code, StringComparison.Ordinal);
        Assert.Contains("MaxHeight = 280", code, StringComparison.Ordinal);
    }
}
