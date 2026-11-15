using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            var json = File.ReadAllText(FilePath);
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
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, FilePath, overwrite: true);
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
