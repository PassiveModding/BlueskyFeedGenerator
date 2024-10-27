using System.Text.Json.Serialization;
using BlueskyFeed.Auth;
using BlueskyFeed.Config;
using BlueskyFeed.Generators;
using BlueskyFeed.Services;
using FishyFlip.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BlueskyFeed.Controllers;

[ApiController]
public class FeedController : ControllerBase, IDisposable
{
    private readonly ILogger<FeedController> _logger;
    private readonly IOptions<AtProtoConfig> _config;
    private readonly ProtoHandler _protoHandler;
    private readonly DidResolver _didResolver;
    private readonly IEnumerable<IFeedGenerator> _generators;
    
    public FeedController(ILogger<FeedController> logger, 
        IOptions<AtProtoConfig> config,
        ProtoHandler protoHandler,
        DidResolver didResolver,
        IEnumerable<IFeedGenerator> generators)

    {
        _logger = logger;
        _config = config;
        _protoHandler = protoHandler;
        _didResolver = didResolver;
        _generators = generators;
    }

    [HttpGet("/ping")]
    public IActionResult Ping()
    {
        return Ok(new { message = "pong", timestamp = DateTime.UtcNow });
    }
    
    [HttpGet("/xrpc/app.bsky.feed.describeFeedGenerator")]
    public IActionResult DescribeFeedGenerator()
    {
        var response = new
        {
            encoding = "application/json",
            body = new 
            {
                did = _config.Value.ServiceDid,
                feeds = _generators.Select(x => new
                {
                    uri = x.GetUri(new ATDid(_config.Value.LoginIdentifier)),
                }).ToArray()
            },
        };
        return Ok(response);
    }
    
    private async Task<string?> GetIssuerDidAsync()
    {
        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (authHeader == null)
        {
            throw new Exception("Missing authorization header");
        }

        var token = authHeader.Replace("Bearer ", "").Trim();
        var validation = await Auth.Auth.VerifyJwt(token, null, _didResolver);
        return validation;
    }
    
    [HttpGet("/xrpc/app.bsky.feed.getFeedSkeleton")]
    public async Task<IActionResult> GetFeedSkeletonAsync([FromQuery] string feed, [FromQuery] string? cursor, [FromQuery] string limit, CancellationToken cancellationToken)
    {
        // feed format: at://{publisher-did}/app.bsky.feed.generator/{short-name}
        var session = await _protoHandler.GetSessionAsync(_config.Value, cancellationToken);
        var algo = _generators.FirstOrDefault(x => x.Matches(feed, session.Did));
        if (algo == null)
        {
            return BadRequest(new
            {
                error = "UnsupportedAlgorithm",
                error_description = "Unsupported Algorithm"
            });
        }
        
        _logger.LogInformation("Retrieving feed {Feed} with cursor {Cursor} and limit {Limit}", feed, cursor, limit);
        var body = await algo.RetrieveAsync(cursor, int.Parse(limit), GetIssuerDidAsync, cancellationToken);
        return Ok(body);
    }
    
    [HttpGet("/.well-known/did.json")]
    public IActionResult Get()
    {
        return Ok(new WellKnownDidResponse(
            Context: ["https://www.w3.org/ns/did/v1"],
            Id: _config.Value.ServiceDid,
            Service:
            [
                new Service(
                    Id: "#bsky_fg",
                    Type: "BskyFeedGenerator",
                    ServiceEndpoint: $"https://{_config.Value.HostName}"
                )
            ]
        ));
    }

    private record WellKnownDidResponse(
        [property: JsonPropertyName("@context")] string[] Context,
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("service")] Service[] Service);

    private record Service(
        [property: JsonPropertyName("id")] string Id, 
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("serviceEndpoint")] string ServiceEndpoint);

    public void Dispose()
    {
    }
}