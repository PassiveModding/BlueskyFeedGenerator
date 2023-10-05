using Microsoft.Extensions.Logging;

namespace Bluesky.Firehose.Classifiers;

public class ClassifierFactory
{
    private readonly Dictionary<string, IClassifier> classifiers;

    public ClassifierFactory(Dictionary<string, IClassifier> classifiers)
    {
        this.classifiers = classifiers;
    }

    public IClassifier? GetFeed(string topic)
    {
        if (classifiers.TryGetValue(topic, out var classifier))
        {
            return classifier;
        }
        return null;
    }

    public IEnumerable<(string, IClassifier)> GetClassifiers()
    {
        return classifiers.Select(x => (x.Key, x.Value));
    }
}