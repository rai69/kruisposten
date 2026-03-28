namespace Triodos.KruispostMonitor.Notifications;

public record NotificationMessage(string Subject, string PlainText, string Html);

public interface INotificationSender
{
    Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default);
    bool IsEnabled { get; }
}
