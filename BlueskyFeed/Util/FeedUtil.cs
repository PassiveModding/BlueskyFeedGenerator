using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FishyFlip;
using FishyFlip.Models;

namespace BlueskyFeed.Util;

internal static class FeedUtil
{
    internal static Task<Result<RecordRef>> CreateFeedGeneratorRecord(this ATProtoRepo repo, CreateFeedGeneratorRecord generatorRecord,
        JsonSerializerOptions options,
        CancellationToken cancellationToken)
    {
        var recordRefTypeInfo = (JsonTypeInfo<RecordRef>) options.GetTypeInfo(typeof(RecordRef));
        var createFeedRecordTypeInfo = (JsonTypeInfo<CreateFeedGeneratorRecord>)FeedSourceGen.Default.Options.GetTypeInfo(typeof(CreateFeedGeneratorRecord));

        return repo.CreateRecord(generatorRecord, createFeedRecordTypeInfo, recordRefTypeInfo, cancellationToken);
    }
    
    internal static Task<Result<Success>> DeleteFeedGeneratorRecord(this ATProtoRepo repo, string rKey,
        CancellationToken cancellationToken)
    {
        return repo.DeleteRecordAsync(Constants.FeedType.Generator, rKey, cancellationToken: cancellationToken);
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull | JsonIgnoreCondition.WhenWritingDefault)]
[JsonSerializable(typeof(CreateFeedGeneratorRecord))]
[JsonSerializable(typeof(RecordRef))]
internal partial class FeedSourceGen : JsonSerializerContext
{
}

internal class CreateFeedGeneratorRecord(ATDid repo, string rKey, GeneratorRecord record)
{
    [JsonPropertyName("collection")] 
    public string Collection { get; init; } = Constants.FeedType.Generator;

    [JsonPropertyName("repo")]
    public string Repo { get; init; } = repo.Handler;

    [JsonPropertyName("rkey")]
    public string RKey { get; init; } = rKey;

    [JsonPropertyName("record")]
    public GeneratorRecord Record { get; init; } = record;
}

internal class GeneratorRecord(string did, string displayName, string? avatar, string description, DateTime createdAt)
    : ATRecord
{
    [JsonPropertyName("did")]
    public string Did { get; init; } = did;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = displayName;

    [JsonPropertyName("avatar")]
    public string? Avatar { get; init; } = avatar;
    
    [JsonPropertyName("description")]
    public string? Description { get; init; } = description;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; } = createdAt;
}

public record GeneratorRecordRequest(string RKey, string DisplayName, string? Avatar, string Description, DateTime CreatedAt, bool Enabled);
