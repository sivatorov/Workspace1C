using System.Threading.Tasks;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Сервис администрирования информационных баз (Этап 6 дорожной карты):
/// проверка целостности файловой ИБ через <c>chdbfl</c> и запуск консоли
/// администрирования серверов 1С для клиент-серверных баз.
/// Чистый сервис — без UI-зависимостей; исполняемые файлы ищутся в каталоге
/// установленной платформы 1С рядом с <c>1cv8</c>/<c>1cv8.exe</c>.
/// </summary>
public interface IInfobaseAdminService
{
    /// <summary>
    /// Быстрый запуск проверки целостности файловой ИБ (<c>chdbfl</c>).
    /// Доступно только для файловых баз: вызывается с путём к файлу базы
    /// (<c><путь>\1Cv8.1CD</c>). Возвращает true, если процесс запущен.
    /// </summary>
    bool CheckIntegrity(Infobase infobase);

    /// <summary>
    /// Тихий запуск проверки целостности файловой ИБ (<c>chdbfl</c>) с ожиданием завершения
    /// (0.3.9.87): процесс запускается без окна, метод дожидается его выхода и возвращает
    /// результат (успех или ошибка с кодом выхода). Используется планировщиком заданий.
    /// </summary>
    Task<BackupRunResult> CheckIntegrityQuiet(Infobase infobase);

    /// <summary>
    /// Запуск внешней консоли администрирования серверов 1С (оснастка MMC
    /// <c>1CV8Servers.msc</c>) выбранной версии платформы.
    /// Консоль не привязана к конкретной базе (issue #295): параметр опционален
    /// и используется только как источник настроек платформы; при null берётся
    /// новейшая установленная платформа с оснасткой.
    /// Возвращает результат с причиной отказа (issue #135), а не «молчаливый» bool.
    /// </summary>
    /// <param name="infobase">База-источник настроек платформы (может быть null).</param>
    /// <param name="platformVersion">Выбранный вариант версии («8.3.27.1688 (64)») или null.</param>
    ServerConsoleResult OpenServerAdminConsole(Infobase? infobase, string? platformVersion = null);

    /// <summary>
    /// Установленные платформы, в каталоге <c>bin</c> которых есть оснастка консоли
    /// администрирования серверов (<c>1CV8Servers.msc</c> и альтернативные имена,
    /// issue #135), новейшие первыми. На Linux внешней консоли нет — пустой список.
    /// </summary>
    IReadOnlyList<ServerConsoleCandidate> GetServerConsoleCandidates();

    /// <summary>
    /// Все установленные платформы со статусом оснастки консоли администрирования
    /// серверов (issue #135). Диалог выбора версии показывается, когда платформ
    /// больше одной, и отображает статус по каждой версии — даже если оснастка не
    /// найдена ни в одной из них. На Linux внешней консоли нет — пустой список.
    /// </summary>
    IReadOnlyList<ServerConsolePlatformOption> GetServerConsolePlatformOptions();

    /// <summary>
    /// Регистрирует компоненты внешней консоли (<c>radmin.dll</c>, <c>comctrl.dll</c>)
    /// для указанной версии платформы через regsvr32 с повышением прав (только Windows).
    /// Отсутствующий файл и отмена UAC попадают в отчёт, исключение не бросается.
    /// </summary>
    /// <param name="platformVersion">Вариант версии («8.3.27.1688 (64)») или null — новейшая с оснасткой.</param>
    ServerConsoleRegistrationResult RegisterServerConsoleComponents(string? platformVersion);
}