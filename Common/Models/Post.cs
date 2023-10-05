namespace Bluesky.Common.Models;

public class Post
{
    public string Uri { get; set; } = null!;
    public string Cid { get; set; } = null!;
    public string Path { get; set; } = null!;
    public string? Text { get; set; }
    public string? SanitizedText { get; set; } = null!;
    public string? Langs { get; set; } = null!;
    public string? Blob { get; set; } = null!;
    public DateTime IndexedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PostTopic> PostTopics { get; set; } = null!;
}