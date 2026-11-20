using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Services.Mcp;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Хук MCP-инструментов для модулей ПРО: регистрация, приоритет встроенных имён,
/// снятие и вызов обработчика.
/// </summary>
public sealed class McpToolHooksTests
{
    private static McpToolDefinition Tool(string name, string description = "external") => new()
    {
        Name = name,
        Description = description,
        Handler = (_, _) => Task.FromResult<object>(new { ok = true })
    };

    [Fact]
    public void Build_IncludesRegisteredTool()
    {
        McpToolHooks.Clear();
        try
        {
            McpToolHooks.Register(Tool("run_pipeline"));

            var tools = McpToolRegistry.Build(new McpSafetyGate(null));

            Assert.Contains(tools, t => t.Name == "run_pipeline");
        }
        finally
        {
            McpToolHooks.Clear();
        }
    }

    [Fact]
    public void Build_BuiltInNameWinsOverExternal()
    {
        McpToolHooks.Clear();
        try
        {
            McpToolHooks.Register(Tool("list_infobases", "external override"));

            var tools = McpToolRegistry.Build(new McpSafetyGate(null));

            Assert.Single(tools.Where(t => t.Name == "list_infobases"));
            Assert.NotEqual("external override",
                tools.Single(t => t.Name == "list_infobases").Description);
        }
        finally
        {
            McpToolHooks.Clear();
        }
    }

    [Fact]
    public async Task RegisteredHandler_IsInvokable()
    {
        McpToolHooks.Clear();
        try
        {
            McpToolHooks.Register(Tool("run_pipeline"));
            var tool = McpToolRegistry.Build(new McpSafetyGate(null))
                .Single(t => t.Name == "run_pipeline");

            var data = await tool.Handler(null, CancellationToken.None);

            Assert.NotNull(data);
        }
        finally
        {
            McpToolHooks.Clear();
        }
    }

    [Fact]
    public void Register_ReplacesByName_AndUnregisterRemoves()
    {
        McpToolHooks.Clear();
        try
        {
            McpToolHooks.Register(Tool("run_pipeline", "first"));
            McpToolHooks.Register(Tool("run_pipeline", "second"));
            Assert.Single(McpToolHooks.Tools);
            Assert.Equal("second", McpToolHooks.Tools[0].Description);

            McpToolHooks.Unregister("run_pipeline");
            Assert.Empty(McpToolHooks.Tools);
        }
        finally
        {
            McpToolHooks.Clear();
        }
    }
}
