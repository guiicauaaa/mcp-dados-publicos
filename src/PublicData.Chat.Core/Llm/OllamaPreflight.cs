using System.Diagnostics;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;

namespace PublicData.Chat.Core.Llm;

public sealed record PreflightResult(
    string OllamaVersion,
    string RequestedModel,
    string EffectiveModel,
    bool DerivedModelCreated,
    TimeSpan WarmupTime,
    string? Warning);

public sealed class OllamaUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class ModelNotInstalledException(string model)
    : Exception($"O modelo '{model}' não está instalado no Ollama. Rode: ollama pull {model}")
{
    public string Model { get; } = model;
}

/// <summary>
/// Checks the Ollama server, creates the derived model when needed and warms the model up, so the first
/// question does not pay the model load.
/// </summary>
public static class OllamaPreflight
{
    public static async Task<PreflightResult> RunAsync(
        OllamaApiClient ollama,
        ChatSettings settings,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string version;
        try
        {
            version = (await ollama.GetVersionAsync(cancellationToken)).ToString();
        }
        catch (HttpRequestException ex)
        {
            throw new OllamaUnavailableException(
                $"Não consegui falar com o Ollama em {settings.OllamaBaseUrl}. Abra o app do Ollama ou rode 'ollama serve'.", ex);
        }

        var installed = (await ollama.ListLocalModelsAsync(cancellationToken)).Select(m => Normalize(m.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requested = settings.Model;
        var effective = requested;
        var created = false;
        string? warning = null;

        if (string.Equals(requested, DerivedModel.Name, StringComparison.OrdinalIgnoreCase) && !installed.Contains(Normalize(requested)))
        {
            if (!installed.Contains(Normalize(DerivedModel.BaseModel)))
            {
                throw new ModelNotInstalledException(DerivedModel.BaseModel);
            }

            progress?.Invoke($"Criando {DerivedModel.Name} a partir do {DerivedModel.BaseModel} (mesmos pesos; só o template de ferramentas muda)…");
            try
            {
                await foreach (var _ in ollama.CreateModelAsync(new CreateModelRequest
                {
                    Model = DerivedModel.Name,
                    From = DerivedModel.BaseModel,
                    Template = DerivedModel.LoadTemplate(),
                }, cancellationToken))
                {
                }

                created = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                effective = DerivedModel.BaseModel;
                warning = $"Não consegui criar o modelo derivado ({ex.Message}). Usando {DerivedModel.BaseModel} original; " +
                          $"rode 'ollama create {DerivedModel.Name} -f ollama/Modelfile'.";
            }
        }
        else if (!installed.Contains(Normalize(requested)))
        {
            throw new ModelNotInstalledException(requested);
        }

        var stopwatch = Stopwatch.StartNew();
        await foreach (var _ in ollama.ChatAsync(new ChatRequest
        {
            Model = effective,
            Messages = [],
            KeepAlive = settings.KeepAlive,
            // Same num_ctx as the chat requests: Ollama reloads the model when the runner options change.
            Options = new RequestOptions { NumCtx = settings.NumCtx },
        }, cancellationToken))
        {
        }

        return new PreflightResult(version, requested, effective, created, stopwatch.Elapsed, warning);
    }

    /// <summary>Ollama lists "llama3.2:latest"; the user types "llama3.2".</summary>
    public static string Normalize(string model) =>
        model.EndsWith(":latest", StringComparison.OrdinalIgnoreCase) ? model[..^":latest".Length] : model;
}
