using AIStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AIStudio.Infrastructure.Health;

public sealed class DatabaseHealthCheck(
    IDbContextFactory<ApplicationDbContext> dbContextFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext =
                await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("PostgreSQL connection succeeded.")
                : new HealthCheckResult(
                    context.Registration.FailureStatus,
                    "PostgreSQL connection failed.");
        }
        catch (Exception exception)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                "PostgreSQL health check failed.",
                exception);
        }
    }
}
