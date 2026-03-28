using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Triodos.KruispostMonitor.Configuration;

namespace Triodos.KruispostMonitor.Notifications;

public class SlackNotificationSender : INotificationSender
{
    private readonly HttpClient _httpClient;
    private readonly SlackSettings _settings;
    private readonly ILogger<SlackNotificationSender> _logger;

    public bool IsEnabled => _settings.Enabled && !string.IsNullOrEmpty(_settings.WebhookUrl);

    public SlackNotificationSender(
        HttpClient httpClient,
        IOptions<NotificationSettings> settings,
        ILogger<SlackNotificationSender> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value.Slack;
        _logger = logger;
    }

    public async Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled) return;

        var payload = JsonSerializer.Serialize(new { text = message.PlainText });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(_settings.WebhookUrl, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Slack notification failed with status {StatusCode}", response.StatusCode);
        }
    }
}
