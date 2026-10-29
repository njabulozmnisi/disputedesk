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
    /// <summary>
    /// The shared, read-only instance with these settings, for every body the application serialises itself: outbox
    /// payloads, stored event data and stored responses (Microsoft Learn, "Configure JsonSerializerOptions": reuse one
    /// instance). The Api applies the same settings to the framework's own instance with <see cref="Apply"/>.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static void Apply(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new SingleNameEnumConverter());
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Apply(options);
        // The reflection resolver, as the JsonSerializer methods use it, so the instance can be frozen.
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
