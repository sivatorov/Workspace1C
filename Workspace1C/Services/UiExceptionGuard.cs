using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Workspace1C.Localization;

namespace Workspace1C.Services;

/// <summary>
/// Единая точка перехвата исключений в UI-обработчиках (issue #96). До 1.0.1.18 любое
/// необработанное исключение в конвейере клика по строкам списка баз (MouseDown дерева,
/// подписки Loaded/Unloaded строк, построение контекстного меню) всплывало в глобальный
/// обработчик Dispatcher и показывало пользователю голое окно «Ошибка интерфейса» без
/// контекста, а журнал не содержал ни имени обработчика, ни данных выделенной базы.
/// <para>
/// <see cref="Guard"/> выполняет тело обработчика; при исключении:
/// 1) пишет в штатный журнал (<see cref="IAppLogger"/>, файл logs/app-YYYYMMDD.log —
/// записи ERROR пишутся всегда, независимо от флагов трассировки) ПОЛНЫЙ стек, версию
/// приложения, ОС, имя обработчика и контекст (Id базы, модификаторы, ClickCount);
/// 2) дублирует краткую запись в errors.log каталога данных (унификация с Linux);
/// 3) показывает пользователю содержательный диалог: тип исключения, сообщение,
/// имя обработчика и путь к журналу — вместо «Ошибка интерфейса» без деталей;
/// 4) подавляет ПОВТОРНЫЕ диалоги той же ошибки в течение окна анти-спама
/// (<see cref="DialogRepeatWindowMs"/>), но каждый случай по-прежнему пишется в журнал.
/// </para>
/// Хелпер не привязан к WPF/Avalonia: диалог показывается через <see cref="IDialogService"/>.
/// Он не меняет e.Handled — вызывающий решает сам, событие уже помечено штатно. Все
/// побочные операции (журнал, errors.log, диалог) обёрнуты в try/catch: сбой диагностики
/// не должен маскировать исходную ошибку и ронять UI.
/// </summary>
public static class UiExceptionGuard
{
    /// <summary>Окно анти-спама диалогов: та же ошибка (тип + сообщение + обработчик)
    /// не показывается повторно в течение этого времени. Записи в журнал не глушатся.</summary>
    internal static readonly int DialogRepeatWindowMs = 10_000;

    // Точки подмены для юнит-тестов (сборке Workspace1C.Tests выдан InternalsVisibleTo).
    // По умолчанию null — используются штатные AppServices/Localization.
    internal static Func<IAppLogger?>? LoggerProvider;
    internal static Action<string, string>? DialogShower; // (message, title)
    internal static Func<long>? TickProvider;
    internal static Func<string?>? ErrorsLogPathProvider; // подмена пути для тестов

    private static readonly object Sync = new();
    private static long? _lastDialogTick;
    private static string? _lastDialogKey;

    /// <summary>
    /// Выполняет <paramref name="body"/>; при исключении — журнал + errors.log + диалог
    /// (с анти-спамом), исключение наружу НЕ пробрасывается. При штатном выполнении
    /// поведение идентично прямому вызову body() — обёртка «прозрачна» (риски #340/#97).
    /// </summary>
    /// <param name="handlerName">Имя UI-обработчика для журнала и диалога.</param>
    /// <param name="body">Тело обработчика.</param>
    /// <param name="context">Ленивый контекст («что делал пользователь»: Id базы,
    /// модификаторы, ClickCount). Вычисляется только при исключении.</param>
    public static void Guard(string handlerName, Action body, Func<string?>? context = null)
    {
        try
        {
            body();
        }
        catch (Exception ex)
        {
            string? ctx = null;
            try { ctx = context?.Invoke(); }
            catch { /* контекст вторичен — не должен маскировать исходную ошибку */ }
            Report(handlerName, ex, ctx);
        }
    }

    /// <summary>
    /// Полный цикл обработки перехваченного исключения UI-обработчика. Никогда не бросает.
    /// </summary>
    public static void Report(string handlerName, Exception ex, string? context = null)
    {
        if (ex is null)
            return;

        // 1) Штатный журнал: полный стек (без обрезки) + версия + ОС + обработчик + контекст.
        //    Каждое вхождение пишется — анти-спам глушит только диалоги.
        try
        {
            var logger = LoggerProvider is not null
                ? LoggerProvider()
                : AppServices.TryGetService<IAppLogger>();
            logger?.Error(BuildLogRecord(handlerName, ex, context));
        }
        catch
        {
            // Журнал недоступен (диск/права) — не маскируем исходную ошибку.
        }

        // 2) Дубликат в errors.log каталога данных — унификация с Avalonia-путём
        //    (ShowFatalError на Linux пишет только туда).
        try
        {
            AppendErrorsLog(handlerName, ex, context);
        }
        catch
        {
            // Файл недоступен — не критично.
        }

        // 3) Диалог с анти-спамом.
        try
        {
            ShowDialogThrottled(handlerName, ex, context);
        }
        catch
        {
            // Диалог не должен падать и ронять UI в полуразобранном состоянии.
        }
    }

