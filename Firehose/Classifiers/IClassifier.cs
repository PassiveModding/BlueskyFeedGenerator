using Bluesky.Common.Models;

namespace Bluesky.Firehose.Classifiers;

public interface IClassifier
{
    PostTopic[] ClassifyText(string sanitizedText);
}
