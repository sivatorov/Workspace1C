using System.IO;

namespace Workspace1C.Services.Secrets;

/// <summary>
/// Миграция существующих JSON-хранилищ профиля между открытым и зашифрованным видом
/// (issue #163, план 1.0.1.120, T2).
///
/// Обходит <c>*.json</c> в каталоге активного профиля и применяет
/// <see cref="SecretJsonProtector"/> к секретным полям. Файл перезаписывается атомарно
/// и только если содержимое реально изменилось — остальные файлы не трогаются.
/// Ошибка миграции не удаляет исходные данные: запись идёт через временный файл.
/// </summary>
public static class SecretStoreMigrator
{
    /// <summary>Зашифровать все секретные поля в JSON-файлах каталога.</summary>
    /// <returns>Число перезаписанных файлов.</returns>
    public static int EncryptAll(string directory, SecretJsonProtector protector) =>
        TransformAll(directory, protector, encrypt: true);

    /// <summary>Расшифровать все секретные поля в JSON-файлах каталога (возврат к открытому виду).</summary>
    /// <returns>Число перезаписанных файлов.</returns>
    public static int DecryptAll(string directory, SecretJsonProtector protector) =>
        TransformAll(directory, protector, encrypt: false);

    private static int TransformAll(string directory, SecretJsonProtector protector, bool encrypt)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return 0;

        var changed = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            // Служебные/временные файлы не трогаем.
            if (path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                continue;

            string original;
            try
            {
                original = File.ReadAllText(path);
            }
            catch
            {
                continue; // недоступный файл не должен ломать миграцию остальных
            }

            string transformed;
            try
            {
                transformed = encrypt ? protector.Protect(original) : protector.Unprotect(original);
            }
            catch
            {
                // Один проблемный файл не отменяет миграцию остальных; данные не теряются.
                continue;
            }

            if (string.Equals(original, transformed, StringComparison.Ordinal))
                continue;

            try
            {
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, transformed);
                File.Move(tmp, path, overwrite: true);
                changed++;
            }
            catch
            {
                // Не удалось перезаписать — исходный файл остался нетронутым.
            }
        }

        return changed;
    }
}
