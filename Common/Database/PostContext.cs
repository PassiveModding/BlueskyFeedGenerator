using Bluesky.Common.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Bluesky.Common.Database
{
    public class PostContext : DbContext
    {
        public PostContext(DbContextOptions<PostContext> options) : base(options)
        {
        }

        public DbSet<Post> Posts { get; set; } = null!;
        public DbSet<Topic> Topics { get; set; } = null!;
        public DbSet<PostTopic> PostTopics { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Post>().HasKey(x => x.Uri);
            modelBuilder.Entity<Post>().HasIndex(x => x.Path); 
            modelBuilder.Entity<Post>().HasIndex(x => x.IndexedAt);
            // ignore blob for now
            modelBuilder.Entity<Post>().Ignore(x => x.Blob);

            modelBuilder.Entity<Topic>().HasKey(x => x.Name);

            modelBuilder.Entity<PostTopic>().HasKey(x => new { x.PostId, x.TopicId });

            modelBuilder.Entity<PostTopic>()
                .HasOne(pt => pt.Post)
                .WithMany(p => p.PostTopics)
                .HasForeignKey(pt => pt.PostId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PostTopic>()
                .HasOne(pt => pt.Topic)
                .WithMany(t => t.PostTopics)
                .HasForeignKey(pt => pt.TopicId)
                .OnDelete(DeleteBehavior.Cascade);

            // index for posttopics
            modelBuilder.Entity<PostTopic>().HasIndex(x => x.TopicId);
            modelBuilder.Entity<PostTopic>().HasIndex(x => x.PostId);
        }
    }
}