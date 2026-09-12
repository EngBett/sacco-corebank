using Microsoft.AspNetCore.Routing;

namespace Sacco.Shared.Http;

/// <summary>Marker so the API host can discover and map every module's endpoints uniformly.</summary>
public interface IModuleEndpoints
{
    void Map(IEndpointRouteBuilder app);
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
