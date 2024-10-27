using FishyFlip.Events;
using FishyFlip.Models;

namespace BlueskyFeed.Classifiers;

public class LikeClassifier : IClassifier
{
    private readonly ILogger<LikeClassifier> _logger;
    public LikeClassifier(ILogger<LikeClassifier> logger)
    {
        _logger = logger;
    }
    
    public record ClassifiedLikedPost(DateTime IndexedAt, string LikeRkey, string LikeCid, ATUri Uri, ATDid LikedByRepo);
    
    private readonly List<ClassifiedLikedPost> _posts = new();
    public ClassifiedLikedPost[] GetPosts() => _posts.ToArray();

    public Task Cleanup()
    {
        var removed = _posts.RemoveAll(x => x.IndexedAt < DateTime.UtcNow.AddDays(-1));
        _logger.LogInformation("Removed {Count} liked posts", removed);
        return Task.CompletedTask;
    }
    
    public Task Classify(JetStreamATWebSocketRecordEventArgs args)
    {
        if (args.Record.Commit == null
            || args.Record.Commit.Type == ATWebSocketCommitType.Unknown
            || args.Record.Commit.RKey == null)
        {
            return Task.CompletedTask;
        }
        
        if (args.Record.Commit.Type == ATWebSocketCommitType.Delete)
        {
            _posts.RemoveAll(x => x.LikeRkey == args.Record.Commit.RKey);
            return Task.CompletedTask;
        }
        
        if (args.Record.Commit.RKey == null
            || args.Record.Did == null)
        {
            return Task.CompletedTask;
        }
        
        if (args.Record.Commit.Type == ATWebSocketCommitType.Create 
            && args.Record.Commit.Record is Like {Subject.Uri: not null} like && args.Record.Commit.Cid != null)
        {
            _posts.Add(new ClassifiedLikedPost(
                DateTime.UtcNow, 
                args.Record.Commit.RKey,
                args.Record.Commit.Cid,
                like.Subject.Uri,
                args.Record.Did
            ));
        }

        return Task.CompletedTask;
    }
}