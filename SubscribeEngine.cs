using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Kafka;
using Coflnet.Sky.Subscriptions.Models;
using dev;
using Coflnet.Sky.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Subscriptions
{
    public class SubscribeEngine : BackgroundService
    {
        /// <summary>
        /// Subscriptions for being outbid
        /// </summary>
        private ConcurrentDictionary<string, ConcurrentBag<Subscription>> OutbidSubs = new ConcurrentDictionary<string, ConcurrentBag<Subscription>>();
        /// <summary>
        /// Subscriptions for ended auctions
        /// </summary>
        private ConcurrentDictionary<string, ConcurrentBag<Subscription>> SoldSubs = new ConcurrentDictionary<string, ConcurrentBag<Subscription>>();
        /// <summary>
        /// Subscriptions for new auction/bazaar prices
        /// </summary>
        private ConcurrentDictionary<string, ConcurrentBag<Subscription>> PriceUpdateSubs = new ConcurrentDictionary<string, ConcurrentBag<Subscription>>();
        /// <summary>
        /// All subscrptions to a specific auction
        /// </summary>
        private ConcurrentDictionary<string, ConcurrentBag<Subscription>> AuctionSub = new ConcurrentDictionary<string, ConcurrentBag<Subscription>>();
        private ConcurrentDictionary<string, ConcurrentBag<Subscription>> UserAuction = new ConcurrentDictionary<string, ConcurrentBag<Subscription>>();
        private ConcurrentDictionary<string, ConcurrentBag<Subscription>> UserBuy = new();
        private ConcurrentDictionary<string, (Subscription, SelfUpdatingValue<FlipSettings>)> FlipFilters = new ConcurrentDictionary<string, (Subscription, SelfUpdatingValue<FlipSettings>)>();

        private static Prometheus.Counter consumeCount = Prometheus.Metrics.CreateCounter("sky_subscriptions_consume", "The total amount of consumed messages");
        private static Prometheus.Counter auctionCount = Prometheus.Metrics.CreateCounter("sky_subscriptions_new_auction", "How many new auctions were consumed");
        private static Prometheus.Counter bidCount = Prometheus.Metrics.CreateCounter("sky_subscriptions_new_bid", "How many new bids were consumed");
        private static Prometheus.Counter bazaarCount = Prometheus.Metrics.CreateCounter("sky_subscriptions_bazaar", "How many bazaar updates were consumed");


        public static SubscribeEngine Instance { get; }
        private IConfiguration config;
        private ILogger<SubscribeEngine> logger;



        public SubscribeEngine(
                    IServiceScopeFactory scopeFactory,
                    INotificationService notificationService, IConfiguration config, ILogger<SubscribeEngine> logger)
        {
            this.scopeFactory = scopeFactory;
            this.NotificationService = notificationService;
            this.config = config;
            this.logger = logger;
        }

        public Task ProcessQueues(CancellationToken token)
        {
            var topics = new string[] {
                config["TOPICS:AUCTION_ENDED"],
                config["TOPICS:MISSING_AUCTION"],
                config["TOPICS:SOLD_AUCTION"] };
            Console.WriteLine("consuming");
            return Task.WhenAny(
            Task.Run(() => ProcessSubscription<SaveAuction>(topics, BinSold, token)),
            Task.Run(() => ProcessSubscription<SaveAuction>(new string[] { config["TOPICS:NEW_AUCTION"] }, NewAuction, token)),
            Task.Run(() => ProcessSubscription<BazaarPull>(new string[] { config["TOPICS:BAZAAR"] }, NewBazaar, token)),
            Task.Run(() => ProcessSubscription<SaveAuction>(new string[] { config["TOPICS:NEW_BID"] }, NewBids, token)));
        }

        private Task ProcessSubscription<T>(string[] topics, Action<T> handler, CancellationToken token)
        {
            return Kafka.KafkaConsumer.ConsumeBatch<T>(config, topics, (batch) =>
            {
                foreach (var item in batch)
                {
                    try
                    {
                        handler(item);
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "processing new " + topics[0]);
                    }
                }
                consumeCount.Inc(batch.Count());
                return Task.CompletedTask;
            }, token, "sky-sub-engine", 100, Confluent.Kafka.AutoOffsetReset.Latest);
        }

        public void AddNew(Subscription subscription)
        {
            AddSubscription(subscription);
        }

        public Task Unsubscribe(Subscription subs)
        {
            if (subs.Type.HasFlag(Subscription.SubType.PriceHigherThan) || subs.Type.HasFlag(Subscription.SubType.PriceLowerThan))
                RemoveSubscriptionFromCache(subs.UserId, subs.TopicId, subs.Type, PriceUpdateSubs);
            if (subs.Type.HasFlag(Subscription.SubType.SOLD))
                RemoveSubscriptionFromCache(subs.UserId, subs.TopicId, subs.Type, SoldSubs);
            if (subs.Type.HasFlag(Subscription.SubType.OUTBID))
                RemoveSubscriptionFromCache(subs.UserId, subs.TopicId, subs.Type, OutbidSubs);
            if (subs.Type.HasFlag(Subscription.SubType.AUCTION))
                RemoveSubscriptionFromCache(subs.UserId, subs.TopicId, subs.Type, AuctionSub);
            return Task.CompletedTask;
        }

        private static void RemoveSubscriptionFromCache(int userId, string topic, Subscription.SubType type, ConcurrentDictionary<string, ConcurrentBag<Subscription>> target)
        {
            if (target.Remove(topic, out ConcurrentBag<Subscription> value))
                target.AddOrUpdate(topic, (key) => RemoveOldElement(userId, topic, type, value),
                (key, old) => RemoveOldElement(userId, topic, type, old));
        }

        private static ConcurrentBag<Subscription> RemoveOldElement(int userId, string topic, Subscription.SubType type, ConcurrentBag<Subscription> old)
        {
            return new ConcurrentBag<Subscription>(old.Where(s => !SubscriptionEqual(userId, topic, type, s)));
        }

        private static bool SubscriptionEqual(int userId, string topic, Subscription.SubType type, Subscription s)
        {
            return (s.UserId == userId && s.TopicId == topic && s.Type == type);
        }

        public async Task LoadFromDb()
        {
            using var scope = scopeFactory.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<SubsDbContext>();

            var minTime = DateTime.Now.Subtract(TimeSpan.FromDays(200));
            var all = context.Subscriptions.Where(si => si.GeneratedAt > minTime).Include(s => s.User);
            foreach (var item in all)
            {
                AddSubscription(item);
            }
            Console.WriteLine($"Loaded {all.Count()} subscriptions");
            Console.WriteLine($"{PriceUpdateSubs.Count} price alerts, {UserAuction.Count} user alerts");
        }

        private void AddSubscription(Subscription item)
        {
            if (item.Type.HasFlag(Subscription.SubType.Buy))
                AddSubscription(item, UserBuy);

            if (item.Type.HasFlag(Subscription.SubType.OUTBID))
            {
                AddSubscription(item, OutbidSubs);
            }
            else if (item.Type.HasFlag(Subscription.SubType.SOLD))
            {
                AddSubscription(item, SoldSubs);
            }
            else if (item.Type.HasFlag(Subscription.SubType.PriceLowerThan) || item.Type.HasFlag(Subscription.SubType.PriceHigherThan))
            {
                AddSubscription(item, PriceUpdateSubs);
            }
            else if (item.Type.HasFlag(Subscription.SubType.AUCTION))
            {
                AddSubscription(item, AuctionSub);
            }
            else if (item.Type.HasFlag(Subscription.SubType.PLAYER))
            {
                AddSubscription(item, UserAuction);
            }
            else if (item.Type.HasFlag(Subscription.SubType.FILTER))
            {
                Task.Run(async () =>
                {
                    for (int i = 0; i < 3; i++)
                        try
                        {
                            var filter = await SelfUpdatingValue<FlipSettings>.Create(item.User.ExternalId, "flipSettings");
                            filter.OnChange += (f) =>
                            {
                                f.CopyListMatchers(filter);
                                f.AllowedFinders |= LowPricedAuction.FinderType.USER;
                            };
                            // always enable user finder for notifications
                            filter.Value.AllowedFinders |= LowPricedAuction.FinderType.USER;
                            FlipFilters[item.UserId.ToString()] = (item, filter);
                            logger.LogInformation("Loaded flip filter for " + item.User.ExternalId);
                            return;
                        }
                        catch (Exception e)
                        {
                            logger.LogError(e, "Could not load flip filter for " + item.User.ExternalId);
                            await Task.Delay((1 + i) * TimeSpan.FromSeconds(5));
                        }
                });
            }
            else
                Console.WriteLine("ERROR: unkown subscibe type " + item.Type);
        }

        private static void AddSubscription(Subscription item, ConcurrentDictionary<string, ConcurrentBag<Subscription>> target)
        {
            var itemId = item.TopicId;
            var priceChange = target.GetOrAdd(itemId, itemId => new ConcurrentBag<Subscription>());
            priceChange.Add(item);
        }

        internal void NewBazaar(BazaarPull pull)
        {
            foreach (var item in pull.Products)
            {
                PriceState(item);
            }
            bazaarCount.Inc();
        }

        /// <summary>
        /// Called from <see cref="Updater"/>
        /// </summary>
        /// <param name="auction"></param>
        public void NewAuction(SaveAuction auction)
        {
            if (auction.Start < DateTime.Now - TimeSpan.FromHours(2))
                return; // to old
            if (this.PriceUpdateSubs.TryGetValue(auction.Tag, out ConcurrentBag<Subscription> subscribers))
            {
                foreach (var item in subscribers)
                {
                    var isLower = auction.StartingBid < item.Price && item.Type.HasFlag(Subscription.SubType.PriceLowerThan);
                    var isHigher = auction.StartingBid > item.Price && item.Type.HasFlag(Subscription.SubType.PriceHigherThan);
                    var isBinIfRequired = !item.Type.HasFlag(Subscription.SubType.BIN) || auction.Bin;
                    if ((isLower || isHigher) && isBinIfRequired)
                        NotificationService.AuctionPriceAlert(item, auction);
                }
            }
            if (this.UserAuction.TryGetValue(auction.AuctioneerId, out subscribers))
            {
                foreach (var item in subscribers)
                {
                    NotificationService.NewAuction(item, auction);
                }
            }
            foreach (var item in FlipFilters)
            {
                var flip = FlipperService.LowPriceToFlip(new LowPricedAuction()
                {
                    Auction = auction,
                    DailyVolume = 0,
                    Finder = LowPricedAuction.FinderType.USER,
                    TargetPrice = auction.StartingBid,
                    AdditionalProps = new Dictionary<string, string>()
                });
                var matches = item.Value.Item2.Value.MatchesSettings(flip);
                if (matches.Item1 && matches.Item2.StartsWith("white"))
                    NotificationService.WhitelistedFlip(item.Value.Item1, flip, item.Value.Item2);
            }
            auctionCount.Inc();
        }

        /// <summary>
        /// Called from <see cref="BinUpdater"/>
        /// </summary>
        /// <param name="auction"></param>
        public void BinSold(SaveAuction auction)
        {
            var key = auction.AuctioneerId;
            NotifyIfExisting(this.SoldSubs, key, sub =>
            {
                NotificationService.Sold(sub, auction);
            });
            NotifyIfExisting(this.AuctionSub, auction.Uuid, sub =>
            {
                NotificationService.AuctionOver(sub, auction);
            });
            NotifyIfExisting(UserBuy, auction.Bids.OrderByDescending(s => s.Amount).FirstOrDefault()?.Bidder ?? "None", sub =>
            {
                NotificationService.PlayerBuy(sub, auction);
            });
        }

        private void NotifyIfExisting(ConcurrentDictionary<string, ConcurrentBag<Subscription>> target, string key, Action<Subscription> todo)
        {
            if (target.TryGetValue(key, out ConcurrentBag<Subscription> subscribers))
            {
                foreach (var item in subscribers)
                {
                    todo(item);
                }
            }
        }

        /// <summary>
        /// Called from the <see cref="Indexer"/>
        /// </summary>
        /// <param name="auction"></param>
        public void NewBids(SaveAuction auction)
        {
            foreach (var bid in auction.Bids.OrderByDescending(b => b.Amount).Skip(1))
            {
                NotifyIfExisting(this.OutbidSubs, bid.Bidder, sub =>
                {
                    NotificationService.Outbid(sub, auction, bid);
                });
            }
            NotifyIfExisting(this.AuctionSub, auction.Uuid, sub =>
            {
                NotificationService.NewBid(sub, auction, auction.Bids.OrderBy(b => b.Amount).Last());
            });
            foreach (var bid in auction.Bids)
            {
                NotifyIfExisting(UserAuction, bid.Bidder, sub =>
                {
                    NotificationService.NewBid(sub, auction, bid);
                });
            }
            bidCount.Inc();
        }



        /// <summary>
        /// Called from <see cref="BazaarIndexer"/>
        /// </summary>
        /// <param name="info"></param>
        public void PriceState(ProductInfo info)
        {
            if (this.PriceUpdateSubs.TryGetValue(info.ProductId, out ConcurrentBag<Subscription> subscribers))
            {
                foreach (var item in subscribers)
                {
                    if (item.NotTriggerAgainBefore > DateTime.Now)
                        continue;
                    var value = info.QuickStatus.BuyPrice;
                    if (item.Type.HasFlag(Subscription.SubType.UseSellNotBuy))
                        value = info.QuickStatus.SellPrice;
                    if (value < item.Price && item.Type.HasFlag(Subscription.SubType.PriceLowerThan)
                         || value > item.Price && item.Type.HasFlag(Subscription.SubType.PriceHigherThan))
                    {
                        if (item.NotTriggerAgainBefore < DateTime.Now)
                            return;
                        item.NotTriggerAgainBefore = DateTime.Now + TimeSpan.FromHours(1);
                        logger.LogInformation("Price alert for " + item.TopicId + " " + value + " " + item.Price);
                        NotificationService.PriceAlert(item, info.ProductId, value);
                    }
                }
            }
        }


        private ConcurrentDictionary<string, List<SubLookup>> OnlineSubscriptions = new ConcurrentDictionary<string, List<SubLookup>>();
        private ConcurrentQueue<UnSub> ToUnsubscribe = new ConcurrentQueue<UnSub>();

        public int SubCount => OutbidSubs.Count + SoldSubs.Count + PriceUpdateSubs.Count;

        public static TimeSpan BazzarNotificationBackoff = TimeSpan.FromHours(1);
        private IServiceScopeFactory scopeFactory;

        public INotificationService NotificationService { get; }

        public void NotifyChange(string topic, SaveAuction auction)
        {
            GenericNotifyAll(topic, "updateAuction", auction);
        }

        public void NotifyChange(string topic, ProductInfo bazzarUpdate)
        {
            GenericNotifyAll(topic, "bazzarUpdate", bazzarUpdate);
        }

        private void GenericNotifyAll<T>(string topic, string commandType, T data)
        {
            if (OnlineSubscriptions.TryGetValue(topic.Truncate(32), out List<SubLookup> value))
                foreach (var sub in value)
                {
                    var resultJson = JsonConvert.SerializeObject(data);
                    //if (!SkyblockBackEnd.SendTo(new MessageData(commandType, resultJson), sub.Id))
                    // could not be reached, unsubscribe
                    ToUnsubscribe.Enqueue(new UnSub(topic, sub.Id));
                }
        }

        public void Subscribe(string topic, int userId)
        {
            if (userId == 0)
                throw new CoflnetException("id_not_set", "There is no `id` set on this connection. To Subscribe you need to pass a random generated id (32 char long) via get parameter (/skyblock?id=uuid) or cookie id");
            var lookup = new SubLookup(userId);
            OnlineSubscriptions.AddOrUpdate(topic.Truncate(32),
            new List<SubLookup>() { lookup },
            (key, list) =>
            {
                RemoveFirstIfExpired(list);
                list.Add(lookup);
                return list;
            });
        }

        public void Unsubscribe(string topic, int userId)
        {
            ToUnsubscribe.Enqueue(new UnSub(topic, userId));
            // unsubscribe stale elements
            while (ToUnsubscribe.TryDequeue(out UnSub result))
            {
                lock (result.Topic)
                {
                    if (OnlineSubscriptions.TryGetValue(result.Topic.Truncate(32), out List<SubLookup> value))
                    {
                        var item = value.Where(v => v.Id == result.id).FirstOrDefault();
                        if (item.Id != 0)
                            value.Remove(item);
                    }
                }
            }
        }

        private static void RemoveFirstIfExpired(List<SubLookup> list)
        {
            if (list.Count > 0 && list.First().SubTime < DateTime.Now - TimeSpan.FromHours(1))
                list.RemoveAt(0);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using (var scope = scopeFactory.CreateScope())
            using (var context = scope.ServiceProvider.GetRequiredService<SubsDbContext>())
            {
                // make sure all migrations are applied
                await context.Database.MigrateAsync();
                logger.LogInformation("Database migrated");
            }
            await LoadFromDb();
            await ProcessQueues(stoppingToken);
        }

        private struct SubLookup
        {
            public DateTime SubTime;
            public long Id;

            public SubLookup(long id)
            {
                SubTime = DateTime.Now;
                Id = id;
            }
        }

        public class UnSub
        {
            public string Topic;
            public long id;

            public UnSub(string topic, long id)
            {
                Topic = topic;
                this.id = id;
            }
        }
    }
}