using DisputeDesk.Infrastructure.CoreBanking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DisputeDesk.Api.IntegrationTests.CoreBanking;

/// <summary>
/// The production registration of <c>ICoreBankingClient</c> and <c>ISimSwapCheck</c> (resilience handler included),
/// pointed at the given stub, with every log record captured in the <c>FakeLogCollector</c> (<c>GetFakeLogCollector()</c>).
/// </summary>
public static class CoreBankingServices
{
    public static ServiceProvider Build(Uri baseUrl, Action<IServiceCollection>? configure = null)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CoreBanking:BaseUrl"] = baseUrl.ToString() })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddFakeLogging());
        services.AddCoreBanking();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }
}
