namespace Bluesky.Common.Models;

public class Topic
{
    public string Name { get; set; } = null!;
    public ICollection<PostTopic> PostTopics { get; set; } = null!;
}