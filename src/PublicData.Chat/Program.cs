using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using OllamaSharp;
using PublicData.Chat;
using PublicData.Chat.Core;
using PublicData.Chat.Core.Chat;
using PublicData.Chat.Core.Llm;
using PublicData.Chat.Core.Mcp;

// Exit codes: 0 ok · 1 --check or --smoke failed · 2 Ollama unreachable · 3 model missing · 4 MCP server did not start.
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
// Console.InputEncoding stays as is: on Windows, UTF-8 input may drop accented letters typed in ReadLine.

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine(ConsoleOptions.Usage);
    return 0;
}

var options = ConsoleOptions.Parse(args);
var settings = options.Settings;
var ui = new ConsoleRenderer();
using var appCancel = new CancellationTokenSource();

ui.Info("Assistente de Dados Públicos: llama3.2 local + MCP (.NET 10)");

// 1) MCP server: child process over stdio (default) or Streamable HTTP service.
var serverLog = Path.Combine(AppContext.BaseDirectory, "mcp-server.log");
McpConnection mcp;
try
{
    mcp = await McpConnection.ConnectAsync(settings.Mcp, line =>
    {
        File.AppendAllText(serverLog, line + Environment.NewLine);
        if (ServerLogFilter.TryFormatForScreen(line, out var text))
        {
            ui.ServerLog(text);
        }
    }, cancellationToken: appCancel.Token);
}
catch (McpStartupException ex)
{
    ui.Fail(ex.Message);
    ui.Dim($"Log do servidor: {serverLog}");
    return 4;
}

await using var _ = mcp;
var exposed = mcp.Tools.Where(t => settings.EffectiveExposedTools.Contains(t.Name)).Cast<AITool>().ToList();
ui.Ok($"MCP conectado via {mcp.Transport}: {mcp.ServerName} {mcp.ServerVersion}, protocolo {mcp.ProtocolVersion}, " +
      $"{mcp.ConnectTime.TotalMilliseconds:0} ms | ferramentas: {string.Join(", ", mcp.Tools.Select(t => t.Name))}");

// 2) Ollama: server up, model installed (the derived one is created from llama3.2), warm-up.
var http = new HttpClient { BaseAddress = settings.OllamaBaseUrl, Timeout = settings.OllamaHttpTimeout };
var ollama = new OllamaApiClient(http, settings.Model);
PreflightResult preflight;
try
{
    preflight = await OllamaPreflight.RunAsync(ollama, settings, ui.Dim, appCancel.Token);
}
catch (OllamaUnavailableException ex)
{
    ui.Fail(ex.Message);
    return 2;
}
catch (ModelNotInstalledException ex)
{
    ui.Fail(ex.Message);
    return 3;
}

ui.Ok($"Ollama {preflight.OllamaVersion} em {settings.OllamaBaseUrl}");
if (preflight.Warning is not null)
{
    ui.Warn(preflight.Warning);
}

var model = preflight.EffectiveModel;
var modelNote = model == DerivedModel.Name ? " (pesos do llama3.2, template de ferramentas ajustado)" : "";
ui.Ok($"Modelo {model}{modelNote}{(preflight.DerivedModelCreated ? " criado agora e" : "")} carregado em " +
      $"{preflight.WarmupTime.TotalSeconds:0.0} s, num_ctx {settings.NumCtx}");
ui.Dim($"Ferramentas oferecidas ao modelo: {string.Join(", ", exposed.Select(t => t.Name))}");

var engine = new ChatEngine(ollama, settings);

switch (options.Mode)
{
    case ConsoleMode.Check:
        return await RunCheckAsync();
    case ConsoleMode.Smoke:
        return await RunSmokeAsync();
    default:
        await RunChatAsync();
        return 0;
}

