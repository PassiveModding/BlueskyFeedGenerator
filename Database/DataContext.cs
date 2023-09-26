using Microsoft.EntityFrameworkCore;
using BlueskyFeedGenerator.Models;

namespace BlueskyFeedGenerator.Database;
public class DataContext : DbContext
{
    public DataContext(DbContextOptions<DataContext> options) : base(options)
    {
    }
    
    public DbSet<Post> Posts { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Post>().HasIndex(it => it.Uri).IsUnique();
        modelBuilder.Entity<Post>().HasIndex(it => it.Cid);
    }
}