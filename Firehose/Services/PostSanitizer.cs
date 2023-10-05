using System.Text.RegularExpressions;
using Bluesky.Common.Database;
using Bluesky.Firehose.Sanitizers;
using LemmaSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bluesky.Firehose.Services;

// partial used due to the generated regexes
public class PostSanitizer : IHostedService
{
    private readonly ILogger<PostSanitizer> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ISanitizer sanitizer;


    public PostSanitizer(ILogger<PostSanitizer> logger, IServiceProvider serviceProvider, ISanitizer sanitizer)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        this.sanitizer = sanitizer;
    }

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
        var compressionCount = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var cancel = new CancellationTokenSource();
                var processing = ProcessPosts(cancel.Token);
                var delayTask = Task.Delay(processingTimeout, cancellationToken);

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
                        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
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


    private async Task<(int, int)> ProcessPosts(CancellationToken cancellationToken)
    {         
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PostContext>();

        // posts where SanitizedText is null
        var posts = dbContext.Posts.Where(p => p.SanitizedText == null && p.Text != null && p.Langs != null && p.Langs.Contains("en"))
            .OrderBy(p => p.Cid)
            .Take(1000).AsQueryable();

        if (!posts.Any())
        {
            return (0, 0);
        }

        int postCount = 0;
        int compression = 0;
        foreach (var post in posts)
        {
            // sanitize text
            var sanitizedText = sanitizer.Sanitize(post.Text!);
            post.SanitizedText = sanitizedText;
            compression += (post.Text?.Length ?? 0) - sanitizedText.Length;
            postCount++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (postCount, compression);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}