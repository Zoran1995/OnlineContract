namespace OnlineContract.Services.Notifications
{
    /// <summary>
    /// Service interface for sending notifications via the Bell system.
    /// Notifications are stored in memory and can be retrieved by the frontend.
    /// </summary>
    public interface INotificationService
    {
        /// <summary>
        /// Sends a notification to a specific user or broadcasts to all.
        /// </summary>
        void SendNotification(NotificationMessage message);

        /// <summary>
        /// Gets pending notifications for a user.
        /// </summary>
        IEnumerable<NotificationMessage> GetNotifications(int? userId = null);

        /// <summary>
        /// Clears notifications for a user.
        /// </summary>
        void ClearNotifications(int? userId = null);
    }

    /// <summary>
    /// In-memory notification service that stores notifications for retrieval by the frontend.
    /// In production, this could be replaced with SignalR or a message queue.
    /// </summary>
    public class NotificationService : INotificationService
    {
        private readonly List<NotificationMessage> _notifications = new();
        private readonly object _lock = new();

        public void SendNotification(NotificationMessage message)
        {
            lock (_lock)
            {
                message.Timestamp = DateTime.UtcNow;
                _notifications.Add(message);
            }
        }

        public IEnumerable<NotificationMessage> GetNotifications(int? userId = null)
        {
            lock (_lock)
            {
                var query = _notifications.AsEnumerable();
                if (userId.HasValue)
                {
                    query = query.Where(n => n.UserId == null || n.UserId == userId);
                }
                return query.OrderByDescending(n => n.Timestamp).ToList();
            }
        }

        public void ClearNotifications(int? userId = null)
        {
            lock (_lock)
            {
                if (userId.HasValue)
                {
                    _notifications.RemoveAll(n => n.UserId == userId);
                }
                else
                {
                    _notifications.Clear();
                }
            }
        }
    }
}
