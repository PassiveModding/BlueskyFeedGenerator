using System.Text.RegularExpressions;
using LemmaSharp;

namespace Bluesky.Firehose.Sanitizers;

public partial class PostSanitizer : ISanitizer
{
    //[GeneratedRegex("[^a-zA-Z0-9 ]+")]
    //private static partial Regex Alphanumeric();

    [GeneratedRegex("[\\p{P}^+$]+")]
    private static partial Regex Punctuation();

    [GeneratedRegex("\\s+")]
    private static partial Regex DuplicateSpaces();

    private readonly HashSet<string> _stopwords;
    private readonly ILemmatizer _lemmatizer;

    public PostSanitizer(string[]? stopwords = null)
    {
        _lemmatizer = InitLemmatizer();
        _stopwords = stopwords != null ? new HashSet<string>(stopwords) : InitStopwords();
    }

    private HashSet<string> InitStopwords()
    {
        var stopwordsPath = Path.Combine(Directory.GetCurrentDirectory(), "stopwords.txt");
        if (File.Exists(stopwordsPath))
        {
            var stopwordData = File.ReadAllLines(stopwordsPath);
            // clean up stopwords
            stopwordData = stopwordData.Select(Normalize).ToArray();
            stopwordData = stopwordData.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            stopwordData = stopwordData.Select(Lemmatize).ToArray();

            return new HashSet<string>(stopwordData);
        }
        return new HashSet<string>();
    }

    private ILemmatizer InitLemmatizer()
    {
        return new LemmatizerPrebuiltFull(LanguagePrebuilt.English);
    }


    public static string Normalize(string input)
    {
        if (input == null)
        {
            return "";
        }

        input = Punctuation().Replace(input, "");
        input = DuplicateSpaces().Replace(input, " ");
        input = input.ToLower();
        return input;
    }

    public string Lemmatize(string input)
    {
        if (input == null)
        {
            return "";
        }

        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var lemmatizedWords = words.Select(w => _lemmatizer.Lemmatize(w));
        return string.Join(' ', lemmatizedWords);
    }

    public string RemoveStopwords(string input)
    {
        if (input == null)
        {
            return "";
        }

        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var filteredWords = words.Where(w => !_stopwords.Contains(w));
        return string.Join(' ', filteredWords);
    }

    public string Sanitize(string input)
    {
        if (input == null)
        {
            return "";
        }

        var sanitizedText = Normalize(input);
        sanitizedText = Lemmatize(sanitizedText);
        sanitizedText = RemoveStopwords(sanitizedText);
        return sanitizedText;
    }

    public IEnumerable<string> GetStopWords()
    {
        return _stopwords.ToArray();
    }
}