using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MockedMpesa.API.Attributes;
using MockedMpesa.API.Data;
using MockedMpesa.API.Models;

namespace MockedMpesa.API.Controllers;

[ApiController]
[Route("mpesa/c2b/v1")]
[ValidateBearerToken]
public class C2BController : ControllerBase
{
    private readonly MockMpesaDbContext _dbContext;
    private readonly ILogger<C2BController> _logger;

    public C2BController(MockMpesaDbContext dbContext, ILogger<C2BController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpPost("registerurl")]
    public IActionResult RegisterUrl([FromBody] C2BRegisterRequest request)
    {
        _logger.LogInformation(
            "C2B Register URL request - ShortCode: {ShortCode}, Validation: {ValidationUrl}, Confirmation: {ConfirmationUrl}",
            request.ShortCode, request.ValidationURL, request.ConfirmationURL);

        var conversationId = $"AG_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString()[..8]}";
        var originatorConversationId = $"OG_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString()[..8]}";

        // Save registration asynchronously (fire-and-forget)
        _ = Task.Run(async () =>
        {
            try
            {
                // Check if registration already exists
                var filter = Builders<C2BRegistration>.Filter.Eq(r => r.ShortCode, request.ShortCode);
                var existingRegistration = await _dbContext.C2BRegistrations
                    .Find(filter)
                    .FirstOrDefaultAsync();

                if (existingRegistration != null)
                {
                    // Update existing registration
                    var update = Builders<C2BRegistration>.Update
                        .Set(r => r.ValidationUrl, request.ValidationURL)
                        .Set(r => r.ConfirmationUrl, request.ConfirmationURL)
                        .Set(r => r.ResponseType, request.ResponseType)
                        .Set(r => r.UpdatedAt, DateTime.UtcNow);
                    await _dbContext.C2BRegistrations.UpdateOneAsync(filter, update);
                    _logger.LogDebug("Updated C2B registration for ShortCode: {ShortCode}", request.ShortCode);
                }
                else
                {
                    // Create new registration
                    var registration = new C2BRegistration
                    {
                        ShortCode = request.ShortCode,
                        ValidationUrl = request.ValidationURL,
                        ConfirmationUrl = request.ConfirmationURL,
                        ResponseType = request.ResponseType,
                        RegisteredAt = DateTime.UtcNow
                    };
                    await _dbContext.C2BRegistrations.InsertOneAsync(registration);
                    _logger.LogDebug("Created C2B registration for ShortCode: {ShortCode}", request.ShortCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save C2B registration for ShortCode: {ShortCode}", request.ShortCode);
            }
        });

        return Ok(new C2BRegisterResponse
        {
            ConversationID = conversationId,
            OriginatorCoversationID = originatorConversationId,
            ResponseDescription = "success"
        });
    }
}
