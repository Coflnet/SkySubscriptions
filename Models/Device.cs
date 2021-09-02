using System.Runtime.Serialization;

namespace Coflnet.Sky.Subscriptions.Models
{
    [DataContract]
    public class Device
    {
        [DataMember(Name = "Id")]
        public int Id { get; set; }
        [DataMember]
        public int UserId { get; set; }
        [DataMember(Name = "conId")]
        [System.ComponentModel.DataAnnotations.MaxLength(32)]
        public string ConnectionId { get; set; }
        [DataMember(Name = "name")]
        [System.ComponentModel.DataAnnotations.MaxLength(40)]
        public string Name { get; set; }
        [DataMember(Name = "token")]
        public string Token { get; set; }
    }
}