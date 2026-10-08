using System.Text.Json.Serialization;
using TwitterBenchmark.Models;

namespace TwitterBenchmark;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    GenerationMode = JsonSourceGenerationMode.Default,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(FeedItem))]
[JsonSerializable(typeof(FeedItem[]))]
[JsonSerializable(typeof(List<FeedItem>))]
[JsonSerializable(typeof(CreatePostRequest))]
[JsonSerializable(typeof(CreatedPostResponse))]
[JsonSerializable(typeof(LikeResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(AuthorDto))]
public partial class BenchmarkJsonContext : JsonSerializerContext
{
}
