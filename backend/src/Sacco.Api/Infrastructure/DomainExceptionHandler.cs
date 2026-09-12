using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Sacco.Shared.Domain;

namespace Sacco.Api.Infrastructure;

/// <summary>Maps domain outcomes to RFC 9457 ProblemDetails. Programming errors fall through to the default 500 handler.</summary>
public sealed class DomainExceptionHandler(IProblemDetailsService problemDetails, ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        if (exception is not DomainException domain) return false;

        var status = domain switch
        {
            NotFoundException => StatusCodes.Status404NotFound,
            ConflictException => StatusCodes.Status409Conflict,
            MakerCheckerViolationException or ForbiddenException => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status422UnprocessableEntity,
        };

        logger.LogInformation("Domain rule outcome {Code} ({Status}): {Message}", domain.Code, status, domain.Message);

        http.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = domain.Code,
                Detail = domain.Message,
                Type = $"urn:sacco:problem:{domain.Code}",
            },
        });
    }
}
