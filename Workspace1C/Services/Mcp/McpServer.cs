using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Workspace1C.Services.Mcp;

/// <summary>
/// MCP-сервер Workspace 1C по stdio (п.6 «MCP-сервер и расширение CLI», выпуск
/// 1.0.0.88): JSON-RPC 2.0, каждая строка stdin — одно сообщение, ответы — строками
/// в stdout. В stdout пишутся ТОЛЬКО JSON-RPC сообщения; вся диагностика — в stderr.
/// <para>
/// Методы MCP: <c>initialize</c>, <c>notifications/initialized</c>, <c>ping</c>,
/// <c>tools/list</c>, <c>tools/call</c>. Инструменты регистрируются
/// <see cref="McpToolRegistry"/>; опасные проходят <see cref="McpSafetyGate"/>.
/// </para>
/// </summary>
public sealed class McpServer
{
    /// <summary>Опции сериализации данных инструментов (компактно, читаемая кириллица).</summary>
    private static readonly JsonSerializerOptions DataJsonOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly TextWriter _output;
    private readonly TextWriter _diagnostics;
    private readonly McpSafetyGate _safetyGate;
    private readonly System.Collections.Generic.IReadOnlyList<McpToolDefinition> _tools;

    /// <param name="output">Поток ответов JSON-RPC (по умолчанию Console.Out).</param>
    /// <param name="diagnostics">Поток диагностики человека (по умолчанию Console.Error) — в stdout не попадает.</param>
    /// <param name="safetyGate">Ворота подтверждения опасных операций (по умолчанию — реальный Telegram-сервис).</param>
    public McpServer(TextWriter? output = null, TextWriter? diagnostics = null, McpSafetyGate? safetyGate = null)
    {
        _output = output ?? Console.Out;
        _diagnostics = diagnostics ?? Console.Error;
        _safetyGate = safetyGate ?? new McpSafetyGate(
            AppServices.TryGetService<TelegramConfirmationService>());
        _tools = McpToolRegistry.Build(_safetyGate);
    }

    /// <summary>
    /// Запускает stdio-цикл: читает строки из <paramref name="input"/> до EOF и
    /// обрабатывает каждую (sync-обёртка над <see cref="RunAsync"/> для CLI-точки входа).
    /// </summary>
    public int Run(TextReader input, CancellationToken cancellationToken = default)
        // Sync-context OK (CLI-точка входа): у процесса MCP нет UI-контекста.
        => RunAsync(input, cancellationToken).GetAwaiter().GetResult();

    /// <summary>
    /// Асинхронный stdio-цикл (аудит sync-over-async): читает строки из
    /// <paramref name="input"/> до EOF и обрабатывает каждую через
    /// <see cref="HandleMessageAsync"/> — инструменты выполняются await'ом,
    /// без блокировки потока. Возвращает код выхода процесса (0 — нормальное завершение).
    /// </summary>
    public async Task<int> RunAsync(TextReader input, CancellationToken cancellationToken = default)
    {
        WriteDiagnostic($"MCP-сервер запущен (инструментов: {_tools.Count}).");
        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = input.ReadLine();
            }
            catch (Exception ex)
            {
                WriteDiagnostic("Ошибка чтения stdin: " + ex.Message);
                break;
            }

            if (line is null)
                break; // EOF — клиент закрыл канал, завершаемся.

            try
            {
                var response = await HandleMessageAsync(line, cancellationToken).ConfigureAwait(false);
                if (response is not null)
                {
                    await _output.WriteLineAsync(response).ConfigureAwait(false);
                    await _output.FlushAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // Сбой обработки строки не должен ронять цикл: пишем внутреннюю ошибку
                // в ответ (id неизвестен) и продолжаем принимать следующие сообщения.
                WriteDiagnostic("Внутренняя ошибка MCP: " + ex);
                try
                {
                    _output.WriteLine(McpJsonRpc.SerializeError(
                        null, McpProtocolError.InternalError, "Внутренняя ошибка сервера: " + ex.Message));
                    _output.Flush();
                }
                catch
                {
                    // stdout может быть закрыт клиентом — дальнейшие ответы невозможны.
                }
            }
        }

