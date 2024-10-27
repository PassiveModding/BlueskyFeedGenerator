using BlueskyFeed.Auth;
using BlueskyFeed.Config;
using BlueskyFeed.Generators;
using BlueskyFeed.Services;
using BlueskyFeed.Util;

namespace BlueskyFeed;

public class Program
{
    
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        
        // Add services to the container.
        builder.Services.AddAuthorization();
        builder.Services.AddHostedService<JetStreamProvider>();
        builder.Services.AddHostedService<FeedSetup>();
        builder.Services.AddHostedService<ClassifierCleanup>();
        builder.Services.AddSingleton<ProtoHandler>();
        builder.Services.AddSingleton<FollowersFeedHelper>();
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<DidResolver>();

        builder.Services.AddFeedGenerators();
        builder.Services.AddClassifiers();
        builder.Services.Configure<AtProtoConfig>(builder.Configuration.GetSection(AtProtoConfig.SectionName));
        builder.Services.Configure<Setup>(builder.Configuration.GetSection(Setup.SectionName));

        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddControllers();

        var app = builder.Build();
        app.MapControllers();
        
        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();
        app.UseAuthorization();

        app.Run();
    }
}