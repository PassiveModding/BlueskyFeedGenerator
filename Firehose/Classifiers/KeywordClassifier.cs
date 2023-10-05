using Bluesky.Common.Models;
using Bluesky.Firehose.Models;
using Microsoft.Extensions.Logging;

namespace Bluesky.Firehose.Classifiers;

public class KeywordClassifier : IClassifier
{
    private readonly Keyword[] keywords;
    private readonly ILogger<KeywordClassifier> logger;

    public KeywordClassifier(ILogger<KeywordClassifier> logger, Keyword[] keywords)
    {
        this.logger = logger;
        this.keywords = keywords ?? throw new ArgumentNullException(nameof(keywords));
    }

    public KeywordClassifier(ILogger<KeywordClassifier> logger, string keywordsFile)
    {
        this.logger = logger;
        this.keywords = GetKeywordsFromFile(keywordsFile).ToArray();
    }

    private static List<Keyword> GetKeywordsFromFile(string keywordsFile)
    {
        var keywordFile = Path.Combine(Directory.GetCurrentDirectory(), "keywords", keywordsFile);
        var topic = Path.GetFileNameWithoutExtension(keywordFile);
        var keywords = File.ReadAllLines(keywordFile);
        var keywordList = new List<Keyword>();
        foreach (var keyword in keywords)
        {
            var parts = keyword.Split(',');
            if (parts.Length != 2)
            {
                continue;
            }

            if (!int.TryParse(parts[1], out var weight))
            {
                continue;
            }

            var newKeyword = new Keyword(parts[0].Split('|'), weight);
            // ensure no duplicate keywords
            if (keywordList.Any(k => k.Keywords.SequenceEqual(newKeyword.Keywords)))
            {
                continue;
            }

            keywordList.Add(newKeyword);
        }

        return keywordList;
    }

    public int GenerateScore(string sanitizedText)
    {
        return GenerateScore(keywords, sanitizedText);
    }

    public static int GenerateScore(Keyword[] keywords, string sanitizedText)
    {
        var topics = new List<PostTopic>();
        var matchedKeywords = new List<Keyword>();

        foreach (var currentKeyword in keywords)
        {
            bool isMatch = IsKeywordMatch(currentKeyword, sanitizedText);
            if (!isMatch)
            {
                continue;
            }

            matchedKeywords.Add(currentKeyword);
        }

        // remove any keywords that are a subset of another keyword excluding the current keyword
        // keep the keyword with the highest weight
        var keywordsToRemove = new List<Keyword>();
        foreach (var currentKeyword in matchedKeywords)
        {
            var matched = matchedKeywords.Where(k => k != currentKeyword).ToArray();
            foreach (var otherKeyword in matched)
            {
                if (IsSubsetOrSubstring(otherKeyword, currentKeyword))
                {
                    if (otherKeyword.Weight > currentKeyword.Weight)
                    {
                        keywordsToRemove.Add(currentKeyword);
                    }
                    else
                    {
                        keywordsToRemove.Add(otherKeyword);
                    }
                }
            }
        }

        foreach (var keywordToRemove in keywordsToRemove)
        {
            matchedKeywords.Remove(keywordToRemove);
        }

        var score = matchedKeywords.Sum(k => k.Weight);
        return score;
    }

    public static bool IsKeywordMatch(Keyword keyword, string text)
    {
        return keyword.Keywords.All(term => text.Contains($" {term} ") || text.StartsWith($"{term} ") || text.EndsWith($" {term}"));
    }

    public static bool IsSubsetOrSubstring(Keyword keyword, Keyword other)
    {
        // case 1: other is a subset of keyword
        if (other.Keywords.Intersect(keyword.Keywords).Count() == other.Keywords.Length)
        {
            return true;
        }

        // case 2: other is a substring of keyword (surrounded by spaces)
        foreach (var term in other.Keywords)
        {
            if (keyword.Keywords.Any(k => k.Contains($" {term} ") || k.StartsWith($"{term} ") || k.EndsWith($" {term}")))
            {
                return true;
            }
        }

        return false;
    }
}
