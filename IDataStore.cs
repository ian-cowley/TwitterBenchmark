using TwitterBenchmark.Models;

namespace TwitterBenchmark.Data;

public interface IDataStore : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<FeedItem[]> GetFeedAsync(CancellationToken ct = default);
    Task<FeedItem?> GetPostByIdAsync(long id, CancellationToken ct = default);
    Task<bool> LikePostAsync(long userId, long postId, CancellationToken ct = default);
    Task<CreatedPostResponse> CreatePostAsync(long userId, string content, CancellationToken ct = default);
}
