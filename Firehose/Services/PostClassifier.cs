using System.Diagnostics;
using Bluesky.Common.Database;
using Bluesky.Common.Models;
using Bluesky.Firehose.Classifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bluesky.Firehose.Services;

public class PostClassifier : IHostedService
{
    private readonly ILogger<PostClassifier> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ClassifierFactory classifierFactory;

    public PostClassifier(ILogger<PostClassifier> logger, IServiceProvider serviceProvider, ClassifierFactory classifierFactory)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        this.classifierFactory = classifierFactory;
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
        var processingTimeout = TimeSpan.FromSeconds(30);
        var loggingInterval = TimeSpan.FromSeconds(30);
        var lastLogTime = DateTime.UtcNow;
        var processedCount = 0;
        int batchSize = 100;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var cancel = new CancellationTokenSource();
                var processed = ProcessPosts(batchSize, cancel.Token);
                var delayTask = Task.Delay(processingTimeout, cancellationToken);

                // if processing does not complete before the delay, cancel it
                var completedTask = await Task.WhenAny(processed, delayTask);
                if (completedTask == delayTask)
                {
                    cancel.Cancel();
                    _logger.LogInformation("Processing posts took longer than {processingInterval}, cancelling", processingTimeout);
                }
                else
                {
                    var postCount = await processed;
                    var posts = postCount.Sum();
                    processedCount += posts;
                    if (posts == 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                    }
                }

                if (DateTime.UtcNow - lastLogTime > loggingInterval)
                {
                    _logger.LogInformation("Classified {count} posts in the last {time} seconds", processedCount, loggingInterval.TotalSeconds);
                    lastLogTime = DateTime.UtcNow;
                    processedCount = 0;
                }                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing posts");
            }
        }
    }

    private async Task<int[]> ProcessPosts(int batchSize, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PostContext>();
        var topics = await dbContext.Topics.ToListAsync(cancellationToken);

        int[] processed = new int[topics.Count];
        // get posts which do not have a record for each topic
        for (int i = 0; i < topics.Count; i++)
        {
            Topic? topic = topics[i];
            var classifier = classifierFactory.GetFeed(topic.Name);
            if (classifier == null)
            {
                continue;
            }

            var sw = Stopwatch.StartNew();
            // get only post text and id
            var posts = await dbContext.Posts.Where(p => p.SanitizedText != null && !p.PostTopics.Any(pt => pt.TopicId == topic.Name))
                .OrderByDescending(p => p.IndexedAt)
                .Take(batchSize)
                .Select(p => new
                {
                    p.Uri,
                    p.SanitizedText,
                    p.Text
                })                
                .ToListAsync(cancellationToken);
            sw.Stop();
            _logger.LogTrace("Retrieved {count} posts for {topic} in {time}ms", posts.Count, topic.Name, sw.ElapsedMilliseconds);

            if (posts.Count == 0)
            {
                continue;
            }


            sw.Restart();
            int scoresOver100 = 0;
            foreach (var post in posts)
            {
                processed[i]++;
                var topicWeight = classifier.GenerateScore(post.SanitizedText!);
                var postTopic = new PostTopic
                {
                    PostId = post.Uri,
                    TopicId = topic.Name,
                    Weight = topicWeight
                };
                dbContext.PostTopics.Add(postTopic);
                if (topicWeight >= 100)
                {
                    scoresOver100++;
                }
            }
            sw.Stop();
            _logger.LogTrace("Classified {count} posts for {topic} in {time}ms", posts.Count, topic.Name, sw.ElapsedMilliseconds);

            sw.Restart();
            await dbContext.SaveChangesAsync(cancellationToken);
            sw.Stop();
            _logger.LogTrace("Saved {count} posts for {topic} in {time}ms", posts.Count, topic.Name, sw.ElapsedMilliseconds);
            
            _logger.LogDebug("Processed {count} posts for {topic}, {over100} with score over 100", posts.Count, topic.Name, scoresOver100);
        }

        return processed;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}