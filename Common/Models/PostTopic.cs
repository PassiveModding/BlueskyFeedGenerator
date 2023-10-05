namespace Bluesky.Common.Models;

public class PostTopic
{
    // Uri of the post
    public string PostId { get; set; } = null!;
    public Post Post { get; set; } = null!;

    // Name of the topic
    public string TopicId { get; set; } = null!;
    public Topic Topic { get; set; } = null!;

    // Likelyhood of the post being about the topic
    public int Weight { get; set; }
}
