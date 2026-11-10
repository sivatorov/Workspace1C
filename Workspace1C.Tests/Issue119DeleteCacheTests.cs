using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты issue #119 «Кэш баз при удалении»:
/// <list type="bullet">
/// <item>junction-safe очистка кэша базы при удалении из списка: ссылка
/// (junction/symlink) сохраняется, очищается только содержимое цели;</item>
/// <item>размер кэша базы считается по цели ссылки и возвращается из
/// <see cref="OneCCacheCleaner.GetSize(Infobase, OneCCacheKind)"/>;</item>
/// <item>страж-тесты UI: чекбокс «Удалить кэш» есть в диалоге удаления обеих
/// платформ (WPF и Avalonia) и включён по умолчанию; размер кэша вычисляется
/// через <see cref="OneCCacheCleaner"/>; флаг протянут во все 4 точки вызова
/// удаления; строки локализации ru/en на месте.</item>
/// </list>
/// </summary>
public sealed class Issue119DeleteCacheTests
{
    // ===================== Функциональные тесты очистки кэша =====================

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "w1c_issue119_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteFile(string path, int bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }

    /// <summary>Создаёт каталог-ссылку: junction на Windows (mklink /J, прав
    /// администратора не требует), симлинк на других ОС. null — не поддержано средой.</summary>
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

    [Fact]
    public void Clear_OnBaseDelete_JunctionLinkIsKept_TargetContentsCleared()
    {
        var root = NewTempDir();
        try
        {
            // Реальный каталог кэша с данными; каталог кэша базы — ссылка на него
            // (пользователь перенёс кэш 1С на другой диск, issue #109/#119).
            var real = Path.Combine(root, "real");
            WriteFile(Path.Combine(real, "a.dat"), 100);
            WriteFile(Path.Combine(real, "nested", "b.dat"), 50);

            var cacheRoot = Path.Combine(root, "1cv8");
            var link = TryCreateDirectoryLink(Path.Combine(cacheRoot, "cache-id"), real);
            if (link is null)
                return; // среда не даёт создавать ссылки

            var ib = new Infobase { Id = "cache-id", Name = "Удаляемая база" };
            OneCCacheCleaner.SetAdditionalRoots(new[] { cacheRoot });

            // Размер считается по цели ссылки (ссылка не даёт ноль).
            Assert.Equal(150, OneCCacheCleaner.GetSize(ib, OneCCacheKind.User));

            // «Удаление базы из списка с очисткой кэша»: каталогов найдено/удалено 1.
            var removed = OneCCacheCleaner.Clear(ib, OneCCacheKind.User);
            Assert.Equal(1, removed);

            // Junction-safe: ссылка сохранена, содержимое цели удалено.
            Assert.True(Directory.Exists(link), "Ссылка (junction/symlink) не должна удаляться вместе с кэшем базы.");
            Assert.True(OneCCacheCleaner.IsReparsePoint(link));
            Assert.Empty(Directory.GetFileSystemEntries(link));

            // Повторный расчёт размера — 0: кэш очищен.
            Assert.Equal(0, OneCCacheCleaner.GetSize(ib, OneCCacheKind.User));
        }
        finally
        {
            OneCCacheCleaner.SetAdditionalRoots(null);
            DeleteDir(root);
        }
    }

    [Fact]
    public void Clear_OnBaseDelete_RegularCacheDirRemoved()
    {
        var root = NewTempDir();
        try
        {
            // Обычный (не ссылка) каталог кэша базы в доп. корне.
            var cacheRoot = Path.Combine(root, "1cv8");
            var cacheDir = Path.Combine(cacheRoot, "cache-id2");
            WriteFile(Path.Combine(cacheDir, "c.dat"), 256);

            var ib = new Infobase { Id = "cache-id2", Name = "Обычная база" };
            OneCCacheCleaner.SetAdditionalRoots(new[] { cacheRoot });

            Assert.Equal(256, OneCCacheCleaner.GetSize(ib, OneCCacheKind.User));

            var removed = OneCCacheCleaner.Clear(ib, OneCCacheKind.User);
            Assert.Equal(1, removed);
            Assert.False(Directory.Exists(cacheDir));
            Assert.Equal(0, OneCCacheCleaner.GetSize(ib, OneCCacheKind.User));
        }
        finally
        {
            OneCCacheCleaner.SetAdditionalRoots(null);
            DeleteDir(root);
        }
    }

