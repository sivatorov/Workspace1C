using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Реализация <see cref="ICustomProgramsStore"/> (issue #91): пользовательские программы
/// группы «Мои программы» хранятся в едином читаемом JSON-файле
/// <c>custom_programs.json</c> в каталоге данных профиля (рядом с <c>settings.json</c>),
/// по образцу <see cref="CustomActionsStore"/>. Файл можно править вручную и переносить
/// между установками. Запись — атомарная (временный файл + замена), JSON с отступами и
/// читаемой кириллицей. Битый файл не роняет загрузку: возвращается пустой список,
/// файл на диске не трогается (пользователь сможет его поправить вручную).
/// </summary>
public sealed class CustomProgramsStore : ICustomProgramsStore
{
    private readonly IProfileService? _profileService;
    private readonly string? _directoryOverride;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>Имя файла пользовательских программ (рядом с настройками приложения).</summary>
    public const string FileName = "custom_programs.json";

    /// <summary>
    /// Issue #178 (1.0.1.129): запись — всегда UTF-8 без BOM. Раньше кодировка записи
    /// зависела от перегрузки <see cref="File.WriteAllText(string,string)"/> (UTF-8 без
    /// BOM), но чтение не гарантировало UTF-8 для файлов, сохранённых вручную в ANSI
    /// (cp1251) — кириллица в названиях/путях/учётных записях показывалась
    /// «кракозябрами». Теперь кодировка задана явно на обеих сторонах.
    /// </summary>
    private static readonly UTF8Encoding Utf8NoBom =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    /// <summary>Строгий UTF-8: невалидные байты — <see cref="DecoderFallbackException"/>.</summary>
    private static readonly UTF8Encoding Utf8Strict =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <param name="profileService">Профиль для определения каталога данных (может быть null).</param>
    /// <param name="directoryOverride">Явный каталог хранения вместо каталога данных
    /// (используется в юнит-тестах для изоляции от реальных данных профиля).</param>
    public CustomProgramsStore(IProfileService? profileService = null, string? directoryOverride = null)
    {
        _profileService = profileService;
        _directoryOverride = directoryOverride;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            // Кириллицу и прочие не-ASCII символы пишем читаемыми UTF-8,
            // а не \uXXXX-последовательностями (паттерн цикла 0.3.9.155).
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
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
    public IReadOnlyList<CustomProgram> LoadAll()
    {
        if (!File.Exists(FilePath))
            return Array.Empty<CustomProgram>();
        try
        {
            // Issue #178: читаем СЫРЫЕ байты и декодируем явно (UTF-8, при невалидном
            // UTF-8 — ANSI/cp1251). Файл можно править вручную, и сохранение в ANSI
            // больше не превращает кириллицу в «кракозябры» в редакторе «Мои программы».
            var json = DecodeJsonBytes(File.ReadAllBytes(FilePath));
            var list = JsonSerializer.Deserialize<List<CustomProgram>>(json, _jsonOptions)
                       ?? new List<CustomProgram>();
            return Normalize(list);
        }
        catch
        {
            // Битый/нечитаемый файл не должен ронять загрузку остальных функций:
            // возвращаем пустой список, сам файл на диске не трогаем (пользователь
            // сможет его поправить вручную).
            return Array.Empty<CustomProgram>();
        }
    }

    /// <inheritdoc />
    public void Save(CustomProgram program)
    {
        if (program is null)
            return;
        if (string.IsNullOrWhiteSpace(program.Id))
            program.Id = Guid.NewGuid().ToString("N");

        // Единый файл-список (по образцу CustomActionsStore.Save): загружаем текущие
        // записи, заменяем/добавляем по Id и записываем список целиком.
        var all = new List<CustomProgram>(LoadAll());
        var index = all.FindIndex(p => string.Equals(p.Id, program.Id, StringComparison.Ordinal));
        if (index >= 0)
            all[index] = program;
        else
            all.Add(program);

        WriteAtomic(all);
    }

    /// <inheritdoc />
    public void SaveAll(IEnumerable<CustomProgram> programs)
    {
        if (programs is null)
            return;
        var list = Normalize(programs);
        WriteAtomic(list);
    }

    /// <inheritdoc />
    public void Delete(string id)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id) || !File.Exists(FilePath))
                return; // нет файла/пустого Id — no-op
            var all = LoadAll();
            var remaining = all.Where(p => !string.Equals(p.Id, id, StringComparison.Ordinal)).ToList();
            if (remaining.Count == all.Count)
                return; // записи нет — no-op, файл не трогаем

            WriteAtomic(remaining);
        }
        catch
        {
            // Удаление — не критично: файл мог быть уже удалён или занят.
        }
    }

    /// <inheritdoc />
    public CustomProgram? Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        return LoadAll().FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
    }

    /// <summary>
    /// Атомарная запись списка: сначала во временный файл, затем замена целевого
    /// (как <see cref="CustomActionsStore"/>), чтобы не оставить битый файл при сбое
    /// в середине записи.
    /// </summary>
    private void WriteAtomic(List<CustomProgram> programs)
    {
        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(programs, _jsonOptions);
        var tempPath = FilePath + ".tmp";
        // Issue #178: явный UTF-8 без BOM — кириллица хранится в стабильной кодировке.
        File.WriteAllText(tempPath, json, Utf8NoBom);
        File.Move(tempPath, FilePath, overwrite: true);
    }

    /// <summary>
    /// Issue #178 (1.0.1.129): декодирует содержимое <c>custom_programs.json</c> из
    /// сырых байтов. Порядок: BOM UTF-8/UTF-16 → строгий UTF-8 → ANSI (cp1251).
    /// <para>
    /// Файл пользовательских программ можно править вручную, и блокнот на русской
    /// Windows по умолчанию сохраняет в ANSI (cp1251). Чтение такого файла как UTF-8
    /// давало символы замены U+FFFD и «кракозябры» в названиях программ и учётных
    /// записях. Явный fallback на cp1251 восстанавливает кириллицу; следующий
    /// <see cref="SaveAll"/> перезаписывает файл в UTF-8.
    /// </para>
    /// Internal — покрыто юнит-тестами (issue #178).
    /// </summary>
    internal static string DecodeJsonBytes(byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return string.Empty;

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Utf8NoBom.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try
        {
            return Utf8Strict.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Не UTF-8 — файл сохранён в ANSI (cp1251) на русской Windows.
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(1251).GetString(bytes);
            }
            catch
            {
                // Кодовая страница недоступна — читаем с заменами, но без исключения.
                return Utf8NoBom.GetString(bytes);
            }
        }
    }

    /// <summary>
    /// Приводит список к безопасному виду: отбрасывает null-элементы, восстанавливает
    /// null-строки из повреждённого/ручного файла, назначает уникальные Id (пустые и
    /// дубли заменяются) и сортирует по <see cref="CustomProgram.Order"/> устойчиво
    /// (равные значения сохраняют порядок файла; отрицательные значения поднимаются
    /// к нулю). Порядок списка сохраняется как в файле/редакторе.
    /// </summary>
    internal static List<CustomProgram> Normalize(IEnumerable<CustomProgram> programs)
    {
        var result = new List<CustomProgram>();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var program in programs)
        {
            if (program is null)
                continue;
            program.Name ??= string.Empty;
            program.FilePath ??= string.Empty;
            program.Arguments ??= string.Empty;
            program.WorkingDirectory ??= string.Empty;
            program.IconPath ??= string.Empty;
            // Имя — обязательное поле кнопки панели: без него запись бесполезна.
            if (string.IsNullOrWhiteSpace(program.Name))
                continue;
            if (string.IsNullOrWhiteSpace(program.Id) || !usedIds.Add(program.Id))
            {
                program.Id = Guid.NewGuid().ToString("N");
                usedIds.Add(program.Id);
            }
            if (program.Order < 0)
                program.Order = 0;
            result.Add(program);
        }
        // OrderBy устойчив: при равных Order сохраняется порядок файла.
        return result.OrderBy(p => p.Order).ToList();
    }
}
