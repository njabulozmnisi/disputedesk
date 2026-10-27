using System.Text.Json;
using System.Text.Json.Serialization;

namespace DisputeDesk.Contracts.Json;

/// <summary>
/// The one set of JSON settings for API bodies and dispute events (AD-5): camelCase property names and camelCase
/// enum values, so <c>UnderReview</c> is written as <c>underReview</c>.
/// </summary>
public static class DisputeDeskJson
{
    public static void Apply(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }
}
