using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Bluesky.Common.Database;
using Bluesky.Firehose.Services;
using Bluesky.Firehose.Classifiers;
using Bluesky.Firehose.Sanitizers;

namespace Bluesky.Firehose;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = CreateHostBuilder(args).Build();

        // migrate database
        using (var scope = builder.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PostContext>();
            await dbContext.Database.MigrateAsync();
        }

        await builder.RunAsync();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostContext, configBuilder) =>
            {
                var currentDirectory = Directory.GetCurrentDirectory();
                configBuilder
                    .SetBasePath(currentDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{hostContext.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true)
                    .AddEnvironmentVariables();
            })
            .ConfigureServices((hostContext, services) =>
            {
                services.Configure<ServiceConfig>(hostContext.Configuration.GetSection("Service"));
                services.AddDbContext<PostContext>(options =>
                {
                    options.UseNpgsql(hostContext.Configuration.GetConnectionString("DefaultConnection"));
                });

                services.AddHostedService<FirehoseListener>();

                services.AddSingleton<ISanitizer, DefaultSanitizer>();
                services.AddHostedService<PostProcessor>();
                
                services.AddSingleton<IClassifier, KeywordClassifier>();
                services.AddHostedService<Classifier>();
            })
            .ConfigureLogging((hostContext, loggingBuilder) =>
            {
                loggingBuilder.ClearProviders();
                loggingBuilder.AddSimpleConsole(opts =>
                {
                    opts.IncludeScopes = true;
                    opts.SingleLine = true;
                    opts.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
                    opts.UseUtcTimestamp = true;
                    opts.ColorBehavior = LoggerColorBehavior.Enabled;
                });
            });
}
