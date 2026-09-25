namespace PublicData.Api.Services;

internal sealed class NoopHostedService : IHostedService
{
    public static readonly NoopHostedService Instance = new();

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
