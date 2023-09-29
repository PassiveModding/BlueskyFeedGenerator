namespace BlueskyFeedGenerator.Config;

public class FeedConfig
{
    public const string SectionName = "Feed";

    public string PublisherDid { get; set; } = null!;
    public string ServiceDid { get; set; } = null!;
    public string HostName {get; set; } = null!;

    // map allowing user to map an internal shortname to a feed name which is used in the ATProtocol
    // e.g. "ffxiv" => "ffxiv-feed"
    public Dictionary<string, string> Feeds { get; set; } = null!;
}