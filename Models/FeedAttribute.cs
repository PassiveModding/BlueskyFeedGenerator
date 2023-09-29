namespace BlueskyFeedGenerator.Models;

public class FeedAttribute : Attribute
{
    public string Name { get; }
    public FeedAttribute(string name)
    {
        Name = name;
    }
}