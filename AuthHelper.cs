using System.Text.RegularExpressions;

namespace TwitterBenchmark;

public static class AuthHelper
{
    public static bool TryExtractUserId(HttpContext context, out long userId)
    {
        userId = 0;

        // 1. Check Authorization header
        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var headerStr = authHeader.ToString().AsSpan();
            if (headerStr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                headerStr = headerStr.Slice(7).Trim();
            }

            if (headerStr.StartsWith("user_", StringComparison.OrdinalIgnoreCase))
            {
                headerStr = headerStr.Slice(5);
            }
            else if (headerStr.StartsWith("token_", StringComparison.OrdinalIgnoreCase))
            {
                headerStr = headerStr.Slice(6);
            }

            if (long.TryParse(headerStr, out userId) && userId > 0)
            {
                return true;
            }

            // Fallback for non-numeric tokens: deterministically hash to range [1, 50000]
            if (headerStr.Length > 0)
            {
                uint hash = 2166136261;
                foreach (char c in headerStr)
                {
                    hash = (hash ^ c) * 16777619;
                }
                userId = (hash % 50000) + 1;
                return true;
            }
        }

        // 2. Check X-User-Id header as fallback
        if (context.Request.Headers.TryGetValue("X-User-Id", out var xUserIdHeader) &&
            long.TryParse(xUserIdHeader, out userId) && userId > 0)
        {
            return true;
        }

        return false;
    }
}
