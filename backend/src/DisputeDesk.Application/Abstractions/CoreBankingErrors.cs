using DisputeDesk.Domain;

namespace DisputeDesk.Application.Abstractions;

/// <summary>The expected failures of <see cref="ICoreBankingClient"/>. The codes are AD-3 problem slugs.</summary>
public static class CoreBankingErrors
{
    public static readonly Error NotFound = Error.NotFound("not-found", "Core banking has no such record for this customer.");

    public static readonly Error InvalidPaging = Error.Validation("validation", "The cursor or limit is not valid.");

    public static readonly Error Unavailable = Error.Unavailable("core-banking-unavailable", "Core banking is unavailable.");
}
