namespace BlueskyFeedGenerator.Auth;

public class ParsedDidKey
{
    public ParsedDidKey(string did, string alg, byte[] keyBytes)
    {
        Did = did;
        Alg = alg;
        KeyBytes = keyBytes;
    }
    
    public string Did { get; set; }
    public string Alg { get; set; }
    public byte[] KeyBytes { get; set; }
}