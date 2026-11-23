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
    /// Асинхронное чтение (issue #143): файл читается потоком, не блокируя вызывающий
    /// поток. Отмена прерывает чтение; битый/недоступный файл по-прежнему даёт
    /// значение по умолчанию (поведение <see cref="Load{T}"/> сохранено).
    /// </summary>
    public static async Task<T> LoadAsync<T>(string fileName, CancellationToken ct = default)
        where T : new()
    {
        var path = PathOf(fileName);
        try
        {
            if (!File.Exists(path))
                return new T();
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 4096, useAsync: true);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, Options, ct)
                .ConfigureAwait(false);
            return value ?? new T();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new T();
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
    /// Асинхронная запись (issue #143): данные пишутся во временный файл потоком,
    /// затем атомарно переименовываются. Отмена проверяется до/после записи и
    /// передаётся в файловые операции — зависший сетевой профиль больше не держит
    /// UI-поток. Формат JSON не меняется (тот же <see cref="Options"/>).
    /// </summary>
    public static async Task SaveAsync<T>(string fileName, T value, CancellationToken ct = default)
    {
        var path = PathOf(fileName);
        var dir = Path.GetDirectoryName(path);
        ct.ThrowIfCancellationRequested();
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        ct.ThrowIfCancellationRequested();
        await using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write,
            FileShare.None, 4096, useAsync: true))
        {
            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
        ct.ThrowIfCancellationRequested();
        File.Move(tmp, path, overwrite: true);
    }
}
