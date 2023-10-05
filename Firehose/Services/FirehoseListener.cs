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
    private readonly ConcurrentBag<SubscribedRepoEventArgs> eventQueue = new();


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
            eventQueue.Add(e);
        };
        client.OnConnectionUpdated += (o, e) => HandleConnectionUpdated(o, e, client);
        await client.StartSubscribeReposAsync();

        // timer for event processing
        var _ = Task.Run(async () =>
        {
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                // take all events from the queue
                var events = new List<SubscribedRepoEventArgs>();
                while (eventQueue.TryTake(out var e))
                {
                    events.Add(e);
                }

                if (events.Count > 0)
                {
                    _logger.LogInformation("Processing {count} events", events.Count);
                }

                var processing = Task.Run(async () => 
                {
                    using var scope = _serviceProvider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<PostContext>();
                    
                    foreach (var e in events)
                    {
                        try
                        {
                            if (e.Message.Record?.Type == "app.bsky.feed.post")
                            {
                                await HandlePost(db, e);
                            }
                            else if (e.Message.Record == null && e.Message.Commit != null && e.Message.Commit.Ops != null)
                            {
                                if (e.Message.Commit.Ops[0].Action == "delete")
                                {
                                    await DeletePost(db, e.Message.Commit!.Ops![0]);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error handling event");
                        }
                    }
                    
                    await db.SaveChangesAsync();
                });

                await Task.WhenAll(processing, Task.Delay(TimeSpan.FromSeconds(10), cancellationTokenSource.Token));
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

    private async Task HandlePost(PostContext db, SubscribedRepoEventArgs e)
    {
        if (e.Message.Record?.Type != "app.bsky.feed.post" || e.Message.Record is not FishyFlip.Models.Post post)
        {
            return;
        }

        var op = e.Message.Commit!.Ops![0];
        try
        {
            if (op.Action == "create")
            {
                await CreatePost(db, e, op, post);
            }
            else if (op.Action == "delete")
            {
                await DeletePost(db, op);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling post {path}", op.Path);
        }
    }

    private async Task CreatePost(PostContext db, SubscribedRepoEventArgs e, Ops op, FishyFlip.Models.Post post)
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

    private async Task DeletePost(PostContext db, Ops op)
    {
        if (op.Path == null || !op.Path!.Contains("posts/"))
        {
            return;
        }

        try
        {
            // check if exists and delete if true, only return the cid
            var cid = await db.Posts.Where(p => p.Path == op.Path).Select(p => p.Cid).FirstOrDefaultAsync();
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