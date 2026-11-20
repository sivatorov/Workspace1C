using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Services.Licensing;

namespace Workspace1C.Services;

/// <summary>
/// Реестр CLI-команд, добавляемых модулями ПРО (2.x): <c>--pipeline</c>,
/// <c>--decrypt</c>. Публичный парсер распознаёт команды всегда; обработчик
/// регистрирует модуль в <c>RegisterProModules</c>. Без лицензии команда
/// завершается кодом <see cref="CliExitCodes.ProRequired"/> (6).
/// </summary>
public static class ProCliCommands
{
    /// <summary>Обработчик команды: разобранные аргументы → код возврата.</summary>
    public delegate Task<int> Handler(CliParseResult parsed, CancellationToken cancellationToken);

    private static readonly ConcurrentDictionary<CliCommandKind, Handler> Handlers = new();

    /// <summary>Регистрирует (или заменяет) обработчик команды.</summary>
    public static void Register(CliCommandKind kind, Handler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Handlers[kind] = handler;
    }

    /// <summary>Снимает обработчик (тесты, выгрузка модуля).</summary>
    public static void Unregister(CliCommandKind kind) => Handlers.TryRemove(kind, out _);

    /// <summary>Есть ли обработчик для команды.</summary>
    public static bool IsRegistered(CliCommandKind kind) => Handlers.ContainsKey(kind);

    /// <summary>
    /// Выполняет ПРО-команду: без лицензии — код 6, иначе зарегистрированный
    /// обработчик (или код 6, если сборка без ПРО-модулей).
    /// </summary>
    public static async Task<int> ExecuteAsync(CliParseResult parsed, ProFeature feature,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        if (!EditionService.Has(feature))
            return CliLicenseCommands.ProRequired(CliCommands.CommandName(parsed.Kind), feature,
                parsed.Options.Json);

        return await InvokeAsync(parsed, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Вызов обработчика без проверки лицензии (тесты, внутренняя диспетчеризация).</summary>
    internal static async Task<int> InvokeAsync(CliParseResult parsed,
        CancellationToken cancellationToken = default)
    {
        if (parsed is not null && Handlers.TryGetValue(parsed.Kind, out var handler))
            return await handler(parsed, cancellationToken).ConfigureAwait(false);

        // Лицензия есть, но модуль не подключён (бесплатная сборка из исходников).
        return CliExitCodes.ProRequired;
    }
}
