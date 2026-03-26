namespace Triodos.KruispostMonitor.Notifications;

public interface INotificationSender
{
    Task SendAsync(string message, CancellationToken cancellationToken = default);
    bool IsEnabled { get; }
}
