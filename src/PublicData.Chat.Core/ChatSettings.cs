using PublicData.Chat.Core.Llm;

namespace PublicData.Chat.Core;

/// <summary>Settings shared by the console chat (env vars and args) and the API (appsettings "Chat" section).</summary>
public sealed class ChatSettings
{
    public const string SectionName = "Chat";

    public static readonly string[] DefaultExposedTools = ["get_city_amendments"];

    public Uri OllamaBaseUrl { get; set; } = new("http://localhost:11434");

    /// <summary>Default: the derived model, created automatically from llama3.2 (same weights, fixed tool template).</summary>
    public string Model { get; set; } = DerivedModel.Name;

    /// <summary>Must be the same in the warm-up and in every chat request, or Ollama reloads the model.</summary>
    public int NumCtx { get; set; } = 8192;

    /// <summary>Sent as a string: the OllamaSharp mapper casts keep_alive to string.</summary>
    public string KeepAlive { get; set; } = "30m";

    public float Temperature { get; set; }

    public int Seed { get; set; } = 42;

    /// <summary>Becomes num_predict and bounds the degenerate repetition loops of a small model.</summary>
    public int MaxOutputTokens { get; set; } = 400;

    /// <summary>Text-only history (user and assistant): 3 exchanges. Old tool calls and results confuse a 3B model.</summary>
    public int HistoryMessages { get; set; } = 6;

    public TimeSpan ToolTimeout { get; set; } = TimeSpan.FromSeconds(45);

    public TimeSpan TurnTimeout { get; set; } = TimeSpan.FromMinutes(4);

    /// <summary>The first request on CPU loads the model; the HttpClient default of 100 s is not enough.</summary>
    public TimeSpan OllamaHttpTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Tools offered to the model. The server also exposes get_current_datetime, but the date goes in the
    /// system prompt: measured, the extra tool made llama3.2 call tools on greetings.
    /// </summary>
    public string[]? ExposedTools { get; set; }

    public McpSettings Mcp { get; set; } = new();

    public IReadOnlyList<string> EffectiveExposedTools =>
        ExposedTools is { Length: > 0 } tools ? tools : DefaultExposedTools;
}

public sealed class McpSettings
{
    /// <summary>"stdio" (default: child process, like the reference repository) or "http" (Streamable HTTP service).</summary>
    public string Transport { get; set; } = "stdio";

    /// <summary>Streamable HTTP endpoint, for example http://mcp-server:8080/mcp in Docker Compose.</summary>
    public Uri? HttpUrl { get; set; }

    /// <summary>Overrides the server DLL path for stdio. Default: mcp-server/PublicData.McpServer.dll next to the host.</summary>
    public string? ServerDll { get; set; }

    public bool UseHttp => string.Equals(Transport, "http", StringComparison.OrdinalIgnoreCase);
}
