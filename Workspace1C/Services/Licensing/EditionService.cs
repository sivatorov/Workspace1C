using System;
using System.IO;

namespace Workspace1C.Services.Licensing;

/// <summary>
/// Текущая редакция программы: бесплатная или ПРО (docs/pro/PRO-EDITION.md).
/// Ключ хранится в <c>license.key</c> в каталоге данных программы (не профиля:
/// лицензия привязана к компьютеру, а не к пользователю программы) и не входит
/// в синхронизацию настроек.
/// </summary>
public static class EditionService
{
    /// <summary>Имя файла ключа в каталоге данных программы.</summary>
    public const string LicenseFileName = "license.key";

    /// <summary>Переопределение каталога хранения ключа (тесты).</summary>
    public static string? DirectoryOverride { get; set; }

    private static readonly object Sync = new();
    private static LicenseCheckResult? _current;

    /// <summary>Лицензия сменилась (активация/удаление ключа). Вызывается в потоке активации.</summary>
    public static event EventHandler? Changed;

    /// <summary>
    /// В сборку встроены модули ПРО из приватного репозитория (символ PRO_MODULES).
    /// Без них новые функции 2.x отсутствуют даже при действительном ключе.
    /// </summary>
    public static bool HasProModules =>
#if PRO_MODULES
        true;
#else
        false;
#endif

    /// <summary>Путь к файлу ключа.</summary>
    public static string LicenseFilePath =>
        Path.Combine(DirectoryOverride ?? PlatformPaths.AppDataDirectory, LicenseFileName);

    /// <summary>Результат проверки сохранённого ключа (кэшируется до активации/сброса).</summary>
    public static LicenseCheckResult Current
    {
        get
        {
            lock (Sync)
            {
                return _current ??= LicenseVerifier.Verify(ReadStoredKey());
            }
        }
    }

    /// <summary>Активна редакция ПРО.</summary>
    public static bool IsPro => Current.IsValid;

    /// <summary>
    /// Доступна ли функция ПРО: нужен действительный ключ и сам код функции в сборке
    /// (функции 1.0.x есть в публичном коде, новые 2.x — только в официальной сборке).
    /// </summary>
    public static bool Has(ProFeature feature) =>
        IsPro && (ProFeatures.IsInPublicCore(feature) || HasProModules);

    /// <summary>
    /// Проверяет и сохраняет ключ. Недействительный ключ не сохраняется, текущая
    /// лицензия при этом не меняется.
    /// </summary>
    public static LicenseCheckResult Activate(string? keyText)
    {
        var result = LicenseVerifier.Verify(keyText);
        if (!result.IsValid || result.Key is null)
            return result;

        var path = LicenseFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, result.Key.Text);
        lock (Sync)
        {
            _current = result;
        }
        Changed?.Invoke(null, EventArgs.Empty);
        return result;
    }

    /// <summary>Удаляет ключ с этого компьютера (перенос лицензии).</summary>
    public static void Deactivate()
    {
        try
        {
            if (File.Exists(LicenseFilePath))
                File.Delete(LicenseFilePath);
        }
        catch
        {
            // Нет прав на удаление — ключ просто перестанет считываться после сброса кэша.
        }
        Reset();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Сбрасывает кэш проверки (тесты, смена каталога данных).</summary>
    public static void Reset()
    {
        lock (Sync)
        {
            _current = null;
        }
    }

    private static string? ReadStoredKey()
    {
        try
        {
            var path = LicenseFilePath;
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Шлюз ПРО-функций для точек входа (пункты меню, команды, фоновые службы).
/// Проверка без UI — <see cref="EditionService.Has"/>; при отказе <see cref="Ensure"/>
/// показывает окно «Лицензия» через делегат, который регистрирует UI-слой (WPF/Avalonia).
/// </summary>
public static class ProGate
{
    /// <summary>
    /// Показ окна «Лицензия» с подсветкой функции. Регистрируется при старте UI;
    /// в CLI не задан — тогда <see cref="Ensure"/> просто возвращает false.
    /// </summary>
    public static Action<ProFeature?>? ShowLicenseDialog { get; set; }

    /// <summary>
    /// true — функция доступна, можно продолжать. false — функция ПРО недоступна:
    /// пользователю показано окно «Лицензия», вызывающий код должен прервать действие.
    /// </summary>
    public static bool Ensure(ProFeature feature)
    {
        if (EditionService.Has(feature))
            return true;
        try
        {
            ShowLicenseDialog?.Invoke(feature);
        }
        catch
        {
            // Окно не открылось — отказ всё равно остаётся отказом.
        }
        return false;
    }
}
