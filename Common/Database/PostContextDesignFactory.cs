using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bluesky.Common.Database;

public class PostContextDesignFactory : IDesignTimeDbContextFactory<PostContext>
{
    public PostContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PostContext>();
        Console.WriteLine("Enter connection string:");
        var connectionString = Console.ReadLine();
        optionsBuilder.UseNpgsql(connectionString);

        return new PostContext(optionsBuilder.Options);
    }
}
