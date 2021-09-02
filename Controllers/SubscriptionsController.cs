using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Subscriptions.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Subscriptions.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class SubscriptionController : ControllerBase
    {
        private readonly ILogger<SubscriptionController> _logger;
        private readonly SubsDbContext db;
        private readonly SubscribeEngine subEngine;

        public SubscriptionController(ILogger<SubscriptionController> logger, 
            SubsDbContext context,
            SubscribeEngine engine)
        {
            _logger = logger;
            db = context;
            subEngine = engine;
        }

        [HttpGet]
        [Route("{userId}")]
        public async Task<User> GetOrCreate(string userId)
        {
            var user = await db.Users.Where(u => u.ExternalId == userId).Include(u => u.Subscriptions).Include(u => u.Devices).FirstOrDefaultAsync();
            if (user == null)
            {
                user = new User() { ExternalId = userId };
                db.Users.Add(user);
                await db.SaveChangesAsync();
            }
            return user;
        }

        [HttpPut]
        [Route("{userId}/device")]
        public async Task<IEnumerable<Device>> AddOrUpdateDevice(string userId, [FromBody] Device device)
        {
            var user = await GetOrCreate(userId);
            var targetDevice = user.Devices.Where(d => d.Name == device.Name).FirstOrDefault();
            if (targetDevice != null)
            {
                targetDevice.Token = device.Token;
            }
            else
                user.Devices.Add(device);
            await db.SaveChangesAsync();

            return user.Devices;
        }

        /// <summary>
        /// Adds a new subscription
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="subscription"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("{userId}/sub")]
        public async Task<Subscription> AddSubscription(string userId, [FromBody] Subscription subscription)
        {
            var user = await GetOrCreate(userId);
            user.Subscriptions.Add(subscription);
            await db.SaveChangesAsync();
            this.subEngine.AddNew(subscription);

            return subscription;
        }

        /// <summary>
        /// Remove a subscription
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="subscription"></param>
        /// <returns></returns>
        [HttpDelete]
        [Route("{userId}/sub")]
        public async Task<Subscription> RemoveSubscription(string userId, [FromBody] Subscription subscription)
        {
            var user = await GetOrCreate(userId);
            var sub = user.Subscriptions.Where(s=>s.Price == subscription.Price && s.Type == subscription.Type && s.TopicId == subscription.TopicId).FirstOrDefault();
            if(sub == null)
                return subscription;
            db.Subscriptions.Remove(sub);
            db.Update(user);
            await db.SaveChangesAsync();
            await subEngine.Unsubscribe(sub);

            return subscription;
        }
    }
}
