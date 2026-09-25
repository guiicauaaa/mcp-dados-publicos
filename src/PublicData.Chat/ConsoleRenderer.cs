using System.Globalization;
using PublicData.Chat.Core.Llm;

namespace PublicData.Chat;

/// <summary>Colors and a "pensando… N s" status line, with a single lock so lines never interleave.</summary>
public sealed class ConsoleRenderer
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private readonly Lock _gate = new();
    private readonly bool _interactive = !Console.IsOutputRedirected;
    private bool _statusVisible;

    public void Ok(string text) => Line(ConsoleColor.Green, "[ok] " + text);

    public void Fail(string text) => Line(ConsoleColor.Red, "[erro] " + text);

    public void Warn(string text) => Line(ConsoleColor.Yellow, "[aviso] " + text);

    public void Info(string text) => Line(ConsoleColor.White, text);

    public void Dim(string text) => Line(ConsoleColor.DarkGray, text);

    public void Answer(string model, TimeSpan elapsed, string answer) =>
        Line(ConsoleColor.White, $"Assistente ({model}, {elapsed.TotalSeconds.ToString("0.0", PtBr)} s)> {answer}");

    public void Source(string sourceLine) => Line(ConsoleColor.DarkCyan, sourceLine);

    /// <summary>The proof that the query went through MCP: call (cyan), result (green) or error (yellow).</summary>
    public void Trace(ToolTraceEvent e)
    {
        var (color, text) = e.Kind switch
        {
            ToolTraceKind.Call => (ConsoleColor.Cyan, $"  [MCP] chamando {e.ToolName} {e.ArgumentsJson}"),
            ToolTraceKind.Result => (ConsoleColor.Green, $"  [MCP] {e.ToolName} -> ok em {e.ElapsedMs} ms: {e.ResultPreview}"),
            _ => (ConsoleColor.Yellow, $"  [MCP] {e.ToolName} -> erro em {e.ElapsedMs} ms: {e.ResultPreview}"),
        };
        Line(color, text);
    }

    public void ServerLog(string text) => Line(ConsoleColor.DarkGray, "    [mcp-server] " + text);

    /// <summary>Shows elapsed seconds while the model works; only on a real terminal.</summary>
    public async Task<T> WithStatusAsync<T>(Func<Task<T>> work)
    {
        if (!_interactive)
        {
            return await work();
        }

        using var stop = new CancellationTokenSource();
        var started = DateTime.UtcNow;
        var ticker = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(stop.Token).ConfigureAwait(false))
            {
                lock (_gate)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($"\r  pensando… {(int)(DateTime.UtcNow - started).TotalSeconds} s ");
                    Console.ResetColor();
                    _statusVisible = true;
                }
            }
        });

        try
        {
            return await work();
        }
        finally
        {
            await stop.CancelAsync();
            try
            {
                await ticker;
            }
            catch (OperationCanceledException)
            {
            }

            lock (_gate)
            {
                ClearStatus();
            }
        }
    }

    public void Prompt()
    {
        lock (_gate)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("\nVocê> ");
            Console.ResetColor();
        }
    }

    private void Line(ConsoleColor color, string text)
    {
        lock (_gate)
        {
            ClearStatus();
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ResetColor();
        }
    }

    private void ClearStatus()
    {
        if (_statusVisible)
        {
            Console.Write("\r" + new string(' ', 30) + "\r");
            _statusVisible = false;
        }
    }
}
