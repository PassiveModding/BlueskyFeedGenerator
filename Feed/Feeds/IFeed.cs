namespace Bluesky.Feed.Feeds;
public interface IFeed
{
    public bool AuthorizeUser { get; }

    // Get posts for feed
    public Task<object> RetrieveAsync(string? cursor, int limit, string? issuerDid, CancellationToken cancellationToken);
}