using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Хранилище справочника «Сервера управления» (issue #3): список серверов 1С для
/// встроенного монитора (rac) с полями «Описание, адрес и порт, логин и пароль».
/// </summary>
public interface IServerListStore
{
    /// <summary>Читает все записи справочника (пустой список при отсутствии/битости файла).</summary>
    IReadOnlyList<ManagementServer> Load();

    /// <summary>Сохраняет весь список записей (атомарная запись, JSON с отступами).</summary>
    void Save(IReadOnlyCollection<ManagementServer> servers);
}

/// <summary>
/// Реализация <see cref="IServerListStore"/>: файл <c>server_list.json</c> в каталоге
/// данных профиля/приложения (рядом с <c>settings.json</c>), по образцу
/// <see cref="ItsAccountsStore"/>. Запись — атомарная (временный файл + замена),
/// JSON с отступами и читаемой кириллицей. Битый файл не роняет приложение —
/// возвращается пустой список.
/// </summary>
public sealed class ServerListStore : IServerListStore
{
    /// <summary>Имя файла справочника серверов (рядом с настройками приложения).</summary>
    public const string FileName = "server_list.json";

    private readonly IProfileService? _profileService;
    private readonly string? _directoryOverride;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <param name="profileService">Профиль для определения каталога данных (может быть null).</param>
    /// <param name="directoryOverride">Явный каталог хранения вместо каталога данных
    /// (используется в юнит-тестах для изоляции от реальных данных профиля).</param>
    public ServerListStore(IProfileService? profileService = null, string? directoryOverride = null)
    {
        _profileService = profileService;
        _directoryOverride = directoryOverride;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            // Кириллицу и прочие не-ASCII символы пишем читаемыми UTF-8,
            // а не \uXXXX-последовательностями (паттерн цикла 0.3.9.155).
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
    }

    /// <summary>Каталог данных: явный (тесты) → каталог активного профиля → общий каталог приложения.</summary>
    private string DataDirectory =>
        !string.IsNullOrWhiteSpace(_directoryOverride)
            ? _directoryOverride!
            : (!string.IsNullOrWhiteSpace(_profileService?.CurrentProfileDataDirectory)
                ? _profileService!.CurrentProfileDataDirectory
                : PlatformPaths.AppDataDirectory);

    /// <summary>Полный путь к файлу хранилища.</summary>
    public string FilePath => Path.Combine(DataDirectory, FileName);

    /// <inheritdoc />
    public IReadOnlyList<ManagementServer> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new List<ManagementServer>();

            var json = File.ReadAllText(FilePath);
            var raw = JsonSerializer.Deserialize<List<ManagementServer>>(json, _jsonOptions);
            return Normalize(raw);
        }
        catch
        {
            // Битый/нечитаемый файл не должен ронять загрузку: возвращаем пустой
            // список, сам файл не трогаем (пользователь сможет поправить вручную).
            return new List<ManagementServer>();
        }
    }

    /// <inheritdoc />
    public void Save(IReadOnlyCollection<ManagementServer> servers)
    {
        var normalized = Normalize(servers?.ToList() ?? new List<ManagementServer>());
        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(normalized, _jsonOptions);

        // Атомарная запись: сначала во временный файл, затем замена целевого
        // (как ItsAccountsStore.Save), чтобы не оставить битый файл при сбое.
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, FilePath, overwrite: true);
    }

    /// <summary>Приводит список к каноническому виду: у каждой записи есть Id,
    /// описание/адрес не пустые, порт в допустимом диапазоне; битые записи отбрасываются.</summary>
    private static IReadOnlyList<ManagementServer> Normalize(IReadOnlyList<ManagementServer>? source)
    {
        var result = new List<ManagementServer>();
        if (source is null)
            return result;

        foreach (var server in source)
        {
            if (server is null)
                continue;

            var description = (server.Description ?? string.Empty).Trim();
            var address = (server.Address ?? string.Empty).Trim();
            if (description.Length == 0 && address.Length == 0)
                continue;

            result.Add(new ManagementServer
            {
                Id = string.IsNullOrWhiteSpace(server.Id) ? Guid.NewGuid().ToString("N") : server.Id,
                Description = description,
                Address = address.Length == 0 ? "localhost" : address,
                // Фолбэк невалидного порта — дефолт справочника (1545, RAS; issue #3).
                Port = server.Port is >= 1 and <= 65535 ? server.Port : ManagementServer.DefaultPort,
                User = server.User ?? string.Empty,
                Password = server.Password ?? string.Empty,
                RdpHost = (server.RdpHost ?? string.Empty).Trim(),
                RdpUser = (server.RdpUser ?? string.Empty).Trim(),
                SshTarget = (server.SshTarget ?? string.Empty).Trim(),
                SshPort = server.SshPort is >= 1 and <= 65535 ? server.SshPort : 0,
            });
        }
        return result;
    }
}
