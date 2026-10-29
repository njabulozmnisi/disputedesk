using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DisputeDesk.Api.IntegrationTests.CoreBanking;

/// <summary>
/// The stub's minimal-hosting entry point starts Kestrel on its own thread, so Kestrel can be running before
/// <c>WebApplicationFactory</c> returns from building the host. These tests force that order, which CI met by chance.
/// </summary>
public sealed class CoreBankingStubStartTests
{
    [Fact]
    public async Task BaseUrl_KestrelStartedBeforeTheBuildReturns_ServesOnALoopbackPort()
    {
        await using var stub = new StartedBeforeTheBuildReturnsStubFactory();

        Uri baseUrl = stub.BaseUrl;

        Assert.Equal("127.0.0.1", baseUrl.Host);
        Assert.NotEqual(0, baseUrl.Port);
        using HttpClient client = stub.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri(baseUrl, "/health"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed class StartedBeforeTheBuildReturnsStubFactory : CoreBankingStubFactory
    {
        protected override IHost CreateHost(IHostBuilder builder) => base.CreateHost(new WaitForStartBuilder(builder));
    }

    /// <summary>Builds as the wrapped builder does, then returns only once the app's own thread has started the host.</summary>
    private sealed class WaitForStartBuilder(IHostBuilder inner) : IHostBuilder
    {
        public IDictionary<object, object> Properties => inner.Properties;

        public IHost Build()
        {
            IHost host = inner.Build();
            bool started = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));
            Assert.True(started, "The stub's entry point did not start the host within 30 seconds.");
            return host;
        }

        public IHostBuilder ConfigureAppConfiguration(Action<HostBuilderContext, IConfigurationBuilder> configureDelegate) =>
            inner.ConfigureAppConfiguration(configureDelegate);

        public IHostBuilder ConfigureContainer<TContainerBuilder>(Action<HostBuilderContext, TContainerBuilder> configureDelegate) =>
            inner.ConfigureContainer(configureDelegate);

        public IHostBuilder ConfigureHostConfiguration(Action<IConfigurationBuilder> configureDelegate) =>
            inner.ConfigureHostConfiguration(configureDelegate);

        public IHostBuilder ConfigureServices(Action<HostBuilderContext, IServiceCollection> configureDelegate) =>
            inner.ConfigureServices(configureDelegate);

        public IHostBuilder UseServiceProviderFactory<TContainerBuilder>(IServiceProviderFactory<TContainerBuilder> factory)
            where TContainerBuilder : notnull =>
            inner.UseServiceProviderFactory(factory);

        public IHostBuilder UseServiceProviderFactory<TContainerBuilder>(Func<HostBuilderContext, IServiceProviderFactory<TContainerBuilder>> factory)
            where TContainerBuilder : notnull =>
            inner.UseServiceProviderFactory(factory);
    }
}
