using BlueskyFeed.Util;
using FishyFlip.Models;

namespace BlueskyFeed.Generators;

public interface IFeedGenerator
{
    /// <summary>
    /// "at://{publisherDid}/app.bsky.feed.generator/{rKey}"
    /// </summary>
    /// <returns></returns>
    public string GetUri(ATDid publisherDid);
    
    public GeneratorRecordRequest GetRecordRequest();
    
    public bool Matches(string rkey, ATDid publisherDid);

    public Task<object> RetrieveAsync(string? cursor, int limit, Func<Task<string?>> resolveIssuerDid, CancellationToken cancellationToken);
}