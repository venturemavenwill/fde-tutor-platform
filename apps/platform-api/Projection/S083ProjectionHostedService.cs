using FdeTutor.Persistence;

namespace FdeTutor.Api.Projection;

public sealed class S083ProjectionHostedService(
    IServiceScopeFactory serviceScopeFactory,
    IConfiguration configuration)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerId = $"{Environment.MachineName}:{Environment.ProcessId}";
        var batchSize = configuration.GetValue("Projection:BatchSize", 100);
        var idleDelay = TimeSpan.FromMilliseconds(
            configuration.GetValue("Projection:IdleDelayMilliseconds", 1000));

        while (!stoppingToken.IsCancellationRequested)
        {
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider
                .GetRequiredService<S083ProjectionBatchProcessor>();
            var count = await processor.ProcessBatchAsync(
                workerId,
                batchSize,
                stoppingToken);
            if (count == 0)
            {
                await Task.Delay(idleDelay, stoppingToken);
            }
        }
    }
}
