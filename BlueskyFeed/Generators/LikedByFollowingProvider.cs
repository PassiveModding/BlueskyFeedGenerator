using BlueskyFeed.Util;
using FishyFlip.Models;

namespace BlueskyFeed.Generators;

public class LikedByFollowingProvider : IFeedGenerator
{
    private readonly ILogger<LikedByFollowingProvider> _logger;
    private readonly FollowersFeedHelper _followersFeedHelper;

    private readonly GeneratorRecordRequest _recordRequest = new(
        "follow-liked",
        "Liked by following",
        null,
        "Posts liked by people who you follow",
        DateTime.UtcNow,
        true
    );
    
    public LikedByFollowingProvider(ILogger<LikedByFollowingProvider> logger, 
        FollowersFeedHelper followersFeedHelper)
    {
        _logger = logger;
        _followersFeedHelper = followersFeedHelper;
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

    public async Task<object> RetrieveAsync(string? cursor, int limit, Func<Task<string?>> resolveIssuerDid,
        CancellationToken cancellationToken)
    {
        // can resolve the request issuers DID here, so we can personalize the feed is needed
        var issuerDid = await resolveIssuerDid();
        if (issuerDid == null)
        {
            throw new Exception("Missing issuer DID");
        }

        return await _followersFeedHelper.RetrieveAsync(cursor, limit, issuerDid, FollowersFeedHelper.FeedType.Following,
            cancellationToken);
    }
}