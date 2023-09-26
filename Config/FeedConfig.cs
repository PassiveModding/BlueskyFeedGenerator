namespace BlueskyFeedGenerator.Config;

public class FeedConfig
{
    public const string SectionName = "Feed";

    public string PublisherDid { get; set; } = null!;
    public string ServiceDid { get; set; } = null!;
    public string HostName {get; set; } = null!;
}