using Bluesky.Common.Database;
using Bluesky.Common.Models;
using Bluesky.Firehose.Classifiers;
using Bluesky.Firehose.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bluesky.Firehose.Services;

public class Classifier : IHostedService
{
    private readonly ILogger<Classifier> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IClassifier _classifier;

    public Classifier(ILogger<Classifier> logger, IServiceProvider serviceProvider, IClassifier classifier)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _classifier = classifier;
    }

    // take posts from db, classify them into one or more feeds, save to topics table
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // process posts
        var _ = Task.Run(async () => await ProcessLoop(cancellationToken), cancellationToken);

        return Task.CompletedTask;
    }

    private async Task ProcessLoop(CancellationToken cancellationToken)
    {
        int logCounter = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (logCounter % 60 == 0)
                {
                    await LogStats(cancellationToken);
                }

                var process = ProcessPosts(cancellationToken);

                await Task.WhenAll(process, Task.Delay(TimeSpan.FromSeconds(10), cancellationToken));
                logCounter++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing posts");
            }
        }
    }

    // cache last 10 post ids so we don't keep printing them
    private readonly Dictionary<string, HashSet<string>> lastPostIds = new();

    private async Task LogStats(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PostContext>();

        var postCount = await dbContext.Posts.CountAsync(cancellationToken);
        var postTopicCount = await dbContext.PostTopics.CountAsync(cancellationToken);
        var topicCount = await dbContext.Topics.CountAsync(cancellationToken);

        _logger.LogInformation("Post count: {postCount}, PostTopic count: {postTopicCount}, Topic count: {topicCount}", postCount, postTopicCount, topicCount);

        // print last 10 posts matching each category
        var topics = await dbContext.Topics.ToListAsync(cancellationToken);
        foreach (var topic in topics)
        {
            // posts where score > 100 and topic matches
            var posts = await dbContext.Posts.Where(p => p.PostTopics.Any(pt => pt.TopicId == topic.Name && pt.Weight >= 100))
                .OrderByDescending(p => p.IndexedAt)
                .Take(10)
                .ToListAsync(cancellationToken);

            if (posts.Count > 0 && posts.Any(p => !lastPostIds.ContainsKey(topic.Name) || !lastPostIds[topic.Name].Contains(p.Uri)))
            {
                if (!lastPostIds.ContainsKey(topic.Name))
                {
                    lastPostIds[topic.Name] = new HashSet<string>();
                }

                _logger.LogInformation("Last posts for {topic}:", topic.Name);
                foreach (var post in posts)
                {
                    if (lastPostIds[topic.Name].Contains(post.Uri))
                    {
                        continue;
                    }

                    _logger.LogInformation("  {path} - {text}", post.Path, post.Text);
                    lastPostIds[topic.Name].Add(post.Uri);
                }

                // clear out old post ids
                if (lastPostIds[topic.Name].Count > 10)
                {
                    var toRemove = lastPostIds[topic.Name].Take(lastPostIds[topic.Name].Count - 10).ToList();
                    foreach (var remove in toRemove)
                    {
                        lastPostIds[topic.Name].Remove(remove);
                    }
                }
            }
        }
    }

    private async Task ProcessPosts(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PostContext>();

        // unknown topic is a fallback if there are no other topics being used since we wont be able to classify them
        var unknownTopic = await dbContext.Topics.FirstOrDefaultAsync(t => t.Name == "unknown", cancellationToken);
        if (unknownTopic == null)
        {
            unknownTopic = new Topic
            {
                Name = "unknown"
            };
            dbContext.Topics.Add(unknownTopic);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var posts = dbContext.Posts.Where(p => p.SanitizedText != null && p.PostTopics.Count == 0)
            .OrderByDescending(p => p.IndexedAt)
            .Take(100).AsQueryable();

        if (!posts.Any())
        {
            return;
        }

        var trackedTopics = await dbContext.Topics.ToListAsync(cancellationToken);
        var classificationDict = new Dictionary<string, int>();
        foreach (var post in posts)
        {
            // classify text
            var topics = _classifier.ClassifyText(post.SanitizedText!);
            post.PostTopics ??= new List<PostTopic>();

            foreach (var topic in topics)
            {
                // avoid adding duplicate topic names
                var trackedMatch = trackedTopics.FirstOrDefault(t => t.Name == topic.Topic.Name);
                if (trackedMatch != null)
                {
                    topic.Topic = trackedMatch;
                }
                else
                {
                    trackedTopics.Add(topic.Topic);
                }

                post.PostTopics.Add(topic);

                // increment classification count if score is 100 or higher
                if (topic.Weight >= 100)
                {
                    classificationDict[topic.Topic.Name] = classificationDict.GetValueOrDefault(topic.Topic.Name) + 1;
                }
            }

            if (post.PostTopics.Count == 0)
            {
                post.PostTopics.Add(new PostTopic
                {
                    Topic = unknownTopic,
                    Weight = 100
                });

                classificationDict[unknownTopic.Name] = classificationDict.GetValueOrDefault(unknownTopic.Name) + 1;
            }
        }

        foreach (var (topicName, count) in classificationDict)
        {
            _logger.LogInformation("Classified {count} posts into {topic}", count, topicName);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}