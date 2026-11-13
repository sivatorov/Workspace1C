using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты issue #109 (v1.0.1.54) «Очистка кэша»:
/// <list type="bullet">
/// <item><see cref="OneCCacheCleaner.GetRootTotalSize"/> — честный общий объём кэша в
/// корнях: несопоставленные базе каталоги учитываются (раньше «15 ГБ» на диске
/// показывались «15 МБ»), junction-дубликаты корней считаются один раз;</item>
/// <item>поиск каталогов кеша во вложенных каталогах версий глубже одного уровня;</item>
/// <item>клиент-серверное имя каталога при пустом DatabaseName строится по имени базы;</item>
/// <item>фильтр имён и нормализация значений реестровых параметров переноса кэша
/// (issue #109б) без обращения к реестру;</item>
/// <item>страж-тесты: итоговые строки окна очистки кэша считаются через
/// <see cref="OneCCacheCleaner.GetRootTotalSize"/>, форматтер размеров имеет
/// полный набор единиц (ГБ не может превратиться в МБ).</item>
/// </list>
/// </summary>
[Collection(OneCCacheCleanerStaticState.Name)]
public sealed class Issue109CacheRootTotalTests
{
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ws1c-issue109-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteFile(string path, int bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }

    /// <summary>Создаёт каталог-ссылку: junction на Windows, симлинк на других ОС.
    /// null — не поддержано средой.</summary>
    private static string? TryCreateDirectoryLink(string link, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(15000);
            return Directory.Exists(link) ? link : null;
        }

