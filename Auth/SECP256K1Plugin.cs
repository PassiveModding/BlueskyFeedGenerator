using NBitcoin;
using NBitcoin.Crypto;

namespace BlueskyFeedGenerator.Auth;

public class SECP2561KPlugin : IDidKeyPlugin
{
    public static readonly byte[] SECP256K1_PREFIX = new byte[] { 0xe7, 0x01 };
    public const string JWT_ALG = "ES256K";

    public byte[] Prefix => SECP256K1_PREFIX;

    public string JtwAlt => JWT_ALG;

    public bool VerifyDidSig(ParsedDidKey did, byte[] data, byte[] sig)
    {
        if (did.Alg != JWT_ALG)
        {
            throw new ArgumentException("Unsupported algorithm", nameof(did));
        }

        return VerifySignature(did.KeyBytes, data, sig);
    }

    public bool VerifySignature(byte[] publicKey, byte[] dataToVerify, byte[] signature)
    { 
        var pubKey = new PubKey(publicKey);
        ECDSASignature sig;
        try
        {
            sig = ECDSASignature.FromDER(signature);
        }
        catch (Exception)
        {
            if (!ECDSASignature.TryParseFromCompact(signature, out sig))
            {
                throw new ArgumentException("Invalid signature format", nameof(signature));
            }
        }

        var hash = new uint256(Hashes.SHA256(dataToVerify));
        return pubKey.Verify(hash, sig);
    }

    public byte[] DecompressPubKey(byte[] compressed)
    {
        var pubKey = new PubKey(compressed);
        var decompressed = pubKey.Decompress().ToBytes();
        return decompressed;
    }

    public byte[] CompressPubKey(byte[] pubKeyBytes)
    {
        var pubKey = new PubKey(pubKeyBytes);
        var compressed = pubKey.Compress().ToBytes();
        return compressed;
    }
}