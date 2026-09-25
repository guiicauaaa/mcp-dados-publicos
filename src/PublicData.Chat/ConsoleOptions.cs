using PublicData.Chat.Core;

namespace PublicData.Chat;

public enum ConsoleMode
{
    Chat,
    Check,
    Smoke,
}

/// <summary>
/// The console reads environment variables and arguments only, no appsettings.json: the MCP server runs from
/// this folder tree and must not share configuration files with its host.
/// </summary>
public sealed record ConsoleOptions(ConsoleMode Mode, ChatSettings Settings, int Passes, string[]? OnlyCases, string? JsonOut, string? MarkdownOut)
{
    public const string Usage = """
        Uso: dotnet run --project src/PublicData.Chat [-- opções]

          (sem opções)          conversa no terminal
          --check               diagnóstico (Ollama, modelo, MCP, Transferegov) com código de saída 0/1
          --smoke               roteiro de perguntas contra o modelo real (critério para gravar o vídeo)
            --passes N            rodadas (padrão 2)
            --only id1,id2        só alguns casos
            --out arquivo.json    grava o resultado em JSON
            --md arquivo.md       grava a tabela em Markdown
          --model NOME          modelo do Ollama (padrão llama3.2-mcp-v1; ex.: llama3.2, qwen3:4b-instruct)
          --mcp-http URL        usa o servidor MCP por Streamable HTTP (ex.: http://localhost:5100/mcp)

        Variáveis de ambiente: OLLAMA_BASE_URL (padrão http://localhost:11434), OLLAMA_MODEL, MCP_HTTP_URL.
        """;

    public static ConsoleOptions Parse(string[] args)
    {
        string? Value(string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        var settings = new ChatSettings();
        if (Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") is { Length: > 0 } url)
        {
            settings.OllamaBaseUrl = new Uri(url);
        }

        settings.Model = Value("--model") ?? Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? settings.Model;

        if ((Value("--mcp-http") ?? Environment.GetEnvironmentVariable("MCP_HTTP_URL")) is { Length: > 0 } mcpUrl)
        {
            settings.Mcp.Transport = "http";
            settings.Mcp.HttpUrl = new Uri(mcpUrl);
        }

        var mode = args.Contains("--check") ? ConsoleMode.Check
            : args.Contains("--smoke") ? ConsoleMode.Smoke
            : ConsoleMode.Chat;

        return new ConsoleOptions(
            mode,
            settings,
            int.TryParse(Value("--passes"), out var passes) && passes > 0 ? passes : 2,
            Value("--only")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Value("--out"),
            Value("--md"));
    }
}