        try
        {
            Directory.CreateSymbolicLink(link, target);
            return Directory.Exists(link) ? link : null;
        }
        catch
        {
            return null;
        }
    }

    private static void DeleteDir(string? path)
    {
        if (path is null || !Directory.Exists(path))
            return;
        try { Directory.Delete(path, recursive: true); }
        catch { /* Лучшее усилие: temp-каталоги чистит ОС. */ }
    }

    // ===================== GetRootTotalSize (issue #109а) =====================

    [Fact]
    public void GetRootTotalSize_None_ReturnsZero()
    {
        Assert.Equal(0, OneCCacheCleaner.GetRootTotalSize(OneCCacheKind.None));
    }

    [Fact]
    public void GetRootTotalSize_IncludesUnmappedDirectories()
    {
        var temp = CreateTempDir();
        try
        {
            var root = Path.Combine(temp, "1cv8");
            // Каталог кеша, сопоставленный базе (50 байт).
            WriteFile(Path.Combine(root, "cache-id", "data.dat"), 50);
            // «Осиротевший» каталог — не сопоставлен ни одной базе (100 байт).
            // Раньше такой каталог не попадал в «общий объём» окна очистки.
            WriteFile(Path.Combine(root, "не-стандартное-имя-каталога", "junk.dat"), 100);

            OneCCacheCleaner.SetAdditionalRoots(new[] { root });
            try
            {
                var ib = new Infobase { Id = "cache-id", Name = "База" };

                // Старый расчёт по базам видит только сопоставленный каталог.
                Assert.Equal(50, OneCCacheCleaner.GetSize(ib, OneCCacheKind.User));

                // Честный общий объём включает и несопоставленные каталоги.
                Assert.True(OneCCacheCleaner.GetRootTotalSize(OneCCacheKind.User) >= 150,
                    "Общий объём должен включать несопоставленные каталоги корня.");
            }
            finally
            {
                OneCCacheCleaner.SetAdditionalRoots(null);
            }
        }
        finally
        {
            DeleteDir(temp);
        }
    }

    [Fact]
    public void GetRootTotalSize_DoesNotDoubleCountJunctionRoots()
    {
        var temp = CreateTempDir();
        try
        {
            var real = Path.Combine(temp, "real-cache");
            WriteFile(Path.Combine(real, "a.dat"), 100);

            var link = TryCreateDirectoryLink(Path.Combine(temp, "link-cache"), real);
            if (link is null)
                return; // создание ссылок не поддержано средой — проверка неприменима

            // Базовая линия: корень-цель посчитан один раз.
            OneCCacheCleaner.SetAdditionalRoots(new[] { real });
            long baseline;
            try
            {
                baseline = OneCCacheCleaner.GetRootTotalSize(OneCCacheKind.User);
            }
            finally
            {
                OneCCacheCleaner.SetAdditionalRoots(null);
            }

            Assert.True(baseline >= 100);

            // Корень-ссылка на тот же каталог не удваивает объём (issue #109б:
            // перенос кэша junction'ом не должен ломать честный общий объём).
            OneCCacheCleaner.SetAdditionalRoots(new[] { real, link });
            try
            {
                Assert.Equal(baseline, OneCCacheCleaner.GetRootTotalSize(OneCCacheKind.User));
            }
            finally
            {
                OneCCacheCleaner.SetAdditionalRoots(null);
            }
        }
        finally
        {
            DeleteDir(temp);
        }
    }

    // ===================== Поиск каталогов кеша (issue #109а) =====================

    [Fact]
    public void GetSize_FindsCacheInNestedVersionDirectories()
    {
        var temp = CreateTempDir();
        try
        {
            var root = Path.Combine(temp, "1cv8");
            // Кеш на глубине двух уровней версий: 1cv8\<версия>\<версия-подкаталог>\<имя>.
            var nested = Path.Combine(root, "8.3.24.1000", "8.3.24.2000", "cache-id");
            WriteFile(Path.Combine(nested, "data.dat"), 128);

            OneCCacheCleaner.SetAdditionalRoots(new[] { root });
            try
            {
                var ib = new Infobase { Id = "cache-id", Name = "База" };

                var dirs = OneCCacheCleaner.GetCacheDirectories(ib, OneCCacheKind.User);
                Assert.Contains(dirs, d => d.Replace('\\', '/')
                    .EndsWith("8.3.24.1000/8.3.24.2000/cache-id", StringComparison.OrdinalIgnoreCase));
                Assert.Equal(128, OneCCacheCleaner.GetSize(ib, OneCCacheKind.User));
            }
            finally
            {
                OneCCacheCleaner.SetAdditionalRoots(null);
            }
        }
        finally
        {
            DeleteDir(temp);
        }
    }

    [Fact]
    public void GetSize_ClientServerWithoutDatabaseName_UsesBaseName()
    {
        var temp = CreateTempDir();
        try
        {
            var root = Path.Combine(temp, "1cv8");
            var cacheDir = Path.Combine(root, "Srvr__srv1c__Ref__База 1С__");
            WriteFile(Path.Combine(cacheDir, "data.dat"), 64);

            OneCCacheCleaner.SetAdditionalRoots(new[] { root });
            try
            {
                var ib = new Infobase { Id = "ib-id", Name = "База 1С" };
                ib.Connection.Type = ConnectionType.ClientServer;
                ib.Connection.Server = "srv1c";
                ib.Connection.DatabaseName = ""; // строка соединения без Ref=

                Assert.Equal(64, OneCCacheCleaner.GetSize(ib, OneCCacheKind.User));
            }
            finally
            {
                OneCCacheCleaner.SetAdditionalRoots(null);
            }
        }
        finally
        {
            DeleteDir(temp);
        }
    }

    // ===================== Реестровые параметры переноса (issue #109б) =====================

    [Theory]
    [InlineData("CacheLocation", true)]
    [InlineData("CacheDir", true)]
    [InlineData("CachedDataLocation", true)]
    [InlineData("cachelocation", true)]
    [InlineData("InstalledLocation", false)]
    [InlineData("DistributionLocation", false)]
    [InlineData("DefaultVersion", false)]
    [InlineData("", false)]
    public void IsCacheLocationValueName_MatchesOnlyCacheParameters(string name, bool expected)
    {
        Assert.Equal(expected, OneCCacheCleaner.IsCacheLocationValueName(name));
    }

    [Theory]
    [InlineData("D:\\1C_Cache", "D:\\1C_Cache")]
    [InlineData("  D:\\1C_Cache  ", "D:\\1C_Cache")]
    [InlineData("\"E:\\1c\\cache\"", "E:\\1c\\cache")]
    [InlineData("D:\\", "D:\\")]
    [InlineData("\\\\server\\share\\1c-cache", "\\\\server\\share\\1c-cache")]
    [InlineData("cache", null)]
    [InlineData("relative\\path", null)]
    [InlineData("", null)]
    public void NormalizeRegistryDir_AcceptsOnlyAbsoluteWindowsPaths(string value, string? expected)
    {
        Assert.Equal(expected, OneCCacheCleaner.NormalizeRegistryDir(value));
    }

