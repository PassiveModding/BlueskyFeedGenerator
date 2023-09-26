using BlueskyFeedGenerator.Database;
using BlueskyFeedGenerator.Models;

namespace BlueskyFeedGenerator.Feeds;

public static class FeedHelpers
{
    /* Break down retrieve into smaller methods */
    public static (IQueryable<Post> builder, DateTime? indexedAt, string? cid) GetBuilder(this DataContext db, string? cursor, FeedFlag flag)
    {
        // if cursor exists, use it to get the next set of posts
        var builder = db.Posts.Where(p => p.Flags.HasFlag(flag))
            .OrderByDescending(p => p.IndexedAt)
            .ThenByDescending(p => p.Cid)
            .AsQueryable();

        DateTime? indexedAt = null;
        string? cid = null;

        if (!string.IsNullOrEmpty(cursor))
        {
            string[] parts = cursor.Split("::", StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                throw new ArgumentException("Invalid cursor", nameof(cursor));
            }

            // Epoch ms
            indexedAt = DateTime.Parse(parts[0]);
            cid = parts[1];

            builder = builder.Where(p => p.IndexedAt <= indexedAt && p.Cid.CompareTo(cid) < 0);
        }

        return (builder, indexedAt, cid);
    }

    public static object GetFeedResponse(this List<Post> posts)
    {
        var last = posts.LastOrDefault();

        if (last == null)
        {
            return new
            {
                cursor = $"{DateTime.UtcNow.ToUniversalTime():o}::",
                feed = new List<string>()
            };
        }

        return new
        {
            cursor = $"{last.IndexedAt:O}::{last.Cid}",
            feed = posts.Select(p => new
            {
                post = p.Uri
            }).ToArray()
        };
    }
}