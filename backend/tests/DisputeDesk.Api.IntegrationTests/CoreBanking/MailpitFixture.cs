using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace DisputeDesk.Api.IntegrationTests.CoreBanking;

/// <summary>A real Mailpit, the same image and digest as docker-compose.yml, standing in for the SMS gateway.</summary>
public sealed class MailpitFixture : IAsyncLifetime
{
    private const int SmtpPort = 1025;
    private const int ApiPort = 8025;

    private readonly IContainer _container =
        new ContainerBuilder("axllent/mailpit:v1.32.0@sha256:0790692be935ea4420d085694f7985b1f692ca6a8771ab0e0bd8ba75ac180498")
            .WithPortBinding(SmtpPort, assignRandomHostPort: true)
            .WithPortBinding(ApiPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(ApiPort).ForPath("/readyz")))
            .Build();

    public string Host => _container.Hostname;

    public int Port => _container.GetMappedPublicPort(SmtpPort);

    public Uri ApiUrl => new($"http://{_container.Hostname}:{_container.GetMappedPublicPort(ApiPort)}/");

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
