using Bluesky.Feed.Auth;
using Bluesky.Feed.Config;
using Bluesky.Feed.Feeds;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Bluesky.Feed.Controllers;

[ApiController]
public class FeedController : ControllerBase
{
    private readonly FeedConfig config;
    private readonly FeedFactory feedFactory;
    private readonly DidResolver didResolver;
    private readonly ILogger<FeedController> logger;

    public FeedController(IOptions<FeedConfig> config, FeedFactory feedFactory, DidResolver didResolver, ILogger<FeedController> logger)
    {
        this.config = config.Value;
        this.feedFactory = feedFactory;
        this.didResolver = didResolver;
        this.logger = logger;
    }

    /* Returns information about a given feed generator including TOS & offered feed URIs. */
    [HttpGet("/xrpc/app.bsky.feed.describeFeedGenerator")]
    public Task<IActionResult> DescribeFeedGenerator()
    {
        var response = new
        {
            encoding = "application/json",
            body = new 
            {
                did = config.ServiceDid,
                feeds = feedFactory.GetFeeds().Select(x => new 
                {
                    uri = x.Item1,
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
        var algo = feedFactory.GetFeed(feed);
        if (algo == null)
        {
            return BadRequest("Unknown feed");
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
                var validation = await Auth.Auth.VerifyJwt(token, null, didResolver);       
                logger.LogInformation("User {validation} authorized for feed {feed}", validation, feed);        
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

    [HttpGet("/.well-known/did.json")]
    public Task<IActionResult> Get()
    {
        if (string.IsNullOrEmpty(config.ServiceDid) || !config.ServiceDid.EndsWith(config.HostName))
        {
            return Task.FromResult<IActionResult>(NotFound());
        }

        return Task.FromResult<IActionResult>(Ok(new
        {
            @context = new[] { "https://www.w3.org/ns/did/v1" },
            id = config.ServiceDid,
            service = new[]
            {
                new
                {
                    id = "#bsky_fg",
                    type = "BskyFeedGenerator",
                    serviceEndpoint = $"https://{config.HostName}",
                },
            },
        }));
    }
}