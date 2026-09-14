using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace JunDevNotes.Publisher.WordPress;

public sealed class WordPressClient
{
    private const string UsernameEnvironmentVariable = "WP_USERNAME";
    private const string PasswordEnvironmentVariable = "WP_APP_PASSWORD";

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;
    private readonly AuthenticationHeaderValue _authorization;

    public WordPressClient(HttpClient httpClient, string wordPressBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(wordPressBaseUrl);

        if (!Uri.TryCreate(wordPressBaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException(
                "The WordPress base URL must be an absolute HTTP or HTTPS URL.",
                nameof(wordPressBaseUrl));
        }

        var username = Environment.GetEnvironmentVariable(UsernameEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException(
                $"Required environment variable {UsernameEnvironmentVariable} is missing or empty.");
        }

        var applicationPassword = Environment.GetEnvironmentVariable(PasswordEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(applicationPassword))
        {
            throw new InvalidOperationException(
                $"Required environment variable {PasswordEnvironmentVariable} is missing or empty.");
        }

        _httpClient = httpClient;
        _baseUri = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);

        var credentialBytes = Encoding.UTF8.GetBytes($"{username}:{applicationPassword}");
        var encodedCredentials = Convert.ToBase64String(credentialBytes);
        _authorization = new AuthenticationHeaderValue("Basic", encodedCredentials);
    }

    public async Task<WordPressPostResponse> UpdateDraftAsync(
        int postId,
        string content,
        string title,
        string? excerpt,
        int categoryId,
        WordPressSeoMeta seoMeta,
        CancellationToken cancellationToken = default)
    {
        if (postId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(postId),
                postId,
                "The WordPress post ID must be a positive integer.");
        }

        var endpoint = new Uri(
            _baseUri,
            $"wp-json/wp/v2/posts/{postId}?context=edit");
        var payload = BuildDraftRequest(content, title, excerpt, categoryId, seoMeta);

