using System.Globalization;
using BlueskyFeed.Classifiers;
using BlueskyFeed.Util;
using FishyFlip;
using FishyFlip.Models;

namespace BlueskyFeed.Generators;

public class HelloFeedProvider : IFeedGenerator
{
    private readonly ILogger<HelloFeedProvider> _logger;
    private readonly HelloClassifier _helloClassifier;

    private readonly GeneratorRecordRequest _recordRequest = new(
        "test-feed",
        "Hello Feed",
        null,
        "(Test) Posts containing the word 'hello'",
        DateTime.UtcNow,
        true
    );
    
    public HelloFeedProvider(ILogger<HelloFeedProvider> logger, HelloClassifier helloClassifier)
    {
        _logger = logger;
        _helloClassifier = helloClassifier;
    }
    
    public GeneratorRecordRequest GetRecordRequest() => _recordRequest;
    public string GetUri(ATDid publisherDid)
    {
        return $"at://{publisherDid.Handler}/app.bsky.feed.generator/{_recordRequest.RKey}";
    }
    
    public bool Matches(string rkey, ATDid publisherDid)
    {
        return rkey == GetUri(publisherDid) && _recordRequest.Enabled;
    }

    public async Task<object> RetrieveAsync(string? cursor, int limit, Func<Task<string?>> resolveIssuerDid, CancellationToken cancellationToken)
    {
        // can resolve the request issuers DID here, so we can personalize the feed is needed
        var issuerDid = await resolveIssuerDid();

        IEnumerable<HelloClassifier.ClassifiedPost> posts = _helloClassifier.GetPosts()
            .OrderByDescending(x => x.IndexedAt)
            .ThenBy(x => x.Cid)
            .ToArray();
        if (!string.IsNullOrEmpty(cursor))
        {
            var parts = cursor.Split("::", StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                throw new ArgumentException("Invalid cursor", nameof(cursor));
            }

            var indexedAt = DateTime.Parse(parts[0]).ToUniversalTime();
            var cid = parts[1];

            posts = posts
                .Where(x => x.IndexedAt <= indexedAt && string.CompareOrdinal(x.Cid, cid) < 0);
        }

        var postArray = posts.Take(limit).ToArray();
        
        if (postArray.Length == 0)
        {
            _logger.LogInformation("No posts to return");
            return new
            {
                cursor = $"{DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)}::",
                feed = Array.Empty<object>()
            };
        }
        
        var last = postArray.Last();
        var newCursor = $"{last.IndexedAt.ToString("O", CultureInfo.InvariantCulture)}::{last.Cid}";
        var feed = postArray.Select(x => new
        {
            post = $"at://{x.Repo}/{Constants.FeedType.Post}/{x.RKey}"
        }).ToArray();
        _logger.LogInformation("Returning {Count} posts with cursor {Cursor}", feed.Length, newCursor);
        return new
        {
            cursor = newCursor,
            feed = feed
        };
    }
}