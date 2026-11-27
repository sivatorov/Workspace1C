using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #143 (1.0.1.129): «кракозябры» в окне «Резервные копии СУБД» и в
/// <c>dbms-backup-debug.log</c>, а также работа под ограниченным пользователем.
/// <para>
/// Проверяем: декодирование вывода <c>pg_dump</c>/<c>sqlcmd</c>/<c>pg_isready</c>
/// в cp866/cp1251/UTF-8 (кириллица читаема, без mojibake), читаемость сообщений об
/// ошибках утилиты, маскирование паролей, диагностику отсутствия прав на каталог
/// (пробная запись), отсутствие «битого» файла и то, что дамп не запускается при
/// отсутствии прав.
/// </para>
/// Все тесты — в коллекции с отключённой параллельностью: они подменяют статические
/// <see cref="DbmsBackupService.RunToolOverride"/> и
/// <see cref="DbmsBackupService.ProbeWriteOverride"/>.
/// </summary>
[Collection("LocalDataStoreState")]
public sealed class Issue143EncodingAndPermissionsTests
{
    static Issue143EncodingAndPermissionsTests()
    {
        // Кодовые страницы 866/1251 нужны и в тестах (построение ожидаемых байтов).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    // ===================== Кодировка вывода утилит =====================

    [Fact]
    public void Decode_PgDumpCp1251Error_IsReadableCyrillic()
    {
        // pg_dump на русской Windows в части сборок пишет сообщения в ANSI (cp1251);
        // до фикса они читались как cp866 и превращались в псевдографику.
        const string expected = "pg_dump: ошибка: не удалось подключиться к серверу";
        var bytes = Encoding.GetEncoding(1251).GetBytes(expected);

        var decoded = ConsoleOutputDecoder.Decode(bytes);

        Assert.Equal(expected, decoded);
        Assert.DoesNotContain('\uFFFD', decoded);
    }

    [Fact]
    public void Decode_PgDumpCp866Error_IsReadableCyrillic()
    {
        const string expected = "pg_dump: ошибка: нет доступа к файлу";
        var bytes = Encoding.GetEncoding(866).GetBytes(expected);

        var decoded = ConsoleOutputDecoder.Decode(bytes);

        Assert.Equal(expected, decoded);
    }

    [Fact]
    public void Decode_PgDumpUtf8Error_IsReadableCyrillic()
    {
        // Linux: вывод утилиты в UTF-8 должен проходить без изменений.
        const string expected = "pg_dump: ошибка: версия сервера не совпадает";
        var bytes = new UTF8Encoding(false).GetBytes(expected);

        var decoded = ConsoleOutputDecoder.Decode(bytes);

        Assert.Equal(expected, decoded);
    }

    [Fact]
    public void ScoreCandidate_PrefersReadableCyrillicOverBoxDrawingMojibake()
    {
        var cp866 = Encoding.GetEncoding(866);
        var cp1251 = Encoding.GetEncoding(1251);
        const string text = "Ошибка резервного копирования";
        var correct = cp1251.GetString(cp1251.GetBytes(text));
        var mojibake = cp866.GetString(cp1251.GetBytes(text));

        Assert.True(
            ConsoleOutputDecoder.ScoreCandidate(correct) > ConsoleOutputDecoder.ScoreCandidate(mojibake),
            "Корректная кириллица должна оцениваться выше псевдографического mojibake.");
    }

    // ===================== Читаемость сообщений об ошибках =====================

    [Fact]
    public void BuildToolFailureMessage_DecodedCp1251Output_IsReadable()
    {
        const string expected = "pg_dump: ошибка: неверный пароль пользователя";
        var tail = ConsoleOutputDecoder.Decode(Encoding.GetEncoding(1251).GetBytes(expected));

        var message = DbmsBackupService.BuildToolFailureMessage(
            "pg_dump", @"C:\backups\db.backup", 1, tail);

        Assert.Contains("pg_dump", message, StringComparison.Ordinal);
        Assert.Contains("ошибка", message, StringComparison.Ordinal);
        Assert.DoesNotContain('\uFFFD', message);
        Assert.DoesNotContain('─', message);
    }

    [Fact]
    public void BuildToolFailureMessage_MasksDbPasswordInTail()
    {
        const string tail = "connection: DBPwd=\"Secret123\"; failed";

        var message = DbmsBackupService.BuildToolFailureMessage("pg_dump", "db.backup", 1, tail);

        Assert.DoesNotContain("Secret123", message, StringComparison.Ordinal);
        Assert.Contains("********", message, StringComparison.Ordinal);
    }

    // ===================== Права на каталог =====================

    [Fact]
    public async Task CheckOutputDirectory_ProbeWriteUnauthorized_ReportsNoWriteAccess_AndLeavesNoFile()
    {
        var dir = NewTempDirectory();
        var previous = DbmsBackupService.ProbeWriteOverride;
        // Имитируем ограниченного пользователя: часть пробного файла записалась,
        // затем система отказала в доступе.
        DbmsBackupService.ProbeWriteOverride = probe =>
        {
            File.WriteAllText(probe, "partial");
            throw new UnauthorizedAccessException("Access is denied");
        };
        try
        {
            var result = await DbmsBackupService.CheckOutputDirectoryAsync(Profile(dir));

            Assert.False(result.Success);
            Assert.Equal("Dbms.NoWriteAccess", result.MessageKey);
            Assert.Contains(dir, result.Detail, StringComparison.Ordinal);
            // Ни пробного, ни «битого» файла остаться не должно.
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally
        {
            DbmsBackupService.ProbeWriteOverride = previous;
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task CheckOutputDirectory_ProbeWriteIoError_ReportsUnavailable_AndLeavesNoFile()
    {
        var dir = NewTempDirectory();
        var previous = DbmsBackupService.ProbeWriteOverride;
        DbmsBackupService.ProbeWriteOverride = probe =>
        {
            File.WriteAllText(probe, "partial");
            throw new IOException("There is not enough space on the disk");
        };
        try
        {
            var result = await DbmsBackupService.CheckOutputDirectoryAsync(Profile(dir));

            Assert.False(result.Success);
            Assert.Equal("Dbms.DirUnavailable", result.MessageKey);
            Assert.Contains(dir, result.Detail, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally
        {
            DbmsBackupService.ProbeWriteOverride = previous;
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task RunAsync_NoWriteAccess_DoesNotStartDump_AndLeavesNoPartialFile()
    {
        var dir = NewTempDirectory();
        var previousProbe = DbmsBackupService.ProbeWriteOverride;
        var previousTool = DbmsBackupService.RunToolOverride;
        var calls = 0;
        DbmsBackupService.ProbeWriteOverride = _ => throw new UnauthorizedAccessException("Access is denied");
        DbmsBackupService.RunToolOverride = (_, _, _, _, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new ProcessRunResult { ExitCode = 0 });
        };
        try
        {
            var result = await DbmsBackupService.RunAsync(Profile(dir));

            Assert.False(result.Success);
            // Дамп не должен запускаться, если писать в каталог нельзя.
            Assert.Equal(0, calls);
            Assert.NotNull(result.ErrorMessage);
            Assert.Contains(dir, result.ErrorMessage!, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally
        {
            DbmsBackupService.ProbeWriteOverride = previousProbe;
            DbmsBackupService.RunToolOverride = previousTool;
            TryDeleteDirectory(dir);
        }
    }

    [Theory]
    [InlineData("ru.json", "администратор")]
    [InlineData("en.json", "administrator")]
    public void Localization_NoWriteAccess_HasAdministratorRecommendation(string file, string word)
    {
        var json = ReadProjectFile("Workspace1C", "Localization", "Languages", file);
        var line = json.Split('\n')
            .FirstOrDefault(l => l.Contains("\"Dbms.NoWriteAccess\"", StringComparison.Ordinal));

        Assert.NotNull(line);
        Assert.Contains(word, line!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ru.json")]
    [InlineData("en.json")]
    public void Localization_DirectoryMessages_ExplainWhatToDo(string file)
    {
        var json = ReadProjectFile("Workspace1C", "Localization", "Languages", file);

        Assert.Contains("\"Dbms.NoWriteAccess\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Dbms.DirUnavailable\"", json, StringComparison.Ordinal);
    }

    // ===================== Журнал dbms-backup-debug.log =====================

    [Fact]
    public void Trace_WritesUtf8WithoutBom_AndReadableCyrillic()
    {
        var dir = NewTempDirectory();
        var previousEnabled = DbmsBackupTrace.EnabledOverride;
        var previousDirectory = DbmsBackupTrace.DirectoryOverride;
        try
        {
            DbmsBackupTrace.EnabledOverride = true;
            DbmsBackupTrace.DirectoryOverride = dir;

            DbmsBackupTrace.Log("issue143-encoding", "pg_dump: ошибка: нет доступа");

            var path = Path.Combine(dir, DbmsBackupTrace.FileName);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            var text = string.Empty;
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(path))
                {
                    // Нестрогое UTF-8: строка может дописываться прямо сейчас.
                    text = Encoding.UTF8.GetString(File.ReadAllBytes(path));
                    if (text.Contains("нет доступа", StringComparison.Ordinal))
                        break;
                }
                Thread.Sleep(20);
            }

            Assert.True(File.Exists(path), "Файл диагностики не создан.");
            var bytes = File.ReadAllBytes(path);
            Assert.False(
                bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "Журнал диагностики не должен начинаться с BOM.");
            Assert.Contains("нет доступа", text, StringComparison.Ordinal);
        }
        finally
        {
            DbmsBackupTrace.EnabledOverride = previousEnabled;
            DbmsBackupTrace.DirectoryOverride = previousDirectory;
            TryDeleteDirectory(dir);
        }
    }

    // ===================== Вспомогательные методы =====================

    private static DbmsBackupProfile Profile(string dir) => new()
    {
        InfobaseId = "base-1",
        Dbms = DbmsKind.PostgreSql,
        Host = "127.0.0.1",
        Port = 5432,
        Database = "demo",
        User = "postgres",
        Password = "secret",
        OutputDirectory = dir,
        KeepCount = 0
    };

    private static string NewTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "issue143_enc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Тестовый каталог мог остаться занятым — на результат не влияет.
        }
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
