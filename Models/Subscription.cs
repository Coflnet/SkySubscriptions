using System;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Coflnet.Sky.Core;

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
        [JsonIgnore]
        public DateTime GeneratedAt { get; set; }

        public enum SubType
        {
            NONE = 0,
            PriceLowerThan = 1,
            PriceHigherThan = 2,
            OUTBID = 4,
            SOLD = 8,
            BIN = 16,
            UseSellNotBuy = 32,
            AUCTION = 64,
            PLAYER = 128,
            UNDERCUT = 256,
            /// <summary>
            /// Use flip filter
            /// </summary>
            FILTER = 512,
        }

        [DataMember(Name = "type")]
        public SubType Type { get; set; }

        [IgnoreDataMember]
        [JsonIgnore]
        public DateTime NotTriggerAgainBefore { get; set; }
        [IgnoreDataMember]
        [JsonIgnore]
        public User User { get; set; }
        public int UserId { get; set; }

        [MaxLength(200)]
        [DataMember(Name = "filter")]
        public string Filter { get; set; }
        [IgnoreDataMember]
        [JsonIgnore]
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public Func<SaveAuction, bool> matcherCache;
    }
}