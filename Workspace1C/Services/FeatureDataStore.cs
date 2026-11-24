using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Workspace1C.Services;

/// <summary>
/// Простое JSON-хранилище настроек новых функций (выпуск 1.0.0.69): файл лежит
/// в каталоге данных текущего профиля (как <c>custom_actions.json</c>), читается
/// и пишется целиком. Битый файл не роняет функцию — возвращается значение
/// по умолчанию, файл на диске не перезаписывается до явного сохранения.
/// </summary>
public static class FeatureDataStore
{
    /// <summary>Переопределение каталога (тесты).</summary>
    public static string? DirectoryOverride { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Каталог данных текущего профиля.</summary>
    public static string DataDirectory
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DirectoryOverride))
                return DirectoryOverride!;
            try
            {
                var profiles = AppServices.TryGetService<IProfileService>();
                var dir = profiles?.CurrentProfileDataDirectory;
                if (!string.IsNullOrWhiteSpace(dir))
                    return dir!;
            }
            catch
            {
                // DI ещё не настроен (CLI, тесты) — каталог приложения.
            }
            return PlatformPaths.AppDataDirectory;
        }
    }

    public static string PathOf(string fileName) => Path.Combine(DataDirectory, fileName);

    public static T Load<T>(string fileName) where T : new()
    {
        var path = PathOf(fileName);
        try
        {
            if (!File.Exists(path))
                return new T();
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T();
        }
        catch
        {
            return new T();
        }
    }

    /// <summary>
    /// Асинхронное чтение (issue #143): файловые вызовы (<c>File.Exists</c>,
    /// <c>File.ReadAllText</c>) выполняются на выделенном потоке
    /// (<see cref="BlockingIo"/>), а не на UI-потоке и не на потоке пула —
    /// недоступный SMB/UNC-профиль не «вешает» окно и не вызывает голодание пула.
    /// Отмена прерывает чтение; битый/недоступный файл по-прежнему даёт значение
    /// по умолчанию (поведение <see cref="Load{T}"/> сохранено).
    /// </summary>
    public static async Task<T> LoadAsync<T>(string fileName, CancellationToken ct = default)
        where T : new()
    {
        var path = PathOf(fileName);
        DbmsBackupTrace.Log("io-begin", $"load {DbmsBackupTrace.DescribePath(path)}");
        try
        {
            return await BlockingIo.Run(() =>
            {
                if (!File.Exists(path))
                    return new T();
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<T>(json, Options) ?? new T();
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new T();
        }
        finally
        {
            DbmsBackupTrace.Log("io-end", "load");
        }
    }

    public static void Save<T>(string fileName, T value)
    {
        var path = PathOf(fileName);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// Асинхронная запись (issue #143): сериализация — в памяти, а все блокирующие
    /// файловые операции (<c>Directory.CreateDirectory</c>, запись временного файла,
    /// <c>File.Move</c>) выполняются на выделенном потоке (<see cref="BlockingIo"/>).
    /// Отмена проверяется до/после каждой операции; формат JSON не меняется
    /// (тот же <see cref="Options"/>).
    /// </summary>
    public static async Task SaveAsync<T>(string fileName, T value, CancellationToken ct = default)
    {
        var path = PathOf(fileName);
        DbmsBackupTrace.Log("io-begin", $"save {DbmsBackupTrace.DescribePath(path)}");
        try
        {
            await BlockingIo.Run(() =>
            {
                var dir = Path.GetDirectoryName(path);
                ct.ThrowIfCancellationRequested();
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                var tmp = path + ".tmp";
                var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
                File.WriteAllBytes(tmp, bytes);
                ct.ThrowIfCancellationRequested();
                File.Move(tmp, path, overwrite: true);
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            DbmsBackupTrace.Log("io-end", "save");
        }
    }
}
