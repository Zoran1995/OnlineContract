namespace OnlineContract.Services.Notifications
{
    public class NotificationMessage
    {
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public NotificationType Type { get; set; } = NotificationType.Information;
        public int? UserId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public enum NotificationType
    {
        Information,
        Warning,
        Error
    }
}
