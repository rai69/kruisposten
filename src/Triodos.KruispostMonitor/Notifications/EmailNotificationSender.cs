using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Triodos.KruispostMonitor.Configuration;

namespace Triodos.KruispostMonitor.Notifications;

public class EmailNotificationSender : INotificationSender
{
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailNotificationSender> _logger;

    public bool IsEnabled => _settings.Enabled && !string.IsNullOrEmpty(_settings.SmtpHost);

    public EmailNotificationSender(
        IOptions<NotificationSettings> settings,
        ILogger<EmailNotificationSender> logger)
    {
        _settings = settings.Value.Email;
        _logger = logger;
    }

    public async Task SendAsync(string message, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled) return;

        var email = new MimeMessage();
        email.From.Add(MailboxAddress.Parse(_settings.FromAddress));
        foreach (var to in _settings.ToAddresses)
            email.To.Add(MailboxAddress.Parse(to));

        email.Subject = message.Split('\n', 2)[0];
        email.Body = new TextPart("plain") { Text = message };

        using var smtp = new SmtpClient();
        var socketOptions = _settings.UseSsl
            ? (_settings.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls)
            : SecureSocketOptions.None;
        await smtp.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, socketOptions, cancellationToken);

        if (!string.IsNullOrEmpty(_settings.Username))
            await smtp.AuthenticateAsync(_settings.Username, _settings.Password, cancellationToken);

        await smtp.SendAsync(email, cancellationToken);
        await smtp.DisconnectAsync(true, cancellationToken);

        _logger.LogInformation("Email notification sent to {Recipients}", string.Join(", ", _settings.ToAddresses));
    }
}
