using System.Diagnostics;
using Bluesky.Common.Database;
using Bluesky.Common.Models;
using Bluesky.Firehose.Classifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bluesky.Firehose.Services
{
    // Used to classify existing posts for new topics that have been added.
    // This is a background service that runs continuously.
    public class PostClassifier : BackgroundService
    {
        private readonly ILogger<PostClassifier> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ClassifierFactory _classifierFactory;
        private const int BatchSize = 100;
        private CancellationTokenSource _cancellationTokenSource = null!;

        public PostClassifier(ILogger<PostClassifier> logger, IServiceProvider serviceProvider, ClassifierFactory classifierFactory)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _classifierFactory = classifierFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var processingTimeout = TimeSpan.FromSeconds(30);
            var loggingInterval = TimeSpan.FromSeconds(30);
            var lastLogTime = DateTime.UtcNow;
            var processedCount = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var cancel = new CancellationTokenSource();
                    
                    string[] topics;
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var dbContext = scope.ServiceProvider.GetRequiredService<PostContext>();
                        topics = await dbContext.Topics.Select(x => x.Name).ToArrayAsync(cancellationToken);
                        _logger.LogInformation("Classifying topics {string}", string.Join(", ", topics));
                    }
                    
                    foreach (var topicName in topics)
                    {
                        var processed = ClassifyPosts(BatchSize, topicName, cancel.Token);
                        var delayTask = Task.Delay(processingTimeout, cancellationToken);

                        // if processing does not complete before the delay, cancel it
                        var completedTask = await Task.WhenAny(processed, delayTask);
                        if (completedTask == delayTask)
                        {
                            cancel.Cancel();
                            _logger.LogInformation("Classifying posts took longer than {processingInterval}, cancelling", processingTimeout);
                        }
                        else
                        {
                            var postCount = await processed;
                            processedCount += postCount;
                        }
                    }

                    if (processedCount == 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
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

        private async Task<int> ClassifyPosts(int batchSize, string topicName, CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PostContext>();
            int processed = 0;
            var classifier = _classifierFactory.GetFeed(topicName);
            if (classifier == null)
            {
                return processed;
            }

            var sw = Stopwatch.StartNew();

            var posts = await dbContext.Posts
                .Where(p => p.SanitizedText != null && p.PostTopics.All(pt => pt.Topic.Name != topicName))
                .OrderBy(p => p.Cid)
                .Select(p => new
                {
                    p.Uri,
                    p.SanitizedText
                })
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            sw.Stop();
            _logger.LogInformation("Retrieved {count} posts for {topic} in {time}ms", posts.Count, topicName, sw.ElapsedMilliseconds);

            if (posts.Count == 0)
            {
                return processed;
            }


            sw.Restart();
            int scoresOver100 = 0;
            foreach (var post in posts)
            {
                processed++;
                var topicWeight = classifier.GenerateScore(post.SanitizedText!);
                var postTopic = new PostTopic
                {
                    PostId = post.Uri,
                    TopicId = topicName,
                    Weight = topicWeight
                };
                dbContext.PostTopics.Add(postTopic);
                if (topicWeight >= 100)
                {
                    scoresOver100++;
                }
            }
            sw.Stop();
            _logger.LogTrace("Classified {count} posts for {topic} in {time}ms", posts.Count, topicName, sw.ElapsedMilliseconds);

            sw.Restart();
            await dbContext.SaveChangesAsync(cancellationToken);
            sw.Stop();
            _logger.LogInformation("Saved {count} posts for {topic} in {time}ms", posts.Count, topicName, sw.ElapsedMilliseconds);

            _logger.LogDebug("Processed {count} posts for {topic}, {over100} with score over 100", posts.Count, topicName, scoresOver100);

            return processed;
        }

        public override void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();

            base.Dispose();
        }
    }
}
