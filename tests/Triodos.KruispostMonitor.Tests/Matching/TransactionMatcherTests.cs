using FluentAssertions;
using Triodos.KruispostMonitor.Configuration;
using Triodos.KruispostMonitor.Matching;

namespace Triodos.KruispostMonitor.Tests.Matching;

public class TransactionMatcherTests
{
    private readonly MatchingSettings _settings = new()
    {
        SimilarityThreshold = 0.5,
        TargetBalance = 300.00m
    };

    private static TransactionRecord Debit(string id, decimal amount, string counterpart, string remittance, string date = "2026-03-20") =>
        new(id, -Math.Abs(amount), counterpart, remittance, DateTimeOffset.Parse(date));

    private static TransactionRecord Credit(string id, decimal amount, string counterpart, string remittance, string date = "2026-03-21") =>
        new(id, Math.Abs(amount), counterpart, remittance, DateTimeOffset.Parse(date));

    [Fact]
    public void Match_ExactAmountAndSimilarReference_ReturnsMatched()
    {
        var transactions = new List<TransactionRecord>
        {
            Debit("d1", 35.00m, "Albert Heijn", "Boodschappen week 12"),
            Credit("c1", 35.00m, "Albert Heijn", "Boodschappen week 12")
        };

        var result = new TransactionMatcher(_settings).Match(transactions, []);

        result.Matched.Should().HaveCount(1);
        result.Matched[0].Debit.Id.Should().Be("d1");
        result.Matched[0].Credit.Id.Should().Be("c1");
        result.UnmatchedDebits.Should().BeEmpty();
    }

    [Fact]
    public void Match_NoMatchingCredit_ReturnsUnmatched()
    {
        var transactions = new List<TransactionRecord>
        {
            Debit("d1", 35.00m, "Albert Heijn", "Boodschappen")
        };

        var result = new TransactionMatcher(_settings).Match(transactions, []);

        result.Matched.Should().BeEmpty();
        result.UnmatchedDebits.Should().HaveCount(1);
        result.UnmatchedDebits[0].Id.Should().Be("d1");
    }

    [Fact]
    public void Match_SameAmountDifferentReference_MatchesOnAmountAlone()
    {
        var transactions = new List<TransactionRecord>
        {
            Debit("d1", 35.00m, "Albert Heijn", "Boodschappen"),
            Credit("c1", 35.00m, "Hypotheek Bank", "Maandelijkse afschrijving")
        };

        var result = new TransactionMatcher(_settings).Match(transactions, []);

        // Amount match alone is sufficient at default threshold
        result.Matched.Should().HaveCount(1);
    }

    [Fact]
    public void Match_HighThreshold_RequiresTextSimilarity()
    {
        var settings = new MatchingSettings { SimilarityThreshold = 0.8, TargetBalance = 300m };
        var transactions = new List<TransactionRecord>
        {
            Debit("d1", 35.00m, "Albert Heijn", "Boodschappen"),
            Credit("c1", 35.00m, "Hypotheek Bank", "Maandelijkse afschrijving")
        };

        var result = new TransactionMatcher(settings).Match(transactions, []);

        result.Matched.Should().BeEmpty();
        result.PossibleMatches.Should().HaveCount(1);
    }

    [Fact]
    public void Match_AlreadyMatchedTransactions_AreExcluded()
    {
        var transactions = new List<TransactionRecord>
        {
            Debit("d1", 35.00m, "Albert Heijn", "Boodschappen"),
            Credit("c1", 35.00m, "Albert Heijn", "Boodschappen")
        };
        var alreadyMatched = new HashSet<string> { "d1", "c1" };

        var result = new TransactionMatcher(_settings).Match(transactions, alreadyMatched);

        result.Matched.Should().BeEmpty();
        result.UnmatchedDebits.Should().BeEmpty();
    }

    [Fact]
    public void Match_MultipleCreditsForOneDebit_PicksBestMatch()
    {
        var transactions = new List<TransactionRecord>
        {
            Debit("d1", 50.00m, "Bol.com", "Bestelling 12345"),
            Credit("c1", 50.00m, "Bol.com", "Bestelling 12345"),
            Credit("c2", 50.00m, "Bol.com", "Bestelling 99999")
        };

        var result = new TransactionMatcher(_settings).Match(transactions, []);

        result.Matched.Should().HaveCount(1);
        result.Matched[0].Credit.Id.Should().Be("c1");
        result.UnmatchedCredits.Should().HaveCount(1);
    }

    [Fact]
    public void Match_PossibleMatch_WhenAmountMatchesButHighThreshold()
    {
        var settings = new MatchingSettings { SimilarityThreshold = 0.9, TargetBalance = 300m };
        var transactions = new List<TransactionRecord>
        {
            Debit("d1", 50.00m, "Bol.com", "Bestelling 12345"),
            Credit("c1", 50.00m, "Retour afdeling", "Terugbetaling order")
        };

        var result = new TransactionMatcher(settings).Match(transactions, []);

        result.Matched.Should().BeEmpty();
        result.PossibleMatches.Should().HaveCount(1);
    }

    [Fact]
    public void Match_KruispostPattern_DifferentNamesMatchOnAmount()
    {
        // Real kruispost: debit is merchant name, credit is partner name + description
        var transactions = new List<TransactionRecord>
        {
            Debit("d1", 11.98m, "HOLLAND & BARRETT - ENSCHEDE", "TERMINAL 0MXK4N", "2026-03-01"),
            Credit("c1", 11.98m, "R.F.B. KUIPERS EN/OF I. HOLLAND BARRETT", "IBAN: NL36TRIO2300471469", "2026-03-02")
        };

        var result = new TransactionMatcher(_settings).Match(transactions, []);

        result.Matched.Should().HaveCount(1);
        result.UnmatchedDebits.Should().BeEmpty();
    }

    [Fact]
    public void Match_MultipleCreditsForSameAmount_PicksClosestDate()
    {
        var transactions = new List<TransactionRecord>
        {
            Debit("d1", 12.25m, "FC TWENTE 65", "TERMINAL", "2026-03-02"),
            Credit("c1", 12.25m, "R.F.B. KUIPERS", "FC TWENTE DRINKEN 1", "2026-03-05"),
            Credit("c2", 12.25m, "R.F.B. KUIPERS", "FC TWENTE DRINKEN 2", "2026-03-05")
        };

        var result = new TransactionMatcher(_settings).Match(transactions, []);

        result.Matched.Should().HaveCount(1);
        result.UnmatchedCredits.Should().HaveCount(1);
    }
}
