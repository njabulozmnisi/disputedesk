using System.ComponentModel.DataAnnotations;
using DisputeDesk.Api.Errors;
using DisputeDesk.Application.Transactions;
using DisputeDesk.Contracts.Transactions;
using DisputeDesk.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DisputeDesk.Api.Transactions;

/// <summary><c>GET /api/v1/transactions</c>, on the customer routes.</summary>
public static class TransactionEndpoints
{
    public static IServiceCollection AddTransactions(this IServiceCollection services) =>
        services.AddScoped<ListTransactions>();

    public static RouteGroupBuilder MapTransactions(this RouteGroupBuilder customerRoutes)
    {
        customerRoutes.MapGet("/transactions", ListAsync)
            .WithTags("Transactions")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return customerRoutes;
    }

    /// <summary>
    /// The linked customer's transactions on all their accounts, newest first by transaction date then transaction ID.
    /// <paramref name="limit"/> is 1 to 50 and defaults to 20; pass a page's <c>nextCursor</c> back as
    /// <paramref name="cursor"/> for the next page. 503 <c>core-banking-unavailable</c> when core banking is still down
    /// after the retries.
    /// </summary>
    /// <remarks>
    /// <paramref name="limit"/> is checked by built-in validation (<c>[Range]</c> gives 400 with <c>errors.limit</c>);
    /// a value that is not a whole number fails to bind and gets the same 400. The <c>Linked</c> policy runs in
    /// the authorization middleware, before binding and validation, so an unlinked login gets 403 first (FR-24).
    /// </remarks>
    private static async Task<Results<Ok<TransactionPageResponse>, ProblemHttpResult>> ListAsync(
        ListTransactions handler,
        string? cursor,
        [Range(1, ListTransactions.MaxPageSize)] int limit = ListTransactions.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        Result<TransactionPageResponse> result = await handler.HandleAsync(cursor, limit, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }
}
