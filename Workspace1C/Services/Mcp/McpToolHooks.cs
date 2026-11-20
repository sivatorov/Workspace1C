using System;
using System.Collections.Generic;

namespace Workspace1C.Services.Mcp;

/// <summary>
/// Реестр MCP-инструментов, добавляемых модулями ПРО (2.x). Публичный
/// <see cref="McpToolRegistry"/> строит встроенные инструменты, затем добавляет
/// зарегистрированные здесь; одноимённые встроенные имеют приоритет. Модуль
/// <c>Pipelines</c> регистрирует <c>run_pipeline</c>.
/// </summary>
public static class McpToolHooks
{
    private static readonly object Sync = new();
    private static readonly List<McpToolDefinition> Registered = new();

    /// <summary>Текущие внешние инструменты (копия списка).</summary>
    public static IReadOnlyList<McpToolDefinition> Tools
    {
        get
        {
            lock (Sync)
                return Registered.ToArray();
        }
    }

    /// <summary>Регистрирует инструмент; одноимённый заменяется.</summary>
    public static void Register(McpToolDefinition tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (string.IsNullOrWhiteSpace(tool.Name))
            throw new ArgumentException("MCP tool name is required.", nameof(tool));

        lock (Sync)
        {
            Registered.RemoveAll(t => string.Equals(t.Name, tool.Name, StringComparison.Ordinal));
            Registered.Add(tool);
        }
    }

    /// <summary>Снимает инструмент по имени.</summary>
    public static void Unregister(string name)
    {
        lock (Sync)
            Registered.RemoveAll(t => string.Equals(t.Name, name, StringComparison.Ordinal));
    }

    /// <summary>Снимает все внешние инструменты (тесты, выгрузка модуля).</summary>
    public static void Clear()
    {
        lock (Sync)
            Registered.Clear();
    }
}
