using System.Globalization;
using BlueskyFeed.Classifiers;
using BlueskyFeed.Config;
using BlueskyFeed.Services;
using FishyFlip.Models;
using Microsoft.Extensions.Options;

namespace BlueskyFeed.Generators;

public class FollowersFeedHelper
{
    public FollowersFeedHelper(ILogger<FollowersFeedHelper> logger, 
        ProtoHandler protoHandler, 
        IOptions<AtProtoConfig> config,
        LikeClassifier likeClassifier)
    {
        _logger = logger;
        _protoHandler = protoHandler;
        _config = config;
        _likeClassifier = likeClassifier;
    }
    
    private readonly Dictionary<string, FollowCacheEntry> _followCache = new();
    private readonly ILogger<FollowersFeedHelper> _logger;
    private readonly ProtoHandler _protoHandler;
    private readonly IOptions<AtProtoConfig> _config;
    private readonly LikeClassifier _likeClassifier;

    private record FollowCacheEntry(DateTime UpdatedAt, FeedProfile[] Follows);

    private async Task<List<FeedProfile>> GetFollowersAsync(string issuerDid, CancellationToken cancellationToken)
    {
        if (!_followCache.TryGetValue(issuerDid, out var cached) ||
            DateTime.UtcNow - cached.UpdatedAt > TimeSpan.FromMinutes(5))
        {
            _logger.LogInformation("Refreshing follow cache for {IssuerDid}", issuerDid);
            var proto = await _protoHandler.GetProtocolAsync(_config.Value, cancellationToken);
            var identifier = ATIdentifier.Create(issuerDid);
            if (identifier == null)
            {
                throw new Exception("Invalid issuer DID");
            }
        
            var following = new List<FeedProfile>();
            string? followCursor = null;
            do
            {
                var result = await proto.Graph.GetFollowersAsync(identifier, limit: 100, cursor: followCursor, cancellationToken: cancellationToken);
                var followRecord = result.AsT0 ?? throw new Exception("Failed to get actor feeds");
                var followers = followRecord.Followers ?? throw new Exception("Missing followers");
                following.AddRange(followers);
                followCursor = followRecord.Cursor;
                // limiting count to avoid too many requests
            } while (followCursor != null && following.Count < 1000);
            
            cached = new FollowCacheEntry(DateTime.UtcNow, following.ToArray());
            _followCache[issuerDid] = cached;
        }
        
        return cached.Follows.ToList();
    }
    
    private async Task<List<FeedProfile>> GetFollowingAsync(string issuerDid, CancellationToken cancellationToken)
    {
        if (!_followCache.TryGetValue(issuerDid, out var cached) ||
            DateTime.UtcNow - cached.UpdatedAt > TimeSpan.FromMinutes(5))
        {
            _logger.LogInformation("Refreshing follow cache for {IssuerDid}", issuerDid);
            var proto = await _protoHandler.GetProtocolAsync(_config.Value, cancellationToken);
            var identifier = ATIdentifier.Create(issuerDid);
            if (identifier == null)
            {
                throw new Exception("Invalid issuer DID");
            }
        
            var following = new List<FeedProfile>();
            string? followCursor = null;
            do
            {
                var result = await proto.Graph.GetFollowsAsync(identifier, limit: 100, cursor: followCursor, cancellationToken: cancellationToken);
                var followRecord = result.AsT0 ?? throw new Exception("Failed to get actor feeds");
                var followers = followRecord.Follows ?? throw new Exception("Missing followers");
                following.AddRange(followers);
                followCursor = followRecord.Cursor;
                // limiting count to avoid too many requests
            } while (followCursor != null && following.Count < 1000);
            
            cached = new FollowCacheEntry(DateTime.UtcNow, following.ToArray());
            _followCache[issuerDid] = cached;
        }
        
        return cached.Follows.ToList();
    }

    public enum FeedType
    {
        Followers,
        Following
    }
    
    public async Task<object> RetrieveAsync(string? cursor, int limit, string issuerDid, FeedType feedType, CancellationToken cancellationToken)
    {
        var follows = feedType switch
        {
            FeedType.Followers => await GetFollowersAsync(issuerDid, cancellationToken),
            FeedType.Following => await GetFollowingAsync(issuerDid, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(feedType))
        };

        IEnumerable<(LikeClassifier.ClassifiedLikedPost Post, FeedProfile Follower)> posts = _likeClassifier.GetPosts()
            .OrderByDescending(x => x.IndexedAt)
            .ThenBy(x => x.LikeCid)
            .Select(x => (x, cached: follows.FirstOrDefault(f => f.Did.Handler == x.LikedByRepo.Handler)))
            .Where(x => x.cached != null)
            .Cast<(LikeClassifier.ClassifiedLikedPost, FeedProfile)>();
        if (!string.IsNullOrEmpty(cursor))
        {
            var parts = cursor.Split("::", StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                throw new ArgumentException("Invalid cursor", nameof(cursor));
            }

            var indexedAt = DateTime.Parse(parts[0]).ToUniversalTime();
            var cid = parts[1];

            posts = posts.Where(x => x.Post.IndexedAt <= indexedAt && string.CompareOrdinal(x.Post.LikeCid, cid) < 0);
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
        var newCursor = $"{last.Post.IndexedAt.ToString("O", CultureInfo.InvariantCulture)}::{last.Post.LikeCid}";
        var feed = postArray.Select(x => ToFeedItem(x.Post, x.Follower)).ToArray();
        
        _logger.LogInformation("Returning {Count} posts with cursor {Cursor}", feed.Length, newCursor);
        return new
        {
            cursor = newCursor,
            feed = feed
        };
    }
    
    private object ToFeedItem(LikeClassifier.ClassifiedLikedPost post, FeedProfile argFollower)
    {
        var context = $"Liked by {argFollower.Handle} ({argFollower.DisplayName})";
        return new
        {
            post = post.Uri.ToString(),
            feedContext = context
        };
    }
}