using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.Jobs;

public sealed class JobWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<JobWorkerOptions> options,
    ILogger<JobWorker> logger) : BackgroundService
{
    private readonly JobWorkerOptions workerOptions = options.Value;
    private readonly string workerId =
        $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!workerOptions.Enabled)
        {
            logger.LogInformation("Persistent job worker is disabled.");
            return;
        }

        logger.LogInformation(
            "Persistent job worker {WorkerId} started with poll interval {PollInterval} and lease {LeaseDuration}.",
            workerId,
            workerOptions.PollInterval,
            workerOptions.LeaseDuration);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var processedJob = false;

                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var processor = scope.ServiceProvider.GetRequiredService<JobProcessor>();
                    processedJob = await processor.ProcessNextAsync(
                        workerId,
                        workerOptions.LeaseDuration,
                        stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Persistent job worker {WorkerId} polling cycle failed.",
                        workerId);
                }

                if (!processedJob)
                {
                    await Task.Delay(
                        workerOptions.PollInterval,
                        TimeProvider.System,
                        stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            logger.LogInformation("Persistent job worker {WorkerId} stopped.", workerId);
        }
    }
}
