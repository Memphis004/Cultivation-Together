// MCP bridge process (external, matches the "MCP bridge process" box in the
// architecture diagram). Runs as its own .NET console app, separate from
// Unity. Written in C# specifically so it can use MessagePipe.Interprocess
// directly instead of re-implementing MessagePipe's wire protocol in
// Node/Python - the tradeoff flagged earlier is resolved by keeping both
// sides on .NET, same as Wanxiang.Prelude's Frontend/Backend split.
//
// Talks to Unity over MessagePipe.Interprocess (TCP, localhost).
// Talks to the AI GM client (e.g. Open-LLM-VTuber) over MCP via stdio,
// using the official ModelContextProtocol C# SDK.
//
// NuGet packages needed: MessagePipe, MessagePipe.Interprocess,
// ModelContextProtocol, Microsoft.Extensions.Hosting

using System.ComponentModel;
using System.Text.Json;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Xianxia.Sect;
using Xianxia.Sect.Messages;

var builder = Host.CreateApplicationBuilder(args);

// MCP over stdio uses stdout for protocol messages - logs must go to stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

Console.Error.WriteLine("[mcp-bridge] starting - if you're running this " +
    "directly with `dotnet run`, this is expected to look idle after this " +
    "line: stdio transport just waits for an MCP client (e.g. MCP " +
    "Inspector) to talk to it over stdin, it won't print anything else on " +
    "its own until a client connects and calls a tool.");

// --- connect into Unity's MessagePipe bus as a TCP client ---
// AddMessagePipe() on IServiceCollection returns IMessagePipeBuilder
// directly (unlike VContainer's IContainerBuilder, which needs an extra
// ToMessagePipeBuilder() conversion step) - AddTcpInterprocess() hangs off
// that builder, not off IServiceCollection itself.
var messagePipeBuilder = builder.Services.AddMessagePipe();
messagePipeBuilder.AddTcpInterprocess("127.0.0.1", 3215, options =>
{
    options.HostAsServer = false; // Unity hosts the endpoint; we connect as a client
});

// --- expose MCP tools to the AI GM client ---
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var app = builder.Build();
await app.RunAsync();

// Read (query) tools - map 1:1 to what was sketched in the architecture
// discussion (get_sect_state, get_current_event, ...).
[McpServerToolType]
public static class SectQueryTools
{
    [McpServerTool, Description("Get the current sect state: disciples, wallets, stockpile.")]
    public static async Task<string> GetSectState(
        IRemoteRequestHandler<SectStateQuery, SectStateSnapshot> requestHandler)
    {
        var snapshot = await requestHandler.InvokeAsync(
            new SectStateQuery { RequestId = Guid.NewGuid().ToString() });

        // MessagePack stub decode - swap for the generated protobuf parser
        // once economy.proto is compiled for real.
        var state = SectEconomyState.FromByteArray(snapshot.EconomyStateBytes);
        return JsonSerializer.Serialize(state);
    }

    [McpServerTool, Description("Block until the next world event that requires a GM decision.")]
    public static async Task<string> AwaitNextWorldEvent(
        IRemoteRequestHandler<AwaitWorldEventRequest, AwaitWorldEventResponse> requestHandler)
    {
        // Request-response, not subscribe - IDistributedSubscriber over this
        // TCP transport always opens its own listen socket regardless of
        // HostAsServer, which collides with Unity's already-bound port when
        // called from a non-hub process like this bridge. Request-response
        // only ever connects out (no listen), same mechanism get_sect_state
        // already uses successfully.
        var response = await requestHandler.InvokeAsync(
            new AwaitWorldEventRequest { RequestId = Guid.NewGuid().ToString() });

        // Full response now (description + choices), not just the bare
        // event id - EventData ScriptableObjects actually author this
        // content now instead of it being hardcoded/absent.
        return JsonSerializer.Serialize(response);
    }
}

// Write (execute) tools go in a separate type on purpose - keeps the
// read/write split visible at a glance in the tool list, matching the
// separation decided earlier (query tools vs execute tools).
[McpServerToolType]
public static class SectActionTools
{
    [McpServerTool, Description("Execute a decision for the current world event.")]
    public static async Task<string> ExecuteDecision(
        IDistributedPublisher<string, ExecuteDecisionMessage> publisher,
        [Description("Event id from await_next_world_event")] string eventId,
        [Description("Chosen option id")] string choiceId)
    {
        await publisher.PublishAsync(
            InterprocessTopics.ExecuteDecision,
            new ExecuteDecisionMessage { EventId = eventId, ChoiceId = choiceId });

        return $"Decision sent to Unity: event={eventId} choice={choiceId}";
    }

    [McpServerTool, Description("Have a disciple buy an item from the sect stockpile using their contribution.")]
    public static async Task<string> PurchaseItem(
        IRemoteRequestHandler<PurchaseItemRequest, PurchaseItemResponse> requestHandler,
        [Description("Disciple id, e.g. from get_sect_state")] string discipleId,
        [Description("Item id, e.g. elixir_qi_gathering")] string itemDefId,
        [Description("Item grade, from the stockpile entry in get_sect_state")] int grade,
        [Description("How many to buy")] int quantity)
    {
        var response = await requestHandler.InvokeAsync(new PurchaseItemRequest
        {
            DiscipleId = discipleId,
            ItemDefId = itemDefId,
            Grade = grade,
            Quantity = quantity,
        });

        return response.Message;
    }
}
