using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Workspace1C.Models;
using Workspace1C.Services.Licensing;

namespace Workspace1C.Services;

/// <summary>
/// Реализация <see cref="IUtilitiesMenuLayoutStore"/>: компоновка меню «Утилиты»
/// (issue #374) хранится в едином читаемом JSON-файле <c>utilities_menu_layout.json</c>
/// в каталоге данных профиля (рядом с <c>settings.json</c>), по образцу
/// <see cref="CustomActionsStore"/>. Файл можно править вручную и переносить между
/// установками. Запись — атомарная (временный файл + замена), JSON с отступами и
/// читаемой кириллицей. Битый файл не роняет загрузку: возвращается null (построитель
/// меню использует стандартную компоновку), файл на диске не трогается.
/// </summary>
public sealed class UtilitiesMenuLayoutStore : IUtilitiesMenuLayoutStore
{
    private readonly IProfileService? _profileService;
    private readonly string? _directoryOverride;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>Имя файла компоновки меню (рядом с настройками приложения).</summary>
    public const string FileName = "utilities_menu_layout.json";

    /// <summary>
    /// Имя резервной копии исходного файла перед миграцией схемы 1 → 2 (issue #146):
    /// пользователь может вернуть старую структуру вручную.
    /// </summary>
    public const string BackupFileName = "utilities_menu_layout.v1.backup.json";

    /// <param name="profileService">Профиль для определения каталога данных (может быть null).</param>
    /// <param name="directoryOverride">Явный каталог хранения вместо каталога данных
    /// (используется в юнит-тестах для изоляции от реальных данных профиля).</param>
    public UtilitiesMenuLayoutStore(IProfileService? profileService = null, string? directoryOverride = null)
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
    public UtilitiesMenuLayout? Load()
    {
        if (!File.Exists(FilePath))
            return null;
        try
        {
            var json = File.ReadAllText(FilePath);
            var layout = JsonSerializer.Deserialize<UtilitiesMenuLayout>(json, _jsonOptions);
            var normalized = Normalize(layout);
            // Issue #146: перед миграцией схемы 1 → 2 сохраняем исходный файл рядом,
            // чтобы пользователь мог вернуть прежнюю структуру. Резервная копия
            // создаётся один раз и не перезаписывает более раннюю.
            if (UtilitiesMenuLayouts.NeedsMigration(normalized))
                WriteBackup(json);
            // Миграция сохраняет пользовательское дерево (группы, порядок, видимость,
            // иконки и цвета), добавляя только недостающие функции каталога.
            return UtilitiesMenuLayouts.Migrate(normalized);
        }
        catch
        {
            // Битый/нечитаемый файл не должен ронять загрузку: возвращаем null —
            // построитель меню использует стандартную компоновку. Сам файл на диске
            // не трогаем (пользователь сможет его поправить вручную).
            return null;
        }
    }

    /// <summary>
    /// True — по указанному каталогу лежит читаемый файл компоновки (issue #146).
    /// Используется миграцией настроек: наличие пользовательской компоновки включает
    /// правку структуры меню, если пользователь ещё не сохранял этот флаг.
    /// </summary>
    public static bool HasValidLayoutFile(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return false;
        var path = Path.Combine(directory!, FileName);
        if (!File.Exists(path))
            return false;
        try
        {
            using var _ = JsonDocument.Parse(File.ReadAllText(path));
            return true;
        }
        catch
        {
            // Битый/пустой файл не включает правку: стандартная компоновка надёжнее.
            return false;
        }
    }

