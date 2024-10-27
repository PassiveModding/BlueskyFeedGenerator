namespace BlueskyFeed.Config;

public class AtProtoConfig
{
    public const string SectionName = "AtProto";
    public required string ServiceDid { get; init; } = null!;
    public required string HostName { get; init; } = null!;
    public required string LoginIdentifier { get; init; } = null!;
    public required string LoginToken { get; init; } = null!;
}