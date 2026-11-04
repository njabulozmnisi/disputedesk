using System.Globalization;
using System.Text.Json.Nodes;

namespace DisputeDesk.PlatformTests;

/// <summary>One HTTP response as curl -D - prints it: the status, every header line in order, and the body.</summary>
internal sealed record Reply(int Status, IReadOnlyList<KeyValuePair<string, string>> Headers, string Body)
{
    public static Reply Parse(string curlOutput)
    {
        string text = curlOutput.Replace("\r\n", "\n", StringComparison.Ordinal);
        // An interim 100 Continue comes first when curl sent Expect: 100-continue.
        while (text.StartsWith("HTTP/1.1 100 ", StringComparison.Ordinal))
        {
            text = text[(text.IndexOf("\n\n", StringComparison.Ordinal) + 2)..];
        }

        int end = text.IndexOf("\n\n", StringComparison.Ordinal);
        string[] head = (end < 0 ? text : text[..end]).Split('\n');
        var headers = head.Skip(1)
            .Select(line => new KeyValuePair<string, string>(line[..line.IndexOf(':', StringComparison.Ordinal)].Trim(), line[(line.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim()))
            .ToList();
        return new Reply(int.Parse(head[0].Split(' ')[1], CultureInfo.InvariantCulture), headers, end < 0 ? "" : text[(end + 2)..]);
    }

    /// <summary>The values of every header with this name, in order (names compared without case, RFC 9110 5.1).</summary>
    public string[] Values(string name) =>
        [.. Headers.Where(header => header.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(header => header.Value)];

    public override string ToString() =>
        $"{Status}\n{string.Join('\n', Headers.Select(header => $"{header.Key}: {header.Value}"))}";
}

/// <summary>What the echo server received: its port, the path, the HTTP version and every request header exactly as sent.</summary>
internal sealed record Echoed(string Port, string Uri, string Version, IReadOnlyList<KeyValuePair<string, string>> Headers)
{
    public static Echoed Parse(string body)
    {
        JsonNode echo = JsonNode.Parse(body) ?? throw new InvalidOperationException($"The echo sent no JSON: {body}");
        var headers = echo["headers"]!.AsArray()
            .Select(pair => new KeyValuePair<string, string>(pair![0]!.GetValue<string>(), pair[1]!.GetValue<string>()))
            .ToList();
        return new Echoed(echo["port"]!.GetValue<string>(), echo["uri"]!.GetValue<string>(), echo["version"]!.GetValue<string>(), headers);
    }

    /// <summary>The value of the one header with this name, or null when it was not sent.</summary>
    public string? Header(string name) =>
        Headers.SingleOrDefault(header => header.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    /// <summary>Every header name the upstream received, lower-case and sorted.</summary>
    public string[] Names() => [.. Headers.Select(header => header.Key.ToLowerInvariant()).Order(StringComparer.Ordinal)];

    public override string ToString() => string.Join(", ", Headers.Select(header => $"{header.Key}: {header.Value}"));
}
