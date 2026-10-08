using System.Buffers;
using System.Buffers.Text;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using TwitterBenchmark.Models;

namespace TwitterBenchmark;

/// <summary>
/// Ultra-low-latency zero-allocation UTF-8 response builder.
/// Directly formats feed arrays and single posts into the socket PipeWriter.
/// </summary>
public static class SimdUtf8Writer
{
    private static readonly byte[] ItemStart = "{\"id\":"u8.ToArray();
    private static readonly byte[] ItemUserId = ",\"user_id\":"u8.ToArray();
    private static readonly byte[] ItemUsername = ",\"username\":\""u8.ToArray();
    private static readonly byte[] ItemContent = "\",\"content\":\""u8.ToArray();
    private static readonly byte[] ItemLikes = "\",\"likes_count\":"u8.ToArray();
    private static readonly byte[] ItemCreatedAt = ",\"created_at\":\""u8.ToArray();
    private static readonly byte[] ItemAuthor = "\",\"author\":{\"id\":"u8.ToArray();
    private static readonly byte[] ItemAuthorUsername = ",\"username\":\""u8.ToArray();
    private static readonly byte[] ItemEnd = "\"}}"u8.ToArray();

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static ValueTask<FlushResult> WriteFeedAsync(PipeWriter writer, FeedItem[] items, CancellationToken ct = default)
    {
        var span = writer.GetSpan(64);
        span[0] = (byte)'[';
        writer.Advance(1);

        for (int i = 0; i < items.Length; i++)
        {
            if (i > 0)
            {
                var commaSpan = writer.GetSpan(1);
                commaSpan[0] = (byte)',';
                writer.Advance(1);
            }

            WriteFeedItem(writer, items[i]);
        }

        var endSpan = writer.GetSpan(1);
        endSpan[0] = (byte)']';
        writer.Advance(1);

        return writer.FlushAsync(ct);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static ValueTask<FlushResult> WriteSinglePostAsync(PipeWriter writer, FeedItem item, CancellationToken ct = default)
    {
        WriteFeedItem(writer, item);
        return writer.FlushAsync(ct);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteFeedItem(PipeWriter writer, FeedItem item)
    {
        // Estimate size for an item: ~250-400 bytes
        var span = writer.GetSpan(512);
        int offset = 0;

        // {"id":
        ItemStart.CopyTo(span.Slice(offset));
        offset += ItemStart.Length;

        // <id>
        Utf8Formatter.TryFormat(item.Id, span.Slice(offset), out int written);
        offset += written;

        // ,"user_id":
        ItemUserId.CopyTo(span.Slice(offset));
        offset += ItemUserId.Length;

        // <user_id>
        Utf8Formatter.TryFormat(item.UserId, span.Slice(offset), out written);
        offset += written;

        // ,"username":"
        ItemUsername.CopyTo(span.Slice(offset));
        offset += ItemUsername.Length;

        // <username>
        int enc = Encoding.UTF8.GetBytes(item.Username, span.Slice(offset));
        offset += enc;

        // ","content":"
        ItemContent.CopyTo(span.Slice(offset));
        offset += ItemContent.Length;

        // <content>
        enc = Encoding.UTF8.GetBytes(item.Content, span.Slice(offset));
        offset += enc;

        // ","likes_count":
        ItemLikes.CopyTo(span.Slice(offset));
        offset += ItemLikes.Length;

        // <likes_count>
        Utf8Formatter.TryFormat(item.LikesCount, span.Slice(offset), out written);
        offset += written;

        // ,"created_at":"
        ItemCreatedAt.CopyTo(span.Slice(offset));
        offset += ItemCreatedAt.Length;

        // <created_at (ISO 8601)>
        Utf8Formatter.TryFormat(item.CreatedAt, span.Slice(offset), out written, 'O');
        offset += written;

        // ","author":{"id":
        ItemAuthor.CopyTo(span.Slice(offset));
        offset += ItemAuthor.Length;

        long authorId = item.Author?.Id ?? item.UserId;
        Utf8Formatter.TryFormat(authorId, span.Slice(offset), out written);
        offset += written;

        // ,"username":"
        ItemAuthorUsername.CopyTo(span.Slice(offset));
        offset += ItemAuthorUsername.Length;

        string authorName = item.Author?.Username ?? item.Username;
        enc = Encoding.UTF8.GetBytes(authorName, span.Slice(offset));
        offset += enc;

        // "}}
        ItemEnd.CopyTo(span.Slice(offset));
        offset += ItemEnd.Length;

        writer.Advance(offset);
    }
}
