namespace Coflnet.Sky.Subscriptions.Models
{
    public class Notification
    {
        public long Id { get; set; }
        /// <summary>
        /// The click target of the notification
        /// </summary>
        /// <value></value>
        [System.ComponentModel.DataAnnotations.MaxLength(32)]
        public string Target { get; set; }
        public Subscription Subscription { get; set; }
        public NotificationState State { get; set; }

        public enum NotificationState
        {
            NONE,
            SENT,
            CLICKED,

            OUTDATED = 4,
            REPLACED = 8,
            DELETED = 16

        }
    }
}