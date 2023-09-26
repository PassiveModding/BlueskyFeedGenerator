using BlueskyFeedGenerator.Database;
using BlueskyFeedGenerator.Feeds;
using BlueskyFeedGenerator.Models;
using FishyFlip;
using FishyFlip.Events;
using FishyFlip.Models;

namespace BlueskyFeedGenerator.Services;

public class FeedMessageProcessor : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, IFeed> _feeds;
    private readonly ILogger<FeedMessageProcessor> _logger;


    public FeedMessageProcessor(IServiceProvider serviceProvider, Dictionary<string, IFeed> feeds, ILogger<FeedMessageProcessor> logger)
    {
        _serviceProvider = serviceProvider;
        _feeds = feeds;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var atProto = scope.ServiceProvider.GetRequiredService<ATProtocol>();
        atProto.OnSubscribedRepoMessage += async (o, e) => await HandleRepoMessage(o, e);

        await atProto.StartSubscribeReposAsync(cancellationToken);
        var _ = Task.Run(() => EnsureConnectedAsync(cancellationToken), cancellationToken);
    }

    private DateTime lastRepoMessageTime = DateTime.UtcNow;
    private async Task HandleRepoMessage(object? sender, SubscribedRepoEventArgs args)
    {
        lastRepoMessageTime = DateTime.UtcNow;

        //_logger.LogInformation("Received message {Type}", args.Message.Record?.Type);

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();

        // if type then categorize and persist
        switch (args.Message.Record?.Type)
        {
            case "app.bsky.feed.post":
                {
                    var now = DateTime.UtcNow;
                    var persisted = await ProcessPostAsync(dbContext, args, now);
                    if (persisted)
                    {
                        await dbContext.SaveChangesAsync();
                    }
                }
                break;
            /*
            Other events you may receive:
            case "app.bsky.feed.feed.repost":
            case "app.bsky.feed.feed.like":
            case "app.bsky.feed.graph.follow":
            case "app.bsky.feed.graph.block":
            case "app.bsky.actor.profile":
            ...
            */
            default:
                _logger.LogTrace("Unhandled message type {Type}", args.Message.Record?.Type);
                break;
        }
    }

    // True if the post was added to the database
    private async Task<bool> ProcessPostAsync(DataContext dbContext, SubscribedRepoEventArgs args, DateTime now, CancellationToken cancellationToken = default)
    {
        var userDid = args.Message.Commit!.Repo!;
        if (args.Message.Record is not FishyFlip.Models.Post post)
        {
            _logger.LogWarning("Post is null");
            return false;
        }

        bool persisted = false;
        foreach (var op in args.Message.Commit!.Ops!)
        {
            if (op.Action == "update") continue; // bluesky doesn't let users update posts yet

            var uri = ATUri.Create($"at://{userDid}/{op.Path!}");
            if (op.Action == "create")
            {
                if (op.Cid == null) continue;
                // iterate processors to find if any match your feed rules
                // there may be multiple
                var flags = FeedFlag.None;
                foreach (var feed in _feeds)
                {
                    if (await feed.Value.CategorizeAsync(post!, cancellationToken, out var feedFlags))
                    {
                        flags |= feedFlags;
                        _logger.LogInformation("Post {Uri} added to {Feed}, {Text}", uri, feed.Key, post?.Text?.Trim().Replace("\n", " "));
                    }
                }

                if (flags == FeedFlag.None)
                {
                    continue;
                }

                // check if post already exists before adding
                var existing = await dbContext.Posts.FindAsync(new object?[] { uri.ToString() }, cancellationToken: cancellationToken);
                if (existing != null)
                {
                    _logger.LogInformation("Post {Uri} already exists", uri);
                    continue;
                }

                var newPost = new Models.Post
                {
                    Uri = uri.ToString(),
                    Cid = op.Cid,
                    ReplyParent = post?.Reply?.Parent?.Uri.ToString(),
                    ReplyRoot = post?.Reply?.Root?.Uri.ToString(),
                    IndexedAt = now,
                    Flags = flags,
                };

                dbContext.Posts.Add(newPost);
                persisted = true;
            }

            if (op.Action == "delete")
            {
                var existing = dbContext.Posts.Find(uri.ToString());
                if (existing == null)
                {
                    _logger.LogInformation("Post {Uri} does not exist", uri);
                    continue;
                }

                dbContext.Posts.Remove(existing);
                persisted = true;
            }
        }

        return persisted;
    }


    // Fix for random disconnects
    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            // if last event was within 1 minute, don't refresh
            if (DateTime.UtcNow - lastRepoMessageTime < TimeSpan.FromMinutes(1))
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                continue;
            }

            using var scope = _serviceProvider.CreateScope();
            var atProto = scope.ServiceProvider.GetRequiredService<ATProtocol>();
            await atProto.RefreshSessionAsync();

            try
            {
                await atProto.StopSubscriptionAsync(cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error stopping subscription");
            }

            try
            {
                await atProto.StartSubscribeReposAsync(cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error starting subscription");
            }

            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var atProto = scope.ServiceProvider.GetRequiredService<ATProtocol>();
        atProto.OnSubscribedRepoMessage -= async (o, e) => await HandleRepoMessage(o, e);
        await atProto.StopSubscriptionAsync(cancellationToken);
    }
}
