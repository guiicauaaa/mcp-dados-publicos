using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PublicData.McpServer.Tools;

namespace PublicData.McpServer.Hosting;

public static class McpServerSetup
{
    public const string Name = "public-data-mcp";
    public const string Version = "1.0.0";

    public static void Configure(McpServerOptions options)
    {
        options.ServerInfo = new Implementation { Name = Name, Version = Version };
        options.ServerInstructions =
            "Dados públicos oficiais do Brasil: emendas Pix (transferências especiais) recebidas por municípios, " +
            "via Transferegov, e a data e hora de Brasília.";
    }

    /// <summary>Registers every tool with the JSON options that keep accents readable for the model.</summary>
    public static IMcpServerBuilder WithPublicDataTools(this IMcpServerBuilder builder) => builder
        .WithTools<AmendmentTools>(ToolJson.Options)
        .WithTools<DateTimeTools>(ToolJson.Options);
}
