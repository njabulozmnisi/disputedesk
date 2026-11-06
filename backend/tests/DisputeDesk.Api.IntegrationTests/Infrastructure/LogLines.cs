using Microsoft.Extensions.Logging.Testing;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>The records a <see cref="FakeLogCollector"/> holds, as text a privacy assertion can search.</summary>
public static class LogLines
{
    /// <summary>Every log line as written, followed by each of its structured values as <c>name=value</c>.</summary>
    public static List<string> Lines(this FakeLogCollector logs) => [.. logs.GetSnapshot().SelectMany(LinesOf)];

    private static IEnumerable<string> LinesOf(FakeLogRecord record)
    {
        yield return record.Message;
        foreach ((string key, string? value) in record.StructuredState ?? [])
        {
            yield return $"{key}={value}";
        }
    }
}
