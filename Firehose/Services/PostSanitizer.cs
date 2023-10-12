using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Bluesky.Common.Database;
using Bluesky.Firehose.Sanitizers;
using Microsoft.EntityFrameworkCore;

namespace Bluesky.Firehose.Services
{
    public class PostSanitizer : BackgroundService
    {
        private readonly ILogger<PostSanitizer> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ISanitizer _sanitizer;

        public PostSanitizer(ILogger<PostSanitizer> logger, IServiceProvider serviceProvider, ISanitizer sanitizer)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _sanitizer = sanitizer;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var processingTimeout = TimeSpan.FromSeconds(30);
            var loggingInterval = TimeSpan.FromSeconds(30);
            var lastLogTime = DateTime.UtcNow;
            var processedCount = 0;
            var compressionCount = 0;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var cancel = new CancellationTokenSource();
                    var processing = ProcessPosts(stoppingToken);
                    var delayTask = Task.Delay(processingTimeout, stoppingToken);

                    // if processing does not complete before the delay, cancel it
                    var completedTask = await Task.WhenAny(processing, delayTask);
                    if (completedTask == delayTask)
                    {
                        cancel.Cancel();
                        _logger.LogInformation("Processing posts took longer than {processingInterval}, cancelling", processingTimeout);
                    }
                    else
                    {
                        var (postCount, compression) = await processing;
                        processedCount += postCount;
                        compressionCount += compression;

                        if (postCount == 0)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                        }
                    }

                    if (DateTime.UtcNow - lastLogTime > loggingInterval)
                    {
                        _logger.LogInformation("Sanitized {processedCount} posts ({compressionCount} chars removed) in the last {time} seconds", processedCount, compressionCount, loggingInterval.TotalSeconds);
                        lastLogTime = DateTime.UtcNow;
                        processedCount = 0;
                        compressionCount = 0;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing posts");
                }
            }
        }

        private async Task<(int, int)> ProcessPosts(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PostContext>();

            // posts where SanitizedText is null
            var posts = await dbContext.Posts.Where(p => p.SanitizedText == null && p.Text != null && p.Langs != null && p.Langs.Contains("en"))
                .OrderBy(p => p.Cid)
                .Take(1000)
                .ToListAsync(stoppingToken);

            if (posts.Count == 0)
            {
                return (0, 0);
            }

            int postCount = 0;
            int compression = 0;

            foreach (var post in posts)
            {
                try
                {
                    // sanitize text
                    var sanitizedText = _sanitizer.Sanitize(post.Text!);
                    post.SanitizedText = sanitizedText;
                    compression += (post.Text?.Length ?? 0) - sanitizedText.Length;
                    postCount++;
                    _logger.LogTrace("Sanitized post {uri}", post.Uri);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sanitizing post {uri}", post.Uri);
                    post.SanitizedText = string.Empty;
                }
            }

            await dbContext.SaveChangesAsync(stoppingToken);
            return (postCount, compression);
        }
    }
}
