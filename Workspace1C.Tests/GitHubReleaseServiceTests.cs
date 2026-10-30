using System.Text.Json;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты выбора ассета релиза в GitHubReleaseService (issues #358, #89): точное имя
/// платформенной сборки (Windows — «Workspace1C.exe», Linux — «Workspace1C-linux-x64»)
/// побеждает ZIP-архив с признаком «win-x64»; ZIP-архивы вообще не должны выбираться
/// загрузчиком автообновления, а на Linux отбрасывается и Windows-EXE.
/// </summary>
public sealed class GitHubReleaseServiceTests
{
#if !LINUX
    private const string ExactAssetName = "Workspace1C.exe";
#else
    private const string ExactAssetName = "Workspace1C-linux-x64";
#endif

    private static JsonElement ParseAssets(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string AssetsJson(params (string Name, long Size)[] assets)
    {
        var items = string.Join(",", assets.Select(a =>
            $"{{\"name\":\"{a.Name}\",\"size\":{a.Size},\"browser_download_url\":\"https://github.com/sivatorov/Workspace1C/releases/download/v0.3.11.0/{Uri.EscapeDataString(a.Name)}\"}}"));
        return $"{{\"assets\":[{items}]}}";
    }

    [Fact]
    public void FindAsset_ExactNameBeatsWinX64Zip()
    {
        var root = ParseAssets(AssetsJson(
            ("Workspace1C-0.3.11.0-win-x64.zip", 77_000_000),
            (ExactAssetName, 66_000_000)));

        var (url, size) = GitHubReleaseService.FindAsset(root);

        Assert.Contains("/" + Uri.EscapeDataString(ExactAssetName), url, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(66_000_000, size);
    }

    [Fact]
    public void FindAsset_ZipOnlyAsset_IsNotSelected()
    {
        // В релизе нет bare-exe — загрузчик НЕ должен взять zip «win-x64» (issue #358).
        var root = ParseAssets(AssetsJson(
            ("Workspace1C-0.3.11.0-win-x64.zip", 77_000_000)));

        var (url, size) = GitHubReleaseService.FindAsset(root);

        Assert.Equal(string.Empty, url);
        Assert.Equal(0, size);
    }

    [Fact]
    public void FindAsset_FallsBackToNonZipWinX64Asset()
    {
#if !LINUX
        // Bare-exe отсутствует, но есть некий exe с признаком win-x64 — допустимый запасной путь.
        var root = ParseAssets(AssetsJson(("Workspace1C-0.3.11.0-win-x64.pdb.exe", 55_000_000)));

        var (url, size) = GitHubReleaseService.FindAsset(root);

        Assert.False(string.IsNullOrEmpty(url));
        Assert.Equal(55_000_000, size);
#else
        // На Linux любые .exe отбрасываются (issue #89): даже с признаком win-x64.
        var root = ParseAssets(AssetsJson(("Workspace1C-0.3.11.0-win-x64.pdb.exe", 55_000_000)));

        var (url, size) = GitHubReleaseService.FindAsset(root);

        Assert.Equal(string.Empty, url);
        Assert.Equal(0, size);
#endif
    }

    [Fact]
    public void FindAsset_DoesNotPickOtherPlatformAsset()
    {
#if !LINUX
        // Только Linux-ассет в релизе — Windows-версия его не выбирает (issue #89).
        var root = ParseAssets(AssetsJson(("Workspace1C-linux-x64", 52_000_000)));

        var (url, size) = GitHubReleaseService.FindAsset(root);

        Assert.Equal(string.Empty, url);
        Assert.Equal(0, size);
#else
        // Только Windows-EXE в релизе — Linux-версия его не выбирает (issue #89).
        var root = ParseAssets(AssetsJson(("Workspace1C.exe", 66_000_000)));

        var (url, size) = GitHubReleaseService.FindAsset(root);

        Assert.Equal(string.Empty, url);
        Assert.Equal(0, size);
#endif
    }

    [Fact]
    public void FindAsset_PrefersExactPlatformNameOverOtherPlatformAsset()
    {
#if !LINUX
        // Windows: точный bare-exe «Workspace1C.exe» выигрывает у Linux-ассета (issue #89).
        var root = ParseAssets(AssetsJson(
            ("Workspace1C-linux-x64", 52_000_000),
            ("Workspace1C.exe", 66_000_000)));

        var (url, size) = GitHubReleaseService.FindAsset(root);

        Assert.Contains("/" + Uri.EscapeDataString("Workspace1C.exe"), url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("linux-x64", url, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(66_000_000, size);
#else
        // Linux: точный «Workspace1C-linux-x64» выигрывает у Windows-EXE (issue #89).
        var root = ParseAssets(AssetsJson(
            ("Workspace1C.exe", 66_000_000),
            ("Workspace1C-linux-x64", 52_000_000)));

        var (url, size) = GitHubReleaseService.FindAsset(root);

        Assert.Contains("/" + Uri.EscapeDataString("Workspace1C-linux-x64"), url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".exe", url, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(52_000_000, size);
#endif
    }

    [Fact]
    public void FindAsset_ExactNameWinsRegardlessOfOrder()
    {
        var root = ParseAssets(AssetsJson(
            ("Workspace1C-0.3.11.0-win-x64.7z", 10_000_000),
            (ExactAssetName, 66_000_000),
            ("SHA256SUMS.txt", 500)));

        var (url, size) = GitHubReleaseService.FindAsset(root);

        Assert.Contains("/" + Uri.EscapeDataString(ExactAssetName), url, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(66_000_000, size);
    }

    [Fact]
    public void IsPlatformAsset_RejectsZip()
    {
        Assert.False(GitHubReleaseService.IsPlatformAsset("Workspace1C-0.3.11.0-win-x64.zip"));
        Assert.False(GitHubReleaseService.IsPlatformAsset("something-win-x64.ZIP"));
    }

#if !LINUX
    [Fact]
    public void IsPlatformAsset_AcceptsExeAndWinX64()
    {
        Assert.True(GitHubReleaseService.IsPlatformAsset("Workspace1C.exe"));
        Assert.True(GitHubReleaseService.IsPlatformAsset("Workspace1C-0.3.11.0-win-x64"));
    }
#else
    [Fact]
    public void IsPlatformAsset_AcceptsLinuxNames()
    {
        Assert.True(GitHubReleaseService.IsPlatformAsset("Workspace1C"));
        Assert.True(GitHubReleaseService.IsPlatformAsset("Workspace1C-0.3.11.0-linux-x64"));
        Assert.True(GitHubReleaseService.IsPlatformAsset("Workspace1C-linux-x64"));
    }

    [Fact]
    public void IsPlatformAsset_RejectsExeOnLinux()
    {
        // Issue #89: Windows-сборка (exe) на Linux не является платформенным ассетом.
        Assert.False(GitHubReleaseService.IsPlatformAsset("Workspace1C.exe"));
        Assert.False(GitHubReleaseService.IsPlatformAsset("Workspace1C-linux-x64.exe"));
        Assert.False(GitHubReleaseService.IsPlatformAsset("something.exe"));
    }
#endif
}
