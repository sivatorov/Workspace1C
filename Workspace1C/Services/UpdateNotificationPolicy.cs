namespace Workspace1C.Services;

/// <summary>
/// Источник проверки обновлений (issue #116, комментарий 7OH от 17.11.2026 14:04).
/// </summary>
public enum UpdateNotificationOrigin
{
    /// <summary>Фоновая проверка (при запуске или по таймеру).</summary>
    Background,

    /// <summary>Действие пользователя: ручная проверка, значок в подвале, диалог.</summary>
    UserAction
}

/// <summary>
/// Правило показа системного уведомления «Доступна новая версия» (issue #116).
/// Уведомление допустимо только на фоновых путях: если пользователь сам нажал
/// «Проверить обновления», кликнул по значку обновления в подвале или открыл диалог,
/// он уже знает о новой версии и уведомление лишнее. Пока действие пользователя
/// выполняется, фоновая проверка тоже не должна показывать уведомление — иначе
/// оно «догоняет» пользователя после ручного обновления.
/// </summary>
public static class UpdateNotificationPolicy
{
    /// <summary>
    /// Нужно ли показывать системное уведомление о доступной версии.
    /// </summary>
    /// <param name="origin">Источник проверки, нашедшей релиз.</param>
    /// <param name="userActionInProgress">True — сейчас выполняется действие
    /// пользователя (ручная проверка/диалог), уведомление подавляется.</param>
    public static bool ShouldNotify(UpdateNotificationOrigin origin, bool userActionInProgress)
        => origin == UpdateNotificationOrigin.Background && !userActionInProgress;
}
