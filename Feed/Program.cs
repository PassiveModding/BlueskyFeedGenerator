using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Bluesky.Common.Database;
using Bluesky.Feed.Config;
using Microsoft.AspNetCore;
using FishyFlip;
using Bluesky.Feed.Feeds;
using Microsoft.OpenApi.Models;
using Bluesky.Feed.Auth;
using Microsoft.Extensions.Options;

namespace Bluesky.Feed;

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


    public static IWebHostBuilder CreateHostBuilder(string[] args) =>
        WebHost.CreateDefaultBuilder(args)
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
                services.Configure<AtProtoConfig>(hostContext.Configuration.GetSection("AtProto") ?? throw new Exception("No ATProto config found!"));
                services.AddDbContext<PostContext>(options =>
                {
                    options.UseNpgsql(hostContext.Configuration.GetConnectionString("DefaultConnection"));

                    if (hostContext.HostingEnvironment.IsDevelopment())
                    {
                        options.EnableSensitiveDataLogging();
                    }
                });

                services.AddSingleton(serviceProvider => new ATProtocolBuilder()
                    .EnableAutoRenewSession(true)
                    .WithInstanceUrl(new Uri(serviceProvider.GetRequiredService<IOptions<AtProtoConfig>>().Value.ServiceUrl))
                    .Build());

                services.AddSingleton<DidResolver>();

                services.Configure<FeedConfig>(hostContext.Configuration.GetSection("Feed") ?? throw new Exception("No Feed config found!"));
                services.AddSingleton(serviceProvider =>
                {
                    var config = serviceProvider.GetRequiredService<IOptions<FeedConfig>>().Value;
                    var logger = serviceProvider.GetRequiredService<ILogger<FeedFactory>>();

                    var feedDictionary = new Dictionary<string, IFeed>();
                    foreach (var topic in config.Topics)
                    {
                        var feed = new TopicFeed(serviceProvider, topic.Value);
                        logger.LogInformation("Registering feed {feedUri} for {topic}", $"at://{config.PublisherDid}/app.bsky.feed.generator/{topic.Key}", topic.Value);
                        feedDictionary.Add($"at://{config.PublisherDid}/app.bsky.feed.generator/{topic.Key}", feed);
                    }

                    var feedFactory = new FeedFactory(feedDictionary);

                    return feedFactory;
                });

                services.AddHttpClient();
                services.AddEndpointsApiExplorer();
                services.AddSwaggerGen(c =>
                {
                    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Bluesky.Feed", Version = "v1" });
                });

                services.AddControllers();
            })
            .ConfigureLogging((hostContext, loggingBuilder) =>
            {
                loggingBuilder.ClearProviders();
                loggingBuilder.AddSimpleConsole(opts =>
                {
                    //opts.IncludeScopes = true;
                    opts.SingleLine = true;
                    opts.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
                    opts.UseUtcTimestamp = true;
                    opts.ColorBehavior = LoggerColorBehavior.Enabled;
                });
            })
            .Configure((hostContext, app) =>
            {
                if (hostContext.HostingEnvironment.IsDevelopment())
                {
                    app.UseSwagger();
                    app.UseSwaggerUI();
                }

                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                });
            });
}
