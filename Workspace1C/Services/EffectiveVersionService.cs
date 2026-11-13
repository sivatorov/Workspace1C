using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace Workspace1C.Services;

/// <summary>
/// Эффективная версия приложения (issue #129 «Считать версию прошлой»): при включённом
/// диагностическом флаге <see cref="TraceFlags.PrevVersionFlag"/> (<c>CM_PREV_VERSION</c>
/// в <c>trace.json</c>) приложение виртуально считает себя ПРЕДЫДУЩЕЙ версией —
/// «Что нового» (<see cref="WhatsNewService.ShouldShow"/>/<see cref="WhatsNewService.MarkShown"/>
/// и окна WhatsNewWindow) показывает сводку прошлой версии, чтобы желающие могли
/// проверить тикет СРАЗУ, не дожидаясь следующего релиза.
/// <para>
/// Предыдущая версия определяется так: если программа запущена из git-репозитория и git
/// доступен — берётся реальный предыдущий git-тег (список <c>git tag --sort=-creatordate</code>,
/// выбирается наибольший тег СТРОГО меньше текущей версии; текущая версия может быть ещё
/// не тегнута — тогда предыдущей считается последний выпущенный тег). Если git недоступен
/// (упакованная сборка, git не установлен) — текущая версия с уменьшенной последней частью
/// на 1 («1.0.1.60» → «1.0.1.59»). При недоступности обоих источников возвращается текущая
/// версия без изменений — флаг не должен ломать приложение.
/// </para>
/// <para>
/// Флаг сохраняется в trace.json как остальные флаги вкладки «Логирование» (переживает
/// перезапуск) и автоматически СБРАСЫВАЕТСЯ после успешного обновления
/// (<see cref="ResetPrevVersionFlag"/> вызывается из UpdateService WPF/Avalonia рядом с
/// записью маркера pending_update.json).
/// </para>
/// </summary>
public static class EffectiveVersionService
{
    /// <summary>Таймаут ожидания завершения git-процесса (мс): диагностика не должна подвешивать старт.</summary>
    private const int GitTimeoutMs = 3000;

    /// <summary>Блокировка вокруг кэша git-запроса тегов (запрос выполняется один раз за сессию).</summary>
    private static readonly object Lock = new();

    /// <summary>Кэш вывода «git tag --sort=-creatordate» текущей сессии (null — git недоступен, тоже кэшируется).</summary>
    private static string? _cachedTagsOutput;
    private static bool _cacheFilled;

    /// <summary>
    /// Точка подмены git-запроса для юнит-тестов (возвращает вывод
    /// «git tag --sort=-creatordate» либо null — git недоступен). По умолчанию —
    /// реальный запуск git (<see cref="RunGitTagsQuietly"/>).
    /// </summary>
    internal static Func<string?> GitTagsProvider { get; set; } = RunGitTagsQuietly;

    /// <summary>Сброс кэша git-запроса (тесты): следующий <see cref="GetEffectiveVersion"/> перечитает теги.</summary>
    internal static void ResetGitCacheForTesting()
    {
        lock (Lock)
        {
            _cachedTagsOutput = null;
            _cacheFilled = false;
        }
    }

    /// <summary>
    /// Включён ли диагностический режим «Считать версию прошлой» (issue #129):
    /// флаг <see cref="TraceFlags.PrevVersionFlag"/> в <c>trace.json</c>. Читается
    /// через <see cref="TraceFlags.IsEnabled"/> — применяется без перезапуска.
    /// </summary>
    public static bool IsPrevVersionModeEnabled() => TraceFlags.IsEnabled(TraceFlags.PrevVersionFlag);

    /// <summary>
    /// Эффективная версия приложения (issue #129): при выключенном флаге — текущая
    /// (<paramref name="currentVersion"/>, обычно <see cref="WhatsNewService.CurrentVersion"/>);
    /// при включённом — предыдущая версия из git-тегов, а если git недоступен — текущая
    /// с уменьшенной последней частью на 1. Если ни один источник не дал предыдущей
    /// версии — возвращается текущая (флаг не должен ломать приложение).
    /// </summary>
    public static string GetEffectiveVersion(string currentVersion)
    {
        if (string.IsNullOrWhiteSpace(currentVersion))
            return currentVersion ?? string.Empty;
        if (!IsPrevVersionModeEnabled())
            return currentVersion;

        // 1) Реальный предыдущий git-тег (запущено из репозитория и git доступен).
        var previous = GetPreviousVersionFromGit(currentVersion);
        if (previous is not null)
            return previous;

        // 2) Fallback: текущая версия с уменьшенной последней частью на 1.
        return DecrementLastPart(currentVersion) ?? currentVersion;
    }

    /// <summary>
    /// Предыдущая версия из git-тегов: запускает <c>git tag --sort=-creatordate</c>
    /// в каталоге приложения (git сам находит корень репозитория переходом вверх),
    /// выбирает наибольший тег СТРОГО меньше <paramref name="currentVersion"/>.
    /// Результат кэшируется на сессию (в т.ч. «git недоступен»). Ошибки глотаются —
    /// диагностика не должна влиять на работу приложения.
    /// </summary>
    internal static string? GetPreviousVersionFromGit(string currentVersion)
    {
        string? tagsOutput;
        lock (Lock)
        {
            if (!_cacheFilled)
            {
                _cachedTagsOutput = RunGitTagsProviderQuietly();
                _cacheFilled = true;
            }
            tagsOutput = _cachedTagsOutput;
        }

        return string.IsNullOrEmpty(tagsOutput)
            ? null
            : PickPreviousFromTags(tagsOutput, currentVersion);
    }

