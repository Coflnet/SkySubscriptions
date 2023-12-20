using Coflnet.Sky.Core;
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
            //notifi.Setup(n => n.AuctionPriceAlert(It.IsAny<Subscription>(), "ASPECT", 5));
            var engine = new SubscribeEngine(null, notifi.Object, null, null);
            var sub = new Subscription()
            {
                Price = 1,
                Type = Subscription.SubType.BIN | Subscription.SubType.PriceHigherThan,
                TopicId = "ASPECT"
            };
            var auction = new SaveAuction()
            {
                StartingBid = 5,
                Tag = "ASPECT",
                Start = System.DateTime.Now,
                AuctioneerId = "",
                Bin = true
            };
            engine.AddNew(sub);
            engine.NewAuction(auction);
            notifi.Verify(n => n.AuctionPriceAlert(sub, auction), Times.Once);
        }
    }
}