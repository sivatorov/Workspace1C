using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using Workspace1C.Models;
using Workspace1C.Services.Secrets;

namespace Workspace1C.Services;

/// <summary>
/// Реализация <see cref="IItsAccountsStore"/>: учётные записи ИТС 1С (issue #333) хранятся
/// в отдельном читаемом JSON-файле <c>its_accounts.json</c> в каталоге данных приложения
/// (рядом с <c>settings.json</c>), по образцу <see cref="CustomConfigTypesStore"/>. Запись —
/// атомарная (временный файл + замена), JSON с отступами и читаемой кириллицей
/// (<see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/>). При первом обращении, когда
/// файла ещё нет, текущие логин/пароль ИТС из старых настроек
/// (<see cref="AppSettings.UpdatesLogin"/>/<see cref="AppSettings.UpdatesPassword"/>) переносятся
/// в запись «Основная» — миграция идемпотентна (файл создаётся один раз, повторный запуск
/// дублей не создаёт) и не удаляет старые настройки (обратная совместимость).
/// </summary>
public sealed class ItsAccountsStore : IItsAccountsStore
{
    private readonly IInfobaseRepository? _repository;
    private readonly IProfileService? _profileService;
    private readonly string? _directoryOverride;
    private readonly SecretJsonProtector? _secrets;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>Имя файла учётных записей ИТС (рядом с настройками приложения).</summary>
    public const string FileName = "its_accounts.json";

    /// <summary>Имя записи, создаваемой при миграции старых полей настроек (issue #333).</summary>
    public const string PrimaryName = "Основная";

    /// <param name="repository">Репозиторий для чтения старых настроек при миграции (может быть null).</param>
    /// <param name="profileService">Профиль для определения каталога данных (может быть null).</param>
    /// <param name="directoryOverride">Явный каталог хранения вместо каталога данных
    /// (используется в юнит-тестах для изоляции от реальных данных профиля).</param>
    public ItsAccountsStore(
        IInfobaseRepository? repository = null,
        IProfileService? profileService = null,
        string? directoryOverride = null,
        SecretJsonProtector? secretProtector = null)
    {
        _repository = repository;
        _profileService = profileService;
        _directoryOverride = directoryOverride;
        _secrets = secretProtector;
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

    /// <inheritdoc />
    public string FilePath => Path.Combine(DataDirectory, FileName);

    /// <inheritdoc />
    public IReadOnlyList<ItsAccount> Load()
    {
        if (File.Exists(FilePath))
            return ReadFile();
        // Файла ещё нет — переносим логин/пароль ИТС из старых настроек (AppSettings).
        // Миграция идемпотентна: файл создаётся один раз, при последующих запусках
        // читается только файл; старые настройки не изменяются и не удаляются.
        return MigrateFromSettings();
    }

    /// <inheritdoc />
    public void Save(IReadOnlyCollection<ItsAccount> accounts)
    {
        var normalized = Normalize(accounts ?? new List<ItsAccount>());
        Directory.CreateDirectory(DataDirectory);
        // Пароли ИТС шифруются паролем учётной записи, если шифрование включено.
        // Без ключа Protect бросает SecretProtectionException — открытый пароль не пишется.
        var json = JsonSerializer.Serialize(normalized, _jsonOptions);
        if (_secrets is not null)
            json = _secrets.Protect(json);

        // Атомарная запись: сначала во временный файл, затем замена целевого
        // (как CustomConfigTypesStore.Save), чтобы не оставить битый файл
        // при сбое в середине записи.
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, FilePath, overwrite: true);
    }

