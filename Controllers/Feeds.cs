using System.Text.Json.Serialization;
using BlueskyFeedGenerator.Auth;
using BlueskyFeedGenerator.Config;
using BlueskyFeedGenerator.Feeds;
using BlueskyFeedGenerator.Models;
using Microsoft.AspNetCore.Mvc;

namespace BlueskyFeedGenerator.Controllers;


[ApiController]
public class FeedController : ControllerBase
{
    public FeedController(FeedConfig config, Dictionary<string, IFeed> feeds, DidResolver didResolver, ILogger<FeedController> logger)
    {
        Config = config;
        Feeds = feeds;
        DidResolver = didResolver;
        Logger = logger;
    }

    public FeedConfig Config { get; }

    public Dictionary<string, IFeed> Feeds { get; }
    public DidResolver DidResolver { get; }
    public ILogger<FeedController> Logger { get; }

    /* Returns information about a given feed generator including TOS & offered feed URIs. */
    [HttpGet("/xrpc/app.bsky.feed.describeFeedGenerator")]
    public Task<IActionResult> DescribeFeedGenerator()
    {
        var response = new
        {
            encoding = "application/json",
            body = new 
            {
                did = Config.ServiceDid,
                feeds = Feeds.Select(x => new 
                {
                    uri = x.Key,
                }).ToList(),
            },
        };
        return Task.FromResult<IActionResult>(Ok(response));
    }

    /* A skeleton of a feed provided by a feed generator. */
    [HttpGet("/xrpc/app.bsky.feed.getFeedSkeleton")]
    public async Task<IActionResult> GetFeedSkeletonAsync([FromQuery] string feed, [FromQuery] string? cursor, [FromQuery] string limit, CancellationToken cancellationToken)
    {
        // feed format: at://{publisher-did}/app.bsky.feed.generator/{short-name}
        if (!Feeds.TryGetValue(feed, out var algo))
        {
            return BadRequest("Unsupported algorithm");
        }

        string issuerDid = "";
        if (algo.AuthorizeUser)
        {
            // get auth header
            var authHeader = Request.Headers["Authorization"].FirstOrDefault();
            if (authHeader == null)
            {
                return BadRequest("Missing authorization header");
            }

            var token = authHeader.Replace("Bearer ", "").Trim();

            try 
            {
                var validation = await Auth.Auth.VerifyJwt(token, null, DidResolver);       
                Logger.LogInformation("User {validation} authorized for feed {feed}", validation, feed);        
                issuerDid = validation; 
            }
            catch (Exception)
            {
                return BadRequest("Invalid authorization header");
            }
        }

        try
        {
            var body = await algo.RetrieveAsync(cursor, int.Parse(limit), issuerDid, cancellationToken);
            return Ok(body);
        }
        catch (TaskCanceledException)
        {
            return BadRequest("Request timed out");
        }
        catch (Exception)
        {
            return BadRequest("Malformed cursor");
        }
    }

    [Route(".well-known/did.json")]
    [HttpGet]
    public Task<IActionResult> Get()
    {
        if (string.IsNullOrEmpty(Config.ServiceDid) || !Config.ServiceDid.EndsWith(Config.HostName))
        {
            return Task.FromResult<IActionResult>(NotFound());
        }

        return Task.FromResult<IActionResult>(Ok(new
        {
            @context = new[] { "https://www.w3.org/ns/did/v1" },
            id = Config.ServiceDid,
            service = new[]
            {
                new
                {
                    id = "#bsky_fg",
                    type = "BskyFeedGenerator",
                    serviceEndpoint = $"https://{Config.HostName}",
                },
            },
        }));
    }
}