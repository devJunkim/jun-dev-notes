using System.Text.Json.Serialization;

namespace JunDevNotes.Publisher.WordPress;

public sealed record WordPressCategoryResponse
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}
