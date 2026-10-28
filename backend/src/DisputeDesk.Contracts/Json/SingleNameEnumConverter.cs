using System.Text.Json;
using System.Text.Json.Serialization;

namespace DisputeDesk.Contracts.Json;

/// <summary>
/// The built-in <see cref="JsonStringEnumConverter"/> (camelCase names, no numbers) that also refuses a list of names.
/// The built-in converter reads <c>"a, b"</c> as the two members combined even on an enum without <c>[Flags]</c>, which
/// can bind another member or an undefined value, and System.Text.Json has no option to turn that off. Everything else,
/// reading one name and writing, is the built-in converter's.
/// </summary>
public sealed class SingleNameEnumConverter : JsonConverterFactory
{
    private static readonly JsonStringEnumConverter BuiltIn = new(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    // The factory pattern of the System.Text.Json docs: one generic converter per enum type, made by reflection.
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter?)Activator.CreateInstance(
            typeof(SingleNameConverter<>).MakeGenericType(typeToConvert), BuiltIn.CreateConverter(typeToConvert, options))
        ?? throw new InvalidOperationException($"No converter was created for {typeToConvert}.");

    private sealed class SingleNameConverter<TEnum>(JsonConverter<TEnum> builtIn) : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String && reader.GetString()?.Contains(',', StringComparison.Ordinal) == true)
            {
                throw new JsonException($"A {typeof(TEnum).Name} value is one name, not a list of names.");
            }

            return builtIn.Read(ref reader, typeToConvert, options);
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
            builtIn.Write(writer, value, options);
    }
}
