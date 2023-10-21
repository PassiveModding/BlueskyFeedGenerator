using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bluesky.Common.Database;

public class PostContextDesignFactory : IDesignTimeDbContextFactory<PostContext>
{
    public PostContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PostContext>();

        var connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING") ?? throw new Exception("POSTGRES_CONNECTION_STRING is not set");
        optionsBuilder.UseNpgsql(connectionString);

        return new PostContext(optionsBuilder.Options);
    }
}
