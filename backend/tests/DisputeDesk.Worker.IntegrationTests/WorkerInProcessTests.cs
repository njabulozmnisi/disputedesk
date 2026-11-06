using System.Diagnostics;
using DisputeDesk.Infrastructure.Outbox;
using DisputeDesk.Infrastructure.Persistence;
using DisputeDesk.Infrastructure.Sla;
using DisputeDesk.Worker.IntegrationTests.Outbox;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace DisputeDesk.Worker.IntegrationTests;

/// <summary>
/// The Api host runs the outbox relay and the SLA sweep when <c>Worker:InProcess</c> is true, as the local stack sets it,
/// and neither when it is unset, as in AWS, where the Worker runs them (AD-27). In the relay group: the relay's lock key
/// and heartbeat file are the same in every host.
/// </summary>
[Collection(OutboxRelayTestGroup.Name)]
public class WorkerInProcessTests(OutboxFixture outbox)
{
    private static readonly TimeSpan ShutdownBudget = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ApiHost_WorkerInProcessTrue_RunsTheRelayAndTheSweepAndPublishes()
    {
        DateTime start = DateTime.UtcNow.AddSeconds(-1);
        // Earlier tests leave the files dated by their fake clocks, which can be ahead of real time.
        File.Delete(OutboxRelay.HeartbeatPath);
        File.Delete(SlaSweep.HeartbeatPath);
        OutboxMessage row = await outbox.RaiseAsync(TimeProvider.System.GetUtcNow());
        await using ApiHostFactory api = ApiHost(new() { ["Worker:InProcess"] = "true" });

        Assert.Equal([typeof(OutboxRelay), typeof(SlaSweep)], RelayAndSweep(api));
        await outbox.WaitUntilSentAsync([row.Id]);
        Assert.Single(outbox.RecordsFor(row.DisputeId));
        // AD-25: the Api host's relay and sweep touch the same heartbeat files the Compose health check reads.
        Assert.True(File.GetLastWriteTimeUtc(OutboxRelay.HeartbeatPath) >= start, "The relay's heartbeat file was not touched.");
        Assert.True(File.GetLastWriteTimeUtc(SlaSweep.HeartbeatPath) >= start, "The sweep's heartbeat file was not touched.");

        // AD-25: the host stops both within its shutdown timeout; idle, they stop well inside it.
        var stopping = Stopwatch.StartNew();
        await api.DisposeAsync();
        Assert.True(stopping.Elapsed < ShutdownBudget, $"Stopping took {stopping.Elapsed}.");
    }

    [Fact]
    public async Task ApiHost_WorkerInProcessTrueOnAnEmptyDatabase_StartsTheSweepOnlyOnceTheMigrationsAreIn()
    {
        string emptyDatabase = await CreateEmptyDatabaseAsync();
        // AD-22: the probe starts just before the sweep and records which migrations are still pending at that moment.
        await using ApiHostFactory api = ApiHost(
            new() { ["Worker:InProcess"] = "true", ["ConnectionStrings:DisputeDesk"] = emptyDatabase },
            services => InsertBeforeTheSweep(services, new MigrationsAtSweepStart(emptyDatabase)));

        MigrationsAtSweepStart probe = api.Services.GetRequiredService<MigrationsAtSweepStart>();

        Assert.NotNull(probe.Pending);
        Assert.Empty(probe.Pending);
    }

    [Fact]
    public async Task ApiHost_WorkerInProcessUnset_RunsNeitherTheRelayNorTheSweep()
    {
        await using ApiHostFactory api = ApiHost([]);

        Assert.Empty(RelayAndSweep(api));
    }

    /// <summary>The Api against the fixture's database and broker, with the settings its start-up validation needs.</summary>
    private ApiHostFactory ApiHost(Dictionary<string, string?> settings, Action<IServiceCollection>? configureServices = null)
    {
        Dictionary<string, string?> all = new()
        {
            ["ConnectionStrings:DisputeDesk"] = outbox.ConnectionString,
            ["Kafka:BootstrapServers"] = outbox.BootstrapServers,
            ["Zitadel:Issuer"] = "https://zitadel.test",
            ["Zitadel:WebApp:ClientId"] = "web-app-client",
            ["Zitadel:WebApp:ClientSecret"] = "web-app-secret",
            ["Zitadel:ProjectId"] = "project-test",
            // Nothing listens on port 1.
            ["CoreBanking:BaseUrl"] = "http://127.0.0.1:1",
            ["Sms:Host"] = "127.0.0.1",
            ["Sms:Port"] = "1",
            // 32 bytes of 0x01, base64.
            ["Linking:OtpHmacKey"] = "AQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQE=",
            ["ForwardedHeaders:TrustedNetwork"] = "10.201.89.0/24",
            ["ForwardedHeaders:AllowedHosts"] = "localhost",
        };
        foreach ((string key, string? value) in settings)
        {
            all[key] = value;
        }

        return new ApiHostFactory(all, configureServices);
    }

    /// <summary>A new database on the fixture's server with no table in it; returns its connection string.</summary>
    private async Task<string> CreateEmptyDatabaseAsync()
    {
        string name = $"empty_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(outbox.ConnectionString))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        return new NpgsqlConnectionStringBuilder(outbox.ConnectionString) { Database = name, Pooling = false }.ConnectionString;
    }

    /// <summary>Registers <paramref name="probe"/> as the hosted service the host starts just before the sweep (registration order).</summary>
    private static void InsertBeforeTheSweep(IServiceCollection services, MigrationsAtSweepStart probe)
    {
        services.AddSingleton(probe);
        int sweep = services.ToList().FindIndex(descriptor =>
            descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(SlaSweep));
        services.Insert(sweep, ServiceDescriptor.Singleton<IHostedService>(provider => provider.GetRequiredService<MigrationsAtSweepStart>()));
    }

    /// <summary>The relay and the sweep among the started host's hosted services, in registration order.</summary>
    private static List<Type> RelayAndSweep(WebApplicationFactory<Program> api) =>
        [.. api.Services.GetServices<IHostedService>().Select(service => service.GetType()).Where(type => type == typeof(OutboxRelay) || type == typeof(SlaSweep))];

    /// <summary>The real Api host in Development, with <paramref name="settings"/> as its configuration.</summary>
    private sealed class ApiHostFactory(Dictionary<string, string?> settings, Action<IServiceCollection>? configureServices)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            foreach ((string key, string? value) in settings)
            {
                builder.UseSetting(key, value);
            }

            if (configureServices is not null)
            {
                builder.ConfigureTestServices(configureServices);
            }
        }
    }

    /// <summary>A hosted service that records, as it starts, the migrations not yet applied to the Api's database.</summary>
    private sealed class MigrationsAtSweepStart(string connectionString) : IHostedService
    {
        public IReadOnlyList<string>? Pending { get; private set; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
            Pending = [.. await db.Database.GetPendingMigrationsAsync(cancellationToken)];
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
