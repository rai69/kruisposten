using FluentAssertions;
using Triodos.KruispostMonitor.Matching;

namespace Triodos.KruispostMonitor.Tests.Matching;

public class StringSimilarityTests
{
    [Theory]
    [InlineData("abc", "abc", 1.0)]
    [InlineData("", "", 1.0)]
    [InlineData("abc", "xyz", 0.0)]
    [InlineData("abc", "", 0.0)]
    [InlineData("", "abc", 0.0)]
    public void Calculate_ExactAndEdgeCases(string a, string b, double expected)
    {
        StringSimilarity.Calculate(a, b).Should().BeApproximately(expected, 0.01);
    }

    [Fact]
    public void Calculate_SimilarStrings_ReturnsHighScore()
    {
        var score = StringSimilarity.Calculate(
            "Boodschappen Albert Heijn",
            "Albert Heijn boodschappen");

        score.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public void Calculate_DifferentStrings_ReturnsLowScore()
    {
        var score = StringSimilarity.Calculate(
            "Boodschappen Albert Heijn",
            "Hypotheek betaling");

        score.Should().BeLessThan(0.3);
    }

    [Fact]
    public void Calculate_IsCaseInsensitive()
    {
        var score1 = StringSimilarity.Calculate("Albert Heijn", "albert heijn");
        var score2 = StringSimilarity.Calculate("Albert Heijn", "Albert Heijn");

        score1.Should().Be(score2);
    }

    [Fact]
    public void Calculate_NullInputs_ReturnsZero()
    {
        StringSimilarity.Calculate(null, "abc").Should().Be(0.0);
        StringSimilarity.Calculate("abc", null).Should().Be(0.0);
        StringSimilarity.Calculate(null, null).Should().Be(1.0);
    }
}