    /// <summary>
    /// Показывает содержательный диалог ошибки, подавляя повторы той же ошибки в окне
    /// анти-спама (каждый случай при этом уже записан в журнал).
    /// </summary>
    private static void ShowDialogThrottled(string handlerName, Exception ex, string? context)
    {
        var key = handlerName + "|" + ex.GetType().FullName + "|" + ex.Message;
        var tick = TickProvider is not null ? TickProvider() : Environment.TickCount64;

        lock (Sync)
        {
            if (_lastDialogKey == key && _lastDialogTick is { } last &&
                tick - last >= 0 && tick - last < DialogRepeatWindowMs)
            {
                return; // повтор той же ошибки — диалог уже показан, журнал ведётся
            }
            _lastDialogKey = key;
            _lastDialogTick = tick;
        }

        var message = string.Join(Environment.NewLine, BuildDialogLines(ex, handlerName, context));
        var title = TOr("App.Fatal.Title", "Workspace 1C — ошибка");
        if (DialogShower is not null)
        {
            DialogShower(message, title);
        }
        else
        {
            AppServices.TryGetService<IDialogService>()?.ShowError(message, title);
        }
    }

    /// <summary>Строки содержательного диалога: тип, сообщение, обработчик, контекст,
    /// путь к журналу (чтобы пользователь мог приложить полный отчёт, issue #96).</summary>
    private static List<string> BuildDialogLines(Exception ex, string handlerName, string? context)
    {
        var lines = new List<string>
        {
            TOr("App.Fatal.GuardIntro",
                "Возникла ошибка при обработке действия в интерфейсе. Работа приложения продолжена."),
            string.Empty,
            ex.GetType().FullName ?? ex.GetType().Name,
            ex.Message,
            string.Format(
                TOr("App.Fatal.GuardHandler", "Обработчик: {0}"), handlerName)
        };
        if (!string.IsNullOrEmpty(context))
        {
            lines.Add(string.Format(
                TOr("App.Fatal.GuardContext", "Контекст: {0}"), context));
        }
        lines.Add(string.Empty);
        lines.Add(string.Format(
            TOr("App.Fatal.LogPath", "Полный отчёт (журнал приложения): {0}"),
            PlatformPaths.LogDirectory));
        return lines;
    }

    /// <summary>Полная запись в журнал: версия, ОС, обработчик, контекст, полный стек.</summary>
    private static string BuildLogRecord(string handlerName, Exception ex, string? context)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[UiGuard] Необработанное исключение в UI-обработчике");
        sb.AppendLine("Обработчик: " + handlerName);
        sb.AppendLine("Версия: " + VersionInfo.Display());
        sb.AppendLine("ОС: " + DescribeOs());
        if (!string.IsNullOrEmpty(context))
            sb.AppendLine("Контекст: " + context);
        sb.AppendLine("Исключение: " + ex.GetType().FullName);
        sb.AppendLine("Сообщение: " + ex.Message);
        // Полный стек без обрезки — главное требование владельца по issue #96.
        sb.AppendLine("StackTrace:");
        sb.AppendLine(ex.ToString());
        return sb.ToString().TrimEnd();
    }

    /// <summary>Краткий дубликат в errors.log каталога данных (как ShowFatalError Avalonia).</summary>
    private static void AppendErrorsLog(string handlerName, Exception ex, string? context)
    {
        var path = ErrorsLogPathProvider is not null
            ? ErrorsLogPathProvider()
            : Path.Combine(PlatformPaths.AppDataDirectory, "errors.log");
        if (string.IsNullOrEmpty(path))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [UiGuard] {handlerName}: " +
                   $"{ex.GetType().Name}: {ex.Message}" +
                   (string.IsNullOrEmpty(context) ? "" : $" | {context}") +
                   Environment.NewLine;
        File.AppendAllText(path, line);
    }

    private static string DescribeOs()
    {
        try
        {
            var os = RuntimeInformation.OSDescription;
            return $"{os} ({RuntimeInformation.ProcessArchitecture}, " +
                   $"{(Environment.Is64BitProcess ? "x64" : "x86")} процесс)";
        }
        catch
        {
            return Environment.OSVersion.VersionString;
        }
    }

    /// <summary>Возвращает перевод ключа, а при его отсутствии — встроенный запасной текст
    /// (локализация может быть ещё не загружена, см. issue #213).</summary>
    private static string TOr(string key, string fallback)
    {
        var text = LocalizationManager.T(key);
        return string.Equals(text, key, StringComparison.Ordinal) ? fallback : text;
    }

    /// <summary>Сброс состояния анти-спама и подмен для юнит-тестов.</summary>
    internal static void ResetForTests()
    {
        lock (Sync)
        {
            _lastDialogKey = null;
            _lastDialogTick = null;
        }
        LoggerProvider = null;
        DialogShower = null;
        TickProvider = null;
        ErrorsLogPathProvider = null;
    }
}
