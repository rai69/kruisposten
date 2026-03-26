namespace Triodos.KruispostMonitor.Configuration;

public class PontoSettings
{
    public const string SectionName = "Ponto";
    public required string ClientId { get; set; }
    public required string ClientSecret { get; set; }
    public required string CertificatePath { get; set; }
    public required string CertificatePassword { get; set; }
    public required string RefreshToken { get; set; }
    public required string AccountIban { get; set; }
    public string ApiEndpoint { get; set; } = "https://api.ibanity.com";
}

public class MatchingSettings
{
    public const string SectionName = "Matching";
    public double SimilarityThreshold { get; set; } = 0.7;
    public decimal TargetBalance { get; set; } = 300.00m;
}

public class SlackSettings
{
    public string WebhookUrl { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

public class EmailSettings
{
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public List<string> ToAddresses { get; set; } = [];
    public bool Enabled { get; set; }
}

public class NotificationSettings
{
    public const string SectionName = "Notifications";
    public bool NotifyOnSuccess { get; set; }
    public SlackSettings Slack { get; set; } = new();
    public EmailSettings Email { get; set; } = new();
}

public class StateSettings
{
    public const string SectionName = "State";
    public string FilePath { get; set; } = "state.json";
}
