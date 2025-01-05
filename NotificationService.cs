using System;
using System.Net.Http;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using RestSharp;
using Coflnet.Sky.Subscriptions.Models;
using Coflnet.Sky.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Coflnet.Sky.Filter;
using Microsoft.Extensions.Logging;
using Confluent.Kafka;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Subscriptions
{
    public interface INotificationService
    {
        void AuctionOver(Subscription sub, SaveAuction auction);
        void AuctionPriceAlert(Subscription sub, SaveAuction auction);
        Task NewAuction(Subscription sub, SaveAuction auction);
        Task WhitelistedFlip(Subscription sub, FlipInstance auction, FlipSettings flipSettings);
        void NewBid(Subscription sub, SaveAuction auction, SaveBids bid);
        void Outbid(Subscription sub, SaveAuction auction, SaveBids bid);
        void PriceAlert(Subscription sub, string productId, double value);
        void Sold(Subscription sub, SaveAuction auction);
        Task<bool> TryNotifyAsync(string to, NotificationService.Notification notification);
        void PlayerBuy(Subscription sub, SaveAuction auction);
    }

    /// <summary>
    /// Sends firebase push notifications
    /// </summary>
    public partial class NotificationService : INotificationService
    {
        public static string BaseUrl = "https://sky.coflnet.com";
        public static string ItemIconsBase = "https://sky.shiiyu.moe/item";
        static string firebaseKey = SimplerConfig.Config.Instance["FIREBASE_KEY"];
        static string firebaseSenderId = SimplerConfig.Config.Instance["FIREBASE_SENDER_ID"];
        static FilterEngine filterEngine = new FilterEngine();
        private ILogger<NotificationService> logger;
        private IProducer<string, string> producer;
        private IConfiguration config;

        public NotificationService(
                    IServiceScopeFactory scopeFactory,
                    IConfiguration config, ILogger<NotificationService> logger,
                    Kafka.KafkaCreator kafkaCreator)
        {
            this.scopeFactory = scopeFactory;
            this.logger = logger;
            this.config = config;
            _ = kafkaCreator.CreateTopicIfNotExist(config["TOPICS:NOTIFICATIONS"]);
            producer = kafkaCreator.BuildProducer<string, string>(false);
        }


        DoubleNotificationPreventer doubleChecker = new DoubleNotificationPreventer();
        private IServiceScopeFactory scopeFactory;

        internal async Task Send(Subscription sub, string title, string text, string url, string icon, Dictionary<string, string> data = null)
        {
            var userId = sub.UserId;
            var not = new Notification(title, text, url, icon, null, data);
            if (!doubleChecker.HasNeverBeenSeen(userId, not))
                return;

            try
            {
                using var scope = scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<SubsDbContext>();
                var user = await context.Users.Where(u => u.Id == userId).Include(u => u.Devices).FirstOrDefaultAsync();
                var devices = user.Devices.ToList();
                if (devices.Count > 0)
                    logger.LogInformation($"Sedning Notification to {userId}: {title}\n{text} {url}");
                foreach (var item in devices)
                {
                    Console.WriteLine("sending to " + item.UserId);
                    var success = await TryNotifyAsync(item.Token, not);
                    if (success)
                    {
                        // store that was sent Notification
                        continue;
                    }
                    dev.Logger.Instance.Error("Sending pushnotification failed to");
                    dev.Logger.Instance.Error(JsonConvert.SerializeObject(item));
                    context.Remove(item);
                }
                await context.SaveChangesAsync();
                if(data == null)
                    data = [];
                not.data["userId"] = user.ExternalId.ToString();
                not.data["subId"] = sub.Id.ToString();
                if (not.title.Contains("whitelisted"))
                    logger.LogInformation($"{sub.UserId}: {not.title} {not.body}");
                await producer.ProduceAsync(config["TOPICS:NOTIFICATIONS"], new Message<string, string>
                {
                    Key = userId.ToString(),
                    Value = JsonConvert.SerializeObject(not)
                });

            }
            catch (Exception e)
            {
                dev.Logger.Instance.Error(e, $"Could not send {not.body} to {userId}");
            }

        }

        /// <summary>
        /// Attempts to send a notification
        /// </summary>
        /// <param name="to"></param>
        /// <param name="notification"></param>
        /// <returns><c>true</c> when the notification was sent successfully</returns>
        public async Task<bool> TryNotifyAsync(string to, Notification notification)
        {
            try
            {
                // Get the server key from FCM console
                var serverKey = string.Format("key={0}", firebaseKey);

                // Get the sender id from FCM console
                var senderId = string.Format("id={0}", firebaseSenderId);

                //var icon = "https://sky.coflnet.com/logo192.png";
                var data = notification.data;
                var payload = new
                {
                    to, // Recipient device token
                    notification,
                    data
                };

                // Using Newtonsoft.Json
                var jsonBody = JsonConvert.SerializeObject(payload);
                var client = new RestClient("https://fcm.googleapis.com");
                var request = new RestRequest("fcm/send", Method.Post);

                request.AddHeader("Authorization", serverKey);
                request.AddHeader("Sender", senderId); request.AddHeader("Content-Type", "application/json");
                request.AddParameter("application/json", jsonBody, ParameterType.RequestBody);
                var response = await client.ExecuteAsync(request);


                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    Console.WriteLine(JsonConvert.SerializeObject(response.Content));
                }

                dynamic res = JsonConvert.DeserializeObject(response.Content);
                var success = res.success == 1;
                if (!success)
                    dev.Logger.Instance.Error(response.Content);

                return success;
            }
            catch (Exception ex)
            {
                dev.Logger.Instance.Error($"Exception thrown in Notify Service: {ex.Message} {ex.StackTrace}");
            }
            Console.WriteLine("done");

            return false;
        }

        public void Sold(Subscription sub, SaveAuction auction)
        {
            var text = $"{auction.ItemName} was sold to {PlayerSearch.Instance.GetNameWithCache(auction.Bids.FirstOrDefault().Bidder)} for {auction.HighestBidAmount}";
            Task.Run(() => Send(sub, "Item Sold", text, AuctionUrl(auction), ItemIconUrl(auction.Tag), FormatAuction(auction))).ConfigureAwait(false);
        }

        public void Outbid(Subscription sub, SaveAuction auction, SaveBids bid)
        {
            var outBidBy = auction.HighestBidAmount - bid.Amount;
            var text = $"You were outbid on {auction.ItemName} by {PlayerSearch.Instance.GetNameWithCache(auction.Bids.OrderByDescending(b => b.Amount).FirstOrDefault().Bidder)} by {outBidBy}";
            if (sub.NotTriggerAgainBefore > DateTime.UtcNow)
                return;
            sub.NotTriggerAgainBefore = DateTime.UtcNow.AddMinutes(1);
            Task.Run(() => Send(sub, "Outbid", text, AuctionUrl(auction), ItemIconUrl(auction.Tag), FormatAuction(auction))).ConfigureAwait(false);
        }

        public void NewBid(Subscription sub, SaveAuction auction, SaveBids bid)
        {
            var text = $"New bid on {auction.ItemName} by {PlayerSearch.Instance.GetNameWithCache(auction.Bids.FirstOrDefault().Bidder)} for {auction.HighestBidAmount}";
            Task.Run(() => Send(sub, "New bid", text, AuctionUrl(auction), ItemIconUrl(auction.Tag), FormatAuction(auction))).ConfigureAwait(false);
        }

        public void AuctionOver(Subscription sub, SaveAuction auction)
        {
            var text = $"Highest bid is {auction.HighestBidAmount}";
            Task.Run(() => Send(sub, $"Auction for {auction.ItemName} ended", text, AuctionUrl(auction), ItemIconUrl(auction.Tag), FormatAuction(auction))).ConfigureAwait(false);
        }

        public void PriceAlert(Subscription sub, string productId, double value)
        {
            var text = $"{ItemDetails.TagToName(productId)} reached {value.ToString("0.00")}";
            Task.Run(() => Send(sub, $"Price Alert", text, $"{BaseUrl}/item/{productId}", ItemIconUrl(productId))).ConfigureAwait(false);
        }

        public void AuctionPriceAlert(Subscription sub, SaveAuction auction)
        {
            if (!Matches(auction, sub))
                return;
            var text = $"New Auction for {auction.ItemName} for {Format(auction.StartingBid)} coins";
            Task.Run(() => Send(sub, $"Price Alert", text, AuctionUrl(auction), ItemIconUrl(auction.Tag), FormatAuction(auction))).ConfigureAwait(false);
        }


        public Task NewAuction(Subscription sub, SaveAuction auction)
        {
            if (!Matches(auction, sub))
                return Task.CompletedTask;
            return Send(sub, $"New auction", $"{PlayerSearch.Instance.GetNameWithCache(auction.AuctioneerId)} created a new auction", AuctionUrl(auction), ItemIconUrl(auction.Tag), FormatAuction(auction));
        }

        private Dictionary<string, string> FormatAuction(SaveAuction auction)
        {
            return new() { { "type", "auction" }, { "auction", JsonConvert.SerializeObject(auction) } };
        }

        string AuctionUrl(SaveAuction auction)
        {
            return BaseUrl + "/auction/" + auction.Uuid;
        }
        private bool Matches(SaveAuction auction, Subscription sub)
        {
            if (string.IsNullOrEmpty(sub.Filter))
                return true;
            try
            {
                if (sub.matcherCache == null)
                {
                    var filters = JsonConvert.DeserializeObject<Dictionary<string, string>>(sub.Filter);
                    sub.matcherCache = filterEngine.GetMatcher(filters);
                }
                return sub.matcherCache(auction);
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not match filter {sub.Filter} on {JsonConvert.SerializeObject(auction)} retrying with nbt");
                auction.NBTLookup = NBT.CreateLookup(auction);
                var filters = JsonConvert.DeserializeObject<Dictionary<string, string>>(sub.Filter);
                return filterEngine.GetMatcher(filters)(auction);
            }
        }

        static string ItemIconUrl(string tag)
        {
            return ItemIconsBase + $"/{tag}";
        }

        public Task WhitelistedFlip(Subscription sub, FlipInstance flip, FlipSettings flipSettings)
        {
            var matched = WhichMatches(flip, flipSettings);
            var auction = flip.Auction;
            var message = $"`{PlayerSearch.Instance.GetNameWithCache(auction.AuctioneerId)}` listed it for `{Format(auction.StartingBid)}` coins";
            if (matched != null)
            {
                message += $"\n\nIt matched {FormatEntry(matched)}";
            }
            message += $"\n`/viewauction {auction.Uuid}`";
            var data = FormatAuction(auction);
            data["whitelist"] = JsonConvert.SerializeObject(matched);
            return Send(sub, $"New whitelisted auction for {auction.ItemName}", message, AuctionUrl(auction), ItemIconUrl(auction.Tag), data);
        }

        private ListEntry WhichMatches(FlipInstance flip, FlipSettings flipSettings)
        {
            foreach (var item in flipSettings.WhiteList)
            {
                if (item.MatchesSettings(flip, new EmptyPlayerInfo()))
                    return item;
            }
            return null;
        }

        private string Format(long number)
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:n0}", number); ;
        }

        public static string FormatEntry(ListEntry elem)
        {
            return $"{elem.DisplayName ?? elem.ItemTag} {(elem.filter == null ? "" : string.Join(" & ", elem.filter.Select(f => $"{f.Key}=`{(f.Value.Length > 50 ? f.Value[..58] + "..." : f.Value)}`")))}";
        }

        public void PlayerBuy(Subscription sub, SaveAuction auction)
        {
            if (!Matches(auction, sub))
                return;
            Task.Run(() => Send(sub, $"Auction bought", $"{PlayerSearch.Instance.GetNameWithCache(auction.Bids.OrderByDescending(b => b.Amount).FirstOrDefault().Bidder)} bought {auction.ItemName}", AuctionUrl(auction), ItemIconUrl(auction.Tag), FormatAuction(auction)));
        }
    }

    public class EmptyPlayerInfo : IPlayerInfo
    {
        public long Purse { get => 0; set => throw new NotImplementedException(); }
        public long AhSlotsOpen { get => -1; set => throw new NotImplementedException(); }
        public AccountTier SessionTier { get => AccountTier.NONE; set => throw new NotImplementedException(); }
        public string McName { get => "null"; set => throw new NotImplementedException(); }
        public string McUuid { get => Guid.Empty.ToString(); set => throw new NotImplementedException(); }
    }
}