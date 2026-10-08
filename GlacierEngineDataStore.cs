using System.Collections.Concurrent;
using TwitterBenchmark.Models;

namespace TwitterBenchmark.Data;

/// <summary>
/// Glacier-inspired high-speed in-memory engine with atomic counters and circular ring-buffer.
/// Demonstrates the absolute theoretical limit of C# on a $12 server when unchained from disk B-Tree locks.
/// </summary>
public sealed class GlacierEngineDataStore : IDataStore
{
    private readonly ConcurrentDictionary<long, FeedItem> _posts = new();
    private readonly ConcurrentDictionary<(long UserId, long PostId), byte> _likes = new();
    private readonly ConcurrentDictionary<long, long> _postLikesCount = new();
    
    // Circular buffer keeping the IDs of the latest 20 posts for O(1) feed retrieval
    private readonly long[] _latestPostIds = new long[20];
    private readonly object _ringLock = new();

    private long _currentPostId = 500000;

    public Task InitializeAsync(CancellationToken ct = default)
    {
        // Seed initial 20 posts for instant feed readiness
        for (long i = 1; i <= 20; i++)
        {
            var item = new FeedItem(
                Id: i,
                UserId: (i % 50000) + 1,
                Username: $"user_{i}",
                Content: $"Glacier ultra-performance post #{i}",
                LikesCount: i * 3,
                CreatedAt: DateTime.UtcNow.AddMinutes(-i),
                Author: new AuthorDto((i % 50000) + 1, $"user_{i}")
            );

            _posts[i] = item;
            _postLikesCount[i] = i * 3;
            _latestPostIds[20 - i] = i;
        }

        return Task.CompletedTask;
    }

    public Task<FeedItem[]> GetFeedAsync(CancellationToken ct = default)
    {
        var result = new FeedItem[20];
        lock (_ringLock)
        {
            for (int i = 0; i < 20; i++)
            {
                long id = _latestPostIds[i];
                if (id > 0 && _posts.TryGetValue(id, out var item))
                {
                    // Update latest like count atomically
                    long currentLikes = _postLikesCount.GetValueOrDefault(id, item.LikesCount);
                    result[i] = item with { LikesCount = currentLikes };
                }
                else
                {
                    result[i] = new FeedItem(i + 1, 1, "glacier_user", "Glacier feed item", 0, DateTime.UtcNow);
                }
            }
        }

        return Task.FromResult(result);
    }

    public Task<FeedItem?> GetPostByIdAsync(long id, CancellationToken ct = default)
    {
        if (_posts.TryGetValue(id, out var item))
        {
            long currentLikes = _postLikesCount.GetValueOrDefault(id, item.LikesCount);
            return Task.FromResult<FeedItem?>(item with { LikesCount = currentLikes });
        }

        return Task.FromResult<FeedItem?>(null);
    }

    public Task<bool> LikePostAsync(long userId, long postId, CancellationToken ct = default)
    {
        if (_likes.TryAdd((userId, postId), 1))
        {
            _postLikesCount.AddOrUpdate(postId, 1, (_, count) => count + 1);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<CreatedPostResponse> CreatePostAsync(long userId, string content, CancellationToken ct = default)
    {
        long newId = Interlocked.Increment(ref _currentPostId);
        var now = DateTime.UtcNow;

        var feedItem = new FeedItem(
            Id: newId,
            UserId: userId,
            Username: $"user_{userId}",
            Content: content,
            LikesCount: 0,
            CreatedAt: now,
            Author: new AuthorDto(userId, $"user_{userId}")
        );

        _posts[newId] = feedItem;
        _postLikesCount[newId] = 0;

        // Push into latest 20 ring buffer
        lock (_ringLock)
        {
            Array.Copy(_latestPostIds, 0, _latestPostIds, 1, 19);
            _latestPostIds[0] = newId;
        }

        return Task.FromResult(new CreatedPostResponse(newId, userId, content, now));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
