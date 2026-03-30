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
        :86:/CNTP/NL11TRIO1234567890/TRIONL2U/Counterpart A///REMI/USTD//Payment for invoice 001/
        :61:2603250325C100,00NTRFNONREF//PREF
        :86:/CNTP/NL22TRIO9876543210/TRIONL2U/Counterpart B///REMI/USTD//Chargeback invoice 001/
        :61:2603260326D50,50NTRFNONREF//PREF
        :86:/CNTP/NL33TRIO5555555555/TRIONL2U/Counterpart C///REMI/USTD//Payment for invoice 002/
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
    public void Parse_MissingClosingBalance_DefaultsToZero()
    {
        var content = """
            :20:STARTOFSTMT
            :25:NL91TRIO0123456789
            :28C:00001
            :60F:C260325EUR1234,56
            """;
        var result = Mt940Parser.Parse(content);
        result.ClosingBalance.Should().Be(0);
        result.Currency.Should().Be("EUR");
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
    public void Parse_TriodosNpoTransactions_AreIncluded()
    {
        var content = """
            :20:STARTOFSTMT
            :25:NL91TRIO0123456789
            :28C:00001
            :60F:C260326EUR2000,00
            :61:260326C1650,00NPO NONREF
            :86:000>100000000000
            >20TRIONL2U
            >21NL18TRIO0338481796
            >22R.F.B. KUIPERS EN/OF I. OVE>23RIGE UITGAVEN
            >310338505768
            :61:260326D60,00NPO NONREF
            :86:000>100000000000
            >20TRIONL2U
            >21NL06TRIO2300470845
            >22R.F.B. KUIPERS EN/OF I. INI>23A SCHOOL
            >310338505768
            :61:260326D11,98NBA NONREF
            :86:000>100000000000
            >20HOLLAND & BARRETT - ENSCHED>21E - TERMINAL 0MXK4N - 28-02
            >22-2026 11:06 - PASNR. *1991 >23- CONTACTLOOS - APPLE PAY
            >310338505768
            :62F:C260326EUR1878,02
            """;

        var result = Mt940Parser.Parse(content);

        // All transactions should be included (NPO no longer excluded)
        result.Transactions.Should().HaveCount(3);
        result.Transactions[0].Amount.Should().Be(1650.00m);
        result.Transactions[0].TransactionType.Should().Be("NPO");
        result.Transactions[1].Amount.Should().Be(-60.00m);
        result.Transactions[2].Amount.Should().Be(-11.98m);
    }

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

    // --- Structured MT940 format tests ---

    private const string StructuredStatement = """
        :20:1774773974315
        :25:NL55TRIO0338505768
        :28C:1
        :60F:C260201EUR257,54
        :61:260201D37,90NBA0NONREF
        :86:/REMI/USTD//Rituals - Enschede - Terminal 05683709 - 31-01-2026 12:25 - PASNR.  1991 - CONTACTLOOS - Apple Pay/
        :61:260201C37,90NET0NONREF
        :86:/CNTP/NL50TRIO2300470829/TRIONL2U/R.F.B. Kuipers en of I.///REMI/USTD//Rituals kado Inia/
        :61:260203D144,46NID0NONREF
        :86:/CNTP/NL04ADYB2017400157/ADYBNL2A/Wehkamp///EREF/02-02-26 20:40 7180665058869563//REMI/USTD//J6J57CLLJX8Q48G32U74B wehkamp: 969567de/
        :62F:C260201EUR257,54
        """;

    [Fact]
    public void Parse_StructuredCardPayment_ExtractsMerchantName()
    {
        var result = Mt940Parser.Parse(StructuredStatement);
        var card = result.Transactions[0];
        card.CounterpartName.Should().Be("Rituals - Enschede");
        card.RemittanceInformation.Should().Contain("Rituals");
        card.Amount.Should().Be(-37.90m);
    }

    [Fact]
    public void Parse_StructuredTransfer_ExtractsCounterpartAndRemittance()
    {
        var result = Mt940Parser.Parse(StructuredStatement);
        var transfer = result.Transactions[1];
        transfer.CounterpartName.Should().Contain("Kuipers");
        transfer.RemittanceInformation.Should().Contain("Rituals kado Inia");
        transfer.Amount.Should().Be(37.90m);
    }

    [Fact]
    public void Parse_StructuredIdeal_ExtractsCounterpartAndRemittance()
    {
        var result = Mt940Parser.Parse(StructuredStatement);
        var ideal = result.Transactions[2];
        ideal.CounterpartName.Should().Be("Wehkamp");
        ideal.RemittanceInformation.Should().Contain("wehkamp");
        ideal.Amount.Should().Be(-144.46m);
    }
}
