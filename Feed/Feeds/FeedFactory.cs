using System.Linq;

namespace Bluesky.Feed.Feeds;

public class FeedFactory
{
    private readonly Dictionary<string, IFeed> feeds;

    public FeedFactory(Dictionary<string, IFeed> feeds)
    {
        this.feeds = feeds;
    }

    public IFeed? GetFeed(string uri)
    {
        if (feeds.TryGetValue(uri, out var feed))
        {
            return feed;
        }
        return null;
    }

    public IEnumerable<(string, IFeed)> GetFeeds()
    {
        return feeds.Select(x => (x.Key, x.Value));
    }
}