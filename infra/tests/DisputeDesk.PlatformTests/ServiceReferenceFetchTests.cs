using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DisputeDesk.PlatformTests;

/// <summary>
/// Story 8.16 criterion 5 (7.16 criterion 5, carried), offline: every fetch is bounded, credential-free and fails closed,
/// naming the URL. The answers come from a stub HttpMessageHandler, so no request leaves the machine.
/// </summary>
public sealed class ServiceReferenceFetchTests
{
    private const string Index = """[{ "service": "ssm", "url": "https://elsewhere.example/ssm.json" }, { "service": "s3" }]""";
    private const string GlobalKeys = """[{ "Name": "aws:SourceArn" }]""";
    private const string SsmFile = """{ "Actions": [{ "Name": "SendCommand" }], "ConditionKeys": [{ "Name": "ssm:resourceTag/${TagKey}" }] }""";
    private static readonly Uri SsmUrl = new("https://servicereference.us-east-1.amazonaws.com/v1/ssm/ssm.json");

    [Fact]
    public async Task Fetch_ValidFiles_ReadsTheNamedServicesFromTheDocumentedLayout()
    {
        var stub = new StubHandler(Answers(Index, GlobalKeys, SsmFile));
        using HttpClient client = ServiceReference.CreateClient(stub);

        // ec2 is not in the index, so no file is fetched for it; s3 is not named, so neither is its.
        ServiceReference reference = await ServiceReference.FetchAsync(client, TimeSpan.Zero, ["ssm", "ec2", "ssm"], TestContext.Current.CancellationToken);

        Assert.Equal(["s3", "ssm"], reference.Services.Order(StringComparer.Ordinal));
        Assert.Equal(["ssm"], reference.ActionsByService.Keys);
        Assert.Equal(["SendCommand"], reference.ActionsByService["ssm"]);
        Assert.Equal(["aws:SourceArn", "ssm:resourceTag/${TagKey}"], reference.ConditionKeyNames);
        Assert.Equal([ServiceReference.IndexUrl, ServiceReference.GlobalKeysUrl, SsmUrl], stub.Requests.Select(request => request.RequestUri));
        // HTTPS only, and no header but User-Agent: no credential, no cookie.
        Assert.All(stub.Requests, request =>
        {
            Assert.Equal(Uri.UriSchemeHttps, request.RequestUri?.Scheme);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(["User-Agent"], request.Headers.Select(header => header.Key));
            Assert.Equal("DisputeDesk-PlatformTests", request.Headers.UserAgent.ToString());
        });
    }

    [Fact]
    public void CreateClient_HasTheBoundsInTheStory()
    {
        using SocketsHttpHandler handler = ServiceReference.CreateHandler();
        using HttpClient client = ServiceReference.CreateClient(handler);

        Assert.Equal(TimeSpan.FromSeconds(30), client.Timeout);
        Assert.Equal(["User-Agent"], client.DefaultRequestHeaders.Select(header => header.Key));
        Assert.Equal(TimeSpan.FromSeconds(10), handler.ConnectTimeout);
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.Null(handler.Credentials);
        Assert.False(handler.PreAuthenticate);
        Assert.Equal(2, ServiceReference.MaxRetries);
    }

