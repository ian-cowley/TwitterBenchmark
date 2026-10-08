using Microsoft.AspNetCore.Http.Json;
using TwitterBenchmark;
using TwitterBenchmark.Data;
using TwitterBenchmark.Models;

var builder = WebApplication.CreateSlimBuilder(args);

// Configure zero-allocation Source-Generated JSON
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Clear();
    options.SerializerOptions.TypeInfoResolverChain.Add(BenchmarkJsonContext.Default);
});

// Determine DB Provider: "postgres", "sqlite", or "glacier"
var dbProvider = Environment.GetEnvironmentVariable("DB_PROVIDER")
                 ?? builder.Configuration["provider"]
                 ?? "postgres";

IDataStore dataStore;
if (dbProvider.Equals("glacier", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("[TwitterBenchmark] Starting with Glacier In-Memory WAL provider (Pure C# / SIMD Acceleration)");
    dataStore = new GlacierEngineDataStore();
}
else if (dbProvider.Equals("sqlite", StringComparison.OrdinalIgnoreCase))
{
    var connStr = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
                  ?? builder.Configuration["connection-string"]
                  ?? "Data Source=twitter_bench.db;Mode=ReadWriteCreate;Cache=Shared;Pooling=True;";

    Console.WriteLine($"[TwitterBenchmark] Starting with SQLite provider: {connStr}");
    dataStore = new SqliteDataStore(connStr);
}
else
{
    var connStr = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
                  ?? builder.Configuration["connection-string"]
                  ?? "Host=localhost;Port=5432;Database=twitter_bench;Username=postgres;Password=postgres;Pooling=true;Minimum Pool Size=10;Maximum Pool Size=10;Connection Idle Lifetime=0;No Reset On Close=true;Max Auto Prepare=50;Auto Prepare Min Usages=2;";

    Console.WriteLine($"[TwitterBenchmark] Starting with PostgreSQL provider (NoResetOnClose=true): {connStr}");
    dataStore = new PostgresDataStore(connStr);
}

builder.Services.AddSingleton<IDataStore>(dataStore);

var app = builder.Build();

// Initialize DB schema / warmup
await dataStore.InitializeAsync();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new HealthResponse("healthy", dbProvider)));
app.MapGet("/", () => $"TwitterBenchmark C# .NET Engine Running [Provider: {dbProvider}]");

// Register endpoints for both root and /api prefixes
MapRoutes(app, "");
MapRoutes(app, "/api");

app.Run();

static void MapRoutes(IEndpointRouteBuilder app, string prefix)
{
    // 1. Feed Endpoint (Loads 20 newest posts) - Optimized with SIMD UTF-8 streaming
    app.MapGet($"{prefix}/feed", async (HttpContext context, IDataStore store, CancellationToken ct) =>
    {
        var feed = await store.GetFeedAsync(ct);
        context.Response.ContentType = "application/json; charset=utf-8";
        await SimdUtf8Writer.WriteFeedAsync(context.Response.BodyWriter, feed, ct);
    });

    // 2. Open Post Endpoint (Loads single post by ID)
    app.MapGet($"{prefix}/posts/{{id:long}}", async (long id, HttpContext context, IDataStore store, CancellationToken ct) =>
    {
        var post = await store.GetPostByIdAsync(id, ct);
        if (post != null)
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            await SimdUtf8Writer.WriteSinglePostAsync(context.Response.BodyWriter, post, ct);
        }
        else
        {
            await Results.Json(new ErrorResponse("Post not found"), BenchmarkJsonContext.Default.ErrorResponse, statusCode: 404).ExecuteAsync(context);
        }
    });

    // 3. Like Post Endpoint (Likes a post - SIMD Auth)
    app.MapPost($"{prefix}/posts/{{id:long}}/like", async (long id, HttpContext context, IDataStore store, CancellationToken ct) =>
    {
        if (!SimdAuthHelper.TryExtractUserIdSimd(context, out long userId))
        {
            return Results.Json(new ErrorResponse("Unauthorized"), BenchmarkJsonContext.Default.ErrorResponse, statusCode: 401);
        }

        bool success = await store.LikePostAsync(userId, id, ct);
        return Results.Ok(new LikeResponse(success, id, userId));
    });

    // 4. Create Post Endpoint (Creates a post - SIMD Auth)
    app.MapPost($"{prefix}/posts", async (CreatePostRequest req, HttpContext context, IDataStore store, CancellationToken ct) =>
    {
        if (!SimdAuthHelper.TryExtractUserIdSimd(context, out long userId))
        {
            return Results.Json(new ErrorResponse("Unauthorized"), BenchmarkJsonContext.Default.ErrorResponse, statusCode: 401);
        }

        if (string.IsNullOrWhiteSpace(req.Content))
        {
            return Results.BadRequest(new ErrorResponse("Content cannot be empty"));
        }

        var created = await store.CreatePostAsync(userId, req.Content, ct);
        return Results.Json(created, BenchmarkJsonContext.Default.CreatedPostResponse, statusCode: 201);
    });
}
