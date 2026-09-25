using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using PublicData.Chat.Core.Chat;
using PublicData.Chat.Core.Llm;

namespace PublicData.Chat;

public enum Expectation
{
    /// <summary>The model must answer without any tool call.</summary>
    NoTool,

    /// <summary>The model must call get_city_amendments (and, if set, the answer must contain a value).</summary>
    Tool,

    /// <summary>The model must call the tool and receive an error (ambiguous or unknown city).</summary>
    ToolError,

    /// <summary>No data may be shown: either no call, or a call that the server refused.</summary>
    NoData,
}

public sealed record SmokeCase(string Id, string Prompt, Expectation Expect, string? MustContain = null, bool FollowUp = false);

public sealed record SmokeOutcome(string Model, int Pass, string Id, string Prompt, Expectation Expect, bool Ok,
    long Ms, IReadOnlyList<string> Calls, bool MetaLeak, string Answer);

/// <summary>
/// Fixed script against the REAL model. A 3B model varies between runs even with temperature 0, so this runs
/// before every recording. Criterion: the video cases pass in every pass and the control set scores 17/18 or more.
/// </summary>
public sealed partial class SmokeRunner(ChatEngine engine, IReadOnlyList<AITool> tools, string model, ConsoleRenderer ui)
{
    public static readonly SmokeCase[] VideoCases =
    [
        new("capacidades", "Olá! O que você consegue fazer?", Expectation.NoTool),
        new("emendas", "Quanto Campinas (SP) recebeu de emendas Pix em 2026 e de quais parlamentares?", Expectation.Tool, "7,91"),
        new("seguimento", "E em 2025?", Expectation.Tool, "12,50", FollowUp: true),
        new("ambigua", "Quanto Santa Rita recebeu de emendas Pix em 2026?", Expectation.ToolError),
        new("sem_municipio", "Quanto a prefeitura recebeu de emendas Pix em 2025?", Expectation.NoData),
        new("inexistente", "Quanto Xyzópolis recebeu de emendas Pix em 2025?", Expectation.ToolError),
    ];

    public static readonly SmokeCase[] ControlCases =
    [
        new("c01", "oi", Expectation.NoTool),
        new("c02", "Obrigado pela ajuda!", Expectation.NoTool),
        new("c03", "O que é uma emenda Pix?", Expectation.NoTool),
        new("c04", "Que dia é hoje?", Expectation.NoTool),
        new("c05", "que horas são?", Expectation.NoTool),
        new("c06", "Em que ano estamos?", Expectation.NoTool),
        new("c07", "Quanto Recife recebeu de emendas Pix em 2025?", Expectation.Tool),
        new("c08", "Quais parlamentares mandaram emendas Pix para Goiânia este ano?", Expectation.Tool),
        new("c09", "Qual a capital de Goiás?", Expectation.NoTool),
        new("c10", "Me explique a LRF", Expectation.NoTool),
        new("c11", "tchau", Expectation.NoTool),
        new("c12", "Boa tarde, tudo bem?", Expectation.NoTool),
        new("c13", "Qual o dia da semana hoje?", Expectation.NoTool),
        new("c14", "Quanto Belo Horizonte recebeu de emendas Pix no ano passado?", Expectation.Tool),
        new("c15", "Por que as emendas Pix são polêmicas?", Expectation.NoTool),
        new("c16", "Você usa inteligência artificial?", Expectation.NoTool),
        new("c17", "Quanto Anápolis (GO) recebeu de emendas Pix em 2026?", Expectation.Tool),
        new("c18", "Quem é você?", Expectation.NoTool),
    ];