    // ===================== Страж-тесты UI и точек вызова =====================

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Views")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Workspace1C.Tests")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Корень репозитория не найден из тестового каталога.");
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    [Fact]
    public void DeleteDialog_Wpf_HasCacheCheckboxCheckedByDefault()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "DeleteInfobaseWindow.xaml"));
        // Чекбокс «Удалить кэш» включён по умолчанию (issue #119).
        Assert.Contains("<CheckBox x:Name=\"CacheDeleteCheck\" IsChecked=\"True\"", xaml);

        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "DeleteInfobaseWindow.xaml.cs"));
        // Свойство-флаг и расчёт размера кэша через OneCCacheCleaner.
        Assert.Contains("public bool DeleteCache", code);
        Assert.Contains("OneCCacheCleaner.GetSize(infobase, OneCCacheKind.All)", code);
        Assert.Contains("DeleteInfobase.CacheDelete", code);
        Assert.Contains("DeleteCache = CacheDeleteCheck.IsChecked == true", code);
    }

    [Fact]
    public void DeleteDialog_Avalonia_HasCacheCheckboxCheckedByDefault()
    {
        var axaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "DeleteInfobaseWindow.axaml"));
        // Чекбокс «Удалить кэш» включён по умолчанию (issue #119).
        Assert.Contains("<CheckBox x:Name=\"CacheCheck\" IsChecked=\"True\"", axaml);

        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "DeleteInfobaseWindow.Avalonia.cs"));
        Assert.Contains("public bool DeleteCache", code);
        Assert.Contains("OneCCacheCleaner.GetSize(_infobase, OneCCacheKind.All)", code);
        Assert.Contains("DeleteInfobase.CacheDelete", code);
        Assert.Contains("FindControl<CheckBox>(\"CacheCheck\")!.IsChecked == true", code);
    }

    [Fact]
    public void DeleteCallSites_PropagateDeleteCacheFlag_AndClearCache()
    {
        var files = new[]
        {
            Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Commands.cs"),
            Path.Combine("Workspace1C", "ViewModels", "MainViewModel.BatchCommands.cs"),
            Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Avalonia.Tools.cs"),
            Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Avalonia.BatchCommands.cs")
        };

        foreach (var f in files)
        {
            var source = ReadProjectFile(f);
            // Флаг из диалога читается, кэш очищается junction-safe средством OneCCacheCleaner.
            Assert.True(
                source.Contains("dlg.DeleteCache", StringComparison.Ordinal) ||
                source.Contains("dialog.DeleteCache", StringComparison.Ordinal),
                $"Флаг DeleteCache диалога не протянут в {f}.");
            Assert.True(
                source.Contains("OneCCacheCleaner.Clear(", StringComparison.Ordinal),
                $"Очистка кэша OneCCacheCleaner.Clear не вызвана в {f}.");
        }
    }

    [Fact]
    public void Localization_CacheKeysPresent_InRuAndEn()
    {
        var ru = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        var en = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));

        foreach (var key in new[]
                 {
                     "DeleteInfobase.CacheDelete",
                     "DeleteInfobase.CacheHint",
                     "DeleteInfobase.CacheHelp"
                 })
        {
            Assert.True(ru.Contains($"\"{key}\"", StringComparison.Ordinal), $"Ключ {key} отсутствует в ru.json.");
            Assert.True(en.Contains($"\"{key}\"", StringComparison.Ordinal), $"Ключ {key} отсутствует в en.json.");
        }

        // Подпись чекбокса содержит подстановку размера кэша в обеих локализациях.
        Assert.Contains("\"DeleteInfobase.CacheDelete\": \"Удалить кэш базы ({0})\"", ru, StringComparison.Ordinal);
        Assert.Contains("\"DeleteInfobase.CacheDelete\": \"Delete the base cache ({0})\"", en, StringComparison.Ordinal);
    }
}
