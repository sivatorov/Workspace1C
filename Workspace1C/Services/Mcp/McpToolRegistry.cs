using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Workspace1C.Services.Mcp;

/// <summary>
/// Ошибка инструмента MCP с человекочитаемым сообщением. Перехватывается
/// <see cref="McpServer"/> и превращается в результат <c>isError=true</c>
/// (по спецификации MCP текст ошибки инструмента передаётся контентом ответа).
/// </summary>
public sealed class McpToolException : Exception
{
    public McpToolException(string message) : base(message)
    {
    }
}

/// <summary>
/// Определение инструмента MCP (п.6 «MCP-сервер и расширение CLI», выпуск 1.0.0.88):
/// имя, описание, JSON Schema параметров и асинхронный обработчик.
/// Инструменты с <see cref="RequiresConfirmation"/> = true перед выполнением проходят
/// через <see cref="McpSafetyGate"/> (подтверждение inline-кнопками Telegram).
/// </summary>
public sealed class McpToolDefinition
{
    /// <summary>Имя инструмента для tools/call (например <c>list_infobases</c>).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Краткое описание инструмента для ИИ-агента (показывается в tools/list).</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>JSON Schema параметров инструмента (properties/required).</summary>
    public JsonObject InputSchema { get; init; } = new();

    /// <summary>
    /// Требуется ли подтверждение через Telegram перед выполнением. Без исхода
    /// <see cref="McpConfirmOutcome.Approved"/> действие не выполняется.
    /// </summary>
    public bool RequiresConfirmation { get; init; }

    /// <summary>Асинхронный обработчик: параметры (объект) → данные результата.</summary>
    public Func<JsonObject?, CancellationToken, Task<object>> Handler { get; init; } = (_, _) => Task.FromResult<object>(new { });

    /// <summary>Описывает инструмент в контракте tools/list.</summary>
    public object Describe() => new
    {
        name = Name,
        description = Description,
        inputSchema = InputSchema
    };
}

/// <summary>
/// Реестр инструментов MCP-сервера: строит список инструментов Workspace 1C
/// (6 базовых + опасный <c>terminate_session</c> с подтверждением через
/// <see cref="McpSafetyGate"/>). Прод-обработчики получают сервисы через
/// <see cref="AppServices"/> (headless-контейнер, инициализированный до старта
/// MCP-цикла); схемы и описания — чистые данные для юнит-тестов.
/// </summary>
public static class McpToolRegistry
{
    /// <summary>Название свойства параметров «имя/id базы» (общее для инструментов).</summary>
    public const string ArgName = "name";

    /// <summary>Описание локализации параметра «имя базы».</summary>
    public const string ArgNameDescription = "Имя базы (или id) из списка Workspace 1C.";

    /// <summary>Собирает инструменты сервера. Опасный инструмент привязан к воротам
    /// безопасности <paramref name="safetyGate"/> (не null).</summary>
    public static IReadOnlyList<McpToolDefinition> Build(McpSafetyGate safetyGate)
    {
        var tools = new List<McpToolDefinition>
        {
            ListInfobasesTool(),
            LaunchInfobaseTool(),
            CloneInfobaseTool(),
            EventLogQueryTool(),
            ServerSessionsTool(),
            ScheduledJobsStateTool(),
            TerminateSessionTool(safetyGate),
        };

        // Инструменты модулей ПРО (2.x): одноимённые встроенные имеют приоритет.
        foreach (var tool in McpToolHooks.Tools)
        {
            if (!tools.Exists(t => string.Equals(t.Name, tool.Name, StringComparison.Ordinal)))
                tools.Add(tool);
        }
        return tools;
    }

    // ------------------------------------------------------------ list_infobases

