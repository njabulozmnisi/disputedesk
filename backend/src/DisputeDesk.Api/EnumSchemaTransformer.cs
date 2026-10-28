using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DisputeDesk.Api;

/// <summary>
/// Writes each enum schema's values as the shared JSON settings write them (AD-5), and nothing else. The generator
/// reads enum names only from the built-in enum converter, which the shared settings wrap
/// (<see cref="Contracts.Json.SingleNameEnumConverter"/>), and it would add <c>null</c> to a shared enum component when
/// any usage is nullable; a nullable usage already says so itself, as <c>oneOf</c> with <c>null</c>.
/// </summary>
internal sealed class EnumSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        // The component of an enum used only as nullable is made from its Nullable<T> schema.
        Type type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
        if (type.IsEnum)
        {
            schema.Enum = [.. Enum.GetValues(type).Cast<object>().Select(value => WireName(value, type, context.JsonTypeInfo.Options))];
        }

        return Task.CompletedTask;
    }

    private static JsonNode WireName(object value, Type type, JsonSerializerOptions options) =>
        JsonSerializer.SerializeToNode(value, type, options)
            ?? throw new InvalidOperationException($"The enum {type} serialised a value as null.");
}
