using System;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Управление учётными записями (профилями) приложения.
///
/// Отвечает за реестр профилей (<c>profiles.json</c>), хэширование и проверку паролей,
/// пер-профильные каталоги данных и выбор активного профиля. Является общей
/// (кроссплатформенной) абстракцией для Windows/WPF и Linux/Avalonia.
/// </summary>
public interface IProfileService
{
    /// <summary>Полный список профилей (учётных записей) в порядке их создания.</summary>
    IReadOnlyList<UserProfile> Profiles { get; }

    /// <summary>Активный (выбранный на текущую сессию) профиль.</summary>
    UserProfile? CurrentProfile { get; }

    /// <summary>
    /// Каталог данных активного профиля (<c>profiles/<Id>/</c>), в котором хранятся
    /// <c>settings.json</c>, <c>infobases.json</c> и <c>groups.json</c>. Если активный профиль
    /// не выбран — возвращается общий каталог данных приложения (легаси-режим).
    /// </summary>
    string CurrentProfileDataDirectory { get; }

    /// <summary>
    /// Инициализирует сервис: загружает реестр профилей и, при первом запуске,
    /// мигрирует существующие данные из корня каталога данных в профиль по умолчанию.
    /// Должен вызываться один раз до первого обращения к профилям.
    /// </summary>
    void EnsureInitialized();

    /// <summary>Создаёт новый профиль. Имя не должно быть пустым и должно быть уникальным.</summary>
    /// <param name="name">Имя учётной записи.</param>
    /// <param name="password">Необязательный пароль (null или пустая строка — без пароля).</param>
    UserProfile CreateProfile(string name, string? password = null);

    /// <summary>
    /// Переименовывает профиль. Имя не должно быть пустым и должно быть уникальным.
    /// </summary>
    void RenameProfile(string id, string newName);

    /// <summary>
    /// Удаляет профиль вместе с его каталогом данных. Нельзя удалить последний профиль.
    /// </summary>
    /// <returns>True, если профиль удалён.</returns>
    bool DeleteProfile(string id);

    /// <summary>Задаёт или снимает пароль профиля (null/пустая строка — снять пароль).</summary>
    void SetPassword(string id, string? password);

    /// <summary>Проверяет пароль профиля.</summary>
    bool VerifyPassword(string id, string password);

    /// <summary>
    /// true, если активная учётная запись шифрует пароли хранилищ своим паролем
    /// (флаг <see cref="UserProfile.EncryptPasswordsWithProfilePassword"/>, issue #163).
    /// </summary>
    bool IsPasswordEncryptionEnabled { get; }

    /// <summary>
    /// Включает/выключает шифрование паролей активной учётной записи. При включении
    /// существующие открытые значения в файлах профиля перешифровываются, при выключении —
    /// возвращаются в открытый вид. Требует пароль учётной записи в текущем сеансе
    /// (<see cref="PasswordSession"/>); при его отсутствии бросает понятную ошибку.
    /// </summary>
    void SetPasswordEncryptionEnabled(bool enabled);

    /// <summary>
    /// Проверяет пароль указанной записи и запоминает его в сеансе как ключ шифрования
    /// (issue #163). Используется сценариями, где шифрование ещё не включено, но пароль
    /// уже введён (включение флага в T3). Пароль остаётся только в памяти процесса.
    /// </summary>
    /// <returns>true, если пароль верен (или у записи нет пароля).</returns>
    bool TryUnlockPasswordSession(string id, string password);

    /// <summary>
    /// Очищает сеансовый пароль (ключ шифрования) и разблокировку приватных баз:
    /// вызывается при блокировке приложения, выходе и смене учётной записи.
    /// </summary>
    void ClearPasswordSession();

    /// <summary>
    /// Приватные базы (0.3.9.85) разблокированы в текущей сессии: пользователь
    /// ввёл пароль активного профиля (или вошёл с паролем через окно авторизации).
    /// Session-флаг — сбрасывается при смене профиля; при выходе теряется сам.
    /// </summary>
    bool IsPrivateBasesUnlocked { get; }

    /// <summary>
    /// true, если приватные базы должны показываться в списках: профиль без пароля
    /// (защищать нечем — приватность не действует) либо профиль с паролем уже
    /// разблокирован (<see cref="IsPrivateBasesUnlocked"/>).
    /// </summary>
    bool CanShowPrivateBases { get; }

    /// <summary>
    /// Разблокирует приватные базы верным паролем активного профиля. Для профиля
    /// без пароля флаг взводится без проверки (приватность не действует).
    /// </summary>
    /// <returns>True, если разблокировка выполнена (пароль верен или пароль не задан).</returns>
    bool UnlockPrivateBases(string password);

    /// <summary>
    /// Отмечает приватные базы разблокированными после успешного входа через
    /// <c>LoginWindow</c> с паролем профиля (авто-разблокировка при запуске/смене
    /// пользователя). Вызывается ПОСЛЕ <see cref="SetCurrentProfile"/>, который
    /// сбрасывает флаг для нового профиля.
    /// </summary>
    void MarkPrivateBasesUnlocked();

    /// <summary>Сбрасывает разблокировку приватных баз (при смене профиля).</summary>
    void ResetPrivateBasesUnlock();

    /// <summary>Делает профиль активным и запоминает его как использованный последним.</summary>
    void SetCurrentProfile(string id);

    /// <summary>
    /// Событие об изменении реестра учётных записей: создание, переименование или удаление
    /// профиля, смена пароля либо смена активной записи. Позволяет UI, зависящему от числа
    /// профилей (например, видимости кнопки «Смена пользователя»), обновляться без перезапуска.
    /// </summary>
    event EventHandler? ProfilesChanged;
}