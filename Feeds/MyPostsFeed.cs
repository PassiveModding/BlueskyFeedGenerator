using BlueskyFeedGenerator.Models;
using FishyFlip;
using FishyFlip.Models;

namespace BlueskyFeedGenerator.Feeds;

/// <summary>
/// Example feed that retrieves posts from the requestors profile
/// Does not query the database, instead uses the ATProtocol to retrieve posts
/// </summary>
public class MyPostsFeed : IFeed
{
    public FeedFlag Flag => FeedFlag.None;

    public bool AuthorizeUser => true;
    public string Shortname => "myposts";

    public ATProtocol ATProtocol { get; }
    public ILogger<MyPostsFeed> Logger { get; }

    public MyPostsFeed(ATProtocol aTProtocol, ILogger<MyPostsFeed> logger)
    {
        ATProtocol = aTProtocol;
        Logger = logger;
    }

    public Task<bool> CategorizeAsync(FishyFlip.Models.Post post, CancellationToken cancellationToken, out FeedFlag flags)
    {
        flags = FeedFlag.None;
        return Task.FromResult(false);
    }

    public async Task<object> RetrieveAsync(string? cursor, int limit, string? issuerDid, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(issuerDid))
        {
            throw new ArgumentNullException(nameof(issuerDid));
        }

        // lookup user profile using issuerdid
        var posts = await ATProtocol.Repo.ListPostsAsync(ATDid.Create(issuerDid)!, limit, cursor, null, cancellationToken);

        ListRecord[]? records = null;
        string? newCursor = null;
        posts.Switch(
            success => {
                records = success?.Records;
                newCursor = success?.Cursor;
            },
            error => {
                records = null;
                newCursor = null;
            }
        );

        if (records == null)
        {
            throw new Exception("Failed to retrieve posts");
        }

        return new
        {
            cursor = newCursor,
            feed = records.Select(r => new
            {
                post = r.Uri.ToString()
            }).ToArray()
        };
    }
}