using System.ComponentModel.DataAnnotations;
using BlueskyFeedGenerator.Feeds;

namespace BlueskyFeedGenerator.Models;

public class Post
{
    [Key]
    public string Uri { get; set; } = null!;
    
    [Required]
    public string Cid { get; set; } = null!;
    
    public string? ReplyParent { get; set; } = null!;

    public string? ReplyRoot { get; set; } = null!;

    [Required]
    public DateTime IndexedAt { get; set; } = DateTime.UtcNow;

    // will use bit flags to store the algorithm used to generate the feed for this post or multiple algorithms
    [Required]
    public FeedFlag Flags { get; set; }
}
