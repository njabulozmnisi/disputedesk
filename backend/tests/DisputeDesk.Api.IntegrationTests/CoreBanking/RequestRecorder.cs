using System.Collections.Concurrent;

namespace DisputeDesk.Api.IntegrationTests.CoreBanking;

/// <summary>Records the URL, headers and body of every request an HttpClient sends.</summary>
public sealed class RequestRecorder : DelegatingHandler
{
    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        string headers = string.Join('\n', request.Headers.Select(header => $"{header.Key}: {string.Join(',', header.Value)}"));
        Requests.Enqueue(new RecordedRequest(request.RequestUri?.ToString() ?? string.Empty, headers, body));
        return await base.SendAsync(request, cancellationToken);
    }
}
