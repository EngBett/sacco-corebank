using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MockedMpesa.API.Attributes;
using MockedMpesa.API.Data;
using MockedMpesa.API.Models;
using MockedMpesa.API.Services;

namespace MockedMpesa.API.Controllers;

/// <summary>
/// Admin endpoints to control simulation behavior and manage transactions
/// </summary>
[ApiController]
[Route("api/admin")]
[ValidateBearerToken]
public class AdminController : ControllerBase
{
    private readonly MockMpesaDbContext _dbContext;
    private readonly ICallbackService _callbackService;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        MockMpesaDbContext dbContext,
        ICallbackService callbackService,
        ILogger<AdminController> logger)
    {
        _dbContext = dbContext;
        _callbackService = callbackService;
        _logger = logger;
    }

    /// <summary>
    /// Get all transactions
    /// </summary>
    [HttpGet("transactions")]
    public async Task<IActionResult> GetTransactions([FromQuery] int? skip = null, [FromQuery] int? take = null)
    {
        var skipValue = skip ?? 0;
        var takeValue = take ?? 100;

        var transactions = await _dbContext.Transactions
            .Find(FilterDefinition<TransactionRecord>.Empty)
            .SortByDescending(t => t.CreatedAt)
            .Skip(skipValue)
            .Limit(takeValue)
            .ToListAsync();

        return Ok(new
        {
            count = transactions.Count,
            transactions
        });
    }

    /// <summary>
    /// Get specific transaction by ID
    /// </summary>
    [HttpGet("transactions/{id}")]
    public async Task<IActionResult> GetTransaction(string id)
    {
        var filter = Builders<TransactionRecord>.Filter.Eq(t => t.Id, id);
        var transaction = await _dbContext.Transactions.Find(filter).FirstOrDefaultAsync();
        if (transaction == null)
            return NotFound(new { error = "Transaction not found" });

        return Ok(transaction);
    }

    /// <summary>
    /// Set SimulateFailure flag for specific transaction
    /// </summary>
    [HttpPut("transactions/{id}/simulate-failure")]
    public async Task<IActionResult> SetSimulateFailure(string id, [FromBody] bool value)
    {
        var filter = Builders<TransactionRecord>.Filter.Eq(t => t.Id, id);
        var update = Builders<TransactionRecord>.Update.Set(t => t.SimulateFailure, value);
        var result = await _dbContext.Transactions.UpdateOneAsync(filter, update);
        
        if (result.MatchedCount == 0)
            return NotFound(new { error = "Transaction not found" });

        return Ok(new { message = $"SimulateFailure set to {value} for transaction {id}" });
    }

    /// <summary>
    /// Set SkipCallback flag for specific transaction
    /// </summary>
    [HttpPut("transactions/{id}/skip-callback")]
    public async Task<IActionResult> SetSkipCallback(string id, [FromBody] bool value)
    {
        var filter = Builders<TransactionRecord>.Filter.Eq(t => t.Id, id);
        var update = Builders<TransactionRecord>.Update.Set(t => t.SkipCallback, value);
        var result = await _dbContext.Transactions.UpdateOneAsync(filter, update);
        
        if (result.MatchedCount == 0)
            return NotFound(new { error = "Transaction not found" });

        return Ok(new { message = $"SkipCallback set to {value} for transaction {id}" });
    }

    /// <summary>
    /// Retry callback for a transaction
    /// </summary>
    [HttpPost("transactions/{id}/retry-callback")]
    public async Task<IActionResult> RetryCallback(string id)
    {
        var filter = Builders<TransactionRecord>.Filter.Eq(t => t.Id, id);
        var transaction = await _dbContext.Transactions.Find(filter).FirstOrDefaultAsync();
        if (transaction == null)
            return NotFound(new { error = "Transaction not found" });

        if (string.IsNullOrEmpty(transaction.CallbackUrl))
            return BadRequest(new { error = "Transaction has no callback URL" });

        _logger.LogInformation("Retrying callback for transaction {TransactionId}", id);

        try
        {
            // Retry callback based on transaction type
            switch (transaction.TransactionType)
            {
                case "StkPush":
                    await _callbackService.SendStkPushCallbackAsync(
                        transaction.CallbackUrl,
                        transaction.MerchantRequestId,
                        transaction.CheckoutRequestId ?? string.Empty,
                        transaction.Amount,
                        transaction.PhoneNumber ?? string.Empty,
                        transaction.AccountReference ?? string.Empty,
                        transaction.ShortCode ?? string.Empty);
                    break;
                default:
                    return BadRequest(new { error = $"Retry not supported for transaction type: {transaction.TransactionType}" });
            }

            return Ok(new { message = $"Callback retry initiated for transaction {id}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retry callback for transaction {TransactionId}", id);
            return StatusCode(500, new { error = "Failed to retry callback", details = ex.Message });
        }
    }

    /// <summary>
    /// Get current simulation settings
    /// </summary>
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        var settings = await _dbContext.SimulationSettings
            .Find(FilterDefinition<SimulationSettings>.Empty)
            .FirstOrDefaultAsync();
        
        if (settings == null)
        {
            return NotFound(new { error = "Settings not found. Please restart the service to seed default settings." });
        }
        
        return Ok(settings);
    }
    
    /// <summary>
    /// Update simulation settings
    /// </summary>
    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] SimulationSettings updatedSettings)
    {
        var settings = await _dbContext.SimulationSettings
            .Find(FilterDefinition<SimulationSettings>.Empty)
            .FirstOrDefaultAsync();
        
        if (settings == null)
        {
            return NotFound(new { error = "Settings not found" });
        }
        
        var filter = Builders<SimulationSettings>.Filter.Eq(s => s.Id, settings.Id);
        var update = Builders<SimulationSettings>.Update
            .Set(s => s.GlobalSimulateFailure, updatedSettings.GlobalSimulateFailure)
            .Set(s => s.GlobalSkipCallback, updatedSettings.GlobalSkipCallback)
            .Set(s => s.MinDelayMs, updatedSettings.MinDelayMs)
            .Set(s => s.MaxDelayMs, updatedSettings.MaxDelayMs)
            .Set(s => s.FailureResultCode, updatedSettings.FailureResultCode)
            .Set(s => s.FailureResultDesc, updatedSettings.FailureResultDesc)
            .Set(s => s.UpdatedAt, DateTime.UtcNow);
        
        await _dbContext.SimulationSettings.UpdateOneAsync(filter, update);
        
        // Fetch updated settings to return
        settings = await _dbContext.SimulationSettings.Find(filter).FirstOrDefaultAsync();
        
        _logger.LogInformation("Simulation settings updated");
        
        return Ok(settings);
    }

    /// <summary>
    /// Get count of transactions by callback status
    /// </summary>
    [HttpGet("statistics")]
    public async Task<IActionResult> GetStatistics()
    {
        var total = await _dbContext.Transactions.CountDocumentsAsync(FilterDefinition<TransactionRecord>.Empty);
        var skipped = await _dbContext.Transactions.CountDocumentsAsync(
            Builders<TransactionRecord>.Filter.Eq(t => t.CallbackStatus, "Skipped"));
        var success = await _dbContext.Transactions.CountDocumentsAsync(
            Builders<TransactionRecord>.Filter.Eq(t => t.CallbackStatus, "Success"));
        var failed = await _dbContext.Transactions.CountDocumentsAsync(
            Builders<TransactionRecord>.Filter.Eq(t => t.CallbackStatus, "Failed"));
        var pending = await _dbContext.Transactions.CountDocumentsAsync(
            Builders<TransactionRecord>.Filter.Eq(t => t.CallbackStatus, null));

        return Ok(new
        {
            total,
            skipped,
            success,
            failed,
            pending
        });
    }
}