    /// <inheritdoc />
    public ItsAccount? GetById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        return Load().FirstOrDefault(a => string.Equals(a.Id, id, System.StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public ItsAccount? GetPrimary() => Load().FirstOrDefault(a => a.IsPrimary);

    /// <inheritdoc />
    public ItsAccount? Resolve(string? accountId)
        => GetById(accountId ?? string.Empty) ?? GetPrimary();

    /// <inheritdoc />
    public void Upsert(ItsAccount account)
    {
        if (account is null)
            return;

        var accounts = Load().ToList();
        if (string.IsNullOrWhiteSpace(account.Id))
            account.Id = Guid.NewGuid().ToString("N");

        var index = accounts.FindIndex(a => string.Equals(a.Id, account.Id, System.StringComparison.Ordinal));
        if (index >= 0)
        {
            // Правка существующей: флаг «Основная» меняется ТОЛЬКО через SetPrimary
            // (нельзя поставить вторую основную из редактора — issue #333).
            accounts[index].Name = account.Name ?? string.Empty;
            accounts[index].Login = account.Login ?? string.Empty;
            accounts[index].Password = account.Password ?? string.Empty;
        }
        else
        {
            // Новая запись: основной становится только при пустом справочнике
            // (правило «0 основных → первая запись»); иначе — без флага.
            account.IsPrimary = !accounts.Any(a => a.IsPrimary);
            accounts.Add(account);
        }

        Save(accounts);
    }

    /// <inheritdoc />
    public void Delete(string id)
    {
        var accounts = Load().ToList();
        var target = accounts.FirstOrDefault(a => string.Equals(a.Id, id, System.StringComparison.Ordinal));
        if (target is null)
            return;

        var wasPrimary = target.IsPrimary;
        accounts.Remove(target);
        if (wasPrimary && accounts.Count > 0)
            accounts[0].IsPrimary = true; // удаление основной → основной становится первая запись

        Save(accounts);
    }

    /// <inheritdoc />
    public void SetPrimary(string id)
    {
        var accounts = Load().ToList();
        var target = accounts.FirstOrDefault(a => string.Equals(a.Id, id, System.StringComparison.Ordinal));
        if (target is null)
            return;

        foreach (var account in accounts)
            account.IsPrimary = string.Equals(account.Id, id, System.StringComparison.Ordinal);

        Save(accounts);
    }

    /// <summary>
    /// Маскирует пароль в тексте для журналирования (обёртка над
    /// <see cref="SensitiveDataMasker.MaskValue"/>): пароль не должен попадать в лог/журнал.
    /// Пустой пароль или текст возвращаются без изменений.
    /// </summary>
    public static string MaskPasswordForLog(string text, string? password)
        => SensitiveDataMasker.MaskValue(text, password);

    private IReadOnlyList<ItsAccount> ReadFile()
    {
        try
        {
            var json = File.ReadAllText(FilePath);
            // Обратная совместимость: открытые значения читаются как есть; зашифрованные
            // расшифровываются при наличии сеансового ключа.
            if (_secrets is not null)
                json = _secrets.Unprotect(json);
            var list = JsonSerializer.Deserialize<List<ItsAccount>>(json, _jsonOptions)
                       ?? new List<ItsAccount>();
            return Normalize(list);
        }
        catch
        {
            // Битый/нечитаемый файл не должен ронять загрузку остальных функций:
            // возвращаем пустой список, сам файл на диске не трогаем (пользователь
            // сможет его поправить вручную).
            return new List<ItsAccount>();
        }
    }

    private IReadOnlyList<ItsAccount> MigrateFromSettings()
    {
        try
        {
            var settings = _repository?.LoadSettings();
            var login = settings?.UpdatesLogin ?? string.Empty;
            if (string.IsNullOrWhiteSpace(login))
                return new List<ItsAccount>(); // мигрировать нечего — файл не создаём

            var account = new ItsAccount
            {
                Name = PrimaryName,
                Login = login,
                Password = settings?.UpdatesPassword ?? string.Empty,
                IsPrimary = true,
            };

            var normalized = Normalize(new[] { account });
            Save(normalized);
            return normalized;
        }
        catch
        {
            // Ошибка чтения старых настроек не должна блокировать справочник.
            return new List<ItsAccount>();
        }
    }

    /// <summary>
    /// Приводит список к безопасному виду: отбрасывает null-элементы, восстанавливает
    /// null-строки из повреждённого/ручного файла и применяет правила «Основная»
    /// (issue #333): ровно одна основная запись — если в файле 2+ основных, основной
    /// остаётся первый найденный; если 0 — основной становится первая запись.
    /// </summary>
    internal static List<ItsAccount> Normalize(IEnumerable<ItsAccount> accounts)
    {
        var result = new List<ItsAccount>();
        foreach (var account in accounts)
        {
            if (account is null)
                continue;
            if (string.IsNullOrWhiteSpace(account.Id))
                account.Id = Guid.NewGuid().ToString("N");
            account.Name ??= string.Empty;
            account.Login ??= string.Empty;
            account.Password ??= string.Empty;
            result.Add(account);
        }

        var primaryIndex = -1;
        for (var i = 0; i < result.Count; i++)
        {
            if (!result[i].IsPrimary)
                continue;
            if (primaryIndex < 0)
            {
                primaryIndex = i;
                continue;
            }
            // 2+ основных в файле → основной остаётся первый найденный.
            result[i].IsPrimary = false;
        }

        // 0 основных → основной становится первая запись.
        if (primaryIndex < 0 && result.Count > 0)
            result[0].IsPrimary = true;

        return result;
    }
}