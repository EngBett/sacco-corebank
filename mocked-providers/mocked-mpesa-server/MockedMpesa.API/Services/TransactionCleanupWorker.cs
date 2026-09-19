using MongoDB.Driver;
using MockedMpesa.API.Data;
using MockedMpesa.API.Models;

namespace MockedMpesa.API.Services;

/// <summary>
/// Background worker that periodically deletes transactions older than 2 hours
/// to prevent the database from growing indefinitely in the mocked environment
/// </summary>
public class TransactionCleanupWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TransactionCleanupWorker> _logger;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(15); // Run every 15 minutes
    private readonly TimeSpan _retentionPeriod = TimeSpan.FromHours(2); // Keep transactions for 2 hours

    public TransactionCleanupWorker(
        IServiceProvider serviceProvider,
        ILogger<TransactionCleanupWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Transaction Cleanup Worker started. Running every {Interval} minutes, " +
            "deleting transactions older than {RetentionHours} hours",
            _cleanupInterval.TotalMinutes, _retentionPeriod.TotalHours);

        // Wait a bit before first run to allow app to initialize
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        using var timer = new PeriodicTimer(_cleanupInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupOldTransactionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during transaction cleanup");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Transaction Cleanup Worker is stopping");
                break;
            }
        }
    }

    private async Task CleanupOldTransactionsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MockMpesaDbContext>();

        var cutoffTime = DateTime.UtcNow.Subtract(_retentionPeriod);

        _logger.LogInformation(
            "Starting transaction cleanup. Deleting transactions created before {CutoffTime}",
            cutoffTime);

        var filter = Builders<TransactionRecord>.Filter.Lt(t => t.CreatedAt, cutoffTime);
        var result = await dbContext.Transactions.DeleteManyAsync(filter, cancellationToken);
        var deletedCount = result.DeletedCount;

        if (deletedCount > 0)
        {
            _logger.LogInformation(
                "Transaction cleanup completed. Deleted {DeletedCount} transactions older than {RetentionHours} hours",
                deletedCount, _retentionPeriod.TotalHours);
        }
        else
        {
            _logger.LogDebug(
                "Transaction cleanup completed. No transactions older than {RetentionHours} hours found",
                _retentionPeriod.TotalHours);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Transaction Cleanup Worker is stopping");
        return base.StopAsync(cancellationToken);
    }
}
