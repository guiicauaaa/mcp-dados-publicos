using PublicData.Chat.Core;
using PublicData.Chat.Core.Mcp;

namespace PublicData.Api.Services;

public interface IMcpGateway
{
    McpStatus Status { get; }

    /// <summary>Null while the server is not connected; retries right away in that case.</summary>
    Task<McpConnection?> GetConnectionAsync(CancellationToken cancellationToken);

    /// <summary>stderr of the server process (stdio mode): the proof that the call ran in another process.</summary>
    IReadOnlyList<ServerLogLine> LogSince(long sequence);

    Task ReconnectAsync(CancellationToken cancellationToken);
}

/// <summary>
/// One long-lived MCP session for the whole API: stdio child process (local development) or Streamable HTTP
/// service (Docker Compose). Retries until the server answers, since in Compose it may start after the API.
/// </summary>
public sealed class McpGateway(ChatSettings settings, ILoggerFactory loggerFactory) : BackgroundService, IMcpGateway, IAsyncDisposable
{
    private const int MaxLogLines = 300;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly ILogger<McpGateway> _logger = loggerFactory.CreateLogger<McpGateway>();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly LinkedList<ServerLogLine> _log = new();
    private long _sequence;
    private McpConnection? _connection;
    private volatile McpStatus _status = new(ComponentState.Starting, settings.Mcp.Transport, null, null, null, null, [], "Conectando ao servidor MCP…");

    public McpStatus Status => _status;

    public async Task<McpConnection?> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is not null)
        {
            return _connection;
        }

        await TryConnectAsync(cancellationToken);
        return _connection;
    }

    public IReadOnlyList<ServerLogLine> LogSince(long sequence)
    {
        lock (_log)
        {
            return [.. _log.Where(l => l.Sequence > sequence)];
        }
    }

    public async Task ReconnectAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }
        }
        finally
        {
            _gate.Release();
        }

        await TryConnectAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var connection = await GetConnectionAsync(stoppingToken);
            if (connection is null)
            {
                await Task.Delay(RetryDelay, stoppingToken);
                continue;
            }

            // Completes when the session ends: the stdio server process died or the transport closed.
            var details = await connection.Client.Completion.WaitAsync(stoppingToken);
            await DropAsync(connection, $"A sessão MCP terminou ({details.Exception?.Message ?? "o servidor encerrou a conexão"}). Reconectando…");
            await Task.Delay(RetryDelay, stoppingToken);
        }
    }

    /// <summary>Forgets a dead session, unless it was already replaced (ReconnectAsync also ends the old one).</summary>
    private async Task DropAsync(McpConnection connection, string message)
    {
        await _gate.WaitAsync();
        try
        {
            if (!ReferenceEquals(_connection, connection))
            {
                return;
            }

            _connection = null;
            _status = _status with { State = ComponentState.Unavailable, Message = message };
            _logger.LogWarning("{Message}", message);
        }
        finally
        {
            _gate.Release();
        }

        await connection.DisposeAsync();
    }

    private async Task TryConnectAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is not null)
            {
                return;
            }

            var connection = await McpConnection.ConnectAsync(settings.Mcp, AppendLog, loggerFactory, cancellationToken);
            _connection = connection;
            var offered = settings.EffectiveExposedTools;
            _status = new McpStatus(ComponentState.Ready, connection.Transport, connection.Endpoint, connection.ServerName,
                connection.ServerVersion, connection.ProtocolVersion,
                [.. connection.Tools.Select(t => new McpToolInfo(t.Name, t.Title, t.Description, t.JsonSchema, offered.Contains(t.Name)))],
                null);
            _logger.LogInformation("MCP conectado via {Transport}: {Server} {Version}, protocolo {Protocol}, {Tools} ferramentas",
                connection.Transport, connection.ServerName, connection.ServerVersion, connection.ProtocolVersion, connection.Tools.Count);
        }
        catch (McpStartupException ex)
        {
            _status = _status with { State = ComponentState.Unavailable, Message = ex.Message };
            _logger.LogWarning("MCP indisponível: {Message}", ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void AppendLog(string line)
    {
        var entry = ServerLogFilter.Parse(line);
        lock (_log)
        {
            _log.AddLast(new ServerLogLine(++_sequence, DateTimeOffset.UtcNow, entry.Level, entry.Category, entry.Message, entry.IsServerOwn));
            while (_log.Count > MaxLogLines)
            {
                _log.RemoveFirst();
            }
        }
    }

    /// <summary>Stops the stdio child process with the API (the container disposes singletons asynchronously).</summary>
    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }

        Dispose();
    }
}
