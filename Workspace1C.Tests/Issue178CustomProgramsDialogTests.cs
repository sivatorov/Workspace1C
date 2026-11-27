using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #178 (1.0.1.129) «Мои программы — запуск от Администратора»: два замечания
/// из комментариев после 1.0.1.127.
/// <list type="number">
/// <item>RafaIS «кракозябры, кодировка сбилась» — сообщения <c>runas.exe</c> и файл
/// <c>custom_programs.json</c> декодируются центральным правилом
/// <see cref="ConsoleOutputDecoder"/> / явным UTF-8 с fallback на ANSI (cp1251), а не
/// принудительным UTF-8.</item>
/// <item>7OH «поправьте высоту диалога, чтобы без скролла по умолчанию было и не
/// завышено» — профиль <see cref="DialogWorkAreaPolicy.CustomPrograms"/> увеличен до
/// 700/400 DIP под блок «Запускать от имени администратора».</item>
/// </list>
/// </summary>
public sealed class Issue178CustomProgramsDialogTests : IDisposable
{
    private readonly string _tempDir;

    static Issue178CustomProgramsDialogTests()
    {
        // Кодовые страницы 866/1251 нужны для построения ожидаемых байтов.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public Issue178CustomProgramsDialogTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "issue178_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* не критично */ }
    }

    // ===================== Кракозябры: декодирование вывода процесса =====================

    [Fact]
    public void DecodeProcessOutput_Cp866RunAsMessage_IsReadableCyrillic()
    {
        // runas.exe на русской Windows пишет в OEM-кодировке cp866.
        const string expected = "Не удалось запустить программу: неверное имя пользователя";
        var bytes = Encoding.GetEncoding(866).GetBytes(expected);

        var decoded = CustomProgramLauncher.DecodeProcessOutput(null, bytes);

        Assert.Contains("Не удалось", decoded, StringComparison.Ordinal);
        Assert.DoesNotContain('\uFFFD', decoded);
        Assert.Equal(expected, decoded);
    }

    [Fact]
    public void DecodeProcessOutput_Cp1251RunAsMessage_HasNoReplacementChars()
    {
        // ANSI (cp1251) — второй возможный вариант вывода утилиты. Конкретную
        // однобайтовую страницу без образца не различить, но символов замены быть не должно.
        var bytes = Encoding.GetEncoding(1251).GetBytes("Неверное имя пользователя или пароль");

        var decoded = CustomProgramLauncher.DecodeProcessOutput(null, bytes);

        Assert.DoesNotContain('\uFFFD', decoded);
        Assert.Contains(decoded, ch => ch >= 'А' && ch <= 'я');
    }

    [Fact]
    public void DecodeProcessOutput_Utf8RunAsMessage_IsReadableCyrillic()
    {
        const string expected = "Отказано в доступе";
        var bytes = Encoding.UTF8.GetBytes(expected);

        var decoded = CustomProgramLauncher.DecodeProcessOutput(null, bytes);

        Assert.Equal(expected, decoded);
    }

    [Fact]
    public void DecodeProcessOutput_StdoutAndStderr_AreCombined()
    {
        var stdout = Encoding.UTF8.GetBytes("строка вывода");
        var stderr = Encoding.GetEncoding(866).GetBytes("строка ошибки");

        var decoded = CustomProgramLauncher.DecodeProcessOutput(stdout, stderr);

        Assert.Contains("строка вывода", decoded, StringComparison.Ordinal);
        Assert.Contains("строка ошибки", decoded, StringComparison.Ordinal);
    }

