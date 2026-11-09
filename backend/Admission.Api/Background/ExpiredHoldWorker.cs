using Admission.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Admission.Api.Background;

public sealed class ExpiredHoldWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ExpiredHoldWorker> logger,
    TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AdmissionDbContext>();
                var now = timeProvider.GetUtcNow();
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "AdmissionHolds"
                    SET "Status" = 'EXPIRED'
                    WHERE "Status" = 'HELD' AND "ExpiresAt" <= {now}
                    """, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to expire admission holds.");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}
