using BlueskyFeedGenerator.Database;
using BlueskyFeedGenerator.Models;
using Microsoft.EntityFrameworkCore;

namespace BlueskyFeedGenerator.Feeds;

public class FFXIVFeed : IFeed
{
    public FeedFlag Flag => FeedFlag.FFXIV;
    public bool AuthorizeUser => false;
    public string Shortname => "ffxiv";
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FFXIVFeed> _logger;
    public FFXIVFeed(IServiceProvider serviceProvider, ILogger<FFXIVFeed> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task<bool> CategorizeAsync(FishyFlip.Models.Post post, CancellationToken cancellationToken, out FeedFlag flags)
    {
        flags = FeedFlag.None;
        var keywords = new string[] { "ffxiv", "ff14", "gposers", "final fantasy xiv", "final fantasy 14" };
        var postText = post?.Text?.ToLowerInvariant();
        if (postText != null && keywords.Any(k => postText.Contains(k)))
        {
            flags = Flag;
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }
    
    public async Task<object> RetrieveAsync(string? cursor, int limit, string? issuerDid, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        var (builder, indexedAt, cid) = db.GetBuilder(cursor, Flag);

        var posts = await builder.Take(limit).ToListAsync(cancellationToken: cancellationToken);
        return posts.GetFeedResponse();
    }
}