using System.Collections.Generic;

namespace Coflnet.Sky.Subscriptions.Models
{
    public class User
    {
        public int Id { get; set; }
        /// <summary>
        /// The identifier of the account system
        /// </summary>
        /// <value></value>
        public string ExternalId { get; set; }
        /// <summary>
        /// Devices connected to this user
        /// </summary>
        /// <value></value>
        public List<Device> Devices { get; set; }
        public List<Subscription> Subscriptions { get; set; }
    }
}