        return await SaveDraftAsync(
            endpoint,
            payload,
            expectedPostId: postId,
            operation: "update",
            cancellationToken);
    }

    public async Task<WordPressPostResponse> CreateDraftAsync(
        string content,
        string title,
        string? excerpt,
        int categoryId,
        WordPressSeoMeta seoMeta,
        CancellationToken cancellationToken = default)
    {
        var endpoint = new Uri(_baseUri, "wp-json/wp/v2/posts?context=edit");
        var payload = BuildDraftRequest(content, title, excerpt, categoryId, seoMeta);

        return await SaveDraftAsync(
            endpoint,
            payload,
            expectedPostId: null,
            operation: "creation",
            cancellationToken);
    }

    public async Task<bool> PublishExistingDraftAsync(
        int postId,
        CancellationToken cancellationToken = default)
    {
        if (postId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(postId), postId,
                "The WordPress post ID must be a positive integer.");
        }

        var endpoint = new Uri(_baseUri, $"wp-json/wp/v2/posts/{postId}?context=edit");
        using var getRequest = new HttpRequestMessage(HttpMethod.Get, endpoint);
        getRequest.Headers.Authorization = _authorization;
        using var getResponse = await _httpClient.SendAsync(getRequest, cancellationToken);
        if (!getResponse.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"WordPress post lookup failed with HTTP {(int)getResponse.StatusCode} ({getResponse.ReasonPhrase}).",
                inner: null,
                getResponse.StatusCode);
        }

        var getText = StrictUtf8.GetString(
            await getResponse.Content.ReadAsByteArrayAsync(cancellationToken));
        var existing = JsonSerializer.Deserialize<WordPressPostResponse>(getText, JsonOptions)
            ?? throw new InvalidOperationException("WordPress returned an empty post response.");
        if (existing.Id != postId)
        {
            throw new InvalidOperationException(
                $"WordPress post lookup returned ID {existing.Id} instead of {postId}.");
        }

        if (string.Equals(existing.Status, "publish", StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(existing.Status, "draft", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"WordPress post {postId} has status '{existing.Status ?? "<missing>"}', not 'draft'.");
        }

        using var postRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        postRequest.Headers.Authorization = _authorization;
        postRequest.Content = new StringContent(
            "{\"status\":\"publish\"}", Encoding.UTF8, "application/json");
        using var postResponse = await _httpClient.SendAsync(postRequest, cancellationToken);
        if (!postResponse.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"WordPress post publish failed with HTTP {(int)postResponse.StatusCode} ({postResponse.ReasonPhrase}).",
                inner: null,
                postResponse.StatusCode);
        }

        var postText = StrictUtf8.GetString(
            await postResponse.Content.ReadAsByteArrayAsync(cancellationToken));
        var published = JsonSerializer.Deserialize<WordPressPostResponse>(postText, JsonOptions)
            ?? throw new InvalidOperationException("WordPress returned an empty publish response.");
        if (published.Id != postId ||
            !string.Equals(published.Status, "publish", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"WordPress publish verification failed for post {postId}: " +
                $"returned ID {published.Id} with status '{published.Status ?? "<missing>"}'.");
        }

        return false;
    }

    private async Task<WordPressPostResponse> SaveDraftAsync(
        Uri endpoint,
        WordPressPostRequest payload,
        int? expectedPostId,
        string operation,
        CancellationToken cancellationToken)
    {
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = _authorization;
        request.Content = new ByteArrayContent(jsonBytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var responseText = StrictUtf8.GetString(responseBytes);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"WordPress post {operation} failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). " +
                $"Response: {responseText}",
                inner: null,
                response.StatusCode);
        }

        WordPressPostResponse? post;
        try
        {
            post = JsonSerializer.Deserialize<WordPressPostResponse>(responseText, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "WordPress returned a successful response that was not valid post JSON.",
                exception);
        }

        if (post is null)
        {
            throw new InvalidOperationException(
                "WordPress returned an empty post response.");
        }

        VerifySavedPost(post, payload, expectedPostId);

        return post;
    }

    private static WordPressPostRequest BuildDraftRequest(
        string content,
        string title,
        string? excerpt,
        int categoryId,
        WordPressSeoMeta seoMeta)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException(
                "A non-blank title from Markdown front matter is required for publishing.",
                nameof(title));
        }
        if (categoryId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(categoryId),
                categoryId,
                "The WordPress category ID must be a positive integer.");
        }
        ArgumentNullException.ThrowIfNull(seoMeta);
        ValidateSeoMeta(seoMeta);

        return new WordPressPostRequest(
            content,
            title,
            excerpt ?? string.Empty,
            [categoryId],
            seoMeta);
    }

    private static void VerifySavedPost(
        WordPressPostResponse post,
        WordPressPostRequest expected,
        int? expectedPostId)
    {
        if (expectedPostId.HasValue)
        {
            VerifyEqual("post ID", expectedPostId.Value, post.Id);
        }
        else if (post.Id <= 0)
        {
            throw new InvalidOperationException(
                $"WordPress verification failed for post ID: expected a positive newly created ID, " +
                $"but received '{post.Id}'.");
        }

        VerifyEqual("status", expected.Status, post.Status);
        VerifyEqual("title", expected.Title, post.Title?.Raw);
        VerifyEqual("excerpt", expected.Excerpt, post.Excerpt?.Raw);

        if (post.Categories is null ||
            !post.Categories.SequenceEqual(expected.Categories))
        {
            throw new InvalidOperationException(
                $"WordPress verification failed for categories: expected " +
                $"[{string.Join(", ", expected.Categories)}], but received " +
                $"{FormatArray(post.Categories)}.");
        }

        VerifyEqual(
            "meta._siteseo_analysis_target_kw",
            expected.Meta.FocusKeyword,
            post.Meta?.FocusKeyword);
        VerifyEqual(
            "meta._siteseo_titles_desc",
            expected.Meta.Description,
            post.Meta?.Description);
        VerifyEqual(
            "meta._siteseo_social_fb_title",
            expected.Meta.SocialTitle,
            post.Meta?.SocialTitle);
        VerifyEqual(
            "meta._siteseo_social_fb_desc",
            expected.Meta.SocialDescription,
            post.Meta?.SocialDescription);
    }

    private static void VerifyEqual<T>(string field, T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"WordPress verification failed for {field}: expected " +
                $"'{FormatValue(expected)}', but received '{FormatValue(actual)}'.");
        }
    }

    private static string FormatArray(int[]? values) =>
        values is null ? "<missing>" : $"[{string.Join(", ", values)}]";

    private static string FormatValue<T>(T value) =>
        value?.ToString() ?? "<missing>";

    private static void ValidateSeoMeta(WordPressSeoMeta seoMeta)
    {
        if (string.IsNullOrWhiteSpace(seoMeta.FocusKeyword) ||
            string.IsNullOrWhiteSpace(seoMeta.Description) ||
            string.IsNullOrWhiteSpace(seoMeta.SocialTitle) ||
            string.IsNullOrWhiteSpace(seoMeta.SocialDescription))
        {
            throw new ArgumentException(
                "All SiteSEO metadata values must be non-blank before publishing.",
                nameof(seoMeta));
        }
    }

    public async Task<int> ResolveCategoryIdAsync(
        string categoryName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            throw new ArgumentException(
                "A non-blank category name from Markdown front matter is required.",
                nameof(categoryName));
        }

        var encodedName = Uri.EscapeDataString(categoryName);
        var endpoint = new Uri(
            _baseUri,
            $"wp-json/wp/v2/categories?search={encodedName}&per_page=100");

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = _authorization;

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var responseText = StrictUtf8.GetString(responseBytes);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"WordPress category lookup failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). " +
                $"Response: {responseText}",
                inner: null,
                response.StatusCode);
        }

        WordPressCategoryResponse[]? categories;
        try
        {
            categories = JsonSerializer.Deserialize<WordPressCategoryResponse[]>(
                responseText,
                JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "WordPress returned a successful category response that was not valid JSON.",
                exception);
        }

        var exactMatches = (categories ?? [])
            .Where(category => string.Equals(
                category.Name,
                categoryName,
                StringComparison.Ordinal))
            .ToArray();

        if (exactMatches.Length == 0)
        {
            throw new InvalidOperationException(
                $"No WordPress category exactly named '{categoryName}' was found. " +
                "No category was created.");
        }

        if (exactMatches.Length > 1)
        {
            throw new InvalidOperationException(
                $"More than one WordPress category exactly named '{categoryName}' was found; " +
                "the category is ambiguous.");
        }

        return exactMatches[0].Id;
    }
}
