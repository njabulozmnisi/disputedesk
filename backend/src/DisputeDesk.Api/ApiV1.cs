namespace DisputeDesk.Api;

/// <summary>The versioned API surface. Feature stories map their endpoints onto this group.</summary>
public static class ApiV1
{
    public static RouteGroupBuilder MapApiV1(this IEndpointRouteBuilder app) =>
        app.MapGroup("/api/v1")
            .ProducesProblem(StatusCodes.Status500InternalServerError);
}
