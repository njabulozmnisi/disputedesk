using System.Text.Json;
using System.Text.Json.Serialization;

namespace DisputeDesk.Contracts.Json;

/// <summary>
/// The one enum rule of the wire contract (AD-5): writes a member's camelCase name, and reads an enum only from
/// exactly that name (<c>upheld</c>, never <c>Upheld</c>). The built-in <see cref="JsonStringEnumConverter"/> reads names
/// ignoring case and has no option to stop it (Microsoft Learn, <i>JsonStringEnumConverter</i>: "Reading is case
/// insensitive"), so this is a custom converter (Microsoft Learn, <i>How to write custom converters</i>). A number, a list
/// or any other spelling fails to bind. <see cref="CamelCaseNameOnlyEnumConverterFactory"/> creates one per enum type.
/// </summary>
public sealed class CamelCaseNameOnlyEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"A {typeof(TEnum).Name} value is a string.");
        }

        string? name = reader.GetString();
        TEnum[] matches = [.. Enum.GetValues<TEnum>().Where(value => string.Equals(WireName(value), name, StringComparison.Ordinal))];
        if (matches.Length != 1)
        {
            throw new JsonException($"A {typeof(TEnum).Name} value is the camelCase name of one of its members.");
        }

        return matches[0];
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        // A value that is no member has no name: refused, as the built-in converter with integer values off refuses it,
        // rather than written as its number.
        if (!Enum.IsDefined(value))
        {
            throw new JsonException($"A {typeof(TEnum).Name} value written is one of its members.");
        }

        writer.WriteStringValue(WireName(value));
    }

    private static string WireName(TEnum value) => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
