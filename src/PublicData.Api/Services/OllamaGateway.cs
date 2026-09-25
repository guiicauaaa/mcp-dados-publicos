using Microsoft.Extensions.AI;
using OllamaSharp;
using PublicData.Chat.Core;
using PublicData.Chat.Core.Llm;

namespace PublicData.Api.Services;

public interface IModelGateway
{
    ModelStatus Status { get; }

    /// <summary>Null until the preflight succeeds.</summary>
    IChatClient? Client { get; }

    string? Model { get; }

    /// <summary>Retries the preflight right away when the model is not ready yet.</summary>
    Task<bool> EnsureReadyAsync(CancellationToken cancellationToken);

    /// <summary>A chat request failed talking to Ollama: show it in /api/status until the next successful check.</summary>
    void ReportFailure(string message);
}

/// <summary>
/// Runs the Ollama preflight in the background (server up, derived model created, warm-up) and retries until
/// it works, so the API starts even while Ollama is loading or not installed yet. Once ready, a cheap version
/// check every 30 s keeps /api/status honest if Ollama is closed.
/// </summary>
public sealed class OllamaGateway(ChatSettings settings, ILogger<OllamaGateway> logger) : BackgroundService, IModelGateway
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HealthInterval = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile ModelStatus _status = new(ComponentState.Starting, settings.OllamaBaseUrl.ToString(), settings.Model, null, null, false, "Verificando o Ollama…");
    private OllamaApiClient? _client;

    public ModelStatus Status => _status;

    public IChatClient? Client => _client;

    public string? Model => _status.EffectiveModel;

    public async Task<bool> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (_status.State == ComponentState.Ready)
        {
            return true;
        }

        await TryPreflightAsync(cancellationToken);
        return _status.State == ComponentState.Ready;
    }

    public void ReportFailure(string message) =>
        _status = _status with { State = ComponentState.Unavailable, Message = $"Falha ao falar com o Ollama: {message}" };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_status.State == ComponentState.Ready && _client is { } client)
            {
                await Task.Delay(HealthInterval, stoppingToken);
                await CheckHealthAsync(client, stoppingToken);
                continue;
            }

            await TryPreflightAsync(stoppingToken);
            if (_status.State != ComponentState.Ready)
            {
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }

    private async Task CheckHealthAsync(OllamaApiClient client, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await client.GetVersionAsync(timeout.Token);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Next loop runs the full preflight again (and warms the model up when Ollama is back).
            ReportFailure(ex.Message);
            logger.LogWarning("Ollama deixou de responder: {Message}", ex.Message);
        }
    }

    private async Task TryPreflightAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        HttpClient? candidateHttp = null;
        try
        {
            if (_status.State == ComponentState.Ready)
            {
                return;
            }

            candidateHttp = new HttpClient { BaseAddress = settings.OllamaBaseUrl, Timeout = settings.OllamaHttpTimeout };
            var candidate = new OllamaApiClient(candidateHttp, settings.Model);
            var result = await OllamaPreflight.RunAsync(candidate, settings, message => logger.LogInformation("{Message}", message), cancellationToken);

            // The previous client (if any) is replaced but not disposed: a chat turn may still be using it.
            _client = candidate;
            candidateHttp = null;
            _status = new ModelStatus(ComponentState.Ready, settings.OllamaBaseUrl.ToString(), settings.Model, result.EffectiveModel,
                result.OllamaVersion, result.DerivedModelCreated, result.Warning);
            logger.LogInformation("Ollama {Version} pronto com {Model} (aquecimento {Seconds:0.0} s)",
                result.OllamaVersion, result.EffectiveModel, result.WarmupTime.TotalSeconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Any failure (unreachable, model missing, a non-Ollama service answering, warm-up timeout) keeps the
            // API up and shows the reason; an exception escaping a BackgroundService would stop the whole host.
            _status = _status with { State = ComponentState.Unavailable, Message = ex.Message };
            logger.LogWarning("Ollama indisponível: {Message}", ex.Message);
        }
        finally
        {
            candidateHttp?.Dispose(); // failed attempt: nothing else holds this HttpClient
            _gate.Release();
        }
    }

    public override void Dispose()
    {
        _gate.Dispose();
        base.Dispose();
    }
}
