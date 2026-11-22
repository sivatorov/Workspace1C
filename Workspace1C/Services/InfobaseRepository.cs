using System.IO;
using System.Text.Json;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Репозиторий для сохранения и загрузки настроек информационных баз в JSON-файл.
/// Файлы данных хранятся в каталоге активного профиля (<see cref="IProfileService.CurrentProfileDataDirectory"/>),
/// поэтому у каждой учётной записи свой список баз, групп и настроек.
/// </summary>
public class InfobaseRepository : IInfobaseRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Переносы строк и отступы, чтобы файлы infobases.json и groups.json было
        // удобно править вручную (issue #147). Чтение остаётся совместимым как с
        // компактным, так и с форматированным JSON — оба разбираются одинаково.
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        // Не экранировать кириллицу и прочие не-ASCII символы в \uXXXX-последовательности,
        // а записывать их читаемыми UTF-8 (issue #170). Encoder влияет только на запись;
        // при чтении старые файлы с \uXXXX по-прежнему корректно разбираются.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Настройки сериализации файла <c>settings.json</c>: с переносами строк и отступами,
    /// чтобы файл было удобно править вручную (issue #147). Чтение остаётся совместимым
    /// как с компактным, так и с форматированным JSON — оба разбираются одинаково.
    /// </summary>
    private static readonly JsonSerializerOptions SettingsJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        // Не экранировать кириллицу и прочие не-ASCII символы в \uXXXX-последовательности,
        // а записывать их читаемыми UTF-8 (issue #170). Влияет только на запись.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // NaN и ±Infinity (например, ширина колонки скрытого/незамеренного элемента = NaN)
        // сериализуем как именованные литералы, чтобы одно испорченное число не роняло
        // запись всего файла настроек (issue #188). На чтение JsonOptions (базы) не влияет.
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    /// <summary>
    /// Текущая версия схемы файла <c>settings.json</c>. Увеличивается при изменении модели
    /// настроек, несовместимом со старыми файлами. Файлы с большей версией схемы (созданные
    /// более новой версией приложения) не читаются: они откладываются в резервную копию,
    /// а приложение стартует с настройками по умолчанию.
    /// </summary>
    private const int ConfigSchemaVersion = 1;

    private readonly IProfileService? _profileService;
    private readonly string? _explicitDirectory;

    /// <summary>
    /// Создаёт репозиторий. Если передан явный <paramref name="directory"/> — файлы данных
    /// читаются/пишутся в нём (режим совместимости/тестов). Иначе используется каталог данных
    /// активного профиля (<see cref="IProfileService.CurrentProfileDataDirectory"/>), поэтому
    /// у каждой учётной записи свой список баз, групп и настроек. Если сервис профилей не задан —
    /// общий каталог данных приложения (легаси-режим).
    /// </summary>
    public InfobaseRepository(IProfileService? profileService = null, string? directory = null)
    {
        _profileService = profileService;
        _explicitDirectory = directory;
    }

    /// <summary>Каталог, в котором репозиторий хранит файлы данных.</summary>
    private string DataDirectory =>
        !string.IsNullOrEmpty(_explicitDirectory)
            ? _explicitDirectory!
            : (_profileService?.CurrentProfileDataDirectory ?? PlatformPaths.AppDataDirectory);

    private string InfobasesPath => Path.Combine(DataDirectory, "infobases.json");
    private string GroupsPath => Path.Combine(DataDirectory, "groups.json");
    private string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    /// <summary>
    /// Загружает список информационных баз из файла. Если файл отсутствует — возвращает пустой список.
    /// </summary>
    public List<Infobase> Load()
    {
        if (!File.Exists(InfobasesPath))
            return new List<Infobase>();
        try
        {
            var json = File.ReadAllText(InfobasesPath);
            // Отбрасываем пустые элементы, которые могли прийти из легаси/повреждённого файла
            // (иначе потребители спотыкались бы о null и старт «зависал») (issue #64).
            var infobases = (JsonSerializer.Deserialize<List<Infobase>>(json, JsonOptions) ?? new List<Infobase>())
                .Where(i => i is not null)
                .ToList();

            // issue #134 (1.0.1.80): у баз, импортированных из кластера (RacInfobaseMapper
            // создаёт их с Id = ""), идентификатора не было вовсе. Матчер процессов и план
            // подменю «Завершить процессы» такие базы пропускали — в отчёте оставались
            // только счётчики, а подменю для них не появлялось. Восстанавливаем Id так же,
            // как это давно делается для групп (NormalizeGroups), и сохраняем файл, чтобы
            // починка сохранялась между запусками.
            if (NormalizeInfobases(infobases))
            {
                try
                {
                    Save(infobases);
                }
                catch
                {
                    // Сохранение — вспомогательное: даже при отказе записи текущий сеанс
                    // работает с восстановленными идентификаторами.
                }
            }

            return infobases;
        }
        catch (Exception ex)
        {
            // При ошибке десериализации возвращаем пустой список, а повреждённый файл
            // откладываем в резервную копию, чтобы не спотыкаться о него на каждом старте.
            System.Diagnostics.Debug.WriteLine($"Ошибка загрузки баз: {ex.Message}");
            QuarantineCorruptFile(InfobasesPath, "corrupt");
            return new List<Infobase>();
        }
    }

    /// <summary>
    /// Сохраняет список информационных баз в файл.
    /// </summary>
    public void Save(List<Infobase> infobases)
    {
        WriteAtomic(InfobasesPath, JsonSerializer.Serialize(infobases, JsonOptions));
    }

    /// <summary>
    /// Загружает список групп из файла. Если файл отсутствует — возвращает пустой список.
    /// Повреждённые данные групп (пустые или дублирующиеся идентификаторы) автоматически
    /// восстанавливаются, чтобы иерархия «группа в группе» не терялась.
    /// </summary>
    public List<Group> LoadGroups()
    {
        if (!File.Exists(GroupsPath))
            return new List<Group>();
        try
        {
            var json = File.ReadAllText(GroupsPath);
            var groups = JsonSerializer.Deserialize<List<Group>>(json, JsonOptions) ?? new List<Group>();
            // Отбрасываем пустые элементы из легаси/повреждённого файла до проверок
            // идентификаторов, иначе обращение g.Id к null уронило бы загрузку (issue #64).
            groups.RemoveAll(g => g is null);
            var hadInvalidIds = groups.Any(g => string.IsNullOrWhiteSpace(g.Id));
            var hadDuplicateIds = groups.GroupBy(g => g.Id, StringComparer.OrdinalIgnoreCase)
                .Any(g => g.Count() > 1);
            var normalized = NormalizeGroups(groups);
            // Если при загрузке были исправлены идентификаторы, устранены дубликаты или
            // разорваны циклические ссылки — сразу сохраняем, чтобы иерархия групп
            // гарантированно восстановилась на диске и не ломала последующие запуски.
            if (hadInvalidIds || hadDuplicateIds || normalized)
            {
                SaveGroups(groups);
            }
            return groups;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка загрузки групп: {ex.Message}");
            QuarantineCorruptFile(GroupsPath, "corrupt");
            return new List<Group>();
        }
    }

    /// <summary>
    /// Восстанавливает корректность списка баз (issue #134, 1.0.1.80): генерирует
    /// отсутствующие идентификаторы и устраняет дубликаты, сохраняя уже корректные Id.
    /// Возвращает <c>true</c>, если список был изменён (тогда вызывающий сохраняет файл).
    /// <para>
    /// Без этого базы с пустым Id (например импортированные из кластера 1С) не участвовали
    /// в сопоставлении процессов и не попадали в сводку завершения процессов — автор issue
    /// видел только счётчики.
    /// </para>
    /// </summary>
    internal static bool NormalizeInfobases(List<Infobase> infobases)
    {
        var changed = false;
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var infobase in infobases)
        {
            if (infobase is null)
                continue;

            if (string.IsNullOrWhiteSpace(infobase.Id))
            {
                infobase.Id = Guid.NewGuid().ToString("D");
                changed = true;
            }

            // Дубликат Id ломает сопоставление «процесс → база»: оставляем первый,
            // остальным выдаём новый идентификатор.
            while (!usedIds.Add(infobase.Id))
            {
                infobase.Id = Guid.NewGuid().ToString("D");
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Восстанавливает корректность списка групп: генерирует недостающие идентификаторы,
    /// устраняет дубликаты и разрывает циклические ссылки на родителя (A→B→A), сохраняя
    /// корректные ссылки на родителя. Возвращает <c>true</c>, если список был изменён.
    /// </summary>
    private static bool NormalizeGroups(List<Group> groups)
    {
        var changed = false;

        // Идентификаторы, которые уже корректно используются группами.
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Первый проход: присваиваем идентификаторы группам с пустым Id.
        foreach (var group in groups)
        {
            if (string.IsNullOrWhiteSpace(group.Id))
            {
                group.Id = Guid.NewGuid().ToString();
                changed = true;
            }
        }

        // Второй проход: устраняем дубликаты идентификаторов.
        foreach (var group in groups)
        {
            if (usedIds.Contains(group.Id))
            {
                // Обнаружен дубликат Id — назначаем новый уникальный идентификатор
                // и переносим на него ссылки детей.
                var oldId = group.Id;
                var newId = Guid.NewGuid().ToString();
                while (usedIds.Contains(newId))
                {
                    newId = Guid.NewGuid().ToString();
                }
                group.Id = newId;
                changed = true;
                foreach (var child in groups)
                {
                    if (string.Equals(child.ParentId, oldId, StringComparison.OrdinalIgnoreCase))
                    {
                        child.ParentId = newId;
                    }
                }
            }
            usedIds.Add(group.Id);
        }

        // Третий проход: разрываем циклические ссылки на родителя. Циклическая цепочка
        // (A→B→A) в плоском списке приводит к бесконечной вложенности при построении дерева
        // и могла вызывать зависание/переполнение стека на повреждённых или легаси-файлах
        // (issue #64). Разрывается ровно та ссылка, которая замыкает цикл: для группы,
        // чей ParentId указывает на узел, уже присутствующий в цепочке предков, ссылка
        // очищается, и группа становится корневой — как это и делает построитель дерева.
        var idToGroup = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            idToGroup[group.Id] = group;
        }

        foreach (var group in groups)
        {
            if (string.IsNullOrEmpty(group.ParentId))
                continue;

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = group;
            while (current is not null && !string.IsNullOrEmpty(current.ParentId))
            {
                if (!visited.Add(current.Id))
                    break; // Уже прошли этот узел — цепочка замкнулась.

                if (!idToGroup.TryGetValue(current.ParentId, out var parent))
                    break; // Родитель не найден — это обычный корень.

                if (visited.Contains(parent.Id))
                {
                    // Переход к родителю замкнул бы цепочку в цикл — разрываем эту ссылку.
                    current.ParentId = string.Empty;
                    changed = true;
                    break;
                }

                current = parent;
            }
        }

        return changed;
    }

    /// <summary>
    /// Сохраняет список групп в файл.
    /// Перед записью список всегда нормализуется (см. <see cref="NormalizeGroups"/>):
    /// устраняются дубликаты Id, пустым Id назначаются новые, разрываются циклические
    /// ссылки на родителя. Это страховка от попадания на диск «второй» группы с тем же Id
    /// и битого ParentId (issue #280) из любого пути вызова — не только из загрузки.
    /// Если список уже корректен — идентификаторы групп не меняются. Нормализация мутирует
    /// переданный список in-place; поскольку вызовы передают те же объекты, что лежат в
    /// рабочей коллекции групп (WPF передаёт копию списка, но с теми же ссылками на Group),
    /// исправленное состояние остаётся и в памяти, и дерево групп после пересборки строится
    /// по согласованным данным.
    /// </summary>
    public void SaveGroups(List<Group> groups)
    {
        NormalizeGroups(groups);
        WriteAtomic(GroupsPath, JsonSerializer.Serialize(groups, JsonOptions));
    }

    /// <summary>
    /// Загружает настройки интерфейса из файла. Если файл отсутствует — возвращает настройки по умолчанию.
    /// </summary>
    public AppSettings LoadSettings()
    {
        if (!File.Exists(SettingsPath))
        {
#if DEBUG
            Console.Error.WriteLine("[l10n-debug] LoadSettings: file missing (" + SettingsPath + ")");
#endif
            return new AppSettings();
        }
        try
        {
            var json = File.ReadAllText(SettingsPath);
            // Читаем настройки теми же опциями, что и пишем (SettingsJsonOptions):
            // так литералы NaN/±Infinity из сохранённого файла корректно разбираются
            // обратно (issue #188). JsonOptions для баз остаётся без этого флага.
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, SettingsJsonOptions) ?? new AppSettings();

            // Восстанавливаем null-поля, которые могли прийти из легаси/повреждённого файла,
            // чтобы конструкторы не спотыкались о них при старте (issue #64).
            loaded.NormalizeForLoad();

            // Файл создан более новой версией приложения, чем текущая: его схема нам может
            // быть незнакома, безопасно прочитать его нельзя. Откладываем файл в резервную
            // копию и стартуем с чистыми настройками — вместо зависания на непонятных данных.
            if (loaded.SchemaVersion > ConfigSchemaVersion)
            {
                System.Diagnostics.Debug.WriteLine($"Схема настроек {loaded.SchemaVersion} новее поддерживаемой {ConfigSchemaVersion}: сброс к настройкам по умолчанию.");
                QuarantineCorruptFile(SettingsPath, "future-schema");
                return new AppSettings();
            }

            // Issue #146: если рядом лежит пользовательская компоновка меню «Утилиты»,
            // а флаг правки ещё ни разу не сохранялся (обновление с версии без него),
            // включаем правку автоматически и запоминаем это. После явного выключения
            // пользователем флаг уже стоит — значение false уважается.
            if (!loaded.UtilitiesMenuEditingEnabledInitialized)
            {
                loaded.UtilitiesMenuEditingEnabledInitialized = true;
                if (UtilitiesMenuLayoutStore.HasValidLayoutFile(Path.GetDirectoryName(SettingsPath)))
                    loaded.UtilitiesMenuEditingEnabled = true;
                SaveSettings(loaded);
            }

#if DEBUG
            Console.Error.WriteLine("[l10n-debug] LoadSettings: Language=" + loaded.Language + ", file=" + SettingsPath);
#endif
            return loaded;
        }
        catch (Exception ex)
        {
            // Повреждённый или несовместимый файл настроек: делаем резервную копию и стартуем
            // с настройками по умолчанию, чтобы приложение гарантированно открылось.
            System.Diagnostics.Debug.WriteLine($"Ошибка загрузки настроек: {ex.Message}");
            QuarantineCorruptFile(SettingsPath, "corrupt");
            return new AppSettings();
        }
    }

    /// <summary>
    /// Сохраняет настройки интерфейса в файл.
    /// <para>
    /// issue #136: ошибки записи (в том числе <c>UnauthorizedAccessException</c> при
    /// установке в <c>C:\Program Files</c>) не пробрасываются наружу — иначе глобальный
    /// обработчик показывал бы диалог «Ошибка интерфейса» на каждое сворачивание группы.
    /// Ошибка однократно пишется в журнал; изменения остаются в памяти текущего сеанса.
    /// </para>
    /// </summary>
    public void SaveSettings(AppSettings settings)
    {
#if DEBUG
        Console.Error.WriteLine("[l10n-debug] SaveSettings: Language=" + settings.Language + ", file=" + SettingsPath);
#endif
        settings.SchemaVersion = ConfigSchemaVersion;
        // Сериализация — ошибка данных, а не прав доступа: её не скрываем.
        var json = JsonSerializer.Serialize(settings, SettingsJsonOptions);
        try
        {
            WriteAtomic(SettingsPath, json);
        }
        catch (Exception ex)
        {
            // issue #136: сохранение настроек — вспомогательная операция для интерфейса.
            LogSettingsSaveFailureOnce(ex);
        }
    }


    public async Task SaveAsync(List<Infobase> infobases, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(infobases, JsonOptions);
        await WriteAtomicAsync(InfobasesPath, json, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveGroupsAsync(List<Group> groups, CancellationToken cancellationToken = default)
    {
        // Та же защитная нормализация, что и в SaveGroups (issue #280).
        NormalizeGroups(groups);
        var json = JsonSerializer.Serialize(groups, JsonOptions);
        await WriteAtomicAsync(GroupsPath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Асинхронно сохраняет настройки интерфейса. Ошибки записи, как и в
    /// <see cref="SaveSettings"/>, не пробрасываются наружу (issue #136).
    /// </summary>
    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        settings.SchemaVersion = ConfigSchemaVersion;
        var json = JsonSerializer.Serialize(settings, SettingsJsonOptions);
        try
        {
            await WriteAtomicAsync(SettingsPath, json, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // Отмена — не ошибка записи, её обрабатывает вызывающий код.
        }
        catch (Exception ex)
        {
            // issue #136: интерфейс не должен получать исключение на каждое действие.
            LogSettingsSaveFailureOnce(ex);
        }
    }

    /// <summary>
    /// Откладывает повреждённый или несовместимый конфигурационный файл в резервную копию,
    /// чтобы он не мешал запуску. Оригинал переименовывается в «<имя>.<причина>.<метка времени>.bak»
    /// в том же каталоге. Любые ошибки глушатся — карантин не должен блокировать запуск.
    /// </summary>
    private static void QuarantineCorruptFile(string path, string reason)
    {
        try
        {
            if (!File.Exists(path))
                return;

            var directory = Path.GetDirectoryName(path);
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            var stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var backupPath = Path.Combine(directory ?? "", $"{name}.{reason}.{stamp}{ext}.bak");
            File.Move(path, backupPath);
        }
        catch
        {
            // Игнорируем: карантин — вспомогательная операция и не должен ломать запуск.
        }
    }

    /// <summary>
    /// Тестовый переключатель: заставляет <see cref="WriteAtomic"/> и
    /// <see cref="WriteAtomicAsync"/> сразу использовать резервную запись,
    /// как будто <see cref="File.Replace"/>/<see cref="File.Move"/> не удались
    /// (issue #136). В обычной работе всегда <c>false</c>.
    /// </summary>
    internal static bool ForceFallbackWriteForTests { get; set; }

    /// <summary>
    /// Флаг «об ошибке сохранения настроек уже сообщили»: при недоступном каталоге
    /// данных не засоряем журнал одной и той же записью на каждое действие (issue #136).
    /// </summary>
    private static int _settingsSaveFailureLogged;

    /// <summary>
    /// Однократно сообщает об ошибке сохранения настроек интерфейса: в журнал приложения,
    /// а если журнал ещё не настроен — в отладочный вывод (issue #136). Не бросает.
    /// </summary>
    private static void LogSettingsSaveFailureOnce(Exception exception)
    {
        if (Interlocked.Exchange(ref _settingsSaveFailureLogged, 1) != 0)
            return;

        try
        {
            var logger = AppServices.TryGetService<IAppLogger>();
            if (logger is not null)
            {
                logger.Error(
                    "Не удалось сохранить настройки интерфейса: запись в каталог данных не выполнена. " +
                    "Изменения текущего сеанса остаются в памяти, диалог не показывается (issue #136).",
                    exception);
                return;
            }
        }
        catch
        {
            // Журнал недоступен — ниже используем отладочный вывод.
        }

        System.Diagnostics.Debug.WriteLine(
            $"Не удалось сохранить настройки интерфейса (issue #136): {exception}");
    }

    /// <summary>
    /// Атомарная запись: сначала во временный файл, затем замена целевого.
    /// Снижает риск повреждения данных при сбое/отключении питания.
    /// <para>
    /// issue #136: если замена невозможна из-за прав доступа (например, каталог
    /// данных защищён от записи), выполняется резервная запись — копирование
    /// временного файла поверх целевого, затем прямая запись содержимого.
    /// Исключение пробрасывается только если не удались все способы.
    /// </para>
    /// </summary>
    private static void WriteAtomic(string targetPath, string content)
    {
        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var tempPath = targetPath + ".tmp";
        try
        {
            File.WriteAllText(tempPath, content);

            if (!ForceFallbackWriteForTests)
            {
                try
                {
                    // File.Replace надёжнее на Windows при существующем файле; иначе Move.
                    if (File.Exists(targetPath))
                        File.Replace(tempPath, targetPath, null);
                    else
                        File.Move(tempPath, targetPath);
                    return;
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    // issue #136: переходим к резервной записи ниже.
                    System.Diagnostics.Debug.WriteLine(
                        $"Атомарная запись «{targetPath}» не удалась ({ex.GetType().Name}): {ex.Message}. " +
                        "Используется резервная запись.");
                }
            }

            WriteFallback(tempPath, targetPath, content);
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
    }

    /// <summary>
    /// Асинхронная атомарная запись через временный файл (issue #136 — с резервной записью).
    /// </summary>
    private static async Task WriteAtomicAsync(string targetPath, string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var tempPath = targetPath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, content, cancellationToken).ConfigureAwait(false);

            if (!ForceFallbackWriteForTests)
            {
                try
                {
                    if (File.Exists(targetPath))
                        File.Replace(tempPath, targetPath, null);
                    else
                        File.Move(tempPath, targetPath);
                    return;
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Атомарная запись «{targetPath}» не удалась ({ex.GetType().Name}): {ex.Message}. " +
                        "Используется резервная запись.");
                }
            }

            await WriteFallbackAsync(tempPath, targetPath, content, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
    }

    /// <summary>
    /// Резервная запись при недоступности <see cref="File.Replace"/>/<see cref="File.Move"/>
    /// (issue #136): копирование временного файла поверх целевого, затем прямая запись
    /// содержимого. Исключение — только если не удались оба способа.
    /// </summary>
    private static void WriteFallback(string tempPath, string targetPath, string content)
    {
        try
        {
            File.Copy(tempPath, targetPath, overwrite: true);
            return;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Копирование «{tempPath}» → «{targetPath}» не удалось ({ex.GetType().Name}): {ex.Message}. " +
                "Используется прямая запись.");
        }

        try
        {
            File.WriteAllText(targetPath, content);
        }
        catch (Exception ex)
        {
            throw new IOException(
                $"Не удалось сохранить файл «{targetPath}»: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Асинхронный вариант резервной записи (issue #136).
    /// </summary>
    private static async Task WriteFallbackAsync(
        string tempPath, string targetPath, string content, CancellationToken cancellationToken)
    {
        try
        {
            File.Copy(tempPath, targetPath, overwrite: true);
            return;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Копирование «{tempPath}» → «{targetPath}» не удалось ({ex.GetType().Name}): {ex.Message}. " +
                "Используется прямая запись.");
        }

        try
        {
            await File.WriteAllTextAsync(targetPath, content, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new IOException(
                $"Не удалось сохранить файл «{targetPath}»: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Удаляет временный файл после записи; ошибки игнорируются (issue #136).
    /// </summary>
    private static void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch
        {
            // Временный файл не должен мешать сохранению данных.
        }
    }
}