    private static McpToolDefinition ListInfobasesTool() => new()
    {
        Name = "list_infobases",
        Description = "Список информационных баз Workspace 1C (аналог --list): имя, тип " +
                      "подключения (server/web/file), id, группа, теги, приватность.",
        InputSchema = Schema(new Dictionary<string, object?>
        {
            ["includePrivate"] = new
            {
                type = "boolean",
                description = "Включать приватные базы разблокированного профиля (по умолчанию false)."
            }
        }),
        Handler = McpToolHandlers.HandleListInfobases
    };

    // ------------------------------------------------------------ launch_infobase

    private static McpToolDefinition LaunchInfobaseTool() => new()
    {
        Name = "launch_infobase",
        Description = "Запуск информационной базы в режиме «1С:Предприятие» или конструкторе " +
                      "(аналог --run). Параметр name — имя базы или её id.",
        InputSchema = Schema(new Dictionary<string, object?>
        {
            [ArgName] = new { type = "string", description = ArgNameDescription },
            ["designer"] = new
            {
                type = "boolean",
                description = "Запустить в режиме «Конфигуратор» (по умолчанию false — «1С:Предприятие»)."
            }
        }, ArgName),
        Handler = McpToolHandlers.HandleLaunchInfobase
    };

    // ------------------------------------------------------------ clone_infobase

    private static McpToolDefinition CloneInfobaseTool() => new()
    {
        Name = "clone_infobase",
        Description = "Копия клиент-серверной информационной базы (механизм «Дублировать базу»): " +
                      "выгрузка .dt/.cf → создание ИБ на целевом сервере → загрузка данных " +
                      "(полная копия или только конфигурация).",
        InputSchema = Schema(new Dictionary<string, object?>
        {
            ["source"] = new { type = "string", description = "Имя или id исходной клиент-серверной базы." },
            ["mode"] = new
            {
                type = "string",
                @enum = new[] { "full", "config" },
                description = "full — данные и конфигурация (по умолчанию); config — только конфигурация."
            },
            ["cloneName"] = new { type = "string", description = "Наименование копии в списке программы." },
            ["server"] = new { type = "string", description = "Сервер 1С копии (Srvr, возможно «host:port»)." },
            ["databaseName"] = new { type = "string", description = "Имя базы копии на сервере 1С (Ref)." },
            ["dbms"] = new { type = "string", description = "Тип СУБД (MSSQLServer, PostgreSQL, …)." },
            ["dbServer"] = new { type = "string", description = "Сервер СУБД." },
            ["dbName"] = new { type = "string", description = "Имя базы данных на сервере СУБД." },
            ["dbUser"] = new { type = "string", description = "Пользователь СУБД (опционально)." },
            ["dbPassword"] = new { type = "string", description = "Пароль пользователя СУБД (опционально)." },
            ["createSqlDatabase"] = new { type = "boolean", description = "Создавать БД на сервере СУБД (CrSQLDB=Y)." },
            ["blockScheduledJobs"] = new { type = "boolean", description = "Блокировать фоновые задания (SchJobDn=Y)." },
            ["platformVersion"] = new { type = "string", description = "Версия платформы для запуска DESIGNER." }
        }, "source", "server", "databaseName"),
        Handler = McpToolHandlers.HandleCloneInfobase
    };

    // ------------------------------------------------------------ event_log_query

    private static McpToolDefinition EventLogQueryTool() => new()
    {
        Name = "event_log_query",
        Description = "Выборка записей журнала регистрации файловой базы (SQLite .lgd или " +
                      "последовательный .lgf/.lgp): период, пользователь, событие, текст, лимит.",
        InputSchema = Schema(new Dictionary<string, object?>
        {
            ["name"] = new { type = "string", description = ArgNameDescription },
            ["path"] = new
            {
                type = "string",
                description = "Каталог файловой ИБ (1Cv8.1CD) вместо name — журнал читается напрямую."
            },
            ["from"] = new
            {
                type = "string",
                description = "Начало периода, «yyyy-MM-dd[ HH:mm:ss]» (по умолчанию — сегодня)."
            },
            ["to"] = new { type = "string", description = "Конец периода, «yyyy-MM-dd[ HH:mm:ss]»." },
            ["user"] = new { type = "string", description = "Подстрока имени пользователя." },
            ["event"] = new { type = "string", description = "Подстрока имени события (например «Data.Update»)." },
            ["text"] = new { type = "string", description = "Подстрока в комментарии/данных/представлении." },
            ["limit"] = new { type = "integer", description = "Максимум записей (по умолчанию 5000)." }
        }, "name", "path"),
        Handler = McpToolHandlers.HandleEventLogQuery
    };

