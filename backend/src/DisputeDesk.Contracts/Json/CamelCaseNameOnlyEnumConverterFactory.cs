using System.Text.Json;
using System.Text.Json.Serialization;

namespace DisputeDesk.Contracts.Json;

/// <summary>
/// Creates a <see cref="CamelCaseNameOnlyEnumConverter{TEnum}"/> for every enum type, so one registration in
/// <see cref="DisputeDeskJson.Apply"/> gives every enum on the wire the same rule (AD-5). The factory pattern of
/// Microsoft Learn, <i>How to write custom converters</i>: one generic converter per type, made by reflection. A nullable
/// enum needs nothing here: System.Text.Json reads and writes <c>T?</c> with the converter for <c>T</c>.
/// </summary>
public sealed class CamelCaseNameOnlyEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter?)Activator.CreateInstance(typeof(CamelCaseNameOnlyEnumConverter<>).MakeGenericType(typeToConvert))
        ?? throw new InvalidOperationException($"No converter was created for {typeToConvert}.");
}
