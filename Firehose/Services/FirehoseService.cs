using System.Collections.Concurrent;
using System.Net.WebSockets;
using Bluesky.Common.Database;
using Bluesky.Common.Models;
using Bluesky.Firehose.Classifiers;
using Bluesky.Firehose.Sanitizers;
using FishyFlip;
using FishyFlip.Events;
using FishyFlip.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bluesky.Firehose.Services;

public class FirehoseService : BackgroundService
{
    private readonly ILogger<FirehoseService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<ServiceConfig> _serviceConfig;
    private readonly IOptions<FirehoseConfig> firehoseConfig;
    private readonly ISanitizer sanitizer;
    private readonly ClassifierFactory classifierFactory;
    private ConcurrentQueue<SubscribedRepoEventArgs> eventQueue = new();
    private static SemaphoreSlim reconnectSemaphore = new SemaphoreSlim(1, 1);
    private ATProtocol client;

    // Configuration options
    private readonly int EventChunkSize = 100;
    private readonly TimeSpan ProcessingTimeout = TimeSpan.FromSeconds(30);
    private readonly TimeSpan LoggingInterval = TimeSpan.FromSeconds(30);
    private readonly TimeSpan LastEventTimeout = TimeSpan.FromMinutes(1);
    private readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    public FirehoseService(ILogger<FirehoseService> logger, IServiceProvider serviceProvider, IOptions<ServiceConfig> serviceConfig, IOptions<FirehoseConfig> firehoseConfig, ISanitizer sanitizer, ClassifierFactory classifierFactory)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _serviceConfig = serviceConfig;
        this.firehoseConfig = firehoseConfig;
        this.sanitizer = sanitizer;
        this.classifierFactory = classifierFactory;
        client = new ATProtocolBuilder()
            .EnableAutoRenewSession(true)
            .WithInstanceUrl(new Uri(serviceConfig.Value.Url))
            .Build();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            client.OnSubscribedRepoMessage += HandleSubscribedRepoMessage;
            client.OnConnectionUpdated += (sender, e) => HandleConnectionUpdated(sender, e, stoppingToken);

            await client.Server.CreateSessionAsync(_serviceConfig.Value.LoginIdentifier, _serviceConfig.Value.Token, stoppingToken);
            await client.StartSubscribeReposAsync(stoppingToken);

