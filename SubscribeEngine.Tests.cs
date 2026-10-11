using Coflnet.Sky.Core;
using Coflnet.Sky.Subscriptions.Models;
using System.Collections.Generic;
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

        private static (Mock<INotificationService> notifi, SubscribeEngine engine, Subscription sold, Subscription over, Subscription buy) Setup(string bidder = "buyer")
        {
            var notifi = new Mock<INotificationService>();
            var engine = new SubscribeEngine(null, notifi.Object, null, null);
            var sold = new Subscription() { Type = Subscription.SubType.SOLD, TopicId = "seller" };
            var over = new Subscription() { Type = Subscription.SubType.AUCTION, TopicId = "abc" };
            var buy = new Subscription() { Type = Subscription.SubType.Buy, TopicId = bidder };
            engine.AddNew(sold);
            engine.AddNew(over);
            engine.AddNew(buy);
            return (notifi, engine, sold, over, buy);
        }

        private static SaveAuction Auction(List<SaveBids> bids)
        {
            return new SaveAuction() { Uuid = "abc", Tag = "ASPECT", AuctioneerId = "seller", Bids = bids };
        }

        /// <summary>
        /// Auctions that ended without a buyer arrive with null or empty Bids. Nothing was sold or bought,
        /// only the subscribers of the auction itself are told that it ended.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void EndedAuctionWithoutBidsSendsNoSoldOrBuy(bool useNull)
        {
            var (notifi, engine, _, over, _) = Setup();
            var auction = Auction(useNull ? null : new List<SaveBids>());

            Assert.DoesNotThrow(() => engine.BinSold(auction));

            notifi.Verify(n => n.Sold(It.IsAny<Subscription>(), It.IsAny<SaveAuction>()), Times.Never);
            notifi.Verify(n => n.PlayerBuy(It.IsAny<Subscription>(), It.IsAny<SaveAuction>()), Times.Never);
            notifi.Verify(n => n.AuctionOver(over, auction), Times.Once);
        }

        [Test]
        public void SoldAuctionWithBidsNotifiesEveryone()
        {
            var (notifi, engine, sold, over, buy) = Setup("high");
            var auction = Auction(new List<SaveBids>()
            {
                new SaveBids() { Bidder = "low", Amount = 5 },
                new SaveBids() { Bidder = "high", Amount = 10 }
            });

            engine.BinSold(auction);

            notifi.Verify(n => n.Sold(sold, auction), Times.Once);
            notifi.Verify(n => n.Sold(It.IsAny<Subscription>(), It.IsAny<SaveAuction>()), Times.Once);
            notifi.Verify(n => n.AuctionOver(over, auction), Times.Once);
            notifi.Verify(n => n.PlayerBuy(buy, auction), Times.Once);
        }

        [Test]
        public void PlayerBuyOnlyForHighestBidder()
        {
            var (notifi, engine, _, _, _) = Setup("low");
            var auction = Auction(new List<SaveBids>()
            {
                new SaveBids() { Bidder = "low", Amount = 5 },
                new SaveBids() { Bidder = "high", Amount = 10 }
            });

            engine.BinSold(auction);

            notifi.Verify(n => n.PlayerBuy(It.IsAny<Subscription>(), It.IsAny<SaveAuction>()), Times.Never);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void NewBidsWithoutBidsNotifiesNobody(bool useNull)
        {
            var (notifi, engine, _, _, _) = Setup();
            engine.AddNew(new Subscription() { Type = Subscription.SubType.PLAYER, TopicId = "buyer" });
            engine.AddNew(new Subscription() { Type = Subscription.SubType.OUTBID, TopicId = "buyer" });

            Assert.DoesNotThrow(() => engine.NewBids(Auction(useNull ? null : new List<SaveBids>())));

            notifi.Verify(n => n.NewBid(It.IsAny<Subscription>(), It.IsAny<SaveAuction>(), It.IsAny<SaveBids>()), Times.Never);
            notifi.Verify(n => n.Outbid(It.IsAny<Subscription>(), It.IsAny<SaveAuction>(), It.IsAny<SaveBids>()), Times.Never);
        }

        [Test]
        public void NewBidsWithBidsNotifies()
        {
            var (notifi, engine, _, over, _) = Setup();
            var player = new Subscription() { Type = Subscription.SubType.PLAYER, TopicId = "high" };
            var outbid = new Subscription() { Type = Subscription.SubType.OUTBID, TopicId = "low" };
            engine.AddNew(player);
            engine.AddNew(outbid);
            var low = new SaveBids() { Bidder = "low", Amount = 5 };
            var high = new SaveBids() { Bidder = "high", Amount = 10 };
            var auction = Auction(new List<SaveBids>() { low, high });

            engine.NewBids(auction);

            notifi.Verify(n => n.Outbid(outbid, auction, low), Times.Once);
            notifi.Verify(n => n.Outbid(It.IsAny<Subscription>(), It.IsAny<SaveAuction>(), It.IsAny<SaveBids>()), Times.Once);
            notifi.Verify(n => n.NewBid(over, auction, high), Times.Once);
            notifi.Verify(n => n.NewBid(player, auction, high), Times.Once);
        }

        /// <summary>
        /// A bid without bidder name is still a bid, the auction was sold
        /// </summary>
        [Test]
        public void SoldAuctionWithNullBidderStillSends()
        {
            var (notifi, engine, sold, over, _) = Setup();
            var auction = Auction(new List<SaveBids>() { new SaveBids() { Bidder = null, Amount = 5 } });

            Assert.DoesNotThrow(() => engine.BinSold(auction));

            notifi.Verify(n => n.Sold(sold, auction), Times.Once);
            notifi.Verify(n => n.AuctionOver(over, auction), Times.Once);
            // no PlayerBuy assertion: a subscription cannot be keyed by a null bidder, so none could ever match
        }

        /// <summary>
        /// HEAD threw an ArgumentNullException for a bid without bidder, which aborted the remaining notifications
        /// </summary>
        [Test]
        public void NewBidsSkipsBidWithoutBidder()
        {
            var (notifi, engine, _, over, _) = Setup();
            var high = new SaveBids() { Bidder = "high", Amount = 10 };
            var auction = Auction(new List<SaveBids>() { new SaveBids() { Bidder = null, Amount = 5 }, high });

            Assert.DoesNotThrow(() => engine.NewBids(auction));

            notifi.Verify(n => n.NewBid(over, auction, high), Times.Once);
        }

        [Test]
        public void AuctionOverTexts()
        {
            Assert.AreEqual("There were no bids", Auction(null).AuctionOverText());
            Assert.AreEqual("There were no bids", Auction(new List<SaveBids>()).AuctionOverText());
            var auction = Auction(new List<SaveBids>() { new SaveBids() { Bidder = "a", Amount = 7 } });
            auction.HighestBidAmount = 7;
            Assert.AreEqual("Highest bid is 7", auction.AuctionOverText());
        }

        /// <summary>
        /// Sold, Outbid, NewBid and PlayerBuy all return before sending when this is false
        /// </summary>
        [Test]
        public void HasBidsGuard()
        {
            Assert.IsFalse(Auction(null).HasBids());
            Assert.IsFalse(Auction(new List<SaveBids>()).HasBids());
            Assert.IsTrue(Auction(new List<SaveBids>() { new SaveBids() { Bidder = null, Amount = 1 } }).HasBids());
        }

        [Test]
        public void BidderLookupsHandleMissingBids()
        {
            Assert.IsNull(Auction(null).FirstBidder());
            Assert.IsNull(Auction(new List<SaveBids>()).FirstBidder());
            Assert.IsNull(Auction(null).HighestBidder());
            Assert.IsNull(Auction(new List<SaveBids>()).HighestBidder());
            var auction = Auction(new List<SaveBids>() { new SaveBids() { Bidder = "a", Amount = 1 }, new SaveBids() { Bidder = "b", Amount = 9 } });
            Assert.AreEqual("a", auction.FirstBidder());
            Assert.AreEqual("b", auction.HighestBidder());
        }

        private static string Resolve(string uuid)
        {
            Assert.IsNotNull(uuid, "resolver must never be called with null");
            return "name-" + uuid;
        }

        private static SaveAuction ThreeBids()
        {
            // listed order a, b, c; highest is b
            var auction = Auction(new List<SaveBids>()
            {
                new SaveBids() { Bidder = "a", Amount = 5 },
                new SaveBids() { Bidder = "b", Amount = 20 },
                new SaveBids() { Bidder = "c", Amount = 10 }
            });
            auction.ItemName = "Sword";
            auction.HighestBidAmount = 20;
            return auction;
        }

        [TestCase(true)]
        [TestCase(false)]
        public void NoBidsMeansNothingToSend(bool useNull)
        {
            var auction = Auction(useNull ? null : new List<SaveBids>());
            var bid = new SaveBids() { Bidder = "x", Amount = 1 };

            Assert.IsNull(NotificationTexts.Sold(auction, Resolve));
            Assert.IsNull(NotificationTexts.Outbid(auction, bid, Resolve));
            Assert.IsNull(NotificationTexts.NewBid(auction, Resolve));
            Assert.IsNull(NotificationTexts.PlayerBuy(auction, Resolve));
            Assert.IsNotNull(NotificationTexts.AuctionOver(auction));
            Assert.AreEqual("There were no bids", NotificationTexts.AuctionOver(auction).Text);
        }

        [Test]
        public void TextsWithBidsMatchOriginal()
        {
            var auction = ThreeBids();
            var bid = auction.Bids[0];

            Assert.AreEqual(new NotificationMessage("Item Sold", "Sword was sold to name-a for 20"), NotificationTexts.Sold(auction, Resolve));
            Assert.AreEqual(new NotificationMessage("New bid", "New bid on Sword by name-a for 20"), NotificationTexts.NewBid(auction, Resolve));
            Assert.AreEqual(new NotificationMessage("Outbid", "You were outbid on Sword by name-b by 15"), NotificationTexts.Outbid(auction, bid, Resolve));
            Assert.AreEqual(new NotificationMessage("Auction bought", "name-b bought Sword"), NotificationTexts.PlayerBuy(auction, Resolve));
            Assert.AreEqual(new NotificationMessage("Auction for Sword ended", "Highest bid is 20"), NotificationTexts.AuctionOver(auction));
        }

        [Test]
        public void HighestBidTieTakesFirstListed()
        {
            var auction = Auction(new List<SaveBids>()
            {
                new SaveBids() { Bidder = "low", Amount = 1 },
                new SaveBids() { Bidder = "first", Amount = 9 },
                new SaveBids() { Bidder = "second", Amount = 9 }
            });

            Assert.That(NotificationTexts.PlayerBuy(auction, Resolve).Text, Does.Contain("name-first"));
            Assert.That(NotificationTexts.Outbid(auction, auction.Bids[0], Resolve).Text, Does.Contain("name-first"));
        }

        [Test]
        public void NullBidderShowsUnknownWithoutResolving()
        {
            var auction = Auction(new List<SaveBids>() { new SaveBids() { Bidder = null, Amount = 5 } });
            auction.ItemName = "Sword";

            Assert.That(NotificationTexts.Sold(auction, Resolve).Text, Does.Contain("unknown"));
            Assert.That(NotificationTexts.NewBid(auction, Resolve).Text, Does.Contain("unknown"));
            Assert.That(NotificationTexts.Outbid(auction, auction.Bids[0], Resolve).Text, Does.Contain("unknown"));
            Assert.That(NotificationTexts.PlayerBuy(auction, Resolve).Text, Does.Contain("unknown"));
        }
    }
}