        WriteDiagnostic("MCP-сервер завершён (stdin закрыт).");
        return 0;
    }

    /// <summary>
    /// Обрабатывает одну строку JSON-RPC и возвращает сериализованный ответ
    /// (null — уведомление/пустая строка, ответ не требуется). Sync-обёртка над
    /// <see cref="HandleMessageAsync"/> — осталась для юнит-тестов.
    /// </summary>
    internal string? HandleMessage(string line, CancellationToken cancellationToken = default)
        // Sync-context OK (юнит-тесты): тесты проверяют синхронный контракт ответов.
        => HandleMessageAsync(line, cancellationToken).GetAwaiter().GetResult();

    /// <summary>
    /// Асинхронная обработка одной строки JSON-RPC (аудит sync-over-async):
    /// инструменты (<c>tools/call</c>) выполняются await'ом, без блокировки потока.
    /// </summary>
    internal async Task<string?> HandleMessageAsync(string line, CancellationToken cancellationToken = default)
    {
        var (outcome, request) = McpJsonRpc.Parse(line);
        switch (outcome)
        {
            case McpJsonRpc.ParseOutcome.Empty:
                return null;
            case McpJsonRpc.ParseOutcome.InvalidJson:
                return McpJsonRpc.SerializeError(null, McpProtocolError.ParseError, "Parse error: невалидный JSON-RPC.");
            case McpJsonRpc.ParseOutcome.NotAnObject:
                return McpJsonRpc.SerializeError(null, McpProtocolError.InvalidRequest, "Invalid Request: ожидался объект JSON-RPC.");
        }

        var method = request!.Method ?? string.Empty;

        // Уведомления (без id): обрабатываются без ответа.
        if (!request.HasId)
        {
            if (string.Equals(method, "notifications/initialized", StringComparison.Ordinal))
                WriteDiagnostic("Клиент подтвердил инициализацию (notifications/initialized).");
            return null;
        }

        return method switch
        {
            "initialize" => McpJsonRpc.SerializeResult(request.Id, BuildInitializeResult()),
            "ping" => McpJsonRpc.SerializeResult(request.Id, new { }),
            "tools/list" => McpJsonRpc.SerializeResult(request.Id, new
            {
                tools = _tools.Select(t => t.Describe()).ToList()
            }),
            "tools/call" => await HandleToolCallAsync(request.Id, request.Params, cancellationToken).ConfigureAwait(false),
            "notifications/initialized" => McpJsonRpc.SerializeResult(request.Id, new { }),
            _ => McpJsonRpc.SerializeError(
                request.Id, McpProtocolError.MethodNotFound, $"Method not found: {method}")
        };
    }

    /// <summary>Ответ initialize: версия MCP, возможности сервера (tools) и инструкции.</summary>
    private static object BuildInitializeResult() => new
    {
        protocolVersion = McpJsonRpc.ProtocolVersion,
        capabilities = new
        {
            tools = new { listChanged = false }
        },
        serverInfo = new
        {
            name = McpJsonRpc.ServerName,
            version = VersionInfo.Display()
        },
        instructions = "Workspace 1C — инструменты управления информационными базами 1С. " +
                       "Опасные операции (завершение сеансов, удаление баз) требуют подтверждения через Telegram."
    };

    /// <summary>
    /// Обработка tools/call: поиск инструмента по имени и вызов его обработчика.
    /// Ошибки инструмента (McpToolException — «не найдено», «не подтверждено» и т.п.)
    /// возвращаются результатом isError=true (контракт MCP); внутренние сбои — ошибкой
    /// JSON-RPC InternalError.
    /// </summary>
    private async Task<string> HandleToolCallAsync(JsonNode? id, JsonObject? parameters, CancellationToken cancellationToken)
    {
        var name = McpJsonRpc.GetString(parameters, "name");
        if (string.IsNullOrWhiteSpace(name))
            return McpJsonRpc.SerializeError(
                id, McpProtocolError.InvalidParams, "tools/call: параметр name (имя инструмента) обязателен.");

        var tool = _tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
        if (tool is null)
            return McpJsonRpc.SerializeError(id, McpProtocolError.ToolNotFound, $"Tool not found: {name}");

        JsonObject? arguments = null;
        if (parameters is not null
            && parameters.TryGetPropertyValue("arguments", out var argumentsNode)
            && argumentsNode is JsonObject objArgs)
            arguments = objArgs;

        try
        {
            var data = await tool.Handler(arguments, cancellationToken).ConfigureAwait(false);
            var text = JsonSerializer.Serialize(data, DataJsonOptions);
            return McpJsonRpc.SerializeResult(id, new
            {
                content = new[]
                {
                    new { type = "text", text }
                },
                isError = false
            });
        }
        catch (McpToolException ex)
        {
            // Логическая ошибка инструмента: контент с isError=true (спецификация MCP).
            return McpJsonRpc.SerializeResult(id, new
            {
                content = new[]
                {
                    new { type = "text", text = ex.Message }
                },
                isError = true
            });
        }
        catch (OperationCanceledException)
        {
            return McpJsonRpc.SerializeError(id, McpProtocolError.InternalError, "Выполнение инструмента отменено.");
        }
        catch (Exception ex)
        {
            WriteDiagnostic($"Ошибка инструмента {name}: " + ex);
            return McpJsonRpc.SerializeError(
                id, McpProtocolError.InternalError, "Внутренняя ошибка инструмента: " + ex.Message);
        }
    }

    private void WriteDiagnostic(string message)
    {
        try
        {
            _diagnostics.WriteLine("[mcp] " + message);
            _diagnostics.Flush();
        }
        catch
        {
            // stderr может быть перенаправлен/закрыт — диагностика не критична.
        }
    }
}