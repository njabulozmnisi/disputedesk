using System.Text.Json.Serialization;

namespace DisputeDesk.Contracts.Events;

/// <summary>
/// A CloudEvents 1.0 event in structured JSON mode (AD-19), written without an SDK. The attribute names are the
/// specification's lower-case ones; the shared camelCase policy already gives <c>id</c>, <c>source</c>, <c>type</c>,
/// <c>subject</c>, <c>time</c> and <c>data</c>, so only the three compound names are spelled out. <c>time</c> is a UTC
/// <see cref="DateTime"/> because System.Text.Json writes a UTC <see cref="DateTime"/> with <c>Z</c> and a
/// <see cref="DateTimeOffset"/> always with an offset such as <c>+00:00</c> (the consistency conventions: RFC 3339 UTC with Z).
/// </summary>
/// <typeparam name="TData">The event type's data record.</typeparam>
public sealed record CloudEvent<TData>(
    [property: JsonPropertyName("specversion")] string SpecVersion,
    string Id,
    string Source,
    string Type,
    string Subject,
    DateTime Time,
    [property: JsonPropertyName("datacontenttype")] string DataContentType,
    [property: JsonPropertyName("dataschema")] string DataSchema,
    TData Data);