#if !LINUX
    [Fact]
    public void GetRegistryCacheRoots_ResultIsCachedPerSession()
    {
        // Реестр на машине тестов может быть пуст — проверяем контракт кэширования:
        // повторные вызовы возвращают тот же снимок (реестр читается один раз за сеанс).
        var first = OneCCacheCleaner.GetRegistryCacheRoots();
        var second = OneCCacheCleaner.GetRegistryCacheRoots();
        Assert.Same(first, second);
    }
#endif

    // ===================== Страж-тесты UI и форматтера =====================

    [Fact]
    public void CacheCleanWindows_ComputeTotalsViaGetRootTotalSize()
    {
        // Итоговые строки «общий объём» в окне очистки кэша (WPF и Avalonia) обязаны
        // считаться через GetRootTotalSize — иначе несопоставленные каталоги корней
        // выпадают из «общего объёма» (issue #109а: «15 МБ» вместо «15 ГБ»).
        foreach (var file in new[]
                 {
                     Path.Combine("Workspace1C", "Views", "CacheCleanWindow.xaml.cs"),
                     Path.Combine("Workspace1C", "Views", "CacheCleanWindow.Avalonia.cs")
                 })
        {
            var source = ReadProjectFile(file);
            Assert.True(
                source.Contains("OneCCacheCleaner.GetRootTotalSize(OneCCacheKind.Program)", StringComparison.Ordinal),
                $"Итог программного кэша должен считаться через GetRootTotalSize в {file}.");
            Assert.True(
                source.Contains("OneCCacheCleaner.GetRootTotalSize(OneCCacheKind.User)", StringComparison.Ordinal),
                $"Итог пользовательского кэша должен считаться через GetRootTotalSize в {file}.");
        }
    }

    [Fact]
    public void SizeUnits_HaveFullUnitChain_SoGigabytesCannotDegradeToMegabytes()
    {
        // Сторож-тест «15 ГБ не превращается в 15 МБ»: в единицах размеров должны быть
        // все пять ступеней — форматтер поднимается по ним до ГБ/ТБ (issue #109а).
        var ru = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        var en = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));

        Assert.Contains("\"CacheClean.SizeUnits\": \"Б,КБ,МБ,ГБ,ТБ\"", ru, StringComparison.Ordinal);
        Assert.Contains("\"CacheClean.SizeUnits\": \"B,KB,MB,GB,TB\"", en, StringComparison.Ordinal);
    }

    // ===================== Серверный кэш из параметров агента (issue #109) =====================

    [Theory]
    [InlineData("srvinfo", true)]
    [InlineData("CachePath", false)]
    [InlineData("SrvInfoPath", true)]
    [InlineData("srvdir", true)]
    [InlineData("InstalledLocation", false)]
    [InlineData("", false)]
    public void IsServerInfoValueName_MatchesOnlyServerDataParameters(string name, bool expected)
    {
        Assert.Equal(expected, OneCCacheCleaner.IsServerInfoValueName(name));
    }

    [Fact]
    public void ServerCacheRoots_ParticipateInRootsAndSize_ButNotInOrphanScan()
    {
        var temp = CreateTempDir();
        try
        {
            var suffix = Guid.NewGuid().ToString("N");
            var dbName = "Серверная_" + suffix;
            var serverRoot = Path.Combine(temp, "srvinfo");
            var cacheDir = Path.Combine(serverRoot, $"Srvr__srv1c__Ref__{dbName}__");
            WriteFile(Path.Combine(cacheDir, "data.dat"), 300);

            // Подменяем реальный провайдер (реестр служб ragent): на машине тестов
            // службы серверного агента может не быть.
            OneCCacheCleaner.SetServerCacheRootsForTests(new[] { serverRoot });
            try
            {
                // Найденный серверный корень участвует в корнях обоих типов кэша.
                Assert.Contains(serverRoot, OneCCacheCleaner.GetCacheRoots(OneCCacheKind.User));
                Assert.Contains(serverRoot, OneCCacheCleaner.GetCacheRoots(OneCCacheKind.Program));

                // Но не участвует в поиске «остатков»: удалять серверные данные
                // вне подтверждённой принадлежности базе недопустимо.
                Assert.DoesNotContain(serverRoot,
                    OneCCacheCleaner.GetCacheRoots(OneCCacheKind.User, forOrphanScan: true));

                // Размер серверного кэша базы находится по имени Srvr__…__Ref__…__.
                var ib = new Infobase { Id = "ib-id", Name = dbName };
                ib.Connection.Type = ConnectionType.ClientServer;
                ib.Connection.Server = "srv1c";
                ib.Connection.DatabaseName = dbName;
                Assert.Equal(300, OneCCacheCleaner.GetSize(ib, OneCCacheKind.User));

                // Итог окна очистки (GetRootTotalSize) тоже включает серверный корень.
                Assert.True(OneCCacheCleaner.GetRootTotalSize(OneCCacheKind.User) >= 300);
            }
            finally
            {
                OneCCacheCleaner.SetServerCacheRootsForTests(null);
            }
        }
        finally
        {
            DeleteDir(temp);
        }
    }

    [Fact]
    public void GetRootTotalSize_ServerRootAlsoListedAsAdditional_IsCountedOnce()
    {
        var temp = CreateTempDir();
        try
        {
            var root = Path.Combine(temp, "srvinfo");
            WriteFile(Path.Combine(root, "cache-id", "data.dat"), 200);

            OneCCacheCleaner.SetAdditionalRoots(new[] { root });
            long baseline;
            try
            {
                baseline = OneCCacheCleaner.GetRootTotalSize(OneCCacheKind.User);
            }
            finally
            {
                OneCCacheCleaner.SetAdditionalRoots(null);
            }

            // Тот же каталог пришёл и из «серверных» параметров, и из настроек —
            // канонизация путей не должна удваивать объём.
            OneCCacheCleaner.SetAdditionalRoots(new[] { root });
            OneCCacheCleaner.SetServerCacheRootsForTests(new[] { root });
            try
            {
                Assert.Equal(baseline, OneCCacheCleaner.GetRootTotalSize(OneCCacheKind.User));
            }
            finally
            {
                OneCCacheCleaner.SetServerCacheRootsForTests(null);
                OneCCacheCleaner.SetAdditionalRoots(null);
            }
        }
        finally
        {
            DeleteDir(temp);
        }
    }

