namespace Bluesky.Firehose.Models
{
    public record Keyword(string[] Keywords, int Weight)
    {
        public override string ToString()
        {
            return $"{string.Join("|", Keywords)} ({Weight})";
        }
    }
}