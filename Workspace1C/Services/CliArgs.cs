using System;
using System.Linq;

namespace Workspace1C.Services;

/// <summary>Команда CLI, распознанная в аргументах командной строки (функция 10).</summary>
public enum CliCommandKind
{
    /// <summary>Аргументы не содержат команд CLI — обычный запуск приложения.</summary>
    None,

    /// <summary>Запустить базу (--run / --base).</summary>
    Run,

    /// <summary>Вывести список баз (--list).</summary>
    List,

    /// <summary>Добавить базу (--add, этап 0.3.9.219).</summary>
    Add,

    /// <summary>Резервная копия одной базы (--backup, этап 0.3.9.220).</summary>
    Backup,

    /// <summary>Резервная копия всех видимых баз (--backup-all, этап 0.3.9.220).</summary>
    BackupAll,

    /// <summary>Статус базы (--status, этап 0.3.9.221).</summary>
    Status,

    /// <summary>Экспорт списка баз в файл (--export, этап 0.3.9.222).</summary>
    Export,

    /// <summary>Справка по командам (--help, этап 0.3.9.218).</summary>
    Help,

    /// <summary>Dev-уведомление в Telegram (--telegram-send, выпуск 1.0.0.36).</summary>
    TelegramSend,

    /// <summary>Подтверждение через inline-кнопки Telegram (--telegram-confirm, выпуск 1.0.0.36).</summary>
    TelegramConfirm,

    /// <summary>MCP-сервер по stdio (--mcp, выпуск 1.0.0.88): JSON-RPC 2.0, инструменты для ИИ-агентов.</summary>
    Mcp,

    /// <summary>Проверка/применение обновления приложения (--update, выпуск 1.0.0.88).</summary>
    Update,

    /// <summary>Выборка из журнала регистрации с фильтрами (--eventlog, выпуск 1.0.0.88).</summary>
    Eventlog,

    /// <summary>Диагностика окружения/конфигурации (--check, выпуск 1.0.0.88).</summary>
    Check,

    /// <summary>Редакция, код компьютера и состояние лицензии ПРО (--license-info, 2.0).</summary>
    LicenseInfo,

    /// <summary>Активация ключа ПРО (--activate "W1C2.…", 2.0).</summary>
    Activate
}

/// <summary>Общие параметры любой команды CLI.</summary>
public sealed class CliOptions
{
    /// <summary>Машиночитаемый вывод (--json): результат печатается единым JSON-контрактом.</summary>
    public bool Json { get; set; }

    /// <summary>Профиль сессии (--profile <id>).</summary>
    public string? Profile { get; set; }

    /// <summary>Модификатор режима запуска (--designer) — только для --run.</summary>
    public bool Designer { get; set; }

    /// <summary>Текст dev-уведомления (--text; обязателен для --telegram-send).</summary>
    public string? Text { get; set; }

    /// <summary>Заголовок dev-уведомления (--title; по умолчанию «Workspace 1C»).</summary>
    public string? Title { get; set; }

    /// <summary>Вид dev-уведомления (--kind: info/success/warning/error).</summary>
    public string? Kind { get; set; }

    /// <summary>Вопрос подтверждения (--prompt; обязателен для --telegram-confirm).</summary>
    public string? Prompt { get; set; }

    /// <summary>Тексты кнопок подтверждения (--button, повторяемый): 1-я — «Да», 2-я — «Нет».</summary>
    public System.Collections.Generic.List<string> Buttons { get; } = new();

    /// <summary>Таймаут ожидания подтверждения, секунд (--timeout; по умолчанию 300).</summary>
    public int TimeoutSeconds { get; set; }

    // ---- Параметры новых команд CLI (п.6 «MCP-сервер и расширение CLI», выпуск 1.0.0.88) ----

    /// <summary>Применить обновление (--apply; с --update — скачать/подготовить новую версию).</summary>
    public bool Apply { get; set; }

    /// <summary>Начало периода выборки журнала (--from; --eventlog).</summary>
    public string? From { get; set; }

    /// <summary>Конец периода выборки журнала (--to; --eventlog).</summary>
    public string? To { get; set; }

    /// <summary>Подстрока имени пользователя (--user; --eventlog).</summary>
    public string? User { get; set; }

    /// <summary>Подстрока имени события (--event; --eventlog).</summary>
    public string? Event { get; set; }

    /// <summary>Максимум записей выборки (--limit; --eventlog).</summary>
    public int Limit { get; set; }
}

