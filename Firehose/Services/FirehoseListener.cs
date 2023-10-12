using System.Collections.Concurrent;
using System.Net.WebSockets;
using Bluesky.Common.Database;
using FishyFlip;
using FishyFlip.Events;
using FishyFlip.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Bluesky.Firehose.Services;

public class FirehoseListener : IHostedService
{
    private readonly ILogger<FirehoseListener> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<ServiceConfig> serviceConfig;
    private ConcurrentQueue<SubscribedRepoEventArgs> eventQueue = new();
    private readonly ATProtocol client;

    // The amount of events to process per db transaction
    const int EventChunkSize = 100;

    // The amount of time to wait for events to process before timing out
    private readonly TimeSpan ProcessingTimeout = TimeSpan.FromSeconds(30);

    // The amount of time to wait before logging the amount of events processed
    private readonly TimeSpan LoggingInterval = TimeSpan.FromSeconds(30);

    // The amount of time to wait before reconnecting if no events are received
    private readonly TimeSpan LastEventTimeout = TimeSpan.FromMinutes(1);

    // The amount of time to wait before reconnecting if the connection is closed
    private readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    public FirehoseListener(ILogger<FirehoseListener> logger, IServiceProvider serviceProvider, IOptions<ServiceConfig> serviceConfig)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        this.serviceConfig = serviceConfig;
        client = new ATProtocolBuilder()
            .EnableAutoRenewSession(true)
            .WithInstanceUrl(new Uri(serviceConfig.Value.Url))
            .Build();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await client.Server.CreateSessionAsync(serviceConfig.Value.LoginIdentifier, serviceConfig.Value.Token, cancellationToken);
        client.OnSubscribedRepoMessage += (o, e) => {                            
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
        };
        client.OnConnectionUpdated += (o, e) => HandleConnectionUpdated(o, e, client);
        await client.StartSubscribeReposAsync();

        // timer for event processing
        var _ = Task.Run(async () => await ProcessLoop(cancellationToken), cancellationToken);
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
                await Reconnect(client);
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
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                continue;
            }

            // for every 100 events, create a new scope and process them in parallel
            var tasks = new List<Task>();

            var cancel = new CancellationTokenSource();
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

                tasks.Add(Task.Run(async () =>
                {
                    using var scope = _serviceProvider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<PostContext>();
                    foreach (var e in chunk)
                    {
                        try
                        {
                            if (e.Message.Record?.Type == "app.bsky.feed.post")
                            {
                                await HandlePost(db, e, cancel.Token);
                            }
                            else if (e.Message.Record == null && e.Message.Commit != null && e.Message.Commit.Ops != null)
                            {
                                if (e.Message.Commit.Ops[0].Action == "delete")
                                {
                                    await DeletePost(db, e.Message.Commit!.Ops![0], cancel.Token);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error handling event");
                        }
                    }

                    await db.SaveChangesAsync(cancel.Token);
                }, cancel.Token));
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

    private void HandleConnectionUpdated(object? sender, SubscriptionConnectionStatusEventArgs e, ATProtocol client)
    {
        _logger.LogInformation("Connection updated: {state}", e.State);
        if (e.State != WebSocketState.Closed && e.State != WebSocketState.Aborted)
        {
            return;
        }
        
        _logger.LogInformation("Connection closed. Will try reconnecting");
        Task.Run(async () =>
        {
            await Task.Delay(ReconnectDelay);
            await Reconnect(client);
        });
    }

    private async Task Reconnect(ATProtocol client)
    {
        _logger.LogInformation("Attempting to reconnect");

        try
        {
            await client.StopSubscriptionAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping subscription");
        }

        await client.Server.CreateSessionAsync(serviceConfig.Value.LoginIdentifier, serviceConfig.Value.Token);
        await client.StartSubscribeReposAsync();
        _logger.LogInformation("Reconnected");
    }

    private async Task HandlePost(PostContext db, SubscribedRepoEventArgs e, CancellationToken cancellationToken)
    {
        if (e.Message.Record?.Type != "app.bsky.feed.post" || e.Message.Record is not Post post)
        {
            return;
        }

        var op = e.Message.Commit!.Ops![0];
        try
        {
            if (op.Action == "create")
            {
                await CreatePost(db, e, op, post, cancellationToken);
            }
            else if (op.Action == "delete")
            {
                await DeletePost(db, op, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling post {path}", op.Path);
        }
    }

    private async Task CreatePost(PostContext db, SubscribedRepoEventArgs e, Ops op, Post post, CancellationToken cancellationToken)
    {
        if (op.Path == null || op.Cid == null)
        {
            return;
        }

        var userDid = e.Message.Commit!.Repo!;
        var uri = ATUri.Create($"at://{userDid}/{op.Path!}");
        var p = new Common.Models.Post
        {
            Uri = uri.ToString(),
            Cid = op.Cid!,
            Path = op.Path!.ToString()!,
            Text = post.Text,
            Langs = post.Langs?.Length > 0 ? string.Join(",", post.Langs) : null,
            Blob = JsonConvert.SerializeObject(e),
            IndexedAt = DateTime.UtcNow
        };

        // check if exists and return if true
        var pkCompare = uri.ToString();
        var exists = await db.Posts.AnyAsync(p => p.Uri == pkCompare, cancellationToken);
        if (exists)
        {
            return;
        }

        await db.Posts.AddAsync(p, cancellationToken);
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

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}