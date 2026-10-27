using System.Globalization;
using System.Net.Sockets;
using System.Text.Json;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Infrastructure.Sms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.CoreBanking;

/// <summary>The SMS sender delivers over SMTP to the configured host, Mailpit here, and nowhere else (AC6).</summary>
public sealed class SmtpSmsSenderTests(MailpitFixture mailpit) : IClassFixture<MailpitFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SendAsync_OtpMessage_LandsInTheConfiguredInbox()
    {
        await using ServiceProvider services = BuildServices(mailpit.Host, mailpit.Port);

        await services.GetRequiredService<ISmsSender>().SendAsync("+27820000004", "Your DisputeDesk code is 482913.", CancellationToken);

        using var http = new HttpClient { BaseAddress = mailpit.ApiUrl };
        using var inbox = JsonDocument.Parse(await http.GetStringAsync(new Uri("api/v1/messages", UriKind.Relative), CancellationToken));
        JsonElement message = Assert.Single(inbox.RootElement.GetProperty("messages").EnumerateArray());
        Assert.Equal("+27820000004@sms.disputedesk.test", Assert.Single(message.GetProperty("To").EnumerateArray()).GetProperty("Address").GetString());
        Assert.Contains("482913", message.GetProperty("Snippet").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_ConfiguredHostUnreachable_ThrowsInsteadOfSendingElsewhere()
    {
        // Nothing listens on port 1.
        await using ServiceProvider services = BuildServices("127.0.0.1", 1);

        await Assert.ThrowsAnyAsync<SocketException>(() =>
            services.GetRequiredService<ISmsSender>().SendAsync("+27820000004", "Your DisputeDesk code is 482913.", CancellationToken));
    }

    private static ServiceProvider BuildServices(string host, int port)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sms:Host"] = host,
                ["Sms:Port"] = port.ToString(CultureInfo.InvariantCulture),
                ["Sms:UseTls"] = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddSmsSender();
        return services.BuildServiceProvider();
    }
}
