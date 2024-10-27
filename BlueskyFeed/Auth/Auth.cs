using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace BlueskyFeed.Auth;

public class Auth
{
    public static async Task<string> VerifyJwt(string jwtStr, string? ownDid, DidResolver didResolver)
    {
        var jwt = new JwtSecurityToken(jwtStr);

        if (jwt.ValidTo < DateTime.UtcNow)
        {
            throw new ArgumentException("JWT expired", nameof(jwtStr));
        }

        // check if audience matches
        if (ownDid != null && jwt.Audiences.FirstOrDefault() != ownDid)
        {
            throw new ArgumentException("JWT audience mismatch", nameof(jwtStr));
        }

        // GetSigningKey
        var issuer = jwt.Issuer;
        var signingKey = await didResolver.ResolveAtprotoKey(issuer);

        var msg = jwt.RawHeader + "." + jwt.RawPayload;
        var msgBytes = Encoding.UTF8.GetBytes(msg);

        var sig = jwt.RawSignature;
        var sigBytes = Base64UrlEncoder.DecodeBytes(sig);

        // verify signature
        var result = Crypto.VerifySignature(signingKey, msgBytes, sigBytes);
        if (!result)
        {
            throw new ArgumentException("JWT signature invalid", nameof(jwtStr));
        }

        return jwt.Issuer;
    }
}