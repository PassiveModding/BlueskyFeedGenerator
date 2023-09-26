using BlueskyFeedGenerator.Database;
using BlueskyFeedGenerator.Models;
using Microsoft.EntityFrameworkCore;

namespace BlueskyFeedGenerator.Feeds;

public class LinuxFeed : IFeed
{
    public FeedFlag Flag => FeedFlag.Linux;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LinuxFeed> _logger;

    public LinuxFeed(IServiceProvider serviceProvider, ILogger<LinuxFeed> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task<bool> CategorizeAsync(FishyFlip.Models.Post post, CancellationToken cancellationToken, out FeedFlag flags)
    {
        flags = FeedFlag.None;
        if (post?.Text?.Contains("linux", StringComparison.OrdinalIgnoreCase) == true)
        {
            flags = Flag;
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public async Task<object> RetrieveAsync(string? cursor, int limit, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        var (builder, indexedAt, cid) = db.GetBuilder(cursor, Flag);

        var posts = await builder.Take(limit).ToListAsync(cancellationToken: cancellationToken);
        return posts.GetFeedResponse();
    }
}