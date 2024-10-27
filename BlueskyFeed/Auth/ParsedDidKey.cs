namespace BlueskyFeed.Auth;

public record ParsedDidKey(string Did, string Alg, byte[] KeyBytes);