async Task<int> RunCheckAsync()
{
    ui.Ok($"MCP tools/list com {mcp.Tools.Count} ferramenta(s)");
    // Calls the tool directly, without the model, to prove the network path (MCP -> Transferegov).
    var stopwatch = Stopwatch.StartNew();
    var result = await mcp.Client.CallToolAsync("get_city_amendments",
        new Dictionary<string, object?> { ["city"] = "Campinas", ["state"] = "SP", ["year"] = 2026 },
        cancellationToken: appCancel.Token);
    var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";
    if (result.IsError == true)
    {
        ui.Fail($"Transferegov via MCP: {text}");
        return 1;
    }

    ui.Ok($"Transferegov via MCP: {ToolResultShaper.Preview(text, 120)} ({stopwatch.ElapsedMilliseconds} ms)");
    return 0;
}

async Task<int> RunSmokeAsync()
{
    var runner = new SmokeRunner(engine, exposed, model, ui);
    var outcomes = await runner.RunAsync(options.Passes, options.OnlyCases, appCancel.Token);
    foreach (var pass in outcomes.GroupBy(o => o.Pass))
    {
        ui.Info($"Rodada {pass.Key}: {pass.Count(o => o.Ok)}/{pass.Count()} OK");
    }

    if (options.JsonOut is { } json)
    {
        File.WriteAllText(json, SmokeRunner.ToJson(outcomes), new UTF8Encoding(false));
    }

    if (options.MarkdownOut is { } md)
    {
        File.WriteAllText(md, SmokeRunner.ToMarkdown(outcomes, BrasiliaClock.Now(TimeProvider.System)), new UTF8Encoding(false));
    }

    var approved = SmokeRunner.Approved(outcomes);
    if (approved)
    {
        ui.Ok("Smoke aprovado: casos do vídeo OK em todas as rodadas e controle com no máximo 1 falha por rodada.");
    }
    else
    {
        ui.Fail("Smoke reprovado: não grave o vídeo com este modelo/configuração.");
    }

    return approved ? 0 : 1;
}

async Task RunChatAsync()
{
    ui.Info("Digite sua pergunta. Comandos: /ferramentas  /limpar  /sair   (Ctrl+C cancela a resposta em andamento)");

    CancellationTokenSource? turnCancel = null;
    Console.CancelKeyPress += (_, e) =>
    {
        if (turnCancel is { IsCancellationRequested: false })
        {
            e.Cancel = true; // cancel only the current answer
            turnCancel.Cancel();
        }
    };

    List<ChatMessage> history = [];
    while (true)
    {
        ui.Prompt();
        var line = Console.ReadLine();
        if (line is null || line.Trim() == "/sair")
        {
            break;
        }

        var question = line.Trim();
        if (question.Length == 0)
        {
            continue;
        }

        if (question == "/limpar")
        {
            history.Clear();
            ui.Dim("Conversa reiniciada.");
            continue;
        }

        if (question == "/ferramentas")
        {
            foreach (var tool in mcp.Tools)
            {
                var offered = exposed.Any(t => t.Name == tool.Name) ? "oferecida ao modelo" : "não oferecida ao modelo";
                ui.Info($"- {tool.Name} ({tool.Title}) [{offered}]");
                ui.Dim($"  {tool.Description}");
                ui.Dim($"  inputSchema: {tool.JsonSchema.GetRawText()}");
            }

            continue;
        }

        turnCancel = new CancellationTokenSource();
        try
        {
            var result = await ui.WithStatusAsync(() => engine.AskAsync(history, question, exposed, model, ui.Trace, turnCancel.Token));
            ui.Answer(result.Model, result.Elapsed, result.Answer);
            if (result.SourceLine is { } source)
            {
                ui.Source(source);
            }

            history.Add(new ChatMessage(ChatRole.User, question));
            history.Add(new ChatMessage(ChatRole.Assistant, result.Answer));
        }
        catch (OperationCanceledException) when (turnCancel.IsCancellationRequested)
        {
            ui.Warn("(resposta cancelada)");
        }
        catch (OperationCanceledException)
        {
            ui.Warn("A resposta demorou mais que o limite. Tente de novo (o modelo pode estar carregando).");
        }
        catch (HttpRequestException ex)
        {
            ui.Fail($"Perdi a conexão com o Ollama em {settings.OllamaBaseUrl} ({ex.Message}). Verifique se ele está aberto e tente de novo.");
        }
        finally
        {
            turnCancel.Dispose();
            turnCancel = null;
        }
    }
}