    [Theory]
    // Each file in a shape the check does not read.
    [InlineData("""{ "service": "ssm" }""", GlobalKeys, SsmFile, "https://servicereference.us-east-1.amazonaws.com/ is not an array of objects with a string service")]
    [InlineData("""[{ "serviceName": "ssm" }]""", GlobalKeys, SsmFile, "https://servicereference.us-east-1.amazonaws.com/ is not an array of objects with a string service")]
    [InlineData("""[{ "service": 5 }]""", GlobalKeys, SsmFile, "https://servicereference.us-east-1.amazonaws.com/ is not an array of objects with a string service")]
    [InlineData("""["ssm"]""", GlobalKeys, SsmFile, "https://servicereference.us-east-1.amazonaws.com/ is not an array of objects with a string service")]
    [InlineData("<html>", GlobalKeys, SsmFile, "https://servicereference.us-east-1.amazonaws.com/ is not JSON")]
    [InlineData(Index, """[{ "Name": null }]""", SsmFile, "https://servicereference.us-east-1.amazonaws.com/v1/global-condition-keys.json is not an array of objects with a string Name")]
    [InlineData(Index, """{}""", SsmFile, "https://servicereference.us-east-1.amazonaws.com/v1/global-condition-keys.json is not an array of objects with a string Name")]
    [InlineData(Index, GlobalKeys, """{ "Actions": [{ "Name": "SendCommand" }] }""", "https://servicereference.us-east-1.amazonaws.com/v1/ssm/ssm.json is not an object with Actions and ConditionKeys arrays")]
    [InlineData(Index, GlobalKeys, """[]""", "https://servicereference.us-east-1.amazonaws.com/v1/ssm/ssm.json is not an object with Actions and ConditionKeys arrays")]
    [InlineData(Index, GlobalKeys, """{ "Actions": [{ "Name": 1 }], "ConditionKeys": [] }""", "https://servicereference.us-east-1.amazonaws.com/v1/ssm/ssm.json is not an array of objects with a string Name")]
    [InlineData(Index, GlobalKeys, """{ "Actions": [], "ConditionKeys": {} }""", "https://servicereference.us-east-1.amazonaws.com/v1/ssm/ssm.json is not an array of objects with a string Name")]
    public async Task Fetch_FileOfTheWrongShape_FailsNamingTheUrl(string index, string globalKeys, string ssmFile, string expected)
    {
        using HttpClient client = ServiceReference.CreateClient(new StubHandler(Answers(index, globalKeys, ssmFile)));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => ServiceReference.FetchAsync(client, TimeSpan.Zero, ["ssm"], TestContext.Current.CancellationToken));

        Assert.StartsWith(expected, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    // A redirect is not followed and not retried, nor is any other answer that is not a success or a retried status.
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Fetch_HttpErrorNotRetried_FailsAtOnceNamingTheUrl(HttpStatusCode status)
    {
        var stub = new StubHandler(_ => Task.FromResult(Answer(status)));
        using HttpClient client = ServiceReference.CreateClient(stub);

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() => FetchIndexAsync(client));

        Assert.Equal($"Could not fetch {ServiceReference.IndexUrl}: HTTP {(int)status}", exception.Message);
        Assert.Single(stub.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Fetch_RetriedStatusTwiceThenSuccess_Passes(HttpStatusCode status)
    {
        var stub = new StubHandler(FailingThen(failures: 2, () => Answer(status)));
        using HttpClient client = ServiceReference.CreateClient(stub);

        Assert.Equal(Index, await FetchIndexAsync(client));
        Assert.Equal(3, stub.Requests.Count);
    }

    [Fact]
    public async Task Fetch_RetriedStatusThreeTimes_FailsAfterTwoRetriesNamingTheUrl()
    {
        var stub = new StubHandler(FailingThen(failures: 3, () => Answer(HttpStatusCode.ServiceUnavailable)));
        using HttpClient client = ServiceReference.CreateClient(stub);

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() => FetchIndexAsync(client));

        Assert.Equal($"Could not fetch {ServiceReference.IndexUrl}: HTTP 503", exception.Message);
        Assert.Equal(3, stub.Requests.Count);
    }

    [Fact]
    public async Task Fetch_ConnectionFailsTwiceThenSucceeds_Passes()
    {
        var stub = new StubHandler(FailingThen(failures: 2, () => throw new HttpRequestException("Connection refused")));
        using HttpClient client = ServiceReference.CreateClient(stub);

        Assert.Equal(Index, await FetchIndexAsync(client));
        Assert.Equal(3, stub.Requests.Count);
    }

    [Fact]
    public async Task Fetch_ConnectionFailsThreeTimes_FailsNamingTheUrl()
    {
        // As offline: the request never reaches a server.
        var stub = new StubHandler(_ => throw new HttpRequestException("Name or service not known"));
        using HttpClient client = ServiceReference.CreateClient(stub);

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() => FetchIndexAsync(client));

        Assert.Equal($"Could not fetch {ServiceReference.IndexUrl}: Name or service not known", exception.Message);
        Assert.Equal(3, stub.Requests.Count);
    }

