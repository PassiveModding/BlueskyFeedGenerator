using FishyFlip.Events;

namespace BlueskyFeed.Classifiers;

public interface IClassifier
{
    public Task Classify(JetStreamATWebSocketRecordEventArgs args);
    
    public Task Cleanup();
}