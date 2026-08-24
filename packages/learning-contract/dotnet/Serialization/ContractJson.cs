using System.Text.Json;

namespace FdeTutor.Contracts.Serialization;

public static class ContractJson
{
    public static JsonSerializerOptions Options { get; } =
        new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string value) =>
        JsonSerializer.Deserialize<T>(value, Options);
}
