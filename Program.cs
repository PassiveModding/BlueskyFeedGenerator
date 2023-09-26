using BlueskyFeedGenerator.Config;
using BlueskyFeedGenerator.Database;
using BlueskyFeedGenerator.Feeds;
using BlueskyFeedGenerator.Services;
using FishyFlip;
using Microsoft.EntityFrameworkCore;

namespace BlueskyFeedGenerator;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configure feeds
        var feedConfig = builder.Configuration.GetSection(FeedConfig.SectionName).Get<FeedConfig>() ?? throw new Exception("No feed config found!");
        builder.Services.AddSingleton(feedConfig);
        builder.Services.AddSingleton<LinuxFeed>();
        builder.Services.AddSingleton<FFXIVFeed>();
        builder.Services.AddSingleton(x => {
            var feeds = new Dictionary<string, IFeed>
            {
                { $"at://{feedConfig.PublisherDid}/app.bsky.feed.generator/linux-feed", x.GetRequiredService<LinuxFeed>() },
                { $"at://{feedConfig.PublisherDid}/app.bsky.feed.generator/ffxiv-feed", x.GetRequiredService<FFXIVFeed>() }
            };
            return feeds;
        });

        builder.Services.AddLogging();
        builder.Services.AddSingleton(serviceProvider =>
        {
            var atProtoConfig = builder.Configuration.GetSection(AtProtoConfig.SectionName).Get<AtProtoConfig>() ?? throw new Exception("No ATProto config found!");

            var logger = serviceProvider.GetRequiredService<ILogger<ATProtocol>>();
            var client = new ATProtocolBuilder()
                .EnableAutoRenewSession(true)
                .WithInstanceUrl(new Uri(atProtoConfig.ServiceUrl))
                .WithLogger(logger)
                .Build();
            client.Server.CreateSessionAsync(atProtoConfig.LoginIdentifier, atProtoConfig.LoginToken).Wait();
            return client;
        });

        builder.Services.AddDbContext<DataContext>(options =>
        {            
            var connString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new Exception("No connection string found!");
            var contextBuilder = options
                .UseSqlite(connString);

            if (builder.Environment.IsDevelopment())
            {
                contextBuilder.EnableSensitiveDataLogging();
            }
        });

        builder.Services.AddSingleton<FeedMessageProcessor>();
        builder.Services.AddHostedService<FeedMessageProcessor>(x => x.GetRequiredService<FeedMessageProcessor>());

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

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        await db.Database.MigrateAsync();
        await app.RunAsync();
    }
}