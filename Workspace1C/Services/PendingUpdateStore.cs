using System;
using System.IO;
using System.Text.Json;

namespace Workspace1C.Services;

/// <summary>
/// Хранилище маркера «запланировано уведомление об успешном обновлении» (issue #10):
/// один JSON-файл <c>pending_update.json</c> в каталоге данных активного профиля
/// (рядом с <c>settings.json</c>), по образцу <see cref="ServerListStore"/>.
/// </summary>
/// <remarks>
/// Зачем нужен маркер: задание по расписанию «Обновить приложение» скачивает новый
/// исполняемый файл, запускает помощник и СРАЗУ завершает приложение — уведомление
/// о завершении задания из старого процесса не успевает уйти в Telegram (процесс
/// умирает раньше, чем завершится HTTP-запрос). Поэтому перед перезапуском пишется
/// маркер с целевой версией, а уведомление «Приложение обновлено до версии X»
/// отправляется ПОСЛЕ перезапуска, из новой версии приложения (см. стартовый
/// хук в App.xaml.cs / App.axaml.cs). Маркер очищается в любом случае; уведомление
/// отправляется только если версия маркера совпадает с фактически запущенной
/// (иначе помощник не смог заменить исполняемый файл — уведомление было бы ложным).
/// </remarks>
public sealed class PendingUpdateStore
{
    /// <summary>Имя файла маркера (рядом с настройками приложения).</summary>
    public const string FileName = "pending_update.json";

    private readonly IProfileService? _profileService;
    private readonly string? _directoryOverride;

    /// <param name="profileService">Профиль для определения каталога данных (может быть null).</param>
    /// <param name="directoryOverride">Явный каталог хранения вместо каталога данных
    /// (используется в юнит-тестах для изоляции от реальных данных профиля).</param>
    public PendingUpdateStore(IProfileService? profileService = null, string? directoryOverride = null)
    {
        _profileService = profileService;
        _directoryOverride = directoryOverride;
    }

    /// <summary>
    /// Каталог данных: явный (тесты) → каталог активного профиля → общий каталог
    /// приложения (паттерн цикла 0.3.9.155, как у <see cref="ServerListStore"/>).
    /// </summary>
    private string DataDirectory =>
        !string.IsNullOrWhiteSpace(_directoryOverride)
            ? _directoryOverride!
            : (!string.IsNullOrWhiteSpace(_profileService?.CurrentProfileDataDirectory)
                ? _profileService!.CurrentProfileDataDirectory
                : PlatformPaths.AppDataDirectory);

    /// <summary>Полный путь к файлу маркера.</summary>
    public string FilePath => Path.Combine(DataDirectory, FileName);

