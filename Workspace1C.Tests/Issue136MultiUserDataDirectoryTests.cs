using System;
using System.IO;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #136 (1.0.1.81): несколько пользователей на одной машине (терминальный режим,
/// разные учётные записи) не должны перезаписывать настройки друг друга.
/// <para>
/// Обычная установка уже персональна: <c>%APPDATA%\Workspace1C</c> —
/// <c>%LOCALAPPDATA%\Workspace1C</c> — <c><temp>\Workspace1C</c>. Но портативная
/// установка в машинном каталоге (<c>C:\Program Files</c>, <c>%ProgramData%</c>) или
/// общий <c>%TEMP%</c> раньше давали один каталог на всех. Теперь к такому каталогу
/// добавляется персональный сегмент <c>users/<логин>-<хэш профиля></c>.
/// </para>
/// </summary>
public sealed class Issue136MultiUserDataDirectoryTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "w1c-issue136-" + Guid.NewGuid().ToString("N"));

    public Issue136MultiUserDataDirectoryTests()
    {
        Directory.CreateDirectory(_root);
        PlatformPaths.ResetDataDirectoryCacheForTests();
    }

    public void Dispose()
    {
        PlatformPaths.ResetDataDirectoryCacheForTests();
        try
        {
            Directory.Delete(_root, true);
        }
        catch
        {
            // Временный каталог — уборка best effort.
        }
    }

    // ===================== Сегмент пользователя =====================

    [Fact]
    public void SanitizeUserSegment_IsStableAndSafe()
    {
        var first = PlatformPaths.SanitizeUserSegment(@"DOMAIN\Иван Петров", @"C:\Users\ivan");
        var second = PlatformPaths.SanitizeUserSegment(@"DOMAIN\Иван Петров", @"C:\Users\ivan");

        Assert.Equal(first, second);
        Assert.DoesNotContain(@"\", first, StringComparison.Ordinal);
        Assert.DoesNotContain(" ", first, StringComparison.Ordinal);
        Assert.DoesNotContain(":", first, StringComparison.Ordinal);
        Assert.True(first.Length <= 41, $"Слишком длинный сегмент: {first}");
    }

    [Fact]
    public void SanitizeUserSegment_DifferentProfiles_ProduceDifferentSegments()
    {
        var first = PlatformPaths.SanitizeUserSegment("ivan", @"C:\Users\ivan");
        var second = PlatformPaths.SanitizeUserSegment("ivan", @"C:\Users\ivan2");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void SanitizeUserSegment_EmptyUser_UsesPlaceholder()
    {
        var segment = PlatformPaths.SanitizeUserSegment(string.Empty, "seed");

        Assert.StartsWith("user-", segment, StringComparison.Ordinal);
    }

    [Fact]
    public void UserDataSegment_IsStableForCurrentProfile()
    {
        Assert.Equal(PlatformPaths.UserDataSegment, PlatformPaths.UserDataSegment);
        Assert.False(string.IsNullOrWhiteSpace(PlatformPaths.UserDataSegment));
    }

    // ===================== Когда нужен персональный сегмент =====================

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void RequiresPerUserSegment_PortableMode_GuardsOnlyMachineWide(
        bool machineWide, bool expected)
    {
        // Портативный режим: сегмент добавляется только в машинном каталоге,
        // сменный носитель сохраняет прежний путь (данные переносимы).
        var directory = @"C:\Program Files\Workspace1C\Workspace1C-Data";

        Assert.Equal(expected, PlatformPaths.RequiresPerUserSegment(
            directory, portableMode: true, _ => machineWide));
    }

    [Fact]
    public void RequiresPerUserSegment_NonPortable_GuardsSharedDirectory()
    {
        // Обычный режим: сегмент нужен для любого каталога вне профиля пользователя
        // (например общий %TEMP% на терминальном сервере).
        Assert.True(PlatformPaths.RequiresPerUserSegment(
            @"C:\Windows\Temp\Workspace1C", portableMode: false, _ => false));
    }

    [Fact]
    public void RequiresPerUserSegment_UserProfileDirectory_IsNotGuarded()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Assert.False(PlatformPaths.RequiresPerUserSegment(
            Path.Combine(appData, "Workspace1C"), portableMode: false, _ => false));
    }

    [Fact]
    public void RequiresPerUserSegment_SharedTemp_IsGuarded()
    {
        // Каталог вне профиля (например общий %TEMP% на терминальном сервере).
        var shared = Path.Combine(Path.GetPathRoot(Path.GetTempPath()) ?? "C:\\", "w1c-shared-temp");

        Assert.True(PlatformPaths.RequiresPerUserSegment(
            shared, portableMode: false, _ => false));
    }

    [Fact]
    public void IsUserPrivateDirectory_DetectsProfileAndAppData()
    {
        Assert.True(PlatformPaths.IsUserPrivateDirectory(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        Assert.True(PlatformPaths.IsUserPrivateDirectory(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)));
        Assert.True(PlatformPaths.IsUserPrivateDirectory(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
        Assert.False(PlatformPaths.IsUserPrivateDirectory(@"C:\Windows\Temp\Workspace1C"));
    }

    // ===================== Применение сегмента =====================

    [Fact]
    public void ApplyPerUserGuard_MachineWidePortable_CreatesPerUserDirectoryAndMigrates()
    {
        var portable = Path.Combine(_root, "portable");
        Directory.CreateDirectory(portable);
        File.WriteAllText(Path.Combine(portable, "settings.json"), "{\"Language\":\"ru\"}");

        var resolution = PlatformPaths.ApplyPerUserGuard(
            new PlatformPaths.DataDirectoryResolution(portable, IsFallback: false, Reason: null),
            portableMode: true,
            isMachineWide: _ => true,
            userSegment: "ivan-1a2b3c4d");

        var expected = Path.Combine(portable, "users", "ivan-1a2b3c4d");
        Assert.Equal(expected, resolution.DirectoryPath);
        Assert.True(File.Exists(Path.Combine(expected, "settings.json")));
        // Старый общий каталог не удаляется — к нему можно вернуться.
        Assert.True(File.Exists(Path.Combine(portable, "settings.json")));
    }

    [Fact]
    public void ApplyPerUserGuard_DifferentUsers_DoNotShareSettings()
    {
        var portable = Path.Combine(_root, "portable");
        Directory.CreateDirectory(portable);
        File.WriteAllText(Path.Combine(portable, "settings.json"), "shared");

        var first = PlatformPaths.ApplyPerUserGuard(
            new PlatformPaths.DataDirectoryResolution(portable, IsFallback: false, Reason: null),
            portableMode: true, isMachineWide: _ => true, userSegment: "user-a-11111111");
        var second = PlatformPaths.ApplyPerUserGuard(
            new PlatformPaths.DataDirectoryResolution(portable, IsFallback: false, Reason: null),
            portableMode: true, isMachineWide: _ => true, userSegment: "user-b-22222222");

        Assert.NotEqual(first.DirectoryPath, second.DirectoryPath);
        File.WriteAllText(Path.Combine(first.DirectoryPath, "settings.json"), "first-user");
        Assert.Equal("shared", File.ReadAllText(Path.Combine(second.DirectoryPath, "settings.json")));
    }

    [Fact]
    public void ApplyPerUserGuard_RemovablePortable_KeepsLegacyPath()
    {
        // Сменный носитель: прежний путь сохраняется, данные переносимы вместе с ним.
        var portable = Path.Combine(_root, "usb");
        var resolution = PlatformPaths.ApplyPerUserGuard(
            new PlatformPaths.DataDirectoryResolution(portable, IsFallback: false, Reason: null),
            portableMode: true, isMachineWide: _ => false, userSegment: "ivan-1a2b3c4d");

        Assert.Equal(portable, resolution.DirectoryPath);
    }

    [Fact]
    public void ApplyPerUserGuard_PrivateDirectory_IsUnchanged()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Workspace1C");
        var resolution = PlatformPaths.ApplyPerUserGuard(
            new PlatformPaths.DataDirectoryResolution(appData, IsFallback: false, Reason: null),
            portableMode: false, isMachineWide: _ => false, userSegment: "ivan-1a2b3c4d");

        Assert.Equal(appData, resolution.DirectoryPath);
    }

    // ===================== Диагностика и каталог логов =====================

    [Fact]
    public void ResolveDataDirectoryForTests_WithGuard_ReportsGuardAndUsesGuardedLogDirectory()
    {
        var shared = Path.Combine(_root, "shared-data");
        Directory.CreateDirectory(shared);

        var resolution = PlatformPaths.ResolveDataDirectoryForTests(
            shared,
            Array.Empty<string>(),
            applyPerUserGuard: true,
            portableMode: true,
            isMachineWide: _ => true,
            userSegment: "ivan-1a2b3c4d");

        Assert.Equal(Path.Combine(shared, "users", "ivan-1a2b3c4d"), resolution.DirectoryPath);
        Assert.Equal(resolution.DirectoryPath, PlatformPaths.AppDataDirectory);
        Assert.True(PlatformPaths.IsSharedDirectoryGuardActive);
        Assert.Contains("разделены", PlatformPaths.SharedDirectoryGuardReason, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(resolution.DirectoryPath, PlatformPaths.LogDirectory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveDataDirectoryForTests_WithoutGuard_KeepsSingleDirectory()
    {
        var shared = Path.Combine(_root, "shared-data");
        Directory.CreateDirectory(shared);

        var resolution = PlatformPaths.ResolveDataDirectoryForTests(
            shared, Array.Empty<string>());

        Assert.Equal(shared, resolution.DirectoryPath);
        Assert.False(PlatformPaths.IsSharedDirectoryGuardActive);
        Assert.Null(PlatformPaths.SharedDirectoryGuardReason);
    }
}
