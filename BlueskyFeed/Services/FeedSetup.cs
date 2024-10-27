using BlueskyFeed.Config;
using BlueskyFeed.Generators;
using BlueskyFeed.Util;
using FishyFlip.Models;
using Microsoft.Extensions.Options;

namespace BlueskyFeed.Services;

public class FeedSetup : IHostedService
{
    private readonly IOptions<Setup> _setup;
    private readonly ProtoHandler _protoHandler;
    private readonly ILogger<FeedSetup> _logger;
    private readonly IEnumerable<IFeedGenerator> _generators;
    private readonly AtProtoConfig _config;
    
    public FeedSetup(IOptions<AtProtoConfig> config, 
        IOptions<Setup> setup,
        ProtoHandler protoHandler, 
        ILogger<FeedSetup> logger,
        IEnumerable<IFeedGenerator> generators)

    {
        _setup = setup;
        _protoHandler = protoHandler;
        _logger = logger;
        _generators = generators;
        _config = config.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var proto = await _protoHandler.GetProtocolAsync(_config, cancellationToken);
        var session = await _protoHandler.GetSessionAsync(_config, cancellationToken);
        var allFeeds = new List<GeneratorView>();
        string? cursor = null;
        do
        {
            var result = await proto.Feed.GetActorFeedsAsync(session.Did, cursor: cursor, cancellationToken: cancellationToken);
            var generatorFeed = result.AsT0 ?? throw new Exception("Failed to get actor feeds");
            var feeds = generatorFeed.Feeds;
            allFeeds.AddRange(feeds);
            cursor = generatorFeed.Cursor;
        } while (cursor != null);

        foreach (var generator in _generators)
        {
            var generatorRecord = generator.GetRecordRequest();
            var existingFeed = allFeeds.FirstOrDefault(x => x.Uri.Rkey == generatorRecord.RKey);
            if (_setup.Value.DeleteAllGenerators && existingFeed != null)
            {
                _logger.LogInformation("Deleting feed generator record {RKey}", generatorRecord.RKey);
                var deleteResult =
                    await proto.Repo.DeleteFeedGeneratorRecord(generatorRecord.RKey, cancellationToken);
                if (deleteResult.IsT1)
                {
                    throw new Exception($"[{deleteResult.AsT1.StatusCode}] {deleteResult.AsT1.Detail}");
                }
                
                existingFeed = null;
            }

            if (existingFeed == null && _setup.Value.CreateAllGenerators && generator.GetRecordRequest().Enabled)
            {
                _logger.LogInformation("Creating feed generator record {RKey}", generatorRecord.RKey);
                var createFeedRecord = new CreateFeedGeneratorRecord(session.Did, generatorRecord.RKey,
                    new GeneratorRecord
                    (
                        did: _config.ServiceDid,
                        displayName: generatorRecord.DisplayName,
                        avatar: generatorRecord.Avatar,
                        description: generatorRecord.Description,
                        createdAt: generatorRecord.CreatedAt
                    ));

                var result = await proto.Repo.CreateFeedGeneratorRecord(createFeedRecord,
                    proto.Options.JsonSerializerOptions, cancellationToken);
                if (result.IsT1)
                {
                    throw new Exception($"[{result.AsT1.StatusCode}] {result.AsT1.Detail}");
                }
            }
        }
    }
    
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

