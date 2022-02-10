using System;
using System.Net.Http;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using RestSharp;
using Coflnet.Sky.Subscriptions.Models;
using hypixel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Coflnet.Sky.Filter;

namespace Coflnet.Sky.Subscriptions
{
    /// <summary>
    /// Sends firebase push notifications
    /// </summary>
    public partial class NotificationService
    {
        public static string BaseUrl = "https://sky.coflnet.com";
        public static string ItemIconsBase = "https://sky.shiiyu.moe/item";
        static string firebaseKey = SimplerConfig.Config.Instance["FIREBASE_KEY"];
        static string firebaseSenderId = SimplerConfig.Config.Instance["FIREBASE_SENDER_ID"];
        static FilterEngine filterEngine = new FilterEngine();

        public NotificationService(
                    IServiceScopeFactory scopeFactory,
                    IConfiguration config)
        {
            this.scopeFactory = scopeFactory;
        }


        DoubleNotificationPreventer doubleChecker = new DoubleNotificationPreventer();
        private IServiceScopeFactory scopeFactory;

        internal async Task Send(int userId, string title, string text, string url, string icon, object data = null)
        {
            var not = new Notification(title, text, url, icon, null, data);
            if (!doubleChecker.HasNeverBeenSeen(userId, not))
                return;

            try
            {

                using var scope = scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<SubsDbContext>();
                var devices = context.Users.Where(u => u.Id == userId).SelectMany(u => u.Devices);
                foreach (var item in devices)
                {
                    Console.WriteLine("sending to " + item.UserId);
                    var success = await TryNotifyAsync(item.Token, not);
                    if (success)
                    {
                        // store that was sent Notification
                        return;
                    }
                    dev.Logger.Instance.Error("Sending pushnotification failed to");
                    dev.Logger.Instance.Error(JsonConvert.SerializeObject(item));
                    context.Remove(item);
                }
                await context.SaveChangesAsync();

            }
            catch (Exception)
            {
                dev.Logger.Instance.Error($"Could not send {not.body} to {userId}");
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

                /*       var client = new RestClient();
                       var request = new RestRequest("https://fcm.googleapis.com/fcm/send", Method.POST);
                       Console.WriteLine("y");
                       request.AddHeader("Authorization", serverKey);
                       request.AddHeader("Sender", senderId);
                       request.AddJsonBody(payload);
                       Console.WriteLine(jsonBody);
                       Console.WriteLine(serverKey);
                      //Console.WriteLine(JsonConvert.SerializeObject(request));

                       var response = await client.ExecuteAsync(request);*/

                var client = new RestClient("https://fcm.googleapis.com/fcm/send");
                client.Timeout = -1;
                var request = new RestRequest(Method.POST);

                request.AddHeader("Authorization", serverKey);
                request.AddHeader("Sender", senderId); request.AddHeader("Content-Type", "application/json");
                request.AddParameter("application/json", jsonBody, ParameterType.RequestBody);
                IRestResponse response = await client.ExecuteAsync(request);


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

        internal void Sold(Subscription sub, SaveAuction auction)
        {
            var text = $"{auction.ItemName} was sold to {PlayerSearch.Instance.GetNameWithCache(auction.Bids.FirstOrDefault().Bidder)} for {auction.HighestBidAmount}";
            Task.Run(() => Send(sub.UserId, "Item Sold", text, AuctionUrl(auction), ItemIconUrl(auction.Tag), FormatAuction(auction))).ConfigureAwait(false);
        }

        public void Outbid(Subscription sub, SaveAuction auction, SaveBids bid)
        {
            var outBidBy = auction.HighestBidAmount - bid.Amount;
            var text = $"You were outbid on {auction.ItemName} by {PlayerSearch.Instance.GetNameWithCache(auction.Bids.FirstOrDefault().Bidder)} by {outBidBy}";
            Task.Run(() => Send(sub.UserId, "Outbid", text, AuctionUrl(auction), ItemIconUrl(auction.Tag), FormatAuction(auction))).ConfigureAwait(false);
        }

        public void NewBid(Subscription sub, SaveAuction auction, SaveBids bid)
        {
            var text = $"New bid on {auction.ItemName} by {PlayerSearch.Instance.GetNameWithCache(auction.Bids.FirstOrDefault().Bidder)} for {auction.HighestBidAmount}";
            Task.Run(() => Send(sub.UserId, "New bid", text, AuctionUrl(auction), ItemIconUrl(auction.Tag), auction)).ConfigureAwait(false);
        }

        internal void AuctionOver(Subscription sub, SaveAuction auction)
        {
            var text = $"Highest bid is {auction.HighestBidAmount}";
            Task.Run(() => Send(sub.UserId, $"Auction for {auction.ItemName} ended", text, AuctionUrl(auction), ItemIconUrl(auction.Tag), FormatAuction(auction))).ConfigureAwait(false);
        }

        internal void PriceAlert(Subscription sub, string productId, double value)
        {
            var text = $"{ItemDetails.TagToName(productId)} reached {value.ToString("0.00")}";
            Task.Run(() => Send(sub.UserId, $"Price Alert", text, $"{BaseUrl}/item/{productId}", ItemIconUrl(productId))).ConfigureAwait(false);
        }

        internal void AuctionPriceAlert(Subscription sub, SaveAuction auction)
        {
            if (!Matches(auction, sub.Filter))
                return;
            var text = $"New Auction for {auction.ItemName} for {String.Format("{0:n0}", auction.StartingBid)} coins";
            Task.Run(() => Send(sub.UserId, $"Price Alert", text, AuctionUrl(auction), ItemIconUrl(auction.Tag), FormatAuction(auction))).ConfigureAwait(false);
        }


        internal Task NewAuction(Subscription sub, SaveAuction auction)
        {
            if (!Matches(auction, sub.Filter))
                return Task.CompletedTask;
            return Send(sub.UserId, $"New auction", $"{PlayerSearch.Instance.GetNameWithCache(auction.AuctioneerId)} created a new auction", AuctionUrl(auction), ItemIconUrl(auction.Tag), FormatAuction(auction));
        }

        private object FormatAuction(SaveAuction auction)
        {
            return new { type = "auction", auction = JsonConvert.SerializeObject(auction) };
        }

        string AuctionUrl(SaveAuction auction)
        {
            return BaseUrl + "/auction/" + auction.Uuid;
        }
        private bool Matches(SaveAuction auction, string filter)
        {
            if (string.IsNullOrEmpty(filter))
                return true;
            var filters = JsonConvert.DeserializeObject<Dictionary<string, string>>(filter);
            return filterEngine.GetMatcher(filters)(auction);
        }

        string ItemIconUrl(string tag)
        {
            return ItemIconsBase + $"/{tag}";
        }
    }
}