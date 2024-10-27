using BlueskyFeed.Classifiers;
using BlueskyFeed.Generators;

namespace BlueskyFeed.Util;

public static class FeedGeneratorExtensions
{
    public static IServiceCollection AddFeedGenerators(this IServiceCollection services)
    {
        var feedGeneratorTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(IFeedGenerator).IsAssignableFrom(type) && type is {IsInterface: false, IsAbstract: false});
        
        foreach (var feedGeneratorType in feedGeneratorTypes)
        {
            services.AddSingleton(typeof(IFeedGenerator), feedGeneratorType);
        }
        
        return services;
    }
    
    public static IServiceCollection AddClassifiers(this IServiceCollection services)
    {
        var classifierTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(IClassifier).IsAssignableFrom(type) && type is {IsInterface: false, IsAbstract: false});
        
        foreach (var classifierType in classifierTypes)
        {
            services.AddSingleton(classifierType);
            services.AddSingleton(typeof(IClassifier), x => x.GetRequiredService(classifierType));
        }
        
        return services;
    }
}