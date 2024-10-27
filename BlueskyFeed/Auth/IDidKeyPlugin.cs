namespace BlueskyFeed.Auth;

public interface IDidKeyPlugin
{
    bool VerifyDidSig(ParsedDidKey did, byte[] data, byte[] sig);
    byte[] Prefix { get; }
    string JtwAlt { get; }
    byte[] DecompressPubKey(byte[] compressed);
    byte[] CompressPubKey(byte[] pubKeyBytes);
}