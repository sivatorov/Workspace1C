using System.IO;
using System.Text;

namespace Workspace1C.Services;

/// <summary>
/// Журналы немодальных окон на диске (issue #79, v1.0.1.42): журнал окна
/// «Развернуть тестовую копию» раньше жил только в памяти UI и не попадал
/// в ZIP-диагностику (<see cref="DiagnosticsBundleService"/>) — поддержка видела
/// только общий app-лог, а в нём стека/шагов нет. Окна дублируют каждую строку
/// журнала через <see cref="Append"/> в <c>logs/windows/<windowKey>-YYYYMMDD.log</c>
/// (каталог данных приложения); при сборке диагностики файлы включаются в архив
/// под <c>logs/windows/…</c>. Переживает падение приложения. Пароли/строки
/// подключения в журналы окон не попадают (ошибки COM-скриптов приходят уже
/// замаскированными — <see cref="ComScriptRunner.MaskSecrets"/>).
/// Чистый .NET — обе платформы (WPF и Avalonia). Все сбои IO глотаются:
/// журнал не должен ломать UI.
/// </summary>
public static class WindowJournalService
{
    /// <summary>Лимит одного файла журнала: при превышении файл переименовывается
    /// в <c><name>-prev.log</c> (прежний <c>-prev</c> удаляется) — по образцу
    /// ротации app-логов.</summary>
    public const long MaxFileBytes = 5 * 1024 * 1024;

    private static readonly object Sync = new();

    /// <summary>Каталог журналов окон (внутри каталога журналов приложения).</summary>
    public static string JournalDirectory =>
        Path.Combine(PlatformPaths.LogDirectory, "windows");

    /// <summary>
    /// Дописывает строку в журнал окна <paramref name="windowKey"/> с префиксом
    /// времени <c>HH:mm:ss</c>. Создаёт каталог/файл при необходимости, ротирует
    /// по <see cref="MaxFileBytes"/>. Ни при каких сбоях не выбрасывает исключений.
    /// </summary>
    public static void Append(string windowKey, string? line)
    {
        if (string.IsNullOrWhiteSpace(windowKey) || line is null)
            return;

        try
        {
            lock (Sync)
            {
                var dir = JournalDirectory;
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"{SanitizeKey(windowKey)}-{DateTime.Now:yyyyMMdd}.log");

                // Ротация: текущий файл больше лимита — прежний -prev удаляется,
                // текущий переименовывается в -prev, строка пойдёт в новый файл.
                if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    if (info.Length > MaxFileBytes)
                    {
                        var prev = Path.Combine(dir, $"{SanitizeKey(windowKey)}-prev.log");
                        if (File.Exists(prev))
                            File.Delete(prev);
                        File.Move(path, prev);
                    }
                }

                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}", new UTF8Encoding(false));
            }
        }
        catch
        {
            // Журнал не должен ломать UI: сбой IO (диск/права/антивирус) глотается.
        }
    }

    /// <summary>
    /// Файлы журналов окон для ZIP-диагностики (сортировка по имени). Каталог
    /// отсутствует — пустой список. Сбои перечисления глотаются.
    /// </summary>
    public static IReadOnlyList<string> EnumerateJournalFiles()
    {
        try
        {
            var dir = JournalDirectory;
            return Directory.Exists(dir)
                ? Directory.EnumerateFiles(dir, "*.log").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
                : [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Безопасное имя файла из ключа окна: буквы/цифры/дефис, остальное — «-».</summary>
    private static string SanitizeKey(string key)
    {
        var sb = new StringBuilder(key.Length);
        foreach (var ch in key)
            sb.Append(char.IsLetterOrDigit(ch) || ch == '-' ? ch : '-');
        return sb.ToString();
    }
}
