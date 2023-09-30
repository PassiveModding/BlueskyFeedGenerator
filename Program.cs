using System.Reflection;
using System.Text;
using BlueskyFeedGenerator.Auth;
using BlueskyFeedGenerator.Config;
using BlueskyFeedGenerator.Database;
using BlueskyFeedGenerator.Feeds;
using BlueskyFeedGenerator.Models;
using BlueskyFeedGenerator.Services;
using FishyFlip;
using Microsoft.EntityFrameworkCore;

namespace BlueskyFeedGenerator;

internal class Program
{
    private const string PLC_URL = "https://plc.directory";

    private static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);


        // clear loggers
        builder.Logging.ClearProviders();

        // one line with timestamp and log level
        builder.Logging.AddSimpleConsole(options =>
        {
            options.IncludeScopes = true;
            options.SingleLine = true;
            options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
            options.UseUtcTimestamp = true;
        });

        ConfigureAtproto(builder);
        ConfigureFeeds(builder);
        ConfigureDb(builder);

        // add httpclient factory
        builder.Services.AddHttpClient();

        // new DidResolver(httpclient, "https://plc.directory")
        builder.Services.AddSingleton(serviceProvider => new DidResolver(serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(), PLC_URL));
        builder.Services.AddSingleton<FeedMessageProcessor>();

        // config option for starting the feed message processor
        var startFeedMessageProcessor = builder.Configuration.GetValue<bool>("StartFeedMessageProcessor");
        if (startFeedMessageProcessor)
        {
            builder.Services.AddHostedService(x => x.GetRequiredService<FeedMessageProcessor>());
        }

        builder.Services.AddControllers();

        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        var app = builder.Build();
        app.UseHttpLogging();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.MapControllers();

        await MigrateDatabase(app);
        await app.RunAsync();
    }

    public static async Task MigrateDatabase(IApplicationBuilder app)
    {
        using var scope = app.ApplicationServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        await db.Database.MigrateAsync();
    }

    public static void ConfigureDb(WebApplicationBuilder builder)
    {
        var connString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new Exception("No connection string found!");
        builder.Services.AddDbContext<DataContext>(options =>
        {
            var contextBuilder = options
                .UseSqlite(connString);

            if (builder.Environment.IsDevelopment())
            {
                contextBuilder.EnableSensitiveDataLogging();
            }
        });
    }

    public static void ConfigureAtproto(WebApplicationBuilder builder)
    {
        var atProtoConfig = builder.Configuration.GetSection(AtProtoConfig.SectionName).Get<AtProtoConfig>() ?? throw new Exception("No ATProto config found!");
        builder.Services.AddSingleton(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
            var client = new ATProtocolBuilder()
                .EnableAutoRenewSession(true)
                .WithInstanceUrl(new Uri(atProtoConfig.ServiceUrl))
                .WithLogger(logger)
                .Build();

            logger.LogInformation("Creating ATProtocol session");
            client.Server.CreateSessionAsync(atProtoConfig.LoginIdentifier, atProtoConfig.LoginToken).Wait();
            logger.LogInformation("Created ATProtocol session");
            return client;
        });
    }

    public static void ConfigureFeeds(WebApplicationBuilder builder)
    {
        var feedConfig = builder.Configuration.GetSection(FeedConfig.SectionName).Get<FeedConfig>() ?? throw new Exception("No feed config found!");
        builder.Services.AddSingleton(feedConfig);

        // get all IFeed with FeedAttribute
        List<(string name, Type type)> feedTypes = typeof(IFeed).Assembly.GetTypes()
            .Where(x => typeof(IFeed).IsAssignableFrom(x) && x.GetCustomAttribute<FeedAttribute>() != null)
            .Select(x => (x.GetCustomAttribute<FeedAttribute>()!.Name, x))
            .ToList();

        // check for duplicate feed names
        var duplicateFeedNames = feedTypes.GroupBy(x => x.name).Where(x => x.Count() > 1).Select(x => x.Key).ToList();
        if (duplicateFeedNames.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Duplicate feed names found:");
            foreach (var name in duplicateFeedNames)
            {
                sb.AppendLine($"- {name}");
            }

            throw new Exception(sb.ToString());
        }

        // register all feeds
        foreach (var (name, type) in feedTypes)
        {
            builder.Services.AddSingleton(type);
        }

        // configure feed dictionary
        builder.Services.AddSingleton(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
            var feedDictionary = new Dictionary<string, IFeed>();
            foreach (var (name, type) in feedTypes)
            {
                if (feedConfig.Feeds.TryGetValue(name, out var feedName))
                {
                    // register feed as it's type 
                    var feed = (IFeed)serviceProvider.GetRequiredService(type);
                    feedDictionary.Add($"at://{feedConfig.PublisherDid}/app.bsky.feed.generator/{feedName}", feed);
                    logger.LogInformation("Registered feed {name} ({feedName}) as {type}", name, feedName, type);
                }
                else
                {
                    logger.LogWarning("Feed {name} is not configured in the feed config", name);
                }
            }

            return feedDictionary;
        });
    }
}