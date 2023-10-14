using Bluesky.Firehose.Classifiers;
using Bluesky.Firehose.Models;

namespace Firehose.Tests;

public class KeywordClassifierTests
{
    [Fact]
    public void ClassifyText_Should_Classify_Text_Correctly()
    {
        var text = "this is a test";
        var result = KeywordClassifier.GenerateScore(new[]
                {
                    new Keyword(new[] {"test"}, 100)
                }, text);

        Assert.Equal(100, result);
    }

    [Fact]
    public void ClassifyText_Should_Classify_Text_Correctly_With_Multiple_Keywords()
    {
        var text = "this is a test";
        var result = KeywordClassifier.GenerateScore(new[]
                {
                    new Keyword(new[] {"test"}, 10),
                    new Keyword(new[] {"this"}, 10)
                }, text);

        Assert.Equal(20, result);
    }

    [Fact]
    public void ClassifyText_Should_Classify_Keyword_Only_Once()
    {
        var text = "test test test test";
        var result = KeywordClassifier.GenerateScore(new[]
                {
                    new Keyword(new[] {"test"}, 10),
                    new Keyword(new[] {"this"}, 10)
                }, text);
        Assert.Equal(10, result);
    }

    [Fact]
    public void ClassifyText_Should_Skip_Substring()
    {
        var text = "this is a test";
        var result = KeywordClassifier.GenerateScore(new[]
                {
                    // "test" is a substring of "a test"
                    new Keyword(new[] {"a test"}, 50),
                    new Keyword(new[] {"test"}, 10)
            }, text);
        Assert.Equal(50, result);
    }

    [Fact]
    public void ClassifyText_Should_Remove_Subset()
    {
        var text = "this is a test";
        var result = KeywordClassifier.GenerateScore(new[]
                {
                    // "test" is a subset of "a test"
                    new Keyword(new[] {"test"}, 10),
                    new Keyword(new[] {"a", "test"}, 50)
                }, text);
        Assert.Equal(50, result);
    }

    [Fact]
    public void ClassifyText_Should_Remove_Substring()
    {
        var text = "this is a test";
        var result = KeywordClassifier.GenerateScore(new[]
                {
                    // "test" is a substring of "a test"
                    new Keyword(new[] {"test"}, 10),
                    new Keyword(new[] {"a test"}, 50)
                },
                text);
        Assert.Equal(50, result);
    }

    [Fact]
    public void ClassifyText_Should_Skip_Subset()
    {
        var text = "this is a test";
        var result = KeywordClassifier.GenerateScore(new[]
                {
                    // "test" is a subset of "a test"
                    new Keyword(new[] {"a", "test"}, 50),
                    new Keyword(new[] {"test"}, 10)
                }, text);
        Assert.Equal(50, result);
    }

    [Fact]
    public void ClassifyText_Should_Skip_Equal()
    {
        var text = "this is a test";
        var result = KeywordClassifier.GenerateScore(new[]
                {
                    new Keyword(new[] {"a test"}, 1),
                    new Keyword(new[] {"a", "test"}, 3),
                    new Keyword(new[] {"test"}, 5),
                    new Keyword(new[] {"a"}, 7),
                    new Keyword(new[] {"a", "test"}, 11),
                    new Keyword(new[] {"this is", "test"}, 13),
                }, text);
        // "a", "test" and "this is", "test" are not substrings or subsets of eachother so both will remain
        Assert.Equal(24, result);
    }
}