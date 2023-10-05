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
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private int postCounter;
    private ConcurrentQueue<SubscribedRepoEventArgs> eventQueue = new();


    public FirehoseListener(ILogger<FirehoseListener> logger, IServiceProvider serviceProvider, IOptions<ServiceConfig> serviceConfig)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        this.serviceConfig = serviceConfig;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var client = new ATProtocolBuilder()
            .EnableAutoRenewSession(true)
            .WithInstanceUrl(new Uri(serviceConfig.Value.Url))
            .Build();

        await client.Server.CreateSessionAsync(serviceConfig.Value.LoginIdentifier, serviceConfig.Value.Token, cancellationTokenSource.Token);
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
        var _ = Task.Run(async () =>
        {
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                var newQueue = new ConcurrentQueue<SubscribedRepoEventArgs>();
                var oldQueue = Interlocked.Exchange(ref eventQueue, newQueue);


                if (!oldQueue.IsEmpty)
                {
                    _logger.LogInformation("Processing {count} events", oldQueue.Count);
                }
                else
                {
                    await Task.Delay(1000, cancellationToken);
                    continue;
                }

                // for every 100 events, create a new scope and process them in parallel
                var chunks = 100;
                var tasks = new List<Task>();

                var cancel = new CancellationTokenSource();
                while (!oldQueue.IsEmpty)
                {
                    var chunk = new List<SubscribedRepoEventArgs>();
                    for (var i = 0; i < chunks; i++)
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
                            await HandleEvent(db, e, cancel.Token);
                        }

                        await db.SaveChangesAsync(cancel.Token);
                    }, cancel.Token));
                }

                // max time to wait for all tasks to complete
                var timeout = TimeSpan.FromSeconds(60);
                var allTasks = Task.WhenAll(tasks);
                var completed = await Task.WhenAny(allTasks, Task.Delay(timeout, cancellationToken));     
                if (completed != allTasks)
                {
                    _logger.LogError("Timed out waiting for tasks to complete");
                    cancel.Cancel();
                }  
            }
        }, cancellationToken);
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
        });
    }

    private async Task HandleEvent(PostContext db, SubscribedRepoEventArgs e, CancellationToken cancellationToken)
    {
        try
        {
            if (e.Message.Record?.Type == "app.bsky.feed.post")
            {
                await HandlePost(db, e, cancellationToken);
            }
            else if (e.Message.Record == null && e.Message.Commit != null && e.Message.Commit.Ops != null)
            {
                if (e.Message.Commit.Ops[0].Action == "delete")
                {
                    await DeletePost(db, e.Message.Commit!.Ops![0], cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling event");
        }
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

        // check if exists by using the cid but don't return a value
        var exists = await db.Posts.AnyAsync(p => p.Cid == op.Cid.ToString());
        if (exists)
        {
            return;
        }

        await db.Posts.AddAsync(p);
        postCounter++;
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
        cancellationTokenSource.Cancel();
        return Task.CompletedTask;
    }
}