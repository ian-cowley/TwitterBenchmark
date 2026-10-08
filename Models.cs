using System.Text.Json.Serialization;

namespace TwitterBenchmark.Models;

public sealed record AuthorDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("username")] string Username
);

public sealed record FeedItem(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("user_id")] long UserId,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("likes_count")] long LikesCount,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("author")] AuthorDto? Author = null
);

public sealed record CreatePostRequest(
    [property: JsonPropertyName("content")] string Content
);

public sealed record CreatedPostResponse(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("user_id")] long UserId,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt
);

public sealed record LikeResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("post_id")] long PostId,
    [property: JsonPropertyName("user_id")] long UserId
);

public sealed record ErrorResponse(
    [property: JsonPropertyName("error")] string Error
);

public sealed record HealthResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("provider")] string Provider
);
