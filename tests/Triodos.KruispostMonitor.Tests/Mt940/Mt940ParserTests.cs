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

    private const string TriodosStatement = """
        :20:1774607603607/1
        :25:TRIODOSBANK/0338505768
        :28:1
        :60F:C260301EUR335,31
        :61:260301D11,98NBA NONREF
        :86:000>100000000000
        >20HOLLAND & BARRETT - ENSCHED>21E - TERMINAL 0MXK4N - 28-02
        >22-2026 11:06 - PASNR. *1991 >23- CONTACTLOOS - APPLE PAY
        >310338505768
        :61:260301C35,90NET NONREF
        :86:000>100000000000
        >20TRIONL2U
        >21NL36TRIO2300471469
        >22R.F.B. KUIPERS EN/OF I. VER>23REKENEN BIOMOS BOL
        >310338505768
        :61:260302D35,90NID NONREF
        :86:000>100000000000
        >20INGBNL2A
        >21NL27INGB0000026500
        >22BOL.COM P1657059212 7051616>23855068149 BOL.COM C0001H18T
        >24D
        >310338505768
        :62F:C260301EUR335,31
        """;

    [Fact]
    public void Parse_TriodosCardPayment_ExtractsMerchantName()
    {
        var result = Mt940Parser.Parse(TriodosStatement);
        var card = result.Transactions[0];
        card.CounterpartName.Should().Be("HOLLAND & BARRETT - ENSCHEDE");
        card.Amount.Should().Be(-11.98m);
    }

    [Fact]
    public void Parse_TriodosTransferIn_ExtractsCounterpartFromNarrative()
    {
        var result = Mt940Parser.Parse(TriodosStatement);
        var transfer = result.Transactions[1];
        transfer.CounterpartName.Should().Contain("KUIPERS");
        transfer.RemittanceInformation.Should().Contain("NL36TRIO2300471469");
        transfer.Amount.Should().Be(35.90m);
    }

    [Fact]
    public void Parse_TriodosTransferOut_ExtractsCounterpartFromNarrative()
    {
        var result = Mt940Parser.Parse(TriodosStatement);
        var transfer = result.Transactions[2];
        transfer.CounterpartName.Should().Contain("BOL.COM");
        transfer.RemittanceInformation.Should().Contain("NL27INGB0000026500");
        transfer.Amount.Should().Be(-35.90m);
    }
}