    [Fact]
    public async Task Fetch_RealClientAgainstAServerThatClosesTheConnection_FailsNamingTheUrl()
    {
        // The real handler: a local listener accepts each connection and closes it at once, so the TLS handshake fails.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        CancellationToken cancellation = TestContext.Current.CancellationToken;
        var closer = Task.Run(async () =>
        {
            while (!cancellation.IsCancellationRequested)
            {
                using TcpClient connection = await listener.AcceptTcpClientAsync(cancellation);
            }
        }, cancellation);
        var url = new Uri($"https://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
        using HttpClient client = ServiceReference.CreateClient(ServiceReference.CreateHandler());

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => ServiceReference.FetchAsync(client, TimeSpan.Zero, url, cancellation));

        Assert.StartsWith($"Could not fetch {url}: ", exception.Message, StringComparison.Ordinal);
        // The accept loop ends, with an exception, once the listener stops.
        listener.Stop();
        await Assert.ThrowsAnyAsync<Exception>(() => closer);
    }

    [Fact]
    public async Task Fetch_NoAnswerWithinTheRequestTimeoutThreeTimes_FailsNamingTheUrl()
    {
        // The client's own Timeout ends each attempt; 30 s in the real client (CreateClient_HasTheBoundsInTheStory).
        var stub = new StubHandler(Hang);
        using HttpClient client = ServiceReference.CreateClient(stub);
        client.Timeout = TimeSpan.FromMilliseconds(20);

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() => FetchIndexAsync(client));

        Assert.Equal($"Could not fetch {ServiceReference.IndexUrl}: no answer within 00:00:00.0200000", exception.Message);
        Assert.Equal(3, stub.Requests.Count);
    }

    [Fact]
    public async Task Fetch_DeadlinePassesDuringARequest_FailsNamingTheInterruptedUrl()
    {
        // The index and global keys arrive; the service file never does, and the deadline ends the check there.
        var stub = new StubHandler(request => request.RequestUri == SsmUrl ? Hang(request) : Answers(Index, GlobalKeys, SsmFile)(request));
        using HttpClient client = ServiceReference.CreateClient(stub);
        client.Timeout = Timeout.InfiniteTimeSpan;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(100));

        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(
            () => ServiceReference.FetchAsync(client, TimeSpan.Zero, ["ssm"], deadline.Token));

        Assert.Equal($"The check's deadline passed while fetching {SsmUrl}", exception.Message);
    }

    [Fact]
    public async Task Fetch_DeadlinePassesWhileWaitingToRetry_FailsNamingTheUrl()
    {
        var stub = new StubHandler(_ => Task.FromResult(Answer(HttpStatusCode.ServiceUnavailable)));
        using HttpClient client = ServiceReference.CreateClient(stub);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(100));

        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(
            () => ServiceReference.FetchAsync(client, TimeSpan.FromHours(1), ServiceReference.IndexUrl, deadline.Token));

        Assert.Equal($"The check's deadline passed while fetching {ServiceReference.IndexUrl}", exception.Message);
        Assert.Single(stub.Requests);
    }

    private static Task<string> FetchIndexAsync(HttpClient client) =>
        ServiceReference.FetchAsync(client, TimeSpan.Zero, ServiceReference.IndexUrl, TestContext.Current.CancellationToken);

    private static HttpResponseMessage Answer(HttpStatusCode status, string body = "") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>The three files, each by its URL.</summary>
    private static Func<HttpRequestMessage, Task<HttpResponseMessage>> Answers(string index, string globalKeys, string ssmFile) => request =>
    {
        string body = request.RequestUri?.ToString() switch
        {
            "https://servicereference.us-east-1.amazonaws.com/" => index,
            "https://servicereference.us-east-1.amazonaws.com/v1/global-condition-keys.json" => globalKeys,
            "https://servicereference.us-east-1.amazonaws.com/v1/ssm/ssm.json" => ssmFile,
            _ => "",
        };
        return Task.FromResult(Answer(body.Length == 0 ? HttpStatusCode.NotFound : HttpStatusCode.OK, body));
    };

    /// <summary>The first FAILURES requests get FAIL's answer (or its exception); every later one gets the index.</summary>
    private static Func<HttpRequestMessage, Task<HttpResponseMessage>> FailingThen(int failures, Func<HttpResponseMessage> fail)
    {
        int requests = 0;
        return _ => Task.FromResult(++requests <= failures ? fail() : Answer(HttpStatusCode.OK, Index));
    }

    /// <summary>An answer that never comes.</summary>
    private static Task<HttpResponseMessage> Hang(HttpRequestMessage request) => new TaskCompletionSource<HttpResponseMessage>().Task;

    /// <summary>Answers each request with RESPOND and records it (the documented way to test code that uses HttpClient).</summary>
    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Task<HttpResponseMessage> answer = respond(request);
            return await answer.WaitAsync(cancellationToken);
        }
    }
}
