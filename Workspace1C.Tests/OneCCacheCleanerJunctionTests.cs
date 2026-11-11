using System;
using System.Diagnostics;
using System.IO;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Юнит-тесты очистки кэша 1С при ссылках reparse point — junction (mklink /j) и
/// симлинках (issue #109, автор Nodon84: кэш 1C перенесён на другой диск через
/// <c>mklink /j</c> папок %APPDATA%\1C\1Cv82 и %LOCALAPPDATA%\1C\1cv8).
///
/// Проверяются три контракта [<see cref="OneCCacheCleaner"/>]:
/// 1. размер каталога-ссылки считается по цели ссылки (раньше рекурсия .NET
///    <c>SearchOption.AllDirectories</c> не заходила в reparse points и размер был 0);
/// 2. удаление каталога-ссылки очищает СОДЕРЖИМОЕ цели, но не трогает саму ссылку —
///    иначе перенос кэша пользователя ломался;
/// 3. удаление обычного каталога работает как прежде (рекурсивно).
/// </summary>
[Collection(Workspace1C.Tests.OneCCacheCleanerStaticState.Name)]
public class OneCCacheCleanerJunctionTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "w1c_junction_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteFile(string path, int bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }

    /// <summary>Создаёт каталог-ссылку: junction на Windows (mklink /J, прав администратора
    /// не требует), симлинк на других ОС. null — создание не поддержано средой.</summary>
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
            // Симлинк может требовать привилегий — тест в этом случае пропускается.
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

    [Fact]
    public void GetDirectorySize_JunctionLink_CountsTargetContents()
    {
        if (!SupportsLinks())
            return;

        var root = NewTempDir();
        try
        {
            var real = Path.Combine(root, "real");
            WriteFile(Path.Combine(real, "nested", "a.dat"), 100);
            WriteFile(Path.Combine(real, "b.dat"), 50);

            var link = TryCreateDirectoryLink(Path.Combine(root, "link"), real);
            if (link is null)
                return; // среда не даёт создавать ссылки

            var size = OneCCacheCleaner.GetDirectorySize(link);
            Assert.Equal(150, size);
        }
        finally
        {
            DeleteDir(root);
        }
    }

    [Fact]
    public void GetDirectorySize_JunctionCycle_DoesNotHang()
    {
        if (!SupportsLinks())
            return;

        var root = NewTempDir();
        try
        {
            var real = Path.Combine(root, "real");
            WriteFile(Path.Combine(real, "a.dat"), 10);

            var link = TryCreateDirectoryLink(Path.Combine(root, "link"), real);
            if (link is null)
                return;

            // Цикл: внутри цели — ссылка на саму цель (родителя).
            TryCreateDirectoryLink(Path.Combine(real, "loop"), real);
            if (!OneCCacheCleaner.IsReparsePoint(Path.Combine(real, "loop")))
                return;

            var size = OneCCacheCleaner.GetDirectorySize(link);
            Assert.Equal(10, size); // цикл не раздувает размер и не зависает
        }
        finally
        {
            DeleteDir(root);
        }
    }

    [Fact]
    public void TryDeleteDirectory_JunctionLink_RemovesContentsButKeepsLink()
    {
        if (!SupportsLinks())
            return;

        var root = NewTempDir();
        try
        {
            var real = Path.Combine(root, "real");
            WriteFile(Path.Combine(real, "nested", "a.dat"), 100);
            WriteFile(Path.Combine(real, "b.dat"), 50);

            var link = TryCreateDirectoryLink(Path.Combine(root, "link"), real);
            if (link is null)
                return;

            var removed = OneCCacheCleaner.TryDeleteDirectory(link);

            // Ссылка цела и по-прежнему открывает цель.
            Assert.True(removed);
            Assert.True(Directory.Exists(link), "junction/symlink должен остаться на месте");
            Assert.True(Directory.Exists(real), "цель ссылки не должна удаляться");

            // Содержимое цели очищено.
            Assert.Empty(Directory.GetFileSystemEntries(real));
        }
        finally
        {
            // Сначала снимаем ссылку, чтобы DeleteDir не ушёл рекурсией в real
            // (хотя .NET Directory.Delete(recursive) reparse-потомков не разворачивает).
            if (Directory.Exists(Path.Combine(root, "link")))
                DeleteDir(Path.Combine(root, "link"));
            DeleteDir(root);
        }
    }

    [Fact]
    public void TryDeleteDirectory_JunctionLink_DeletesNestedLinksAsLinks()
    {
        if (!SupportsLinks())
            return;

        var root = NewTempDir();
        try
        {
            var real = Path.Combine(root, "real");
            var other = Path.Combine(root, "other");
            WriteFile(Path.Combine(real, "a.dat"), 10);
            WriteFile(Path.Combine(other, "keep.dat"), 20);

            var link = TryCreateDirectoryLink(Path.Combine(root, "link"), real);
            if (link is null)
                return;
            // Внутри цели — ссылка на чужой каталог; её удаляем как ссылку, цель цела.
            TryCreateDirectoryLink(Path.Combine(real, "sub"), other);

            var removed = OneCCacheCleaner.TryDeleteDirectory(link);

            Assert.True(removed);
            Assert.True(Directory.Exists(link), "ссылка верхнего уровня должна остаться");
            Assert.True(Directory.Exists(other), "цель вложенной ссылки не должна удаляться");
            Assert.True(File.Exists(Path.Combine(other, "keep.dat")));
            Assert.Empty(Directory.GetFileSystemEntries(real));
        }
        finally
        {
            if (Directory.Exists(Path.Combine(root, "link")))
                DeleteDir(Path.Combine(root, "link"));
            DeleteDir(root);
        }
    }

    [Fact]
    public void TryDeleteDirectory_NormalDirectory_RemovedRecursively()
    {
        var root = NewTempDir();
        try
        {
            var dir = Path.Combine(root, "cache");
            WriteFile(Path.Combine(dir, "nested", "a.dat"), 100);

            var removed = OneCCacheCleaner.TryDeleteDirectory(dir);

            Assert.True(removed);
            Assert.False(Directory.Exists(dir), "обычный каталог удаляется целиком");
        }
        finally
        {
            DeleteDir(root);
        }
    }

    [Fact]
    public void IsReparsePoint_Link_True_NormalDirectory_False()
    {
        if (!SupportsLinks())
            return;

        var root = NewTempDir();
        try
        {
            var real = Path.Combine(root, "real");
            Directory.CreateDirectory(real);

            Assert.False(OneCCacheCleaner.IsReparsePoint(real));

            var link = TryCreateDirectoryLink(Path.Combine(root, "link"), real);
            if (link is null)
                return;

            Assert.True(OneCCacheCleaner.IsReparsePoint(link));
        }
        finally
        {
            DeleteDir(root);
        }
    }

    /// <summary>Ссылки поддерживаются на всех платформах; на Windows junction создаётся
    /// без привилегий. Заглушка для единообразия (если понадобится фильтр CI).</summary>
    private static bool SupportsLinks() => true;
}
