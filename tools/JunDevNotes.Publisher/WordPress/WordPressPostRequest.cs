using System.Text.Json.Serialization;

namespace JunDevNotes.Publisher.WordPress;

public sealed record WordPressPostRequest(
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("excerpt")] string Excerpt,
    [property: JsonPropertyName("categories")] int[] Categories,
    [property: JsonPropertyName("meta")] WordPressSeoMeta Meta)
{
    [JsonPropertyName("status")]
    public string Status => "draft";
}
