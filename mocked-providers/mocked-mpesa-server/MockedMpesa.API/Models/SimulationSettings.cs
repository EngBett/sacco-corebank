using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MockedMpesa.API.Models;

/// <summary>
/// Global settings to control mock M-Pesa behavior for testing
/// Stored in database as single-row configuration
/// </summary>
public class SimulationSettings
{
    /// <summary>
    /// Primary key (always 1 for singleton pattern)
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;
    
    /// <summary>
    /// When true, all callbacks will return failure (unless overridden per-transaction)
    /// </summary>
    public bool GlobalSimulateFailure { get; set; }
    
    /// <summary>
    /// When true, all callbacks will be skipped (unless overridden per-transaction)
    /// </summary>
    public bool GlobalSkipCallback { get; set; }
    
    /// <summary>
    /// Minimum delay in milliseconds before sending callback
    /// </summary>
    public int MinDelayMs { get; set; } = 2000;
    
    /// <summary>
    /// Maximum delay in milliseconds before sending callback
    /// </summary>
    public int MaxDelayMs { get; set; } = 5000;
    
    /// <summary>
    /// M-Pesa result code to use when simulating failures
    /// </summary>
    public int FailureResultCode { get; set; } = 1;
    
    /// <summary>
    /// Result description to use when simulating failures
    /// </summary>
    public string FailureResultDesc { get; set; } = "Insufficient funds in the account";
    
    /// <summary>
    /// Last updated timestamp
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