/// <summary>Результат разбора аргументов командной строки.</summary>
public sealed class CliParseResult
{
    /// <summary>Распознанная команда.</summary>
    public CliCommandKind Kind { get; set; } = CliCommandKind.None;

    /// <summary>Целевой аргумент команды (имя/id базы, файл экспорта и т.п.).</summary>
    public string? Target { get; set; }

    /// <summary>Общие параметры.</summary>
    public CliOptions Options { get; set; } = new();

    /// <summary>Текст ошибки валидации комбинации ключей (null — разбор успешен).</summary>
    public string? Error { get; set; }
}

/// <summary>
/// Парсер аргументов командной строки CLI (функция 10). Чистая функция — покрыта юнит-тестами.
/// Поддерживает формы <c>--key value</c> и <c>--key=value</c>, значения в кавычках,
/// глобальные флаги <c>--json</c>/<c>--profile</c> и модификатор <c>--designer</c>.
/// Возвращает null, если аргументы не содержат команд CLI (обычный запуск приложения).
/// </summary>
public static class CliArgs
{
    /// <summary>
    /// Разбирает аргументы командной строки. Возвращает null, если команд CLI нет.
    /// При невалидной комбинации ключей возвращает результат с заполненным <see cref="CliParseResult.Error"/>
    /// (кроме исторического поведения: <c>--run</c> без цели и <c>--designer</c> без <c>--run</c>
    /// трактуются как отсутствие команд — null, совместимость со старым CLI).
    /// </summary>
    public static CliParseResult? Parse(string[]? args)
    {
        if (args is null || args.Length == 0)
            return null;

        var action = CliCommandKind.None;
        var target = string.Empty;
        var options = new CliOptions();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.IsNullOrWhiteSpace(arg))
                continue;

            var eq = arg.IndexOf('=');
            var key = (eq >= 0 ? arg[..eq] : arg).Trim();
            var value = eq >= 0 ? arg[(eq + 1)..].Trim().Trim('"') : null;

            if (string.Equals(key, "--list", StringComparison.OrdinalIgnoreCase))
            {
                action = CliCommandKind.List;
                continue;
            }

            if (string.Equals(key, "--json", StringComparison.OrdinalIgnoreCase))
            {
                options.Json = true;
                continue;
            }

            if (string.Equals(key, "--profile", StringComparison.OrdinalIgnoreCase))
            {
                options.Profile = value ?? ReadNextValue(args, ref i);
                continue;
            }

            if (string.Equals(key, "--telegram-send", StringComparison.OrdinalIgnoreCase))
            {
                action = CliCommandKind.TelegramSend;
                continue;
            }

            if (string.Equals(key, "--telegram-confirm", StringComparison.OrdinalIgnoreCase))
            {
                action = CliCommandKind.TelegramConfirm;
                continue;
            }

            if (string.Equals(key, "--text", StringComparison.OrdinalIgnoreCase))
            {
                options.Text = value ?? ReadNextValue(args, ref i);
                continue;
            }

            if (string.Equals(key, "--title", StringComparison.OrdinalIgnoreCase))
            {
                options.Title = value ?? ReadNextValue(args, ref i);
                continue;
            }

            if (string.Equals(key, "--kind", StringComparison.OrdinalIgnoreCase))
            {
                options.Kind = value ?? ReadNextValue(args, ref i);
                continue;
            }

            if (string.Equals(key, "--prompt", StringComparison.OrdinalIgnoreCase))
            {
                options.Prompt = value ?? ReadNextValue(args, ref i);
                continue;
            }

            if (string.Equals(key, "--button", StringComparison.OrdinalIgnoreCase))
            {
                var button = value ?? ReadNextValue(args, ref i);
                if (!string.IsNullOrWhiteSpace(button))
                    options.Buttons.Add(button.Trim());
                continue;
            }

            if (string.Equals(key, "--timeout", StringComparison.OrdinalIgnoreCase))
            {
                var seconds = value ?? ReadNextValue(args, ref i);
                int.TryParse(seconds, out var parsedTimeout);
                options.TimeoutSeconds = parsedTimeout;
                continue;
            }

            // ---- Новые команды CLI (п.6 «MCP-сервер и расширение CLI», выпуск 1.0.0.88): ----

            if (string.Equals(key, "--mcp", StringComparison.OrdinalIgnoreCase))
            {
                action = CliCommandKind.Mcp;
                continue;
            }

            if (string.Equals(key, "--update", StringComparison.OrdinalIgnoreCase))
            {
                action = CliCommandKind.Update;
                continue;
            }