    /// <summary>
    /// Записывает резервную копию исходного JSON перед миграцией (issue #146).
    /// Ошибки записи не мешают загрузке: миграция продолжится, а файл на диске
    /// останется нетронутым (пользователь сможет поправить его вручную).
    /// </summary>
    private void WriteBackup(string json)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var backupPath = Path.Combine(DataDirectory, BackupFileName);
            if (!File.Exists(backupPath))
                File.WriteAllText(backupPath, json);
        }
        catch
        {
            // Резервная копия — вспомогательная операция, не роняем загрузку меню.
        }
    }

    /// <inheritdoc />
    public void Save(UtilitiesMenuLayout layout)
    {
        if (layout is null)
            return;
        layout.SchemaVersion = UtilitiesMenuLayouts.CurrentSchemaVersion;
        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(layout, _jsonOptions);

        // Атомарная запись: сначала во временный файл, затем замена целевого
        // (как CustomActionsStore.Save), чтобы не оставить битый файл при сбое.
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, FilePath, overwrite: true);
    }

    /// <summary>
    /// Приводит компоновку к безопасному виду: восстанавливает null-коллекции и
    /// null-строки из повреждённого/ручного файла, отбрасывает узлы с неизвестным
    /// типом и пункты с несуществующим идентификатором каталога. Узлы-разделители
    /// (<c>Kind == "separator"</c>) сохраняются как самостоятельные узлы (issue #374),
    /// а старый признак <see cref="UtilitiesMenuNode.SeparatorBefore"/> мигрирует
    /// в узел-разделитель перед узлом — редактор и построители видят единую структуру.
    /// </summary>
    private static UtilitiesMenuLayout? Normalize(UtilitiesMenuLayout? layout)
    {
        if (layout is null)
            return null;
        layout.Root ??= new List<UtilitiesMenuNode>();
        layout.Root = NormalizeList(layout.Root);
        return layout;
    }

    /// <summary>Нормализует список узлов: прогоняет каждый через
    /// <see cref="NormalizeNode"/> и разворачивает <c>SeparatorBefore</c> в узел-разделитель.</summary>
    private static List<UtilitiesMenuNode> NormalizeList(List<UtilitiesMenuNode> nodes)
    {
        var result = new List<UtilitiesMenuNode>();
        foreach (var node in nodes)
        {
            var normalized = NormalizeNode(node);
            if (normalized is null)
                continue;
            // Миграция старого признака SeparatorBefore (issue #374): перед узлом вставляем
            // самостоятельный разделитель (как его показывает редактор). У самого разделителя
            // SeparatorBefore не разворачиваем — иначе получится двойная линия.
            if (normalized.SeparatorBefore && result.Count > 0 &&
                !string.Equals(normalized.Kind, "separator", StringComparison.Ordinal))
            {
                result.Add(new UtilitiesMenuNode { Kind = "separator" });
                normalized.SeparatorBefore = false;
            }
            result.Add(normalized);
        }
        return result;
    }

    private static UtilitiesMenuNode? NormalizeNode(UtilitiesMenuNode? node)
    {
        if (node is null)
            return null;
        node.Kind ??= "item";
        node.Children ??= new List<UtilitiesMenuNode>();
        node.CustomTitle ??= string.Empty;
        node.IconKey ??= string.Empty;
        node.IconColor ??= string.Empty;
        node.ExternalIconPath ??= string.Empty;

        if (string.Equals(node.Kind, "group", StringComparison.Ordinal))
        {
            node.Children = NormalizeList(node.Children);
            return node;
        }

        // Разделитель (issue #374): самостоятельный узел без ItemId/GroupId —
        // возвращаем как есть, НЕ трактуя как пункт каталога.
        if (string.Equals(node.Kind, "separator", StringComparison.Ordinal))
            return node;

        // Issue #146 (T8): заглушка ПРО-функции (публичная сборка) — такой же
        // участник компоновки, как обычный пункт: сохраняем её видимость и позицию.
        if (string.Equals(node.Kind, "placeholder", StringComparison.Ordinal))
        {
            node.ItemId ??= string.Empty;
            return Enum.TryParse<ProFeature>(node.ItemId, out _) ? node : null;
        }

        // Пункт: должен ссылаться на существующий пункт каталога.
        node.ItemId ??= string.Empty;
        if (UtilitiesMenuCatalog.Find(node.ItemId) is null)
            return null;
        return node;
    }
}