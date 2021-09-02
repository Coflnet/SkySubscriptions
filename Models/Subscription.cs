using System;
using System.Runtime.Serialization;

namespace Coflnet.Sky.Subscriptions.Models
{
    [DataContract]
    public class Subscription
    {
        [IgnoreDataMember]
        public int Id { get; set; }
        /// <summary>
        /// Either User,auction or ItemId UserIds are +100.000
        /// </summary>
        /// <value></value>
        [DataMember(Name = "topicId")]
        [System.ComponentModel.DataAnnotations.MaxLength(45)]
        public string TopicId { get; set; }
        /// <summary>
        /// Price point in case of item
        /// </summary>
        /// <value></value>
        [DataMember(Name = "price")]
        public long Price { get; set; }

        [System.ComponentModel.DataAnnotations.Timestamp]
        [IgnoreDataMember]
        public DateTime GeneratedAt { get; set; }

        public enum SubType
        {
            NONE = 0,
            PRICE_LOWER_THAN = 1,
            PRICE_HIGHER_THAN = 2,
            OUTBID = 4,
            SOLD = 8,
            BIN = 16,
            USE_SELL_NOT_BUY = 32,
            AUCTION = 64,
            PLAYER = 128
        }

        [DataMember(Name = "type")]
        public SubType Type { get; set; }

        [IgnoreDataMember]
        public DateTime NotTriggerAgainBefore { get; set; }
        [IgnoreDataMember]
        public User User { get; set; }
        public int UserId { get; set; }
    }
}