            if (string.Equals(key, "--eventlog", StringComparison.OrdinalIgnoreCase))
            {
                action = CliCommandKind.Eventlog;
                continue;
            }

            if (string.Equals(key, "--check", StringComparison.OrdinalIgnoreCase))
            {
                action = CliCommandKind.Check;
                continue;
            }

            // ---- Лицензия редакции ПРО (2.0, docs/pro/PRO-EDITION.md): ----

            if (string.Equals(key, "--license-info", StringComparison.OrdinalIgnoreCase))
            {
                action = CliCommandKind.LicenseInfo;
                continue;
            }

            if (string.Equals(key, "--activate", StringComparison.OrdinalIgnoreCase))
            {
                action = CliCommandKind.Activate;
                options.Text = value ?? ReadNextValue(args, ref i);
                continue;
            }

            if (string.Equals(key, "--apply", StringComparison.OrdinalIgnoreCase))
            {
                options.Apply = true;
                continue;
            }

            // Целевая база/файловая ИБ команды --eventlog (и общая форма --target).
            if (string.Equals(key, "--target", StringComparison.OrdinalIgnoreCase))
            {
                var targetValue = value ?? ReadNextValue(args, ref i);
                if (!string.IsNullOrWhiteSpace(targetValue))
                    target = targetValue.Trim().Trim('"');
                continue;
            }

            if (string.Equals(key, "--from", StringComparison.OrdinalIgnoreCase))
            {
                options.From = value ?? ReadNextValue(args, ref i);
                continue;
            }

            if (string.Equals(key, "--to", StringComparison.OrdinalIgnoreCase))
            {
                options.To = value ?? ReadNextValue(args, ref i);
                continue;
            }

            if (string.Equals(key, "--user", StringComparison.OrdinalIgnoreCase))
            {
                options.User = value ?? ReadNextValue(args, ref i);
                continue;
            }

            if (string.Equals(key, "--event", StringComparison.OrdinalIgnoreCase))
            {
                options.Event = value ?? ReadNextValue(args, ref i);
                continue;
            }

            if (string.Equals(key, "--limit", StringComparison.OrdinalIgnoreCase))
            {
                var limit = value ?? ReadNextValue(args, ref i);
                int.TryParse(limit, out var parsedLimit);
                options.Limit = parsedLimit;
                continue;
            }

            var isRun = string.Equals(key, "--run", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(key, "--base", StringComparison.OrdinalIgnoreCase);
            if (!isRun)
                continue;

            // --list имеет приоритет: смешанный запуск трактуем как вывод списка.
            if (action != CliCommandKind.List)
                action = CliCommandKind.Run;

            // Формы: --run "Имя" / --run="Имя" / --run="Имя" --designer.
            if (value is not null)
            {
                target = value;
                continue;
            }

            if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
            {
                target = args[i + 1].Trim().Trim('"');
                i++;
            }
        }

        // --designer имеет смысл только вместе с --run; без значения — это модификатор
        // режима запуска, а не команда проводника (она обрабатывается ранее, ExplorerCommandLine).
        if (args.Any(a => a.TrimStart('-').Equals("designer", StringComparison.OrdinalIgnoreCase)
                          && !a.Contains('=')))
            options.Designer = true;

        if (action == CliCommandKind.None)
            return null;

        if (action == CliCommandKind.Run && string.IsNullOrWhiteSpace(target))
            return null;

        if (action == CliCommandKind.TelegramSend && string.IsNullOrWhiteSpace(options.Text))
            return new CliParseResult
            {
                Kind = action,
                Options = options,
                Error = "Команда --telegram-send требует текст уведомления (--text <сообщение>)"
            };

        if (action == CliCommandKind.TelegramConfirm && string.IsNullOrWhiteSpace(options.Prompt))
            return new CliParseResult
            {
                Kind = action,
                Options = options,
                Error = "Команда --telegram-confirm требует вопрос (--prompt <что подтвердить?>)"
            };

        if (action == CliCommandKind.Eventlog && string.IsNullOrWhiteSpace(target))
            return new CliParseResult
            {
                Kind = action,
                Options = options,
                Error = "Команда --eventlog требует цель выборки (--target <база или каталог файловой ИБ>)"
            };

        return new CliParseResult { Kind = action, Target = target, Options = options };
    }

    /// <summary>Читает значение ключа из следующего аргумента (форма «--key значение»).</summary>
    private static string? ReadNextValue(string[] args, ref int i)
    {
        if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
        {
            i++;
            return args[i].Trim().Trim('"');
        }

        return null;
    }
}