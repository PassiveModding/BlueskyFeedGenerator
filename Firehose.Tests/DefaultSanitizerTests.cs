using Bluesky.Firehose.Classifiers;
using Bluesky.Firehose.Models;
using Bluesky.Firehose.Sanitizers;
using Microsoft.Extensions.Logging;
using Moq;

namespace Firehose.Tests;

public class DefaultSanitizerTests
{
    private readonly PostSanitizer sanitizer;

    public DefaultSanitizerTests()
    {
        sanitizer = new PostSanitizer();
    }

    [Fact]
    public void Sanitize_Should_Return_Empty_String_When_Null()
    {
        var result = sanitizer.Sanitize(null);

        Assert.Equal("", result);
    }

    [Fact]
    public void Sanitize_Should_Return_Empty_String_When_Empty()
    {
        var result = sanitizer.Sanitize("");

        Assert.Equal("", result);
    }

    [Fact]
    public void Sanitize_Should_Return_Empty_String_When_Whitespace()
    {
        var result = sanitizer.Sanitize(" ");

        Assert.Equal("", result);
    }

    [Fact]
    public void Sanitize_Should_Return_Empty_String_When_Whitespace_With_Extra_Spaces()
    {
        var result = sanitizer.Sanitize("   ");

        Assert.Equal("", result);
    }

    [Fact]
    public void Sanitizer_Passes_Test_Inputs()
    {
        // load SanitizerTestInputs.csv
        var testInputs = File.ReadAllLines(Path.Combine(Directory.GetCurrentDirectory(), "SanitizerTestInputs.csv"));
        foreach (var testInput in testInputs)
        {
            // skip lines that start with #
            if (testInput.StartsWith("#"))
            {
                continue;
            }

            var parts = testInput.Split(',');
            if (parts.Length != 2)
            {
                throw new Exception("Invalid test input: " + testInput);
            }

            var input = parts[0];
            var expected = parts[1];
            var result = sanitizer.Sanitize(input);

            Assert.Equal(expected, result);
        }
    }

    [Fact]
    public void Sanitize_Should_Remove_Special_Characters()
    {
        var input = "!@#$%^&*()_+";
        var result = sanitizer.Sanitize(input);

        Assert.Equal("", result);
    }

    [Fact]
    public void Sanitize_Should_Keep_Unicode()
    {
        var input = "!@#$%^&*()_+😀";
        var result = sanitizer.Sanitize(input);

        Assert.Equal("😀", result);
    }

    [Fact]
    public void Normalize_Should_Not_Affect_Stop_Words()
    {
        // this is to ensure our stopwords will still be useful after normalization
        var stopWords = sanitizer.GetStopWords().ToArray();
        var newStopwords = stopWords.Select(s => PostSanitizer.Normalize(s));
        // export for debugging
        File.WriteAllLines("stopwords_normalized.txt", newStopwords);

        for (int i = 0; i < stopWords.Length; i++)
        {
            Assert.Equal(stopWords[i], newStopwords.ElementAt(i));
        }
    }

    [Fact]
    public void Lemmatize_All_Stop_Words()
    {
        // this is to ensure our stopwords will still be useful after lemmatization
        var stopWords = sanitizer.GetStopWords().ToArray();
        var newStopwords = stopWords.Select(s => sanitizer.Lemmatize(s));
        // export for debugging
        File.WriteAllLines("stopwords_lemmatized.txt", newStopwords);

        for (int i = 0; i < stopWords.Length; i++)
        {
            Assert.Equal(stopWords[i], newStopwords.ElementAt(i));
        }
    }

    [Fact]
    public void Normalize_Should_Not_Affect_Key_Words()
    {
        // this is to ensure our keywords will still be useful after normalization
        var classifierFiles = Directory.GetFiles(Path.Combine(Directory.GetCurrentDirectory(), "keywords"), "*.csv");
        var logger = new Mock<ILogger<KeywordClassifier>>();
        foreach (var file in classifierFiles)
        {
            // create keywordClassifier
            var keywordClassifier = new KeywordClassifier(logger.Object, file);
            var keyWords = keywordClassifier.GetKeywords().ToArray();

            // keyword = Keyword(string[] terms, int weight)
            var newKeywords = keyWords.Select(k => new Keyword(k.Keywords.Select(s => PostSanitizer.Normalize(s)).ToArray(), k.Weight));
            // export for debugging
            File.WriteAllLines(Path.GetFileNameWithoutExtension(file) + "_normalized.txt", newKeywords.Select(k => $"{string.Join("|", k.Keywords)},{k.Weight}"));

            for (int i = 0; i < keyWords.Length; i++)
            {
                var expected = string.Join("|", keyWords[i].Keywords);
                var actual = string.Join("|", newKeywords.ElementAt(i).Keywords);
                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    public void Lemmatize_Should_Not_Affect_Key_Words()
    {
        // this is to ensure our keywords will still be useful after normalization
        var classifierFiles = Directory.GetFiles(Path.Combine(Directory.GetCurrentDirectory(), "keywords"), "*.csv");
        var logger = new Mock<ILogger<KeywordClassifier>>();
        foreach (var file in classifierFiles)
        {
            // create keywordClassifier
            var keywordClassifier = new KeywordClassifier(logger.Object, file);
            var keyWords = keywordClassifier.GetKeywords().ToArray();

            // keyword = Keyword(string[] terms, int weight)
            var newKeywords = keyWords.Select(k => new Keyword(k.Keywords.Select(s => sanitizer.Lemmatize(s)).ToArray(), k.Weight));
            // export for debugging
            File.WriteAllLines(Path.GetFileNameWithoutExtension(file) + "_lemmatized.txt", newKeywords.Select(k => $"{string.Join("|", k.Keywords)},{k.Weight}"));

            for (int i = 0; i < keyWords.Length; i++)
            {
                var expected = string.Join("|", keyWords[i].Keywords);
                var actual = string.Join("|", newKeywords.ElementAt(i).Keywords);
                Assert.Equal(expected, actual);
            }
        }
    }
}