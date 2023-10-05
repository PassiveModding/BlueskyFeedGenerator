using Bluesky.Common.Models;

namespace Bluesky.Firehose.Classifiers;

public interface IClassifier
{
    int GenerateScore(string sanitizedText);
}
