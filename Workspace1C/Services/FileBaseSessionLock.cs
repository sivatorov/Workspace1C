using System;
using System.IO;
using System.Text;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Блокировка сеансов ФАЙЛОВОЙ информационной базы через файл <c>1cv8.cdn</c> (issue #20):
/// создание файла в каталоге базы блокирует сеансы, удаление — снимает блокировку.
/// Формат строки: <c>{1,ГГГГММДДЧЧММСС,ГГГГММДДЧЧММСС,"причина","код разрешения"}</c>.
/// Не требует запуска конфигуратора — работает для файловых ИБ, у которых
/// <see cref="ConnectionSettings.Type"/> == <see cref="ConnectionType.File"/>.
/// </summary>
public class FileBaseSessionLock
{
    /// <summary>Имя файла блокировки файловой ИБ (стандарт платформы 1С).</summary>
    public const string CdnFileName = "1cv8.cdn";

    /// <summary>Устанавливает блокировку: создаёт 1cv8.cdn в каталоге файловой базы.</summary>
    public SessionLockResult Lock(Infobase infobase, SessionLockOptions options)
    {
        if (infobase is null)
            return SessionLockResult.Fail(LocalizationManager.T("SessionLock.NoBase"));
        if (options is null)
            return SessionLockResult.Fail(LocalizationManager.T("SessionLock.Failed"));

        var directory = infobase.Connection?.FilePath;
        if (string.IsNullOrWhiteSpace(directory))
            return SessionLockResult.Fail(LocalizationManager.T("SessionLock.NoBasePath"));
        if (!Directory.Exists(directory))
            return SessionLockResult.Fail(string.Format(
                LocalizationManager.T("SessionLock.DirNotFoundFormat"), directory));

        try
        {
            var path = Path.Combine(directory, CdnFileName);
            // Файл 1cv8.cdn платформа читает в UTF-8 без BOM.
            File.WriteAllText(path, options.BuildCdnString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return SessionLockResult.Ok();
        }
        catch (Exception ex)
        {
            return SessionLockResult.Fail(string.Format(
                LocalizationManager.T("SessionLock.CdnWriteFailedFormat"), ex.Message));
        }
    }

    /// <summary>Снимает блокировку: удаляет 1cv8.cdn из каталога файловой базы.</summary>
    public SessionLockResult Unlock(Infobase infobase)
    {
        var result = UnlockCore(infobase);
        Audit.AuditHub.Write(Audit.AuditActions.SessionUnlock, infobase?.Name ?? string.Empty, result.Success);
        return result;
    }

    private static SessionLockResult UnlockCore(Infobase infobase)
    {
        if (infobase is null)
            return SessionLockResult.Fail(LocalizationManager.T("SessionLock.NoBase"));

        var directory = infobase.Connection?.FilePath;
        if (string.IsNullOrWhiteSpace(directory))
            return SessionLockResult.Fail(LocalizationManager.T("SessionLock.NoBasePath"));

        var path = Path.Combine(directory, CdnFileName);
        if (!File.Exists(path))
            return SessionLockResult.Ok(); // Блокировки нет — снимать нечего.

        try
        {
            File.Delete(path);
            return SessionLockResult.Ok();
        }
        catch (Exception ex)
        {
            return SessionLockResult.Fail(string.Format(
                LocalizationManager.T("SessionLock.CdnDeleteFailedFormat"), ex.Message));
        }
    }
}
