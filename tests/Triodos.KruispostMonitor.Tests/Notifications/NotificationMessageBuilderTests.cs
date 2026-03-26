using FluentAssertions;
using Triodos.KruispostMonitor.Matching;
using Triodos.KruispostMonitor.Notifications;

namespace Triodos.KruispostMonitor.Tests.Notifications;

public class NotificationMessageBuilderTests
{
    [Fact]
    public void Build_WithUnmatchedDebits_IncludesThemInMessage()
    {
        var result = new MatchResult
        {
            UnmatchedDebits =
            [
                new TransactionRecord("d1", -35.00m, "Albert Heijn", "Boodschappen", DateTimeOffset.Parse("2026-03-20"))
            ]
        };

        var message = NotificationMessageBuilder.Build(result, currentBalance: 265.00m, targetBalance: 300.00m);

        message.Should().Contain("1 unmatched expense");
        message.Should().Contain("Albert Heijn");
        message.Should().Contain("35.00");
        message.Should().Contain("265.00");
        message.Should().Contain("300.00");
    }

    [Fact]
    public void Build_WithPossibleMatches_IncludesThemInMessage()
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

        message.Should().Contain("Possible match");
        message.Should().Contain("Terugbetaling");
    }

    [Fact]
    public void Build_BalanceMatchesTarget_DoesNotShowDelta()
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

        message.Should().Contain("-50.00");
    }
}
