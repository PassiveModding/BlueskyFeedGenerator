using System.Text.Json;
using FishyFlip.Models;

namespace BlueskyFeed.Auth;

public class DidResolver(HttpClient client, string plcUrl = "https://plc.directory")
{
    internal async Task<string> ResolveAtprotoKey(string did)
    {
        if (did.StartsWith("did:key:"))
        {
            return did;
        }

        var data = await ResolveAtprotoData(did);
        return data.signingKey;
    }

    private async Task<(string did, string signingKey, string handle, string pds)> ResolveAtprotoData(string did)
    {
        var didDocument = await EnsureResolve(did);
        return AtprotoData.EnsureAtpDocument(didDocument);
    }

    private async Task<DidDoc> EnsureResolve(string did)
    {
        var result = await Resolve(did) ?? throw new ArgumentException("JWT issuer is not a supported DID", nameof(did));
        return result;
    }


    // Expiring cache
    private record CacheEntry(DateTime Expires, DidDoc DidDoc);

    private readonly Dictionary<string, CacheEntry> _cache = new();

    private async Task<DidDoc> Resolve(string did)
    {
        if (_cache.TryGetValue(did, out var entry))
        {
            if (entry.Expires > DateTime.UtcNow)
            {
                return entry.DidDoc;
            }
        }

        var got = await ResolveNoCache(did);
        _cache[did] = new CacheEntry(DateTime.UtcNow.AddMinutes(5), got);
        return got;
    }

    private async Task<DidDoc> ResolveNoCache(string did)
    {
        var got = await ResolveNoCheck(did);
        if (!got.HasValue)
        {
            throw new ArgumentException("JWT issuer is not a supported DID", nameof(did));
        }

        return ValidateDidDoc(did, got.Value);
    }

    private async Task<JsonElement?> ResolveNoCheck(string did)
    {
        if (did.StartsWith("did:plc:"))
        {
            // query plc for public key
            var response = await client.GetAsync($"{plcUrl}/{Uri.EscapeDataString(did)}");
            if (!response.IsSuccessStatusCode)
            {
                throw new ArgumentException("JWT issuer is not a supported DID", nameof(did));
            }

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        else if (did.StartsWith("did:web:"))
        {
            var parsedId = string.Join(":", did.Split(':').Skip(2)); // did:web:example.com -> example.com
            var parts = parsedId.Split(':').Select(Uri.UnescapeDataString).ToArray();
            string path;
            if (parts.Length < 1)
            {
                throw new ArgumentException("JWT issuer is not a supported DID", nameof(did));
            }
            else if (parts.Length == 1)
            {
                path = parts[0] + "/.well-known/did.json";
            }
            else
            {
                throw new ArgumentException("JWT issuer is not a supported DID", nameof(did));
            }

            var url = new Uri($"https://{path}");
            if (url.HostNameType == UriHostNameType.Dns && url.Host.EndsWith("localhost"))
            {
                url = new UriBuilder(url) { Scheme = "http" }.Uri;
            }

            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                throw new ArgumentException("JWT issuer is not a supported DID", nameof(did));
            }

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        else
        {
            throw new ArgumentException("JWT issuer is not a supported DID", nameof(did));
        }
    }

    private DidDoc ValidateDidDoc(string did, JsonElement didDoc)
    {
        var context = didDoc.GetProperty("@context");
        if (context.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("JWT issuer is not a supported DID", nameof(did));
        }


        var id = didDoc.GetProperty("id").GetString();
        if (id != did)
        {
            throw new ArgumentException("JWT issuer is not a supported DID", nameof(did));
        }

        var alsoKnownAs = didDoc.GetProperty("alsoKnownAs");
        if (alsoKnownAs.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("JWT issuer is not a supported DID", nameof(did));
        }


        var verificationMethod = didDoc.GetProperty("verificationMethod");
        if (verificationMethod.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("JWT issuer is not a supported DID", nameof(did));
        }


        var service = didDoc.GetProperty("service");
        if (service.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("JWT issuer is not a supported DID", nameof(did));
        }

        var contextArray = context.EnumerateArray().Select(x => x.GetString()).ToList();
        var alsoKnownAsArray = alsoKnownAs.EnumerateArray().Select(x => x.GetString()).ToList();
        return new DidDoc(contextArray!, id, alsoKnownAsArray!, ResolveVerificationMethods(verificationMethod), ResolveServices(service));
    }
    
    private List<VerificationMethod> ResolveVerificationMethods(JsonElement verificationMethods)
    {
        return verificationMethods.EnumerateArray().Select(x =>
        {
            var id = x.GetProperty("id").GetString();
            var type = x.GetProperty("type").GetString();
            var controller = x.GetProperty("controller").GetString();
            if (id == null || type == null || controller == null)
            {
                throw new ArgumentException("JWT issuer is not a supported DID");
            }

            // allowed to be null but will probably cause issues if it is
            var publicKeyMultibase = x.GetProperty("publicKeyMultibase").GetString()!;

            return new VerificationMethod(id, type, controller, publicKeyMultibase);
        }).ToList();
    }
    
    private List<Service> ResolveServices(JsonElement services)
    {
        return services.EnumerateArray().Select(x =>
        {
            var id = x.GetProperty("id").GetString();
            var type = x.GetProperty("type").GetString();
            var serviceEndpoint = x.GetProperty("serviceEndpoint").GetString();
            if (id == null || type == null || serviceEndpoint == null)
            {
                throw new ArgumentException("JWT issuer is not a supported DID");
            }

            return new Service(id, type, serviceEndpoint);
        }).ToList();
    }
}