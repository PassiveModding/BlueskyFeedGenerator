namespace Bluesky.Firehose.Sanitizers
{
    public interface ISanitizer
    {
        string Sanitize(string input);
    }
}