            await ProcessLoop(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in FirehoseService");
        }
    }

    private void HandleSubscribedRepoMessage(object? sender, SubscribedRepoEventArgs e)
    {
        if (e.Message.Record?.Type == "app.bsky.feed.post")
        {
            eventQueue.Enqueue(e);
        }
        else if (e.Message.Record == null && e.Message.Commit != null && e.Message.Commit.Ops != null)
        {
            if (e.Message.Commit.Ops[0].Action == "delete")
            {
                eventQueue.Enqueue(e);
            }
        }
    }

    private void HandleConnectionUpdated(object? sender, SubscriptionConnectionStatusEventArgs e, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Connection updated: {state}", e.State);
        if (e.State == WebSocketState.Closed || e.State == WebSocketState.Aborted)
        {
            _logger.LogInformation("Connection closed. Will try reconnecting");
            Task.Run(async () =>
            {
                // ignore if already reconnecting
                if (!reconnectSemaphore.Wait(0))
                {
                    return;
                }

                try
                {
                    await SafeReconnect(cancellationToken);
                }
                finally
                {
                    reconnectSemaphore.Release();
                }
            }, cancellationToken).ContinueWith(t =>
            {
                if (t.Exception != null)
                {
                    _logger.LogError(t.Exception, "Error in reconnection task");
                }
            }, cancellationToken);
        }
    }
    
    private async Task SafeReconnect(CancellationToken cancellationToken)
    {
        var retryCount = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Attempting to reconnect");
            try
            {
                await client.StopSubscriptionAsync(cancellationToken);
                client.OnSubscribedRepoMessage -= HandleSubscribedRepoMessage;
                client.OnConnectionUpdated -= (sender, e) => HandleConnectionUpdated(sender, e, cancellationToken);
                client.Dispose();
                // replace client with a new instance
                client = new ATProtocolBuilder()
                    .EnableAutoRenewSession(true)
                    .WithInstanceUrl(new Uri(_serviceConfig.Value.Url))
                    .Build();

                await client.Server.CreateSessionAsync(_serviceConfig.Value.LoginIdentifier, _serviceConfig.Value.Token, cancellationToken);
                client.OnSubscribedRepoMessage += HandleSubscribedRepoMessage;
                client.OnConnectionUpdated += (sender, e) => HandleConnectionUpdated(sender, e, cancellationToken);

                await client.StartSubscribeReposAsync(cancellationToken);
                _logger.LogInformation("Reconnected");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping subscription");
                if (retryCount <= 5)
                {
                    retryCount++;
                    var delay = Math.Pow(2, retryCount); // Exponential backoff (2^retryCount seconds) with max limit of 5 retries
                    await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);       
                }
                else
                    throw new Exception("Failed to reconnect after several attempts", ex); 
            }
        }
    }

    private async Task ProcessLoop(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting event processing loop");
        var lastLogTime = DateTime.UtcNow;
        var lastEventTime = DateTime.UtcNow;
        var eventCount = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (DateTime.UtcNow - lastEventTime >= LastEventTimeout)
            {
                _logger.LogInformation("No events received in the last {timeout} minutes. Reconnecting", LastEventTimeout.TotalMinutes);
                await SafeReconnect(cancellationToken);
                // reset the last event time so we don't reconnect again
                lastEventTime = DateTime.UtcNow;
            }

            var newQueue = new ConcurrentQueue<SubscribedRepoEventArgs>();
            var oldQueue = Interlocked.Exchange(ref eventQueue, newQueue);

            if (!oldQueue.IsEmpty)
            {
                eventCount += oldQueue.Count;
            }
            else
            {
                _logger.LogDebug("No events to process. Waiting for {delay} seconds", ReconnectDelay.TotalSeconds);
                await Task.Delay(ReconnectDelay, cancellationToken);
                continue;
            }

            // for every 100 events, create a new scope and process them in parallel
            var tasks = new List<Task>();

            using var cancel = new CancellationTokenSource();
            _logger.LogDebug("Processing {count} events", oldQueue.Count);
            while (!oldQueue.IsEmpty)
            {
                var chunk = new List<SubscribedRepoEventArgs>();

                for (var i = 0; i < EventChunkSize; i++)
                {
                    if (oldQueue.TryDequeue(out var e))
                    {
                        chunk.Add(e);
                    }
                }

                tasks.Add(ProcessChunkAsync(chunk, cancel.Token));
            }

            var allTasks = Task.WhenAll(tasks);
            var completed = await Task.WhenAny(allTasks, Task.Delay(ProcessingTimeout, cancellationToken));

            if (completed != allTasks)
            {
                _logger.LogError("Timed out waiting for tasks to complete");
                cancel.Cancel();
            }
            else
            {
                lastEventTime = DateTime.UtcNow;
            }

            if (DateTime.UtcNow - lastLogTime >= LoggingInterval)
            {
                _logger.LogInformation("Processed {count} events in the last {interval} seconds", eventCount, LoggingInterval.TotalSeconds);
                lastLogTime = DateTime.UtcNow;
                eventCount = 0;
            }
        }

        _logger.LogInformation("Cancellation requested. Waiting for tasks to complete");
    }

    private async Task ProcessChunkAsync(List<SubscribedRepoEventArgs> chunk, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PostContext>();
        foreach (var e in chunk)
        {
            try
            {
                if (e.Message.Record?.Type == "app.bsky.feed.post" && e.Message.Record is FishyFlip.Models.Post post)
                {
                    var op = e.Message.Commit!.Ops![0];

                    if (op.Action == "create")
                    {
                        await CreatePost(db, e, op, post, cancellationToken);
                    }
                    else if (op.Action == "delete")
                    {
                        await DeletePost(db, op, cancellationToken);
                    }
                }
                else if (e.Message.Record == null && e.Message.Commit != null && e.Message.Commit.Ops != null)
                {
                    if (e.Message.Commit.Ops[0].Action == "delete")
                    {
                        await DeletePost(db, e.Message.Commit.Ops[0], cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling event");
                throw; // rethrowing the exception to make it obvious that an error occurred in processing
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while saving changes");
            throw; 
        }
    }

    private async Task CreatePost(PostContext db, SubscribedRepoEventArgs e, Ops op, FishyFlip.Models.Post post, CancellationToken cancellationToken)
    {
        if (op.Path == null || op.Cid == null)
        {
            return;
        }

        _logger.LogTrace("Processing post {path}", op.Path);

        var userDid = e.Message.Commit!.Repo!;
        var uri = ATUri.Create($"at://{userDid}/{op.Path!}");
        var uriSring = uri.ToString();
        var exists = await db.Posts.AnyAsync(p => p.Uri == uriSring, cancellationToken);
        if (exists)
        {
            return;
        }

        // filter langs on firehoseConfig.languages
        if (post.Langs != null && post.Langs.Length > 0)
        {
            // if langs doesn't contain any of the languages in the config, skip
            if (!post.Langs.Any(l => firehoseConfig.Value.Languages.Contains(l)))
            {
                return;
            }
        }

        var p = new Common.Models.Post
        {
            Uri = uri.ToString(),
            Cid = op.Cid!,
            Path = op.Path!.ToString()!,
            Text = post.Text,
            Languages = post.Langs ?? Array.Empty<string>(),
            IndexedAt = DateTime.UtcNow
        };

        // sanitize post
        p.SanitizedText = sanitizer.Sanitize(p.Text!);
        if (string.IsNullOrWhiteSpace(p.SanitizedText))
        {
            return;
        }
        
        foreach (var (topic, classifier) in classifierFactory.GetClassifiers())
        {
            var topicWeight = classifier.GenerateScore(p.SanitizedText!);
            var postTopic = new PostTopic
            {
                PostId = p.Uri,
                TopicId = topic,
                Weight = topicWeight
            };
            db.PostTopics.Add(postTopic);
        }


        db.Posts.Add(p);
    }

    private async Task DeletePost(PostContext db, Ops op, CancellationToken cancellationToken)
    {
        if (op.Path == null || !op.Path!.Contains("posts/"))
        {
            return;
        }

        try
        {
            // check if exists and delete if true, only return the cid
            var cid = await db.Posts.Where(p => p.Path == op.Path).Select(p => p.Cid).FirstOrDefaultAsync(cancellationToken);
            if (cid == null)
            {
                return;
            }

            var toRemove = new Common.Models.Post { Cid = cid };
            db.Posts.Attach(toRemove);
            db.Posts.Remove(toRemove);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting post {path}", op.Path);
        }
    }
}