    public async Task<IReadOnlyList<SmokeOutcome>> RunAsync(int passes, string[]? only, CancellationToken cancellationToken)
    {
        var cases = VideoCases.Concat(ControlCases).Where(c => only is null || only.Contains(c.Id)).ToList();
        var outcomes = new List<SmokeOutcome>();

        for (var pass = 1; pass <= passes; pass++)
        {
            List<ChatMessage> history = [];
            foreach (var c in cases)
            {
                if (!c.FollowUp)
                {
                    history = [];
                }

                ui.Info($"--- [{model} rodada {pass}] {c.Id}: {c.Prompt}");
                TurnResult? result = null;
                string answer;
                try
                {
                    result = await engine.AskAsync(history, c.Prompt, tools, model, ui.Trace, cancellationToken);
                    answer = result.Answer;
                    history.Add(new ChatMessage(ChatRole.User, c.Prompt));
                    history.Add(new ChatMessage(ChatRole.Assistant, result.Answer));
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    answer = $"EXCEÇÃO {ex.GetType().Name}: {ex.Message}";
                }

                var events = result?.ToolEvents ?? [];
                var calls = events.Where(e => e.Kind == ToolTraceKind.Call).Select(e => $"{e.ToolName} {e.ArgumentsJson}").ToList();
                var errors = events.Count(e => e.Kind == ToolTraceKind.Error);
                var results = events.Count(e => e.Kind == ToolTraceKind.Result);
                var ok = result is not null && c.Expect switch
                {
                    Expectation.NoTool => calls.Count == 0,
                    Expectation.Tool => results > 0 && (c.MustContain is null || answer.Contains(c.MustContain, StringComparison.Ordinal)),
                    Expectation.ToolError => calls.Count > 0 && errors > 0,
                    Expectation.NoData => results == 0,
                    _ => false,
                };
                var leak = calls.Count == 0 && MetaText().IsMatch(answer);

                ui.Info($"    => {(ok ? "OK" : "FALHOU")} em {result?.Elapsed.TotalMilliseconds ?? -1:0} ms | chamadas: {(calls.Count == 0 ? "(nenhuma)" : string.Join(" ; ", calls))}");
                ui.Dim($"    => resposta: {answer.ReplaceLineEndings(" ")}");
                outcomes.Add(new SmokeOutcome(model, pass, c.Id, c.Prompt, c.Expect, ok,
                    (long)(result?.Elapsed.TotalMilliseconds ?? -1), calls, leak, answer));
            }
        }

        return outcomes;
    }

    public static bool Approved(IReadOnlyList<SmokeOutcome> outcomes)
    {
        var videoIds = VideoCases.Select(c => c.Id).ToHashSet();
        var videoOk = outcomes.Where(o => videoIds.Contains(o.Id)).All(o => o.Ok);
        var controlByPass = outcomes.Where(o => !videoIds.Contains(o.Id)).GroupBy(o => o.Pass);
        return videoOk && controlByPass.All(g => g.Count(o => o.Ok) >= Math.Max(0, g.Count() - 1));
    }

    public static string ToMarkdown(IReadOnlyList<SmokeOutcome> outcomes, DateTimeOffset when)
    {
        var md = new StringBuilder();
        md.AppendLine(CultureInfo.InvariantCulture, $"# Smoke com o modelo real ({outcomes.FirstOrDefault()?.Model})");
        md.AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"Gerado por `dotnet run --project src/PublicData.Chat -- --smoke` em {when:dd/MM/yyyy HH:mm}.");
        md.AppendLine();
        foreach (var pass in outcomes.GroupBy(o => o.Pass))
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"Rodada {pass.Key}: {pass.Count(o => o.Ok)}/{pass.Count()} OK, {pass.Count(o => o.MetaLeak)} com meta-texto.");
        }

        md.AppendLine();
        md.AppendLine("| Rodada | Caso | Pergunta | Esperado | Resultado | Tempo | Chamadas MCP |");
        md.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var o in outcomes)
        {
            md.AppendLine(CultureInfo.InvariantCulture,
                $"| {o.Pass} | {o.Id} | {o.Prompt} | {o.Expect} | {(o.Ok ? "OK" : "FALHOU")} | {o.Ms / 1000.0:0.0} s | {(o.Calls.Count == 0 ? "—" : string.Join("<br>", o.Calls).Replace("|", "\\|", StringComparison.Ordinal))} |");
        }

        return md.ToString();
    }

    public static string ToJson(IReadOnlyList<SmokeOutcome> outcomes) => JsonSerializer.Serialize(outcomes, ToolResultShaper.CompactJson);

    // Answers without a tool call that talk about functions or JSON: cosmetic, reported only.
    [GeneratedRegex(@"fun[cç][aã]o|function|ferramenta|JSON", RegexOptions.IgnoreCase)]
    private static partial Regex MetaText();
}
