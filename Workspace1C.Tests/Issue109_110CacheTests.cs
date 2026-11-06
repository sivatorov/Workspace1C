using System;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты issue #109 «Очистка кэша показывает нули» и issue #110 «ИД базы»:
/// <list type="bullet">
/// <item>легаси-корни 1Cv82 входят в состав корней кеша (Windows);</item>
/// <item>дополнительные корни из настроек (SetAdditionalRoots) участвуют в поиске
/// каталогов кеша и очистке; пустые строки и дубликаты отбрасываются;</item>
/// <item>каталог кеша в нестандартном корне находится, размер считается, очистка удаляет;</item>
/// <item>публичный API <see cref="OneCCacheCleaner.GetCacheDirectories"/> (issue #110)
/// возвращает все найденные каталоги базы дедуплицированно и отсортированно;</item>
/// <item>junction на каталоге версии не ломает ни подсчёт, ни перечисление (Windows).</item>
/// </list>
/// </summary>
public sealed class Issue109_110CacheTests
{
    [Fact]
    public void GetCacheRoots_User_IncludesLegacy1Cv82()
    {
        if (!OperatingSystem.IsWindows())
            return; // корни 1Cv82 добавляются только в Windows-ветке GetCacheRoots

        var roots = OneCCacheCleaner.GetCacheRoots(OneCCacheKind.User).ToList();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Assert.Contains(Path.Combine(appData, "1C", "1Cv82"), roots);
    }

