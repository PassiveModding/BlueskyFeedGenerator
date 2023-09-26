using BlueskyFeedGenerator.Models;

namespace BlueskyFeedGenerator.Feeds;
public interface IFeed
{
    public FeedFlag Flag { get; }

    // Get posts for feed
    public Task<object> RetrieveAsync(string? cursor, int limit, CancellationToken cancellationToken);

    // Categorize post for feed
    public Task<bool> CategorizeAsync(FishyFlip.Models.Post post, CancellationToken cancellationToken, out FeedFlag flags);
}