#if !LINUX
    [Fact]
    public void GetServerAgentCacheRoots_ResultIsCachedPerSession()
    {
        // Реестр служб на машине тестов может быть пуст — проверяем контракт:
        // повторные вызовы возвращают тот же снимок (реестр читается один раз).
        var first = OneCCacheCleaner.GetServerAgentCacheRoots();
        var second = OneCCacheCleaner.GetServerAgentCacheRoots();
        Assert.Same(first, second);
    }
#endif

    // ===================== Устойчивость расчёта объёма (issue #109) =====================

    [Fact]
    public void GetDirectorySize_CountsLargeDirectoryCompletely()
    {
        var temp = CreateTempDir();
        try
        {
            var root = Path.Combine(temp, "1cv8", "8.3.24.1000", "cache-id");
            Directory.CreateDirectory(root);
            const int files = 2000;
            const int fileSize = 1024;
            for (var i = 0; i < files; i++)
                File.WriteAllBytes(Path.Combine(root, $"f{i}.bin"), new byte[fileSize]);

            // «Большой» каталог: объём не должен обрезаться/теряться из-за порций перечисления.
            Assert.Equal((long)files * fileSize, OneCCacheCleaner.GetDirectorySize(root));
        }
        finally
        {
            DeleteDir(temp);
        }
    }

    [Fact]
    public void GetDirectorySize_BrokenLink_DoesNotThrowAndCountsRegularFiles()
    {
        var temp = CreateTempDir();
        try
        {
            var root = Path.Combine(temp, "1cv8");
            WriteFile(Path.Combine(root, "cache-id", "data.dat"), 128);

            // «Битая» ссылка: цель не существует. Создание симлинка может быть
            // недоступно без прав — тогда тест всё равно проверяет обычный подсчёт.
            var broken = Path.Combine(root, "broken-cache");
            try
            {
                Directory.CreateSymbolicLink(broken, Path.Combine(temp, "missing-target"));
            }
            catch
            {
                // Нет прав/поддержки ссылок — пропускаем создание.
            }

            Assert.Equal(128, OneCCacheCleaner.GetDirectorySize(root));
        }
        finally
        {
            DeleteDir(temp);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Views")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Workspace1C.Tests")))
                return dir.FullName;
            dir = dir.Parent!;
        }

        throw new InvalidOperationException("Корень репозитория не найден из тестового каталога.");
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }
}
