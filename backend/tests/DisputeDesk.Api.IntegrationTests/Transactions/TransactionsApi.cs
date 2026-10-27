using DisputeDesk.Api.IntegrationTests.CoreBanking;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Transactions;

/// <summary>The Api on its own freshly migrated database, so each test links exactly the logins it needs.</summary>
public static class TransactionsApi
{
    public const string CustomerA = "CUS-000001";
    public const string CustomerB = "CUS-000002";

    /// <summary>The Api with its clock fixed at <paramref name="now"/>, calling core banking at <paramref name="coreBankingUrl"/>.</summary>
    public static async Task<ApiFactory> StartAsync(
        PostgresFixture postgres, Uri coreBankingUrl, DateTimeOffset now, Action<IServiceCollection>? configureServices = null) =>
        new(
            await postgres.CreateMigratedDatabaseAsync(),
            configureServices: configureServices,
            settings: new Dictionary<string, string?> { ["CoreBanking:BaseUrl"] = coreBankingUrl.ToString() },
            clock: new FixedTimeProvider(now));

    /// <summary>The Api against the stub, both reading the stub's instant.</summary>
    public static Task<ApiFactory> StartAsync(PostgresFixture postgres, TransactionsStub stub, Action<IServiceCollection>? configureServices = null) =>
        StartAsync(postgres, stub.BaseUrl, stub.Now, configureServices);

    /// <summary>Links the login <paramref name="loginSub"/> to the customer, as linking or the seeder would.</summary>
    public static async Task LinkAsync(this ApiFactory factory, string loginSub, string customerId)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Set<CustomerLink>().Add(new CustomerLink
        {
            Id = Guid.NewGuid(),
            LoginSub = loginSub,
            CustomerId = customerId,
            LinkedAtUtc = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow(),
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