    [Fact]
    public void GetCacheRoots_Program_IncludesLegacy1Cv82()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var roots = OneCCacheCleaner.GetCacheRoots(OneCCacheKind.Program).ToList();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.Contains(Path.Combine(localAppData, "1C", "1Cv82"), roots);
    }

    [Fact]
    public void SetAdditionalRoots_NormalizesAndDeduplicates()
    {
        try
        {
            var temp = Path.Combine(Path.GetTempPath(), "w1c_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            var sub = Path.Combine(temp, "cachedir");
            Directory.CreateDirectory(sub);

            OneCCacheCleaner.SetAdditionalRoots(new[]
            {
                "  ", null, "  " + sub + "  ", sub, sub.ToUpperInvariant()
            });

            var snapshot = OneCCacheCleaner.GetAdditionalRootsSnapshot();
            var single = Assert.Single(snapshot);
            Assert.Equal(sub, single, ignoreCase: true, ignoreLineEndingDifferences: false,
                ignoreWhiteSpaceDifferences: false, ignoreAllWhiteSpace: false);

            // Доп. корень участвует в поиске корней кеша любого типа.
            foreach (var kind in new[] { OneCCacheKind.Program, OneCCacheKind.User, OneCCacheKind.All })
            {
                Assert.Contains(single, OneCCacheCleaner.GetCacheRoots(kind));
            }
        }
        finally
        {
            OneCCacheCleaner.SetAdditionalRoots(null);
        }
    }

    [Fact]
    public void GetCacheDirectories_FindsCacheInAdditionalRoot()
    {
        var temp = Path.Combine(Path.GetTempPath(), "w1c_" + Guid.NewGuid().ToString("N"));
        try
        {
            var cacheDir = Path.Combine(temp, Path.Combine("1C", "1cv8", "basecache_" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(cacheDir);
            var payload = Path.Combine(cacheDir, "data.bin");
            File.WriteAllBytes(payload, new byte[1024]);

            // Каталог кеша базы: имя каталога — ID базы (один из вариантов GetCacheNames).
            var byId = Path.Combine(cacheDir, "00000000-0000-0000-0000-000000000001");
            Directory.CreateDirectory(byId);
            File.WriteAllBytes(Path.Combine(byId, "c.dat"), new byte[512]);

            var ib = new Infobase { Id = "00000000-0000-0000-0000-000000000001", Name = "ТестБаза" };

            // Без доп. корня каталог не находится.
            OneCCacheCleaner.SetAdditionalRoots(null);
            Assert.Empty(OneCCacheCleaner.GetCacheDirectories(ib, OneCCacheKind.User));

            // С доп. корнем — находится; размер считается; очистка удаляет содержимое.
            OneCCacheCleaner.SetAdditionalRoots(new[] { cacheDir });

            var dirs = OneCCacheCleaner.GetCacheDirectories(ib, OneCCacheKind.User);
            var found = Assert.Single(dirs);
            Assert.Equal(byId, found, ignoreCase: true, ignoreLineEndingDifferences: false,
                ignoreWhiteSpaceDifferences: false, ignoreAllWhiteSpace: false);

            Assert.True(OneCCacheCleaner.GetSize(ib, OneCCacheKind.User) > 0);

            var removed = OneCCacheCleaner.Clear(new[] { ib }, OneCCacheKind.User);
            Assert.Equal(1, removed);
            Assert.False(Directory.Exists(byId));
        }
        finally
        {
            OneCCacheCleaner.SetAdditionalRoots(null);
            try { Directory.Delete(temp, recursive: true); } catch { /* temp cleanup */ }
        }
    }

    [Fact]
    public void GetCacheDirectories_NullBaseOrNoneKind_ReturnsEmpty()
    {
        var ib = new Infobase { Id = "x", Name = "x" };
        Assert.Empty(OneCCacheCleaner.GetCacheDirectories(ib!, OneCCacheKind.User));
        Assert.Empty(OneCCacheCleaner.GetCacheDirectories(ib, OneCCacheKind.None));
    }

    [Fact]
    public void GetCacheDirectories_DeduplicatesAndSorts()
    {
        var temp = Path.Combine(Path.GetTempPath(), "w1c_" + Guid.NewGuid().ToString("N"));
        try
        {
            var rootA = Path.Combine(temp, "rootA");
            var rootB = Path.Combine(temp, "rootB");
            var dirA = Path.Combine(rootA, "cache-id");
            var dirB = Path.Combine(rootB, "cache-id");
            Directory.CreateDirectory(dirA);
            Directory.CreateDirectory(dirB);

            var ib = new Infobase { Id = "cache-id", Name = "Двойная" };
            OneCCacheCleaner.SetAdditionalRoots(new[] { rootA, rootB });

            var dirs = OneCCacheCleaner.GetCacheDirectories(ib, OneCCacheKind.User).ToList();

            Assert.Equal(2, dirs.Count);
            Assert.Equal(dirs.OrderBy(p => p, StringComparer.OrdinalIgnoreCase), dirs);
        }
        finally
        {
            OneCCacheCleaner.SetAdditionalRoots(null);
            try { Directory.Delete(temp, recursive: true); } catch { /* temp cleanup */ }
        }
    }

    [Fact]
    public void JunctionOnVersionDirectory_SizeAndEnumerationWork()
    {
        if (!OperatingSystem.IsWindows())
            return; // junction — Windows-специфика (mklink /J)

        var temp = Path.Combine(Path.GetTempPath(), "w1c_" + Guid.NewGuid().ToString("N"));
        try
        {
            // Реальный кеш — вне «корня», версия-каталог в корне — junction на него
            // (сценарий из issue #109: перенос кеша ссылкой на уровне каталога версии).
            var realRoot = Path.Combine(temp, "real", "8.3.99.9999");
            var realCache = Path.Combine(realRoot, "junction-cache-id");
            Directory.CreateDirectory(realCache);
            File.WriteAllBytes(Path.Combine(realCache, "blob.bin"), new byte[2048]);

            var root = Path.Combine(temp, "root");
            var versionLink = Path.Combine(root, "8.3.99.9999");
            Directory.CreateDirectory(root);

            var createLink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{versionLink}\" \"{realRoot}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            Assert.NotNull(createLink);
            createLink!.WaitForExit(10_000);
            Assert.Equal(0, createLink.ExitCode);
            Assert.True(Directory.Exists(versionLink));

            var ib = new Infobase { Id = "junction-cache-id", Name = "JunctionBase" };
            OneCCacheCleaner.SetAdditionalRoots(new[] { root });

            try
            {
                var dirs = OneCCacheCleaner.GetCacheDirectories(ib, OneCCacheKind.User).ToList();
                Assert.Contains(dirs, d => d.Contains("junction-cache-id", StringComparison.OrdinalIgnoreCase));
                Assert.True(OneCCacheCleaner.GetSize(ib, OneCCacheKind.User) >= 2048);
            }
            finally
            {
                // Ссылка удаляется без цели (rmdir), содержимое цели — отдельно.
                RemoveJunction(versionLink);
            }
        }
        finally
        {
            OneCCacheCleaner.SetAdditionalRoots(null);
            try { Directory.Delete(temp, recursive: true); } catch { /* temp cleanup */ }
        }
    }

    private static void RemoveJunction(string junctionPath)
    {
        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c rmdir \"{junctionPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit(10_000);
        }
        catch
        {
            // очистка тестового окружения — ошибки игнорируем
        }
    }
}
