using Triodos.KruispostMonitor.Matching;

namespace Triodos.KruispostMonitor.Mt940;

public record Mt940Statement(
    string AccountIdentification,
    decimal ClosingBalance,
    string Currency,
    List<TransactionRecord> Transactions);
