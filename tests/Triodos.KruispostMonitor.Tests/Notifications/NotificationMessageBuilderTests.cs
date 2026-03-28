using FluentAssertions;
using Triodos.KruispostMonitor.Matching;
using Triodos.KruispostMonitor.Notifications;

namespace Triodos.KruispostMonitor.Tests.Notifications;

public class NotificationMessageBuilderTests
{
    [Fact]
    public void Build_WithUnmatchedDebits_IncludesThemInBothFormats()
    {
        var result = new MatchResult
        {
            UnmatchedDebits =
            [
                new TransactionRecord("d1", -35.00m, "Albert Heijn", "Boodschappen", DateTimeOffset.Parse("2026-03-20"))
            ]
        };

        var message = NotificationMessageBuilder.Build(result, currentBalance: 265.00m, targetBalance: 300.00m);

        message.Should().NotBeNull();
        message!.Subject.Should().Contain("1 unmatched expense");

        // Plain text
        message.PlainText.Should().Contain("Albert Heijn");
        message.PlainText.Should().Contain("35.00");
        message.PlainText.Should().Contain("265.00");
        message.PlainText.Should().Contain("300.00");

        // HTML
        message.Html.Should().Contain("<table");
        message.Html.Should().Contain("Albert Heijn");
        message.Html.Should().Contain("35.00");
    }

    [Fact]
    public void Build_WithPossibleMatches_IncludesThemInBothFormats()
    {
        var result = new MatchResult
        {
            UnmatchedDebits =
            [
                new TransactionRecord("d1", -17.50m, "Bol.com", "Bestelling 123", DateTimeOffset.Parse("2026-03-22"))
            ],
            PossibleMatches =
            [
                new PossibleMatch(
                    new TransactionRecord("d1", -17.50m, "Bol.com", "Bestelling 123", DateTimeOffset.Parse("2026-03-22")),
                    new TransactionRecord("c1", 17.50m, "Bol.com", "Terugbetaling", DateTimeOffset.Parse("2026-03-24")),
                    0.5)
            ]
        };

        var message = NotificationMessageBuilder.Build(result, currentBalance: 282.50m, targetBalance: 300.00m);

        message.Should().NotBeNull();
        message!.PlainText.Should().Contain("Possible match");
        message.PlainText.Should().Contain("Bol.com");
        message.Html.Should().Contain("Possible match");
        message.Html.Should().Contain("Bol.com");
    }

    [Fact]
    public void Build_BalanceMatchesTarget_ReturnsNull()
    {
        var result = new MatchResult();

        var message = NotificationMessageBuilder.Build(result, currentBalance: 300.00m, targetBalance: 300.00m);

        message.Should().BeNull();
    }

    [Fact]
    public void Build_BalanceDeviates_ShowsDelta()
    {
        var result = new MatchResult();

        var message = NotificationMessageBuilder.Build(result, currentBalance: 250.00m, targetBalance: 300.00m);

        message.Should().NotBeNull();
        message!.PlainText.Should().Contain("-50.00");
        message.Html.Should().Contain("50.00");
    }

    [Fact]
    public void Build_HtmlEscapesSpecialCharacters()
    {
        var result = new MatchResult
        {
            UnmatchedDebits =
            [
                new TransactionRecord("d1", -10.00m, "H&M <store>", "Test", DateTimeOffset.Parse("2026-03-20"))
            ]
        };

        var message = NotificationMessageBuilder.Build(result, currentBalance: 290.00m, targetBalance: 300.00m);

        message.Should().NotBeNull();
        message!.Html.Should().Contain("H&amp;M &lt;store&gt;");
        message.Html.Should().NotContain("<store>");
    }

    [Fact]
    public void BuildSuccess_ReturnsBothFormats()
    {
        var message = NotificationMessageBuilder.BuildSuccess(300.00m, "EUR");

        message.Subject.Should().Contain("all clear");
        message.PlainText.Should().Contain("300.00");
        message.Html.Should().Contain("<html>");
        message.Html.Should().Contain("300.00");
    }
}
