using DisputeDesk.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>
/// The real Api pipeline against a given database (none when null), in the given environment, with optional
/// extra configuration settings (as environment variables would supply them).
/// </summary>
public sealed class ApiFactory(
    string? connectionString,
    string environment = "Development",
    Action<IServiceCollection>? configureServices = null,
    IReadOnlyDictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        if (connectionString is not null)
        {
            builder.UseSetting($"ConnectionStrings:{DependencyInjection.ConnectionStringName}", connectionString);
        }

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        if (configureServices is not null)
        {
            builder.ConfigureTestServices(configureServices);
        }
    }
}
