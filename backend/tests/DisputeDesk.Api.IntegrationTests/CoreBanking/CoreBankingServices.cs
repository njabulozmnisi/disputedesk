using DisputeDesk.Infrastructure.CoreBanking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DisputeDesk.Api.IntegrationTests.CoreBanking;

/// <summary>
/// The production registration of <c>ICoreBankingClient</c> and <c>ISimSwapCheck</c> (resilience handler included),
/// pointed at the given stub, with every log line captured.
/// </summary>
public static class CoreBankingServices
{
    public static ServiceProvider Build(Uri baseUrl, LogCapture? logs = null, Action<IServiceCollection>? configure = null)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CoreBanking:BaseUrl"] = baseUrl.ToString() })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            if (logs is not null)
            {
                logging.AddProvider(logs);
            }
        });
        services.AddCoreBanking();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }
}
