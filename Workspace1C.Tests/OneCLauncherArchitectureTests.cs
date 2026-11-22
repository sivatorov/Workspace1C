using System;
using System.IO;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты выбора разрядности платформы (issue #73 «Запуск х86 из другого места»):
/// явная разрядность базы (вкладка «Разрядность») должна иметь приоритет над
/// глобальной настройкой «Разрядность по умолчанию», а пользовательские каталоги
/// должны участвовать в поиске 32-битной платформы.
/// </summary>
[Collection("PlatformVersionCache")]
public sealed class OneCLauncherArchitectureTests
{
    [Fact]
    public void ResolveArchitecture_Explicit32_GlobalX64_ReturnsX86()
    {
        // База с явной разрядностью «32» + глобальный дефолт X64 → должна выбраться x86.
        // Раньше глобальный X64 побеждал и ломал запуск 32-битной платформы (issue #73).
        var previousDefault = OneCLauncher.DefaultArchitectureMode;
        var previousSession = OneCLauncher.SessionArchitecture;
        try
        {
            OneCLauncher.DefaultArchitectureMode = "X64";
            OneCLauncher.SessionArchitecture = SessionArchitectureMode.Auto;

            var arch = OneCLauncher.ResolveArchitecture("32", null);

            Assert.Equal(OneCArchitecture.x86, arch);
        }
        finally
        {
            OneCLauncher.DefaultArchitectureMode = previousDefault;
            OneCLauncher.SessionArchitecture = previousSession;
        }
    }

    [Fact]
    public void ResolveArchitecture_Explicit64_GlobalX86_ReturnsX64()
    {
        // Зеркальный случай: явная «64» базы перебивает глобальный X86.
        var previousDefault = OneCLauncher.DefaultArchitectureMode;
        var previousSession = OneCLauncher.SessionArchitecture;
        try
        {
            OneCLauncher.DefaultArchitectureMode = "X86";
            OneCLauncher.SessionArchitecture = SessionArchitectureMode.Auto;

            var arch = OneCLauncher.ResolveArchitecture("64", null);

            Assert.Equal(OneCArchitecture.x64, arch);
        }
        finally
        {
            OneCLauncher.DefaultArchitectureMode = previousDefault;
            OneCLauncher.SessionArchitecture = previousSession;
        }
    }

    [Fact]
    public void ResolveArchitecture_NoBaseArch_GlobalX64_ReturnsX64()
    {
        // Глобальная политика «X64, если не указано» продолжает работать,
        // когда у базы разрядность не задана.
        var previousDefault = OneCLauncher.DefaultArchitectureMode;
        var previousSession = OneCLauncher.SessionArchitecture;
        try
        {
            OneCLauncher.DefaultArchitectureMode = "X64";
            OneCLauncher.SessionArchitecture = SessionArchitectureMode.Auto;

            var arch = OneCLauncher.ResolveArchitecture("", null);

            Assert.Equal(OneCArchitecture.x64, arch);
        }
        finally
        {
            OneCLauncher.DefaultArchitectureMode = previousDefault;
            OneCLauncher.SessionArchitecture = previousSession;
        }
    }

    [Fact]
    public void ResolveArchitecture_NoBaseArch_GlobalX86_ReturnsX86()
    {
        var previousDefault = OneCLauncher.DefaultArchitectureMode;
        var previousSession = OneCLauncher.SessionArchitecture;
        try
        {
            OneCLauncher.DefaultArchitectureMode = "X86";
            OneCLauncher.SessionArchitecture = SessionArchitectureMode.Auto;

            var arch = OneCLauncher.ResolveArchitecture("", null);

            Assert.Equal(OneCArchitecture.x86, arch);
        }
        finally
        {
            OneCLauncher.DefaultArchitectureMode = previousDefault;
            OneCLauncher.SessionArchitecture = previousSession;
        }
    }

    [Fact]
    public void ResolveArchitecture_Explicit32_GlobalX86_ReturnsX86()
    {
        // Явная «32» и глобальный X86 дают один результат — x86.
        var previousDefault = OneCLauncher.DefaultArchitectureMode;
        var previousSession = OneCLauncher.SessionArchitecture;
        try
        {
            OneCLauncher.DefaultArchitectureMode = "X86";
            OneCLauncher.SessionArchitecture = SessionArchitectureMode.Auto;

            var arch = OneCLauncher.ResolveArchitecture("32", null);

            Assert.Equal(OneCArchitecture.x86, arch);
        }
        finally
        {
            OneCLauncher.DefaultArchitectureMode = previousDefault;
            OneCLauncher.SessionArchitecture = previousSession;
        }
    }

    [Fact]
    public void ResolveArchitecture_SessionX64_OverridesExplicit32()
    {
        // «Текущая сессия» — наивысший приоритет: X64 побеждает явную «32» базы.
        var previousDefault = OneCLauncher.DefaultArchitectureMode;
        var previousSession = OneCLauncher.SessionArchitecture;
        try
        {
            OneCLauncher.DefaultArchitectureMode = "X64";
            OneCLauncher.SessionArchitecture = SessionArchitectureMode.X64;

            var arch = OneCLauncher.ResolveArchitecture("32", null);

            Assert.Equal(OneCArchitecture.x64, arch);
        }
        finally
        {
            OneCLauncher.DefaultArchitectureMode = previousDefault;
            OneCLauncher.SessionArchitecture = previousSession;
        }
    }

    [Fact]
    public void ResolveBinDirectory_Explicit32_CustomPath_FindsX86Platform()
    {
#if LINUX
        return; // Windows-раскладка платформы (1cv8.exe/x86) неприменима в Linux-сборке
#endif
        // Пользовательский каталог участвует в поиске 32-битной платформы:
        // база с явной «32» и глобальным X64 должна найти x86-платформу из доп. пути.
        using var temp = new TempDirectory();
        var dir32 = CreatePlatform(temp.Path, "8.3.27.1688", machine: 0x014c); // x86

        var infobase = new Infobase { Id = "ib", Name = "База", PlatformVersion = "8.3.27.1688", Architecture = "32" };

        var previousPaths = PlatformVersionService.GetAdditionalSearchPaths();
        var previousDefault = OneCLauncher.DefaultArchitectureMode;
        var previousSession = OneCLauncher.SessionArchitecture;
        try
        {
            PlatformVersionService.SetAdditionalSearchPaths(new[] { temp.Path });
            OneCLauncher.DefaultArchitectureMode = "X64";
            OneCLauncher.SessionArchitecture = SessionArchitectureMode.Auto;

            var resolved = OneCPlatformLocator.ResolveBinDirectory(infobase);

            Assert.Equal(dir32, resolved);
        }
        finally
        {
            OneCLauncher.DefaultArchitectureMode = previousDefault;
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
