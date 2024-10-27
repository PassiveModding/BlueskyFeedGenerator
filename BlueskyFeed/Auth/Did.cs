using SimpleBase;

namespace BlueskyFeed.Auth;

public class Did
{
    public const string DID_KEY_PREFIX = "did:key:";
    public const string BASE58_MULTIBASE_PREFIX = "z";

    public static List<IDidKeyPlugin> Plugins { get; set; } = new List<IDidKeyPlugin>
    {
        new SECP2561KPlugin()
    };

    public static ParsedDidKey ParseDidKey(string did)
    {
        if (!did.StartsWith(DID_KEY_PREFIX))
        {
            throw new ArgumentException("JWT issuer is not a supported DID", nameof(did));
        }

        return ParseMultiKey(did[DID_KEY_PREFIX.Length..]);
    }

    public static ParsedDidKey ParseMultiKey(string multiKey)
    {
        if (!multiKey.StartsWith(BASE58_MULTIBASE_PREFIX))
        {
            throw new ArgumentException("JWT issuer is not a supported DID", nameof(multiKey));
        }

        var prefixedBytes = Base58.Bitcoin.Decode(multiKey[BASE58_MULTIBASE_PREFIX.Length..]);

        var plugin = Plugins.FirstOrDefault(p => prefixedBytes.Take(p.Prefix.Length).SequenceEqual(p.Prefix));
        if (plugin == null)
        {
            throw new ArgumentException("JWT issuer is not a supported DID", nameof(multiKey));
        }

        var keyBytes = prefixedBytes.Skip(plugin.Prefix.Length).ToArray();
        if (plugin.JtwAlt == SECP2561KPlugin.JWT_ALG)
        {
            keyBytes = plugin.DecompressPubKey(keyBytes);
        }
        // TODO: p256
        else
        {
            throw new ArgumentException("JWT issuer is not a supported DID", nameof(multiKey));
        }

        return new ParsedDidKey(multiKey, plugin.JtwAlt, keyBytes);
    }

    public static string FormatDidKey(string alg, byte[] bytes)
    {
        return DID_KEY_PREFIX + FormatMultiKey(alg, bytes);
    }

    public static string FormatMultiKey(string alg, byte[] bytes)
    {
        var plugin = Plugins.FirstOrDefault(p => p.JtwAlt == alg);
        if (plugin == null)
        {
            throw new ArgumentException("Unsupported algorithm", nameof(alg));
        }

        byte[] keyBytes;
        if (plugin.JtwAlt == SECP2561KPlugin.JWT_ALG)
        {
            keyBytes = plugin.CompressPubKey(bytes);
        }
        // TODO: p256
        else
        {
            throw new ArgumentException("Unsupported algorithm", nameof(alg));
        }

        var prefixedBytes = plugin.Prefix.Concat(keyBytes).ToArray();
        return BASE58_MULTIBASE_PREFIX + Base58.Bitcoin.Encode(prefixedBytes);
    }
}