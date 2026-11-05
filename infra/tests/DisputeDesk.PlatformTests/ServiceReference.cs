using System.Net;
using System.Text.Json;

namespace DisputeDesk.PlatformTests;

/// <summary>
/// What the IAM names check reads from AWS's programmatic service reference (AWS Service Authorization Reference,
/// "Simplified AWS service information for programmatic access"): public JSON over HTTPS with no credentials, so no AWS
/// API is called. Services are the index's service prefixes; ActionsByService holds each fetched service's
/// Actions[].Name; ConditionKeyNames the global keys' and every fetched service's ConditionKeys[].Name.
/// </summary>
internal sealed record ServiceReference(IReadOnlySet<string> Services, IReadOnlyDictionary<string, string[]> ActionsByService, IReadOnlyList<string> ConditionKeyNames)
{
    public static readonly Uri IndexUrl = new("https://servicereference.us-east-1.amazonaws.com/");
    public static readonly Uri GlobalKeysUrl = new(IndexUrl, "v1/global-condition-keys.json");

    /// <summary>Each fetch makes at most this many retries, as story 7.16's curl --retry 2 did.</summary>
    public const int MaxRetries = 2;

    /// <summary>The first retry waits this long and the second twice as long, curl --retry's back-off.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The one client, over CreateHandler(): 30 s a request in all, body included (HttpClient.Timeout). Its one header
    /// is User-Agent, a product token (RFC 9110, section 10.1.5), because the reference answers 403 to a request with
    /// none, as curl's own User-Agent did for story 7.16; no credential, cookie or other header is sent.
    /// </summary>
    public static HttpClient CreateClient(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DisputeDesk-PlatformTests");
        return client;
    }

    /// <summary>10 s to connect (SocketsHttpHandler.ConnectTimeout); no redirect followed, no cookie, no credential.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        Credentials = null,
        ConnectTimeout = TimeSpan.FromSeconds(10),
    };

    /// <summary>
    /// Fetches the index, the global condition keys and v1/&lt;service&gt;/&lt;service&gt;.json for each named service in the
    /// index (the documented layout, never a URL taken from the index). Fails naming the URL when a fetch fails or a
    /// file has the wrong shape, and when the deadline passes during a fetch.
    /// </summary>
    public static async Task<ServiceReference> FetchAsync(HttpClient client, TimeSpan retryDelay, IEnumerable<string> services, CancellationToken deadline)
    {
        HashSet<string> index;
        using (JsonDocument document = await FetchJsonAsync(client, retryDelay, IndexUrl, deadline))
        {
            index = [.. Strings(document.RootElement, "service", IndexUrl)];
        }

        List<string> conditionKeys;
        using (JsonDocument document = await FetchJsonAsync(client, retryDelay, GlobalKeysUrl, deadline))
        {
            conditionKeys = Strings(document.RootElement, "Name", GlobalKeysUrl);
        }

        Dictionary<string, string[]> actions = [];
        foreach (string service in services.Distinct().Where(index.Contains).Order(StringComparer.Ordinal))
        {
            var url = new Uri(IndexUrl, $"v1/{service}/{service}.json");
            using JsonDocument document = await FetchJsonAsync(client, retryDelay, url, deadline);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("Actions", out JsonElement serviceActions)
                || !root.TryGetProperty("ConditionKeys", out JsonElement serviceKeys))
            {
                throw new InvalidDataException($"{url} is not an object with Actions and ConditionKeys arrays");
            }

            actions[service] = [.. Strings(serviceActions, "Name", url)];
            conditionKeys.AddRange(Strings(serviceKeys, "Name", url));
        }

        return new ServiceReference(index, actions, conditionKeys);
    }

    /// <summary>One GET with at most two retries; the deadline, when it passes, ends it naming the URL.</summary>
    public static async Task<string> FetchAsync(HttpClient client, TimeSpan retryDelay, Uri url, CancellationToken deadline)
    {
        try
        {
            return await FetchWithRetriesAsync(client, retryDelay, url, deadline);
        }
        catch (OperationCanceledException exception) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException($"The check's deadline passed while fetching {url}", exception);
        }
    }

    /// <summary>
    /// GET is idempotent, so it may be retried (RFC 9110, section 9.2.2). Retried, as curl --retry does: a connection
    /// failure, a request that times out, and HTTP 408, 429, 500, 502, 503 or 504. Any other answer that is not a
    /// success, a redirect included, fails at once.
    /// </summary>
    private static async Task<string> FetchWithRetriesAsync(HttpClient client, TimeSpan retryDelay, Uri url, CancellationToken deadline)
    {
        string failure = "";
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(retryDelay * attempt, deadline);
            }

            try
            {
                using HttpResponseMessage response = await client.GetAsync(url, deadline);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync(deadline);
                }

                failure = $"HTTP {(int)response.StatusCode}";
                if (!IsRetried(response.StatusCode))
                {
                    break;
                }
            }
            catch (HttpRequestException exception)
            {
                failure = exception.Message;
            }
            catch (TaskCanceledException) when (!deadline.IsCancellationRequested)
            {
                failure = $"no answer within {client.Timeout}";
            }
        }

        throw new HttpRequestException($"Could not fetch {url}: {failure}");
    }

    private static bool IsRetried(HttpStatusCode status) => status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
        or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    private static async Task<JsonDocument> FetchJsonAsync(HttpClient client, TimeSpan retryDelay, Uri url, CancellationToken deadline)
    {
        string body = await FetchAsync(client, retryDelay, url, deadline);
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{url} is not JSON: {exception.Message}", exception);
        }
    }

    /// <summary>The string PROPERTY of each object in the array; anything else fails naming the URL.</summary>
    private static List<string> Strings(JsonElement array, string property, Uri url)
    {
        string shape = $"{url} is not an array of objects with a string {property}";
        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(shape);
        }

        List<string> strings = [];
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty(property, out JsonElement value)
                || value.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException(shape);
            }

            strings.Add(value.GetString() ?? "");
        }

        return strings;
    }
}
