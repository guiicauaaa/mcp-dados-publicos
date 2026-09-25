using System.Text.Json;
using Microsoft.Extensions.AI;

namespace PublicData.Tests.Support;

/// <summary>An AIFunction that returns whatever the test says, like McpClientTool returning a TextContent.</summary>
public sealed class LocalTool(string name, Func<AIFunctionArguments, object?> run) : AIFunction
{
    private static readonly JsonElement EmptySchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone();

    public int Invocations { get; private set; }

    public override string Name => name;

    public override string Description => "test tool";

    public override JsonElement JsonSchema => EmptySchema;

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        Invocations++;
        return ValueTask.FromResult(run(arguments));
    }
}
