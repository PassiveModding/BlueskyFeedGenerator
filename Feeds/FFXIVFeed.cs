using System.Text.RegularExpressions;
using BlueskyFeedGenerator.Database;
using BlueskyFeedGenerator.Models;
using FishyFlip.Models;
using Microsoft.EntityFrameworkCore;
using Org.BouncyCastle.Utilities.Collections;

namespace BlueskyFeedGenerator.Feeds;

[Feed("ffxiv")]
public partial class FFXIVFeed : IFeed
{
    public FeedFlag Flag => FeedFlag.FFXIV;
    public bool AuthorizeUser => false;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FFXIVFeed> _logger;
    private readonly HashSet<ProcessedKeyword> keywords = new();

    // Default keywords have a 100% chance of matching
    private readonly HashSet<(string, int)> defaultKeywords = new()
    { 
        ("ffxiv", 100), 
        ("ff14", 100), 
        ("ffxivart", 100), 
        ("gposers", 100), 
        ("gposer", 100),
        ("final fantasy xiv", 100), 
        ("final fantasy 14", 100) 
    };

    [GeneratedRegex("[^a-zA-Z0-9 ]+")]
    private static partial Regex Alphanumeric();


    [GeneratedRegex("\\s+")]
    private static partial Regex DuplicateSpaces();

    // match newline, carriage return, and tab
    [GeneratedRegex("[\\n\\r\\t]+")]
    private static partial Regex NewlineCarriageReturnTab();

    public record Keyword(string[] Keywords, int Weight);
    public record ProcessedKeyword(string[] Keywords, int Weight, (Regex keyword, Regex plural)[] KeywordRegex);

    public void ProcessKeywords()
    {
        // regular regex = 
        // new Regex($"(^|\\s){x}(\\s|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        // plural regex
        // new Regex($"(^|\\s){x}s(\\s|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        foreach (var keywordSet in defaultKeywords)
        {
            keywords.Add(new ProcessedKeyword(new[] { keywordSet.Item1 }, keywordSet.Item2, 
                new[] { 
                    (new Regex($"(^|\\s){keywordSet.Item1}(\\s|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled), 
                    new Regex($"(^|\\s){keywordSet.Item1}s(\\s|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)) 
                    }
                ));
        }

        // load keywords from file if it exists
        if (!File.Exists("./Keywords/ffxiv-keywords.csv"))
        {
            return;
        }

        // format = keyword, weight
        var lines = File.ReadAllLines("./Keywords/ffxiv-keywords.csv");        
        var keywordMap = new HashSet<ProcessedKeyword>();

        foreach (var line in lines)
        {
            var parts = line.Split(',');
            if (parts.Length != 2)
            {
                _logger.LogWarning("Invalid keyword line {line}", line);
                continue;
            }

            var weight = int.Parse(parts[1]);
            var keywordParts = parts[0].Split("&&")
                .Select(x => x.Trim().ToLowerInvariant())
                .Select(x => Alphanumeric().Replace(x, ""))
                .ToArray();

            var keywordRegex = keywordParts.Select(x => (new Regex($"(^|\\s){x}(\\s|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled), new Regex($"(^|\\s){x}s(\\s|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled))).ToArray();

            keywordMap.Add(new ProcessedKeyword(keywordParts, weight, keywordRegex));
        }

        keywords.UnionWith(keywordMap);

        // update keywords file
        //File.WriteAllText("./Keywords/ffxiv-keywords.csv", string.Join("\n", keywordMap.Select(x => $"{string.Join("&&", x.Keywords)},{x.Weight}")));
    }


    public FFXIVFeed(IServiceProvider serviceProvider, ILogger<FFXIVFeed> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        ProcessKeywords();
    }

    public Task<bool> CategorizeAsync(FishyFlip.Models.Post post, CancellationToken cancellationToken, out FeedFlag flags)
    {
        flags = FeedFlag.None;
        //var keywords = 
        var postText = post.Text?.ToLowerInvariant();
        if (postText == null)
        {
            return Task.FromResult(false);
        }

        // clean up post text
        postText = Alphanumeric().Replace(postText, "");
        postText = DuplicateSpaces().Replace(postText, " ");
        
        // filter out words that are short and not found in the keyword map
        var words = postText.Split(' ').Where(x => x.Length > 2 || keywords.Any(y => y.Keywords.Contains(x))).ToArray();
        postText = string.Join(" ", words);

        // if posttext is now empty, return false
        if (string.IsNullOrWhiteSpace(postText))
        {
            return Task.FromResult(false);
        }

        List<(int weight, string[] keywords)> matches = new();
        foreach (var val in keywords)
        {
            var containsAll = true;
            foreach (var keyword in val.KeywordRegex)
            {
                if (!keyword.keyword.IsMatch(postText) && !keyword.plural.IsMatch(postText))
                {
                    containsAll = false;
                    break;
                }
            }

            if (containsAll)
            {
                matches.Add((val.Weight, val.Keywords));
            }
        }

        if (matches.Count == 0)
        {
            return Task.FromResult(false);
        }

        // sum up weights
        var weightSum = matches.Sum(x => x.weight);
        // if sum is greater than 100, match
        if (weightSum >= 100)
        {
            flags = Flag;
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public async Task<object> RetrieveAsync(string? cursor, int limit, string? issuerDid, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        var (builder, indexedAt, cid) = db.GetBuilder(cursor, Flag);

        var posts = await builder.Take(limit).ToListAsync(cancellationToken: cancellationToken);
        return posts.GetFeedResponse();
    }
}