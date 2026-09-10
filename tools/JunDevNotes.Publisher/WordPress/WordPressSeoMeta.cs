using System.Text.Json.Serialization;

namespace JunDevNotes.Publisher.WordPress;

public sealed record WordPressSeoMeta(
    [property: JsonPropertyName("_siteseo_analysis_target_kw")] string FocusKeyword,
    [property: JsonPropertyName("_siteseo_titles_desc")] string Description,
    [property: JsonPropertyName("_siteseo_social_fb_title")] string SocialTitle,
    [property: JsonPropertyName("_siteseo_social_fb_desc")] string SocialDescription);
