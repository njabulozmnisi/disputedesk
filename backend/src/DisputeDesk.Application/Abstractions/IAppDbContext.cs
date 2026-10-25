namespace DisputeDesk.Application.Abstractions;

/// <summary>
/// The unit of work that Application features use: EF Core directly, no repository wrapper.
/// Only Infrastructure implements it. Feature stories add their DbSet properties here.
/// </summary>
public interface IAppDbContext
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