    // ------------------------------------------------------------ server_sessions

    private static McpToolDefinition ServerSessionsTool() => new()
    {
        Name = "server_sessions",
        Description = "Сеансы пользователей сервера 1С через rac (Remote Administration Client) " +
                      "по записи справочника «Серверы 1С» (параметр server — id/описание/адрес записи).",
        InputSchema = Schema(new Dictionary<string, object?>
        {
            ["server"] = new
            {
                type = "string",
                description = "Запись справочника «Серверы 1С»: id, описание или адрес «host[:port]»."
            },
            ["clusterId"] = new
            {
                type = "string",
                description = "Идентификатор кластера (GUID); по умолчанию — первый кластер сервера."
            },
            ["limit"] = new { type = "integer", description = "Максимум сеансов (по умолчанию без лимита)." }
        }, "server"),
        Handler = McpToolHandlers.HandleServerSessions
    };

    // --------------------------------------------------------- scheduled_jobs_state

    private static McpToolDefinition ScheduledJobsStateTool() => new()
    {
        Name = "scheduled_jobs_state",
        Description = "Состояние заданий по расписанию Workspace 1C (issue #286): тип, расписание, " +
                      "включение и результат последнего запуска каждого задания.",
        InputSchema = Schema(new Dictionary<string, object?>()),
        Handler = McpToolHandlers.HandleScheduledJobs
    };

    // ------------------------------------------------------------ terminate_session

    private static McpToolDefinition TerminateSessionTool(McpSafetyGate safetyGate) => new()
    {
        Name = "terminate_session",
        Description = "ЗАВЕРШЕНИЕ сеанса пользователя сервера 1С (rac «session terminate»). " +
                      "ОПАСНАЯ операция: выполняется ТОЛЬКО после подтверждения через Telegram " +
                      "(inline-кнопки «Да/Нет»); без подтверждения действие не выполняется.",
        RequiresConfirmation = true,
        InputSchema = Schema(new Dictionary<string, object?>
        {
            ["server"] = new
            {
                type = "string",
                description = "Запись справочника «Серверы 1С»: id, описание или адрес «host[:port]»."
            },
            ["sessionId"] = new { type = "string", description = "Идентификатор завершаемого сеанса (GUID)." },
            ["clusterId"] = new { type = "string", description = "Идентификатор кластера (GUID); по умолчанию — первый." },
            ["processId"] = new { type = "string", description = "Идентификатор рабочего процесса (для rac 8.5.x)." },
            ["confirmTimeoutSeconds"] = new
            {
                type = "integer",
                description = "Таймаут ожидания подтверждения Telegram, секунд (по умолчанию 300)."
            }
        }, "server", "sessionId"),
        Handler = (parameters, ct) => McpToolHandlers.HandleTerminateSession(safetyGate, parameters, ct)
    };

    // ------------------------------------------------------------- helpers (schema)

    /// <summary>Строит JSON Schema объекта параметров из словаря свойств и обязательных полей.</summary>
    internal static JsonObject Schema(Dictionary<string, object?> properties, params string[] required)
    {
        var schema = new JsonObject { ["type"] = "object" };
        var props = new JsonObject();
        foreach (var (key, value) in properties)
            props[key] = JsonSerializer.SerializeToNode(value);
        schema["properties"] = props;
        if (required.Length > 0)
        {
            var requiredArray = new JsonArray();
            foreach (var name in required)
                requiredArray.Add(JsonValue.Create(name));
            schema["required"] = requiredArray;
        }
        return schema;
    }
}