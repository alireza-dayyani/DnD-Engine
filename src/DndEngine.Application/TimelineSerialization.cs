using System.Text.Json;
using System.Text.Json.Serialization;

namespace DndEngine.Application;

internal static class TimelineSerialization
{
    public const int SchemaVersion = 2;
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }

    public static JsonElement Serialize<T>(T value) => JsonSerializer.SerializeToElement(value, Options);
}
