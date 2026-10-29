using Microsoft.EntityFrameworkCore;

namespace DisputeDesk.Application.Abstractions;

/// <summary>
/// The unit of work that Application features use: EF Core directly, no repository wrapper (AD-2). Only Infrastructure
/// implements it. Features reach their entities through <see cref="Entities{TEntity}"/>, so no story edits this interface.
/// </summary>
public interface IAppDbContext
{
    /// <summary>
    /// The EF Core set of <typeparamref name="TEntity"/>, which is <c>DbContext.Set&lt;TEntity&gt;()</c>. Not named
    /// <c>Set</c> because CA1716 refuses a keyword of another .NET language as an interface member name.
    /// </summary>
    DbSet<TEntity> Entities<TEntity>() where TEntity : class;

    /// <summary>
    /// Commits every tracked change in one transaction, with the stored event of each dispute change and, for a published
    /// type, its outbox row (AD-14). Throws <see cref="UniqueViolationException"/> when a unique index that stands for an expected failure
    /// refuses the commit (AD-15).
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
