using Coflnet.Sky.Subscriptions.Models;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Subscriptions
{
    public class SubscribeEngineTests
    {
        [Test]
        public void BasicMatch()
        {
            var notifi = new Mock<INotificationService>();
            notifi.Setup(n => n.PriceAlert(It.IsAny<Subscription>(), "ASPECT", 5));
            var engine = new SubscribeEngine(null, notifi.Object, null, null);
            var sub = new Subscription()
            {
                Price = 1,
                Type = Subscription.SubType.BIN | Subscription.SubType.PRICE_HIGHER_THAN,
                TopicId = "ASPECT"
            };
            engine.AddNew(sub);
            engine.NewAuction(new hypixel.SaveAuction()
            {
                StartingBid = 5,
                Tag = "ASPECT",
                Start = System.DateTime.Now,
                AuctioneerId = "",
                Bin = true
            });
            notifi.Verify(n => n.PriceAlert(sub, "ASPECT", 5), Times.Once);
        }
    }
}