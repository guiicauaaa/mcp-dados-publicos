using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using PublicData.McpServer.Municipalities;
using PublicData.McpServer.Transferegov;

namespace PublicData.McpServer.Hosting;

public static class ServiceCollectionExtensions
{
    /// <summary>Everything the tools need. Also used by the tests, which swap the HTTP handler.</summary>
    public static IServiceCollection AddPublicDataServices(this IServiceCollection services, TransferegovTimeouts? timeouts = null)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(_ => MunicipalityDirectory.LoadEmbedded());
        services.AddMemoryCache();
        services.AddTransferegovClient(timeouts);
        return services;
    }

    public static IHttpClientBuilder AddTransferegovClient(this IServiceCollection services, TransferegovTimeouts? timeouts = null)
    {
        var t = timeouts ?? TransferegovTimeouts.Default;

        var builder = services
            .AddHttpClient<TransferegovClient>(client =>
            {
                client.BaseAddress = TransferegovClient.BaseAddress;
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("public-data-mcp", "1.0"));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            // gzip: a year of plans goes from ~233 KB to ~17 KB.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All });

        builder.AddStandardResilienceHandler(options =>
        {
            options.AttemptTimeout.Timeout = t.Attempt;
            options.TotalRequestTimeout.Timeout = t.Total;
            options.Retry.MaxRetryAttempts = t.MaxRetries;
            options.Retry.Delay = t.RetryDelay;
            // The package validates that the sampling window is at least twice the attempt timeout.
            options.CircuitBreaker.SamplingDuration = t.Attempt * 2 > TimeSpan.FromSeconds(30) ? t.Attempt * 2 : TimeSpan.FromSeconds(30);
        });

        return builder;
    }
}
