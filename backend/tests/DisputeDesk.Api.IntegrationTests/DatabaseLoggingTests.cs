using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DisputeDesk.Api.IntegrationTests;

/// <summary>Npgsql spans already carry the SQL, so EF Core's per-command logs stay quiet and never carry values.</summary>
public class DatabaseLoggingTests(PostgresFixture postgres)
{
    [Fact]
    public async Task EfCoreCommandLog_DefaultConfiguration_StartsAtWarning()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);

        // The logger applies the configured filter rules for its category, as every EF Core command log would.
        ILogger logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Microsoft.EntityFrameworkCore.Database.Command");

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
    }

    [Fact]
    public async Task EfCoreOptions_DefaultConfiguration_SensitiveDataLoggingIsOff()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();

        DbContextOptions<AppDbContext> options = scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();

        Assert.False(options.FindExtension<CoreOptionsExtension>()?.IsSensitiveDataLoggingEnabled ?? false);
    }
}
