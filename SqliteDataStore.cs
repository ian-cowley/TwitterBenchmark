using System.Data;
using Microsoft.Data.Sqlite;
using TwitterBenchmark.Models;

namespace TwitterBenchmark.Data;

public sealed class SqliteDataStore : IDataStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private const string FeedSql = @"
        WITH latest_posts AS (
            SELECT id, user_id, content, created_at
            FROM posts
            ORDER BY created_at DESC
            LIMIT 20
        )
        SELECT p.id, p.user_id, u.username, p.content, p.created_at, COUNT(l.user_id) AS likes_count
        FROM latest_posts p
        JOIN users u ON p.user_id = u.id
        LEFT JOIN likes l ON p.id = l.post_id
        GROUP BY p.id, p.user_id, u.username, p.content, p.created_at
        ORDER BY p.created_at DESC;";

    private const string GetPostSql = @"
        SELECT p.id, p.user_id, u.username, p.content, p.created_at, COUNT(l.user_id) AS likes_count
        FROM posts p
        JOIN users u ON p.user_id = u.id
        LEFT JOIN likes l ON p.id = l.post_id
        WHERE p.id = $id
        GROUP BY p.id, p.user_id, u.username, p.content, p.created_at;";

    private const string LikeSql = @"
        INSERT OR IGNORE INTO likes (user_id, post_id, created_at)
        VALUES ($userId, $postId, datetime('now'));";

    private const string CreatePostSql = @"
        INSERT INTO posts (user_id, content, created_at)
        VALUES ($userId, $content, datetime('now'))
        RETURNING id, user_id, content, created_at;";

    public SqliteDataStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Configure WAL mode and performance pragmas
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA cache_size = -64000;
            PRAGMA temp_store = MEMORY;
            PRAGMA mmap_size = 268435456;
            PRAGMA busy_timeout = 5000;

            CREATE TABLE IF NOT EXISTS users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                username TEXT NOT NULL UNIQUE,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS posts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL REFERENCES users(id),
                content TEXT NOT NULL,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS likes (
                user_id INTEGER NOT NULL REFERENCES users(id),
                post_id INTEGER NOT NULL REFERENCES posts(id),
                created_at TEXT NOT NULL DEFAULT (datetime('now')),
                PRIMARY KEY (user_id, post_id)
            );

            CREATE INDEX IF NOT EXISTS idx_posts_created_at_desc ON posts (created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_posts_user_id ON posts (user_id);
            CREATE INDEX IF NOT EXISTS idx_likes_post_id ON likes (post_id);

            INSERT OR IGNORE INTO users (id, username) VALUES (1, 'user1');
        ";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<FeedItem[]> GetFeedAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = FeedSql;

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult, ct);
        var results = new List<FeedItem>(20);

        while (await reader.ReadAsync(ct))
        {
            long id = reader.GetInt64(0);
            long userId = reader.GetInt64(1);
            string username = reader.GetString(2);
            string content = reader.GetString(3);
            DateTime createdAt = DateTime.TryParse(reader.GetString(4), out var dt) ? dt : DateTime.UtcNow;
            long likesCount = reader.GetInt64(5);

            results.Add(new FeedItem(
                Id: id,
                UserId: userId,
                Username: username,
                Content: content,
                LikesCount: likesCount,
                CreatedAt: createdAt,
                Author: new AuthorDto(userId, username)
            ));
        }

        return results.ToArray();
    }

    public async Task<FeedItem?> GetPostByIdAsync(long id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = GetPostSql;
        cmd.Parameters.AddWithValue("$id", id);

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, ct);
        if (await reader.ReadAsync(ct))
        {
            long postId = reader.GetInt64(0);
            long userId = reader.GetInt64(1);
            string username = reader.GetString(2);
            string content = reader.GetString(3);
            DateTime createdAt = DateTime.TryParse(reader.GetString(4), out var dt) ? dt : DateTime.UtcNow;
            long likesCount = reader.GetInt64(5);

            return new FeedItem(
                Id: postId,
                UserId: userId,
                Username: username,
                Content: content,
                LikesCount: likesCount,
                CreatedAt: createdAt,
                Author: new AuthorDto(userId, username)
            );
        }

        return null;
    }

    public async Task<bool> LikePostAsync(long userId, long postId, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = LikeSql;
            cmd.Parameters.AddWithValue("$userId", userId);
            cmd.Parameters.AddWithValue("$postId", postId);

            int rows = await cmd.ExecuteNonQueryAsync(ct);
            return rows > 0;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<CreatedPostResponse> CreatePostAsync(long userId, string content, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = CreatePostSql;
            cmd.Parameters.AddWithValue("$userId", userId);
            cmd.Parameters.AddWithValue("$content", content);

            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, ct);
            if (await reader.ReadAsync(ct))
            {
                long postId = reader.GetInt64(0);
                long authorId = reader.GetInt64(1);
                string postContent = reader.GetString(2);
                DateTime createdAt = DateTime.TryParse(reader.GetString(3), out var dt) ? dt : DateTime.UtcNow;

                return new CreatedPostResponse(postId, authorId, postContent, createdAt);
            }

            throw new InvalidOperationException("Failed to insert post");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _writeLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
