using System.Text.Json.Serialization;

namespace JunDevNotes.Publisher.WordPress;

public sealed record WordPressPostResponse
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("title")]
    public WordPressTextResponse? Title { get; init; }

    [JsonPropertyName("excerpt")]
    public WordPressTextResponse? Excerpt { get; init; }

    [JsonPropertyName("categories")]
    public int[]? Categories { get; init; }

    [JsonPropertyName("meta")]
    public WordPressSeoMetaResponse? Meta { get; init; }
}

public sealed record WordPressTextResponse
{
    [JsonPropertyName("raw")]
    public string? Raw { get; init; }
}

public sealed record WordPressSeoMetaResponse
{
    [JsonPropertyName("_siteseo_analysis_target_kw")]
    public string? FocusKeyword { get; init; }

    [JsonPropertyName("_siteseo_titles_desc")]
    public string? Description { get; init; }

    [JsonPropertyName("_siteseo_social_fb_title")]
    public string? SocialTitle { get; init; }

    [JsonPropertyName("_siteseo_social_fb_desc")]
    public string? SocialDescription { get; init; }
}
