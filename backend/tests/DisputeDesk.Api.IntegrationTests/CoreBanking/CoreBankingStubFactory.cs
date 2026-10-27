extern alias CoreBankingStub;

using CoreBankingStub::DisputeDesk.CoreBankingStub;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DisputeDesk.Api.IntegrationTests.CoreBanking;

/// <summary>
/// The real core-banking stub on a loopback port, reading the real <c>fixtures/local.json</c>, with its clock fixed at
/// <see cref="ClockNow"/>. Everything it logs is captured in <see cref="Logs"/>.
/// </summary>
public sealed class CoreBankingStubFactory : WebApplicationFactory<Bank>
{
    /// <summary>01:00 on 26 October 2026 in South Africa, while it is still 25 October in UTC.</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 25, 23, 0, 0, TimeSpan.Zero);

    /// <summary>The start of the South African day that <see cref="Now"/> falls in.</summary>
    public static readonly DateTimeOffset StartOfToday = new(2026, 10, 26, 0, 0, 0, TimeSpan.FromHours(2));

    public CoreBankingStubFactory() => UseKestrel(0);

    public LogCapture Logs { get; } = new();

    /// <summary>The instant the stub's clock reads: <see cref="Now"/> unless set.</summary>
    public DateTimeOffset ClockNow { get; init; } = Now;

    public Uri BaseUrl
    {
        get
        {
            StartServer();
            string address = Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First();
            return new Uri(address);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging => logging.AddProvider(Logs).AddFilter<LogCapture>(category: null, LogLevel.Trace));
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(new FixedTimeProvider(ClockNow)));
    }
}