    /// <summary>
    /// Записывает маркер с версией, до которой планируется обновление (обычно тег
    /// релиза вида «v1.0.0.9»). Вызывается ПЕРЕД завершением приложения для применения
    /// обновления. Предыдущий маркер перезаписывается. Ошибки записи не всплывают —
    /// маркер сервисный, его потеря не должна ронять обновление.
    /// </summary>
    /// <param name="version">Целевая версия (тег релиза).</param>
    /// <param name="releaseNotes">Описание изменений выпуска (issue #44): попадает в
    /// уведомление «Приложение обновлено до версии X» после перезапуска.</param>
    /// <param name="releaseUrl">Ссылка на страницу релиза.</param>
    /// <param name="notifyOnNextStart">Показывать ли уведомление «Приложение обновлено»
    /// после перезапуска (issue #116). Для обновлений, инициированных пользователем
    /// вручную, уведомление не нужно — пользователь и так знает, что нажал «Обновить».</param>
    public void Write(
        string version, string? releaseNotes = null, string? releaseUrl = null,
        bool notifyOnNextStart = true)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var json = JsonSerializer.Serialize(new PendingUpdateMarker
            {
                Version = version ?? string.Empty,
                ReleaseNotes = releaseNotes ?? string.Empty,
                ReleaseUrl = releaseUrl ?? string.Empty,
                NotifyOnNextStart = notifyOnNextStart
            });
            // Атомарная запись (как ServerListStore.Save): временный файл + замена.
            var tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch
        {
            // Маркер сервисный: если записать не удалось, обновление всё равно
            // применяется, просто уведомление после перезапуска не отправится.
        }
    }

    /// <summary>
    /// Читает маркер и УДАЛЯЕТ файл. Возвращает записанную версию или null,
    /// если маркера нет / файл повреждён. Вызывается при старте новой версии.
    /// Файл удаляется в любом случае, чтобы не копить устаревшие маркеры.
    /// </summary>
    public string? ReadAndClear() => ReadMarkerAndClear()?.Version;

    /// <summary>
    /// Читает маркер целиком (версия, описание изменений, ссылка — issue #44) и
    /// УДАЛЯЕТ файл. Возвращает null, если маркера нет, файл повреждён или версия
    /// пуста. Маркер, записанный прежними версиями (только «Version»), читается
    /// с пустым описанием.
    /// </summary>
    public PendingUpdateMarker? ReadMarkerAndClear()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;

            PendingUpdateMarker? marker = null;
            try
            {
                var json = File.ReadAllText(FilePath);
                marker = JsonSerializer.Deserialize<PendingUpdateMarker>(json);
            }
            catch
            {
                // Битый файл — считаем, что маркера не было.
            }
            finally
            {
                TryDelete(FilePath);
            }

            if (marker is null || string.IsNullOrWhiteSpace(marker.Version))
                return null;
            marker.ReleaseNotes ??= string.Empty;
            marker.ReleaseUrl ??= string.Empty;
            return marker;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Чистая функция решения: уведомлять ли об успешном обновлении. Уведомление
    /// отправляется ТОЛЬКО если версия из маркера совпадает с фактически запущенной
    /// версией приложения — иначе помощник не смог заменить исполняемый файл, и
    /// «успешное обновление» было бы ложным (issue #10).
    /// </summary>
    /// <param name="markerVersion">Версия из маркера (тег релиза, напр. «v1.0.0.9»).</param>
    /// <param name="currentVersion">Текущая версия приложения (<see cref="VersionInfo.Display()"/>).</param>
    public static bool ShouldNotify(string? markerVersion, string? currentVersion)
    {
        if (string.IsNullOrWhiteSpace(markerVersion) || string.IsNullOrWhiteSpace(currentVersion))
            return false;

        // Сравниваем без ведущей «v» у тега релиза: «v1.0.0.9» ≡ «1.0.0.9».
        return NormalizeTag(markerVersion) == NormalizeTag(currentVersion);
    }

    /// <summary>
    /// Чистая функция решения по маркеру (issue #116): уведомление показывается,
    /// только если обновление было фоновым (<see cref="PendingUpdateMarker.NotifyOnNextStart"/>)
    /// и версия маркера совпала с запущенной. Ручное обновление («Обновить»/«Установить»)
    /// уведомления после перезапуска не требует.
    /// </summary>
    public static bool ShouldNotifyForMarker(PendingUpdateMarker? marker, string? currentVersion)
        => marker is not null
           && marker.NotifyOnNextStart
           && ShouldNotify(marker.Version, currentVersion);

    private static string NormalizeTag(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? trimmed.Substring(1) : trimmed;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Не критично: устаревший маркер просто останется на диске.
        }
    }
}

/// <summary>Содержимое файла-маркера <c>pending_update.json</c>.</summary>
public sealed class PendingUpdateMarker
{
    /// <summary>Версия, до которой выполнено обновление (тег релиза).</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Описание изменений выпуска (issue #44); пусто у маркеров прежних версий.</summary>
    public string? ReleaseNotes { get; set; } = string.Empty;

    /// <summary>Ссылка на страницу релиза (issue #44).</summary>
    public string? ReleaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Показывать ли уведомление «Приложение обновлено до версии X» после перезапуска
    /// (issue #116). True — фоновое обновление (пользователь мог не знать о нём);
    /// False — пользователь сам нажал «Обновить»/«Установить». У маркеров прежних
    /// версий поле отсутствует и читается как true — прежнее поведение сохраняется.
    /// </summary>
    public bool NotifyOnNextStart { get; set; } = true;
}