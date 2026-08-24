using FdeTutor.Persistence;

namespace FdeTutor.ProjectionWorker;

public sealed class Worker(
    ILogger<Worker> logger,
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
            try
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
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "S083 projection batch failed.");
                await Task.Delay(idleDelay, stoppingToken);
            }
        }
    }
}
