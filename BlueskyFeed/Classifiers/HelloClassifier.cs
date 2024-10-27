using FishyFlip.Events;
using FishyFlip.Models;

namespace BlueskyFeed.Classifiers;

public class HelloClassifier : IClassifier
{
    public HelloClassifier(ILogger<HelloClassifier> logger)
    {
        _logger = logger;
    }

    public record ClassifiedPost(DateTime IndexedAt, string Cid, string RKey, string Repo);
    private readonly List<ClassifiedPost> _posts = new();
    private readonly ILogger<HelloClassifier> _logger;
    
    public ClassifiedPost[] GetPosts() => _posts.ToArray();

    public Task Cleanup()
    {
        var removed = _posts.RemoveAll(x => x.IndexedAt < DateTime.UtcNow.AddDays(-7));
        _logger.LogInformation("Removed {Count} posts", removed);
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
            _posts.RemoveAll(x => x.RKey == args.Record.Commit.RKey);
            return Task.CompletedTask;
        }
        
        if (args.Record.Commit.RKey == null
            || args.Record.Did == null)
        {
            return Task.CompletedTask;
        }
        
        if (args.Record.Commit.Type == ATWebSocketCommitType.Create 
            && args.Record.Commit.Record is Post post 
            && args.Record.Commit.Cid != null)
        {
            if (post.Text == null) return Task.CompletedTask;

            if (post.Text.Contains("hello"))
            {
                _posts.Add(new ClassifiedPost(DateTime.UtcNow, args.Record.Commit.Cid.Encode(), args.Record.Commit.RKey, args.Record.Did.Handler));
            }
        }

        return Task.CompletedTask;
    }
}