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
public class PostProcessor : IHostedService
{
    private readonly ILogger<PostProcessor> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ISanitizer sanitizer;


    public PostProcessor(ILogger<PostProcessor> logger, IServiceProvider serviceProvider, ISanitizer sanitizer)
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
        while (!cancellationToken.IsCancellationRequested)
        {
            var processing = ProcessPosts(cancellationToken);
            await Task.WhenAll(processing, Task.Delay(TimeSpan.FromSeconds(10), cancellationToken));
        }
    }

    private async Task ProcessPosts(CancellationToken cancellationToken)
    {         
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PostContext>();

        // posts where SanitizedText is null
        var posts = dbContext.Posts.Where(p => p.SanitizedText == null && p.Text != null && p.Langs != null && p.Langs.Contains("en"))
            .OrderBy(p => p.Cid)
            .Take(1000).AsQueryable();

        if (!posts.Any())
        {
            return;
        }

        int compression = 0;
        foreach (var post in posts)
        {
            // sanitize text
            var sanitizedText = sanitizer.Sanitize(post.Text!);
            post.SanitizedText = sanitizedText;
            compression += (post.Text?.Length ?? 0) - sanitizedText.Length;
        }

        if (posts.Any())
        {
            _logger.LogInformation("Sanitized {count} posts, compression: {compression} chars", posts.Count(), compression);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}