using System.Text.Json;

namespace DisputeDesk.Contracts.Json;

/// <summary>
/// The one set of JSON settings for API bodies and dispute events (AD-5): camelCase property names and camelCase
/// enum values, so <c>UnderReview</c> is written as <c>underReview</c>. An enum is read only from exactly one of its
/// names: a number such as <c>3</c> or a list such as <c>"a, b"</c> is not a value of the wire contract, so it fails to
/// bind like any other unknown value.
/// </summary>
public static class DisputeDeskJson
{
    public static void Apply(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new SingleNameEnumConverter());
    }
}
