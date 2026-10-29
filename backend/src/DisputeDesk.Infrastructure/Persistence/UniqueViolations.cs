using DisputeDesk.Domain;
using DisputeDesk.Domain.Disputes;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DisputeDesk.Infrastructure.Persistence;

/// <summary>
/// The one place a unique index that stands for an expected failure becomes its <see cref="Error"/> (AD-15). Matched
/// by the constraint name Postgres reports, so a violation of any other index stays an unexpected failure.
/// </summary>
internal static class UniqueViolations
{
    private static readonly Dictionary<string, Error> ErrorsByIndex = new(StringComparer.Ordinal)
    {
        [DisputeConfiguration.OneDisputePerTransaction] = DisputeErrors.AlreadyDisputed,
        [StoredEventConfiguration.OneEventPerVersion] = DisputeErrors.VersionClash,
    };

    /// <summary>The error for a commit that failed on one of the mapped unique indexes, otherwise null.</summary>
    public static Error? ErrorFor(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: string index }
            && ErrorsByIndex.TryGetValue(index, out Error? error)
            ? error
            : null;
}
