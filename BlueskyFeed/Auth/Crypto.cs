using SimpleBase;

namespace BlueskyFeed.Auth;

public class Crypto
{
    public static byte[] MultibaseToBytes(string multiKey)
    {
        var keyBase = multiKey[..1];
        var key = multiKey[1..];
        return keyBase switch
        {
            "z" => Base58.Bitcoin.Decode(key),
            _ => throw new ArgumentException("Unsupported multibase", nameof(multiKey)),
        };
    }

    public static bool VerifySignature(string didKey, byte[] data, byte[] sig)
    {
        var key = Did.ParseDidKey(didKey);
        var plugin = Did.Plugins.FirstOrDefault(p => p.JtwAlt == key.Alg);
        if (plugin == null)
        {
            throw new ArgumentException("Unsupported algorithm", nameof(didKey));
        }

        return plugin.VerifyDidSig(key, data, sig);
    }
}