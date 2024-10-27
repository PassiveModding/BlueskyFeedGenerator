namespace BlueskyFeed.Config;

public class Setup
{
    public const string SectionName = "Setup";

    public bool SubscribeToJetStream { get; init; } = true;
    public bool DeleteAllGenerators { get; init; } = false;
    public bool CreateAllGenerators { get; init; } = true;
}