    private static string? RunGitTagsProviderQuietly()
    {
        try
        {
            return GitTagsProvider?.Invoke();
        }
        catch
        {
            // Провайдер (в том числе тестовый) не должен ронять диагностику.
            return null;
        }
    }

    /// <summary>
    /// Реальный git-запрос (умолчание <see cref="GitTagsProvider"/>): вывод
    /// <c>git tag --sort=-creatordate</c> или null при любой ошибке (git не установлен,
    /// запущено не из репозитория, таймаут). Рабочий каталог — каталог приложения
    /// (<see cref="AppContext.BaseDirectory"/>).
    /// </summary>
    private static string? RunGitTagsQuietly()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "tag --sort=-creatordate",
                    WorkingDirectory = AppContext.BaseDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = false
            };
            if (!process.Start())
                return null;
            var output = process.StandardOutput.ReadToEnd();
            // Таймаут: git может зависнуть (например на сетевом диске) — убиваем и считаем недоступным.
            if (!process.WaitForExit(GitTimeoutMs))
            {
                try { process.Kill(); } catch { /* процесс мог завершиться сам */ }
                return null;
            }
            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            // git не найден / нет прав — считаем недоступным, сработает fallback.
            return null;
        }
    }

    /// <summary>
    /// Чистая функция выбора предыдущего тега (для юнит-тестов): из вывода
    /// <c>git tag --sort=-creatordate</c> (по тегу на строку, форматы «v1.0.1.58» и
    /// «1.0.1.58») выбирает наибольший тег СТРОГО меньше <paramref name="currentVersion"/>
    /// и возвращает его ВЕРСИЮ БЕЗ префикса «v» (в «Что нового» показывается номер
    /// версии, а не имя тега). Теги, не похожие на 4-частную версию, игнорируются.
    /// null — подходящего тега нет.
    /// </summary>
    internal static string? PickPreviousFromTags(string? tagsOutput, string currentVersion)
    {
        var current = ParseVersion(currentVersion);
        if (current is null || string.IsNullOrEmpty(tagsOutput))
            return null;

        string? bestVersion = null;
        Version best = null!;
        foreach (var rawLine in tagsOutput.Split('\n'))
        {
            var tag = rawLine.Trim();
            if (tag.Length == 0)
                continue;
            // Теги репозитория пишутся с префиксом «v» (см. GetGithubReleaseNotesAsync).
            var versionText = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                ? tag.Substring(1)
                : tag;
            var candidate = ParseVersion(versionText);
            if (candidate is null || candidate.CompareTo(current) >= 0)
                continue; // не версия либо не «прошлая» (>= текущей)
            if (bestVersion is null || candidate.CompareTo(best) > 0)
            {
                best = candidate;
                bestVersion = versionText;
            }
        }
        return bestVersion;
    }

    /// <summary>
    /// Чистая функция снижения версии (для юнит-тестов): последняя (ревизионная) часть
    /// уменьшается на 1 — «1.0.1.60» → «1.0.1.59». null — версия не 4-частная либо
    /// последняя часть уже 0 (снижать нечем; вызывающий оставит текущую версию).
    /// </summary>
    internal static string? DecrementLastPart(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;
        var parts = version.Trim().Split('.');
        if (parts.Length != 4)
            return null;
        // Все части должны быть числами — иначе это не версия (суффиксы и прочее).
        var numbers = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return null;
        }
        if (numbers[3] <= 0)
            return null;
        numbers[3]--;
        return numbers[0] + "." + numbers[1] + "." + numbers[2] + "." + numbers[3];
    }

    /// <summary>Разбор версии «1.2.3.4» (без префиксов/суффиксов); null — не версия.</summary>
    private static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try
        {
            var candidate = new Version(text.Trim());
            return candidate.Build >= 0 && candidate.Revision >= 0 ? candidate : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Сбрасывает флаг «Считать версию прошлой» в <c>trace.json</c> (issue #129).
    /// Вызывается из UpdateService (WPF и Avalonia) ПОСЛЕ успешного применения обновления
    /// (рядом с записью маркера pending_update.json): новая версия, запустившись после
    /// перезапуска, снова считает себя реальной текущей. Остальные флаги конфига
    /// сохраняются (<see cref="TraceFlags.SaveCategorySettings"/> мержит значения).
    /// Ошибки (недоступный trace.json) глотаются — сброс не должен срывать обновление.
    /// </summary>
    public static void ResetPrevVersionFlag()
    {
        try
        {
            TraceFlags.SaveCategorySettings(new Dictionary<string, bool>
            {
                [TraceFlags.PrevVersionFlag] = false
            });
        }
        catch
        {
            // Лог-файл недоступен (нет прав и т.п.) — не падаем: обновление важнее флага.
        }
    }
}
