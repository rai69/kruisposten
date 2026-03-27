using FluentAssertions;
using Triodos.KruispostMonitor.Mt940;

namespace Triodos.KruispostMonitor.Tests.Mt940;

public class Mt940ParserTests
{
    private const string SampleStatement = """
        :20:STARTOFSTMT
        :25:NL91TRIO0123456789
        :28C:00001
        :60F:C260325EUR1234,56
        :61:2603250325D100,00NTRFNONREF//PREF
        :86:Counterpart A
        /REMI/Payment for invoice 001
        :61:2603250325C100,00NTRFNONREF//PREF
        :86:Counterpart B
        /REMI/Chargeback invoice 001
        :61:2603260326D50,50NTRFNONREF//PREF
        :86:Counterpart C
        /REMI/Payment for invoice 002
        :62F:C1184,06EUR
        """;

    [Fact]
    public void Parse_ExtractsAccountIdentification()
    {
        var result = Mt940Parser.Parse(SampleStatement);
        result.AccountIdentification.Should().Be("NL91TRIO0123456789");
    }

    [Fact]
    public void Parse_ExtractsClosingBalance()
    {
        var result = Mt940Parser.Parse(SampleStatement);
        result.ClosingBalance.Should().Be(1184.06m);
        result.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Parse_ExtractsTransactions()
    {
        var result = Mt940Parser.Parse(SampleStatement);
        result.Transactions.Should().HaveCount(3);
    }

    [Fact]
    public void Parse_DebitTransaction_HasNegativeAmount()
    {
        var result = Mt940Parser.Parse(SampleStatement);
        var debit = result.Transactions[0];
        debit.Amount.Should().Be(-100.00m);
        debit.CounterpartName.Should().Be("Counterpart A");
        debit.RemittanceInformation.Should().Be("Payment for invoice 001");
        debit.ExecutionDate.Should().Be(new DateTimeOffset(2026, 3, 25, 0, 0, 0, TimeSpan.Zero));
        debit.IsDebit.Should().BeTrue();
    }

    [Fact]
    public void Parse_CreditTransaction_HasPositiveAmount()
    {
        var result = Mt940Parser.Parse(SampleStatement);
        var credit = result.Transactions[1];
        credit.Amount.Should().Be(100.00m);
        credit.CounterpartName.Should().Be("Counterpart B");
        credit.RemittanceInformation.Should().Be("Chargeback invoice 001");
        credit.IsCredit.Should().BeTrue();
    }

    [Fact]
    public void Parse_GeneratesDeterministicIds()
    {
        var result1 = Mt940Parser.Parse(SampleStatement);
        var result2 = Mt940Parser.Parse(SampleStatement);
        result1.Transactions[0].Id.Should().Be(result2.Transactions[0].Id);
        result1.Transactions[0].Id.Should().HaveLength(16);
    }

    [Fact]
    public void Parse_GeneratesUniqueIdsPerTransaction()
    {
        var result = Mt940Parser.Parse(SampleStatement);
        var ids = result.Transactions.Select(t => t.Id).ToList();
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Parse_EmptyContent_ThrowsFormatException()
    {
        var act = () => Mt940Parser.Parse("");
        act.Should().Throw<FormatException>().WithMessage("*:25:*");
    }

    [Fact]
    public void Parse_MissingClosingBalance_ThrowsFormatException()
    {
        var content = """
            :20:STARTOFSTMT
            :25:NL91TRIO0123456789
            :28C:00001
            :60F:C260325EUR1234,56
            """;
        var act = () => Mt940Parser.Parse(content);
        act.Should().Throw<FormatException>().WithMessage("*:62F:*");
    }

    [Fact]
    public void Parse_ClosingBalanceDebit_ReturnsNegativeBalance()
    {
        var content = """
            :20:STARTOFSTMT
            :25:NL91TRIO0123456789
            :28C:00001
            :60F:C260325EUR1234,56
            :62F:D500,00EUR
            """;
        var result = Mt940Parser.Parse(content);
        result.ClosingBalance.Should().Be(-500.00m);
    }
}
