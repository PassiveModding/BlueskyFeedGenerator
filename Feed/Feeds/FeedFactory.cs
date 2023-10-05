using System.Linq;

namespace Bluesky.Feed.Feeds;

public class FeedFactory
{
    private readonly ILogger<FeedFactory> logger;
    private readonly IServiceProvider serviceProvider;
    private readonly Dictionary<string, IFeed> feeds;

    public FeedFactory(ILogger<FeedFactory> logger, IServiceProvider serviceProvider, Dictionary<string, IFeed> feeds)
    {
        this.logger = logger;
        this.serviceProvider = serviceProvider;
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