namespace Workspace1C.Services.Secrets;

/// <summary>
/// Сеансовый пароль учётной записи (issue #163, план 1.0.1.120, T2).
///
/// Пароль (и производный от него ключ) существует ТОЛЬКО в памяти процесса:
/// <list type="bullet">
/// <item>устанавливается после успешной проверки пароля профиля
/// (<see cref="Services.ProfileService.VerifyPassword"/>);</item>
/// <item>очищается при смене учётной записи, блокировке приложения и выходе;</item>
/// <item>никогда не журналируется и не сериализуется.</item>
/// </list>
/// Статический класс-синглтон: сервисы хранилищ создаются контейнером DI и не обязаны
/// знать друг о друге, но должны видеть один и тот же сеансовый ключ.
/// </summary>
public static class PasswordSession
{
    private static readonly object Sync = new();
    private static string? _password;
    private static string? _profileId;

    /// <summary>Идентификатор профиля, которому принадлежит сеансовый пароль (null — ключа нет).</summary>
    public static string? ProfileId
    {
        get
        {
            lock (Sync)
                return _profileId;
        }
    }

    /// <summary>true, если в сеансе есть пароль учётной записи.</summary>
    public static bool HasKey
    {
        get
        {
            lock (Sync)
                return !string.IsNullOrEmpty(_password);
        }
    }

    /// <summary>
    /// Запоминает пароль учётной записи на время сеанса. Пустой пароль очищает сессию.
    /// </summary>
    public static void SetFromPassword(string? profileId, string? password)
    {
        lock (Sync)
        {
            if (string.IsNullOrEmpty(password))
            {
                _password = null;
                _profileId = null;
                return;
            }

            _password = password;
            _profileId = profileId;
        }
    }

    /// <summary>Возвращает сеансовый пароль (только для внутреннего использования сервисами шифрования).</summary>
    public static bool TryGetPassword(out string password)
    {
        lock (Sync)
        {
            if (string.IsNullOrEmpty(_password))
            {
                password = string.Empty;
                return false;
            }

            password = _password!;
            return true;
        }
    }

    /// <summary>Очищает сеансовый ключ (выход, блокировка, смена учётной записи).</summary>
    public static void Clear()
    {
        lock (Sync)
        {
            _password = null;
            _profileId = null;
        }
    }
}
