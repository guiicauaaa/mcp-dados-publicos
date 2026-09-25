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
}

/// <summary>
/// Runs the Ollama preflight in the background (server up, derived model created, warm-up) and retries until
/// it works, so the API starts even when Ollama is still loading or not installed yet.
/// </summary>
public sealed class OllamaGateway(ChatSettings settings, ILogger<OllamaGateway> logger) : BackgroundService, IModelGateway
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(15);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile ModelStatus _status = new(ComponentState.Starting, settings.OllamaBaseUrl.ToString(), settings.Model, null, null, false, "Verificando o Ollama…");
    private IChatClient? _client;

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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested && _status.State != ComponentState.Ready)
        {
            await TryPreflightAsync(stoppingToken);
            if (_status.State != ComponentState.Ready)
            {
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }

    private async Task TryPreflightAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_status.State == ComponentState.Ready)
            {
                return;
            }

            var http = new HttpClient { BaseAddress = settings.OllamaBaseUrl, Timeout = settings.OllamaHttpTimeout };
            var ollama = new OllamaApiClient(http, settings.Model);
            var result = await OllamaPreflight.RunAsync(ollama, settings, message => logger.LogInformation("{Message}", message), cancellationToken);

            _client = ollama;
            _status = new ModelStatus(ComponentState.Ready, settings.OllamaBaseUrl.ToString(), settings.Model, result.EffectiveModel,
                result.OllamaVersion, result.DerivedModelCreated, result.Warning);
            logger.LogInformation("Ollama {Version} pronto com {Model} (aquecimento {Seconds:0.0} s)",
                result.OllamaVersion, result.EffectiveModel, result.WarmupTime.TotalSeconds);
        }
        catch (Exception ex) when (ex is OllamaUnavailableException or ModelNotInstalledException or HttpRequestException)
        {
            _status = _status with { State = ComponentState.Unavailable, Message = ex.Message };
            logger.LogWarning("Ollama indisponível: {Message}", ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }
}
