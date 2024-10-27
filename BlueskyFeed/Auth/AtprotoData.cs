using FishyFlip.Models;

namespace BlueskyFeed.Auth;

public class AtprotoData
{
    public static (string did, string signingKey, string handle, string pds) EnsureAtpDocument(DidDoc doc)
    {
        var document = ParseToAtprotoDocument(doc);
        if (document.did == null)
        {
            throw new ArgumentException("Could not parse id from document", nameof(doc));
        }

        if (document.signingKey == null)
        {
            throw new ArgumentException("Could not parse signing key from document", nameof(doc));
        }

        if (document.handle == null)
        {
            throw new ArgumentException("Could not parse handle from document", nameof(doc));
        }

        if (document.pds == null)
        {
            throw new ArgumentException("Could not parse pds from document", nameof(doc));
        }

        return (document.did, document.signingKey, document.handle, document.pds);
    }

    private static (string did, string signingKey, string handle, string? pds) ParseToAtprotoDocument(DidDoc doc)
    {
        var did = GetDid(doc);
        var key = GetKey(doc);
        var handle = GetHandle(doc);
        var pds = GetPds(doc);

        return (did, key, handle, pds);
    }

    private static string GetDid(DidDoc doc)
    {
        return doc.Id;
    }

    private static string GetHandle(DidDoc doc)
    {
        var aka = doc.AlsoKnownAs;
        if (aka == null || aka.Count == 0)
        {
            throw new ArgumentException("JWT issuer is not a supported DID", nameof(doc));
        }

        var found = aka.FirstOrDefault(x => x.StartsWith("at://"));
        if (found == null)
        {
            throw new ArgumentException("JWT issuer is not a supported DID", nameof(doc));
        }

        // Stop of at:// prefix
        return found["at://".Length..];
    }

    private static string? GetPds(DidDoc doc)
    {
        return GetServiceEndpoint(doc, ("#atproto_pds", "AtprotoPersonalDataServer"));
    }

    private static string? GetServiceEndpoint(DidDoc doc, (string id, string type) opts)
    {
        var did = GetDid(doc);
        var services = doc.Service;
        if (services == null) return null;

        var found = services.FirstOrDefault(x => x.Id == opts.id || x.Id == $"{did}{opts.id}");
        if (found == null)
        {
            throw new ArgumentException("JWT issuer is not a supported DID", nameof(did));
        }

        if (found.Type != opts.type)
        {
            throw new ArgumentException("JWT issuer is not a supported DID", nameof(did));
        }

        ValidateUrl(found.ServiceEndpoint);
        return found.ServiceEndpoint;
    }

    private static void ValidateUrl(string url)
    {
        var uri = new Uri(url);
        var hostname = uri.Host;
        var protocol = uri.Scheme;
        if (protocol != "https" && protocol != "http")
        {
            throw new ArgumentException("Invalid pds protocol", nameof(url));
        }

        if (hostname == null)
        {
            throw new ArgumentException("Invalid pds hostname", nameof(url));
        }
    }

    private static string GetKey(DidDoc doc)
    {
        var did = GetDid(doc);

        var keys = doc.VerificationMethod;

        var found = keys.FirstOrDefault(x => x.Id == "#atproto" || x.Id == $"{did}#atproto");
        if (found == null)
        {
            throw new ArgumentException("JWT issuer is not a supported DID", nameof(doc));
        }

        if (found.PublicKeyMultibase == null)
        {
            throw new ArgumentException("JWT issuer is not a supported DID", nameof(doc));
        }

        string didKey;
        var keyBytes = Crypto.MultibaseToBytes(found.PublicKeyMultibase);
        if (found.Type == "EcdsaSecp256r1VerificationKey2019")
        {
            didKey = Did.FormatDidKey("ES256", keyBytes);
        }
        else if (found.Type == "EcdsaSecp256k1VerificationKey2019")
        {
            // SECP256K1_JWT_ALG
            didKey = Did.FormatDidKey("ES256K", keyBytes);
        }
        else if (found.Type == "Multikey")
        {
            var parsed = Did.ParseMultiKey(found.PublicKeyMultibase);
            didKey = Did.FormatDidKey(parsed.Alg, parsed.KeyBytes);
        }
        else
        {
            throw new ArgumentException("JWT issuer is not a supported DID", nameof(doc));
        }

        return didKey;
    }
}