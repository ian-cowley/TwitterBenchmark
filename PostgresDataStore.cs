using System.Data;
using Npgsql;
using NpgsqlTypes;
using TwitterBenchmark.Models;

namespace TwitterBenchmark.Data;

public sealed class PostgresDataStore : IDataStore
{
    private readonly NpgsqlDataSource _dataSource;

    // Single query for Feed: joins 20 newest posts with authors and likes count
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

    // Single query for GetPostById
    private const string GetPostSql = @"
        SELECT p.id, p.user_id, u.username, p.content, p.created_at, COUNT(l.user_id) AS likes_count
        FROM posts p
        JOIN users u ON p.user_id = u.id
        LEFT JOIN likes l ON p.id = l.post_id
        WHERE p.id = $1
        GROUP BY p.id, p.user_id, u.username, p.content, p.created_at;";

    // Single query for LikePost
    private const string LikeSql = @"
        INSERT INTO likes (user_id, post_id, created_at)
        VALUES ($1, $2, NOW())
        ON CONFLICT (user_id, post_id) DO NOTHING;";

    // Single query for CreatePost
    private const string CreatePostSql = @"
        INSERT INTO posts (user_id, content, created_at)
        VALUES ($1, $2, NOW())
        RETURNING id, user_id, content, created_at;";

    public PostgresDataStore(string connectionString)
    {
        var csb = new NpgsqlConnectionStringBuilder(connectionString)
        {
            // CRITICAL OPTIMIZATION:
            // Arjay's benchmark suffered because default Npgsql issued 'DISCARD ALL' on every connection return,
            // which consumed 60% of the single CPU on Postgres.
            // Disabling reset on close eliminates all DISCARD ALL round-trips!
            NoResetOnClose = true,
            Pooling = true,
            MinPoolSize = 10,
            MaxPoolSize = 10,
            ConnectionIdleLifetime = 0,
            MaxAutoPrepare = 50,
            AutoPrepareMinUsages = 2
        };

        var builder = new NpgsqlDataSourceBuilder(csb.ConnectionString);
        _dataSource = builder.Build();
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        const string initSql = @"
            CREATE TABLE IF NOT EXISTS users (
                id BIGSERIAL PRIMARY KEY,
                username VARCHAR(50) NOT NULL UNIQUE,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS posts (
                id BIGSERIAL PRIMARY KEY,
                user_id BIGINT NOT NULL REFERENCES users(id),
                content TEXT NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS likes (
                user_id BIGINT NOT NULL REFERENCES users(id),
                post_id BIGINT NOT NULL REFERENCES posts(id),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                PRIMARY KEY (user_id, post_id)
            );

            CREATE INDEX IF NOT EXISTS idx_posts_created_at_desc ON posts (created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_posts_user_id ON posts (user_id);
            CREATE INDEX IF NOT EXISTS idx_likes_post_id ON likes (post_id);

            INSERT INTO users (id, username) VALUES (1, 'user1') ON CONFLICT (id) DO NOTHING;
        ";

        await using var cmd = new NpgsqlCommand(initSql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<FeedItem[]> GetFeedAsync(CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(FeedSql, conn);
        cmd.Prepare();

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult, ct);
        var results = new List<FeedItem>(20);

        while (await reader.ReadAsync(ct))
        {
            long id = reader.GetInt64(0);
            long userId = reader.GetInt64(1);
            string username = reader.GetString(2);
            string content = reader.GetString(3);
            DateTime createdAt = reader.GetDateTime(4);
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
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(GetPostSql, conn);
        cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint, Value = id });
        cmd.Prepare();

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, ct);
        if (await reader.ReadAsync(ct))
        {
            long postId = reader.GetInt64(0);
            long userId = reader.GetInt64(1);
            string username = reader.GetString(2);
            string content = reader.GetString(3);
            DateTime createdAt = reader.GetDateTime(4);
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
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(LikeSql, conn);
        cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint, Value = userId });
        cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint, Value = postId });
        cmd.Prepare();

        int rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    public async Task<CreatedPostResponse> CreatePostAsync(long userId, string content, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(CreatePostSql, conn);
        cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint, Value = userId });
        cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = content });
        cmd.Prepare();

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, ct);
        if (await reader.ReadAsync(ct))
        {
            long postId = reader.GetInt64(0);
            long authorId = reader.GetInt64(1);
            string postContent = reader.GetString(2);
            DateTime createdAt = reader.GetDateTime(3);

            return new CreatedPostResponse(postId, authorId, postContent, createdAt);
        }

        throw new InvalidOperationException("Failed to insert post");
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync();
    }
}
