using System.Globalization;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

public static class MetricTags
{
    /// <summary>The measurement's tags as text, sorted by name, for comparing with an expected list.</summary>
    public static (string Name, string Value)[] SortedTags<T>(this CollectedMeasurement<T> measurement)
        where T : struct =>
        [.. measurement.Tags.Select(tag => (tag.Key, Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? "")).Order()];
}
