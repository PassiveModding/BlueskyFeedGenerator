using System.Text;
using System.Text.RegularExpressions;
using Bluesky.Common.Database;
using Bluesky.Common.Models;
using Bluesky.Feed.Models;
using Microsoft.EntityFrameworkCore;
using static Bluesky.Feed.Config.FeedConfig;

namespace Bluesky.Feed.Feeds;

public partial class TopicFeed : IFeed
{
    public bool AuthorizeUser => false;
    private readonly IServiceProvider _serviceProvider;
    private readonly TopicConifg topicConifg;
    private readonly ILogger<TopicFeed> _logger;

    public TopicFeed(IServiceProvider serviceProvider, TopicConifg topicConifg)
    {
        _serviceProvider = serviceProvider;
        this.topicConifg = topicConifg;
        _logger = _serviceProvider.GetRequiredService<ILogger<TopicFeed>>();
    }

    public async Task<object> RetrieveAsync(string? cursor, int limit, string? issuerDid, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PostContext>();

        var posts = db.Posts.OrderByDescending(p => p.IndexedAt)
            .ThenByDescending(p => p.Cid)
            .Where(x => x.PostTopics.Any(t => t.Topic.Name == topicConifg.Name && t.Weight >= 100))
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
            indexedAt = DateTime.Parse(parts[0]).ToUniversalTime();
            cid = parts[1];

            posts = posts.Where(p => p.IndexedAt <= indexedAt && p.Cid.CompareTo(cid) < 0);
        }

        var postList = await posts.Take(limit).ToListAsync(cancellationToken);

        _logger.LogInformation("Retrieved {count} posts for topic {topicName}", postList.Count, topicConifg.Name);
        return GetFeedResponse(postList, cursor);
    }

    private object GetFeedResponse(IEnumerable<Post> posts, string? cursor = null)
    {
        var last = posts.LastOrDefault();

        if (last == null)
        {
            return new
            {
                cursor = $"{DateTime.UtcNow.ToUniversalTime():o}::",
                feed = cursor == null ? topicConifg.PinnedPosts?.Select(p => new
                {
                    post = p
                }).ToArray() : Array.Empty<object>()
            };
        }

        if (topicConifg.PinnedPosts != null && cursor == null || string.IsNullOrWhiteSpace(cursor))
        {
            return new
            {
                cursor = $"{last.IndexedAt:O}::{last.Cid}",
                feed = topicConifg.PinnedPosts!.Select(p => new
                {
                    post = p
                }).Concat(posts.Select(p => new
                {
                    post = p.Uri
                })).ToArray()
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