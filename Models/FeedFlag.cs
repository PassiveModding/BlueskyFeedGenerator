namespace BlueskyFeedGenerator.Models;

[Flags]
public enum FeedFlag
{
    None = 0x0,
    Linux = 0x1,
    FFXIV = 0x2
}