using System;
using System.IO;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты корректной платформы для подстановок сценариев (issue #67: «%full_exe_name% для базы
/// с платформой 8.3.3 подставило 8.5.4.1878… платформы не на диске С»): каталог bin должен
/// резолвиться по версии базы (префиксно и точно), даже если нужная разрядность по умолчанию
/// не совпадает с фактической версией (X64-дефолт против установленной x86), и только при
/// полном отсутствии версии — запасной вариант «новейшая установленная».
/// </summary>
[Collection("PlatformVersionCache")]
public sealed class OneCPlatformLocatorPlatformVersionTests
{
    [Fact]
    public void ResolveBinDirectory_PartialVersion_OtherArch_FindsRequestedVersionNotNewest()
    {
#if LINUX
        return; // Windows-раскладка платформы (1cv8.exe/x86) неприменима в Linux-сборке
#endif
        // База: платформа «8.3.3» (частичная), разрядность не указана (дефолт X64).
        // Установлена только 32-битная 8.3.3.1 и новейшая 64-битная 8.5.4.1878.
        using var temp = new TempDirectory();
        var dir830 = CreatePlatform(temp.Path, "8.3.3.1", machine: 0x014c); // x86
        CreatePlatform64(temp.Path, "8.5.4.1878");                          // 64

        var infobase = new Infobase { Id = "ib", Name = "База", PlatformVersion = "8.3.3", Architecture = "" };

        var previousPaths = PlatformVersionService.GetAdditionalSearchPaths();
        var previousSession = OneCLauncher.SessionArchitecture;
        try
        {
            PlatformVersionService.SetAdditionalSearchPaths(new[] { temp.Path });
            OneCLauncher.SessionArchitecture = SessionArchitectureMode.X64;

            var resolved = OneCPlatformLocator.ResolveBinDirectory(infobase);

            // Должна быть найдена именно 8.3.3 (x86), а не новейшая 8.5.4.1878.
            Assert.Equal(dir830, resolved);
        }
        finally
        {
            OneCLauncher.SessionArchitecture = previousSession;
            PlatformVersionService.SetAdditionalSearchPaths(previousPaths);
            PlatformVersionService.InvalidateSearchCache();
        }
    }

    [Fact]
    public void ResolveBinDirectory_ExactVersionInOtherArch_Found()
    {
#if LINUX
        return; // Windows-раскладка платформы (1cv8.exe/x86) неприменима в Linux-сборке
#endif
        // Полная версия «8.3.3.1» указана явно — ищется точно даже в «чужой» разрядности.
        using var temp = new TempDirectory();
        var dir830 = CreatePlatform(temp.Path, "8.3.3.1", machine: 0x014c); // x86
        CreatePlatform64(temp.Path, "8.5.4.1878");

        var infobase = new Infobase { Id = "ib", Name = "База", PlatformVersion = "8.3.3.1", Architecture = "" };

        var previousPaths = PlatformVersionService.GetAdditionalSearchPaths();
        var previousSession = OneCLauncher.SessionArchitecture;
        try
        {
            PlatformVersionService.SetAdditionalSearchPaths(new[] { temp.Path });
            OneCLauncher.SessionArchitecture = SessionArchitectureMode.X64;

            var resolved = OneCPlatformLocator.ResolveBinDirectory(infobase);

            Assert.Equal(dir830, resolved);
        }
        finally
        {
            OneCLauncher.SessionArchitecture = previousSession;
            PlatformVersionService.SetAdditionalSearchPaths(previousPaths);
            PlatformVersionService.InvalidateSearchCache();
        }
    }

    [Fact]
    public void ResolveBinDirectory_UnknownVersion_FallsBackToNewestInstalled()
    {
#if LINUX
        return; // Windows-раскладка платформы (1cv8.exe/x86) неприменима в Linux-сборке
#endif
        // Версия базы нигде не установлена — резерв: новейшая из установленных (64-битная).
        // Версия «9.9.9.999» гарантированно новее любой реально установленной на машине,
        // поэтому победитель fallback-перебора детерминирован независимо от окружения.
        using var temp = new TempDirectory();
        var dirNewest = CreatePlatform64(temp.Path, "9.9.9.999");

        var infobase = new Infobase { Id = "ib", Name = "База", PlatformVersion = "8.3.9", Architecture = "" };

        var previousPaths = PlatformVersionService.GetAdditionalSearchPaths();
        var previousSession = OneCLauncher.SessionArchitecture;
        try
        {
            PlatformVersionService.SetAdditionalSearchPaths(new[] { temp.Path });
            OneCLauncher.SessionArchitecture = SessionArchitectureMode.X64;

            var resolved = OneCPlatformLocator.ResolveBinDirectory(infobase);

            Assert.Equal(dirNewest, resolved);
        }
        finally
        {
            OneCLauncher.SessionArchitecture = previousSession;
            PlatformVersionService.SetAdditionalSearchPaths(previousPaths);
            PlatformVersionService.InvalidateSearchCache();
        }
    }

    // ======================= Хелперы =======================

    /// <summary>Каталог bin платформы версии <paramref name="version"/> с 32-битным
    /// «1cv8.exe» (минимальный PE с Machine=0x014c), чтобы Windows-сканер считал её x86.</summary>
    private static string CreatePlatform(string root, string version, ushort machine)
    {
        var binDir = Path.Combine(root, "1cv8", version, "bin");
        Directory.CreateDirectory(binDir);
        WriteFakePe(Path.Combine(binDir, "1cv8.exe"), machine);
        return binDir;
    }

    private static string CreatePlatform64(string root, string version)
    {
        var binDir = Path.Combine(root, "1cv8", version, "bin");
        Directory.CreateDirectory(binDir);
        // 1cv8x64.exe — признак 64-битной версии для виндового сканера.
        File.WriteAllText(Path.Combine(binDir, "1cv8x64.exe"), string.Empty);
        return binDir;
    }

    /// <summary>Минимальный PE-файл для <see cref="PlatformVersionService"/>: MZ-заголовок,
    /// e_lfanew=0x80, сигнатура «PE\0\0», Machine (0x014c — I386, 0x8664 — AMD64).</summary>
    private static void WriteFakePe(string path, ushort machine)
    {
        var bytes = new byte[0x100];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';          // «MZ»
        bytes[0x3C] = 0x80;            // e_lfanew
        bytes[0x80] = (byte)'P';
        bytes[0x81] = (byte)'E';       // «PE\0\0»
        bytes[0x82] = 0x00;
        bytes[0x83] = 0x00;
        bytes[0x84] = (byte)(machine & 0xFF);
        bytes[0x85] = (byte)(machine >> 8);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>Временный каталог, удаляемый при освобождении.</summary>
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "w1c_" + Guid.NewGuid().ToString("N"));

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Очистка временных файлов не должна ронять тест.
            }
        }
    }
}