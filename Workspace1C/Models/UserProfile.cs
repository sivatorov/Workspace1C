namespace Workspace1C.Models;

/// <summary>
/// Учётная запись (профиль) приложения.
///
/// Каждый профиль имеет собственный каталог данных
/// (<c>profiles/<Id>/</c> внутри <see cref="Workspace1C.Services.PlatformPaths.AppDataDirectory"/>),
/// в котором хранятся свои <c>settings.json</c>, <c>infobases.json</c> и <c>groups.json</c>.
/// Поэтому у каждого пользователя свои настройки интерфейса и свой список баз и групп.
///
/// Пароль профиля (необязательный) хранится в виде хэша <see cref="PasswordHash"/>
/// (PBKDF2 с солью) — открытым текстом пароль нигде не сохраняется.
/// </summary>
public class UserProfile
{
    /// <summary>
    /// Уникальный идентификатор профиля (GUID). Используется как имя подкаталога данных
    /// профиля и как ключ реестра профилей в <c>profiles.json</c>.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Отображаемое имя учётной записи (например «Пользователь» или «Администратор»).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Хэш пароля профиля в формате «<c>iterations.saltBase64.hashBase64</c>».
    /// Пустая строка означает, что у профиля нет пароля (вход без авторизации).
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Признак того, что профиль защищён паролем.</summary>
    public bool HasPassword => !string.IsNullOrWhiteSpace(PasswordHash);

    /// <summary>
    /// Шифровать пароли хранилищ (баз, ИТС, СУБД, SMTP, обновлений, сценариев) паролем
    /// этой учётной записи (issue #163, план 1.0.1.120). Флаг хранится в
    /// <c>profiles.json</c>; сам пароль на диск не пишется — только его хэш
    /// (<see cref="PasswordHash"/>) и зашифрованные значения в файлах профиля.
    /// </summary>
    public bool EncryptPasswordsWithProfilePassword { get; set; }

    /// <summary>Дата и время создания профиля (UTC).</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}