    [Fact]
    public void DecodeProcessOutput_NullAndEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, CustomProgramLauncher.DecodeProcessOutput(null, null));
        Assert.Equal(string.Empty, CustomProgramLauncher.DecodeProcessOutput(Array.Empty<byte>(), Array.Empty<byte>()));
    }

    [Fact]
    public void RunAsStartInfo_CapturesStderrInsteadOfForcingUtf8()
    {
        // Флаг доступен только на Windows; на других платформах профиль запуска не runas.
        var program = new CustomProgram
        {
            FilePath = Path.Combine(_tempDir, "app.exe"),
            RunAsUserName = "Administrator",
            RunAsAdministrator = true,
        };

        var startInfo = CustomProgramLauncher.BuildStartInfo(program);

        if (!OperatingSystem.IsWindows())
        {
            // На Linux флаг игнорируется — runas.exe не используется.
            Assert.NotEqual("runas.exe", startInfo.FileName, StringComparer.OrdinalIgnoreCase);
            return;
        }

        Assert.Equal("runas.exe", startInfo.FileName, StringComparer.OrdinalIgnoreCase);
        Assert.True(startInfo.RedirectStandardError);
        // Принудительная кодировка чтения не задаётся: декодируем сами.
        Assert.Null(startInfo.StandardErrorEncoding);
    }

    // ===================== Кракозябры: файл custom_programs.json =====================

    [Fact]
    public void Store_Cp1251File_ReadsCyrillicWithoutMojibake()
    {
        // Файл сохранён вручную в ANSI (cp1251) — типичный сценарий «кодировка сбилась».
        var json =
            "[\n" +
            "  {\n" +
            "    \"Id\": \"a1\",\n" +
            "    \"Name\": \"Моя программа\",\n" +
            "    \"FilePath\": \"C:\\\\Program Files\\\\pgAdmin 4\\\\pgAdmin4.exe\",\n" +
            "    \"RunAsAdministrator\": true,\n" +
            "    \"RunAsUserName\": \"Администратор\"\n" +
            "  }\n" +
            "]";
        var filePath = Path.Combine(_tempDir, CustomProgramsStore.FileName);
        File.WriteAllBytes(filePath, Encoding.GetEncoding(1251).GetBytes(json));

        var store = new CustomProgramsStore(directoryOverride: _tempDir);
        var programs = store.LoadAll();

        var program = Assert.Single(programs);
        Assert.Equal("Моя программа", program.Name);
        Assert.Equal("Администратор", program.RunAsUserName);
        Assert.True(program.RunAsAdministrator);
        Assert.DoesNotContain('\uFFFD', program.Name);
        Assert.DoesNotContain('\uFFFD', program.RunAsUserName);
    }

    [Fact]
    public void Store_WritesUtf8WithoutBom_AndKeepsCyrillic()
    {
        var store = new CustomProgramsStore(directoryOverride: _tempDir);
        store.SaveAll(new[]
        {
            new CustomProgram
            {
                Name = "Моя программа",
                FilePath = @"C:\tools\app.exe",
                RunAsUserName = "Администратор",
                RunAsAdministrator = true,
            }
        });

        var bytes = File.ReadAllBytes(Path.Combine(_tempDir, CustomProgramsStore.FileName));

        // UTF-8 без BOM.
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        Assert.Contains("Моя программа", text, StringComparison.Ordinal);
        Assert.Contains("Администратор", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DecodeJsonBytes_Utf8WithBom_StripsBom()
    {
        var payload = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes("{\"Name\":\"Программа\"}"))
            .ToArray();

        var decoded = CustomProgramsStore.DecodeJsonBytes(payload);

        Assert.StartsWith("{", decoded, StringComparison.Ordinal);
        Assert.Contains("Программа", decoded, StringComparison.Ordinal);
    }

    // ===================== Высота диалога «Мои программы» =====================

    [Fact]
    public void CustomProgramsProfile_FitsRunAsBlockWithoutScroll()
    {
        var profile = DialogWorkAreaPolicy.CustomPrograms;

        Assert.Equal(700, profile.DefaultHeight, 3);
        Assert.Equal(700, profile.ContentHeight, 3);
        Assert.Equal(400, profile.MinHeight, 3);
        Assert.True(profile.MinHeight < profile.DefaultHeight);
        Assert.True(profile.MaxHeight >= profile.DefaultHeight);

        foreach (var workArea in new[] { 900.0, 1000.0, 1080.0 })
        {
            var size = DialogWorkAreaPolicy.Recommend(profile, workArea);
            Assert.Equal(700, size.Height, 3);
            Assert.Equal(400, size.MinHeight, 3);
            Assert.False(size.RequiresScrolling,
                $"При рабочей области {workArea} DIP «Мои программы» помещаются без прокрутки.");
            Assert.True(size.Height <= workArea);
        }
    }

    [Fact]
    public void CustomProgramsProfile_SmallScreen_ClampsAndRequiresScrolling()
    {
        // Экран 600 DIP: 600 − 48 = 552 — окно урезается, прокрутка становится аварийной.
        var size = DialogWorkAreaPolicy.Recommend(DialogWorkAreaPolicy.CustomPrograms, 600);

        Assert.Equal(552, size.Height, 3);
        Assert.True(size.RequiresScrolling);
        Assert.True(size.MinHeight <= size.Height);
    }

    [Fact]
    public void WpfWindow_MatchesProfileHeightAndMinHeight()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "CustomProgramsWindow.xaml");

        Assert.Contains("Height=\"700\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"400\"", xaml, StringComparison.Ordinal);
        // Высоту считает общая политика (как у остальных редакторов).
        Assert.Contains("DialogWorkAreaPolicy.CustomPrograms", ReadProjectFile(
            "Workspace1C", "Views", "CustomProgramsWindow.xaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaWindow_UsesProfileAndSizeToContent()
    {
        var source = ReadProjectFile("Workspace1C", "Views", "CustomProgramsWindow.Avalonia.cs");

        Assert.Contains("DialogWorkAreaPolicy.CustomPrograms", source, StringComparison.Ordinal);
        Assert.Contains("SizeToContent = Avalonia.Controls.SizeToContent.Height", source, StringComparison.Ordinal);
        Assert.Contains("MinHeight = DialogWorkAreaPolicy.CustomPrograms.MinHeight;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Height = DialogWorkAreaPolicy.CustomPrograms.DefaultHeight;", source, StringComparison.Ordinal);
    }

    // ===================== Локализация =====================

    [Theory]
    [InlineData("ru.json")]
    [InlineData("en.json")]
    public void Localization_RunAsStrings_AreReadable(string fileName)
    {
        var strings = LoadLanguageStrings(fileName);
        foreach (var key in new[]
                 {
                     "Launch.CustomPrograms.RunAsAdministrator",
                     "Launch.CustomPrograms.RunAsAdministratorTooltip",
                     "Launch.CustomPrograms.RunAsUserName",
                     "Launch.CustomPrograms.RunAsUserNameTooltip",
                 })
        {
            Assert.True(strings.TryGetValue(key, out var value), $"Нет ключа {key} в {fileName}");
            Assert.False(string.IsNullOrWhiteSpace(value));
            Assert.NotEqual(key, value);
            Assert.DoesNotContain('\uFFFD', value!);
        }
    }

    // ===================== Вспомогательные методы =====================

    private static Dictionary<string, string> LoadLanguageStrings(string fileName)
    {
        var json = ReadProjectFile("Workspace1C", "Localization", "Languages", fileName);
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in doc.RootElement.GetProperty("strings").EnumerateObject())
            result[property.Name] = property.Value.GetString() ?? string.Empty;
        return result;
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var relativePath = Path.Combine(parts);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var fullPath = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(fullPath))
                return File.ReadAllText(fullPath);
            dir = dir.Parent;
        }

        Assert.Fail($"Файл проекта не найден: {relativePath}");
        return string.Empty;
    }
}
