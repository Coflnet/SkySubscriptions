using Microsoft.EntityFrameworkCore;

namespace Coflnet.Sky.Subscriptions.Models
{
    /// <summary>
    /// <see cref="DbContext"/> fro Payments
    /// </summary>
    public class SubsDbContext : DbContext
    {
        /// <summary>
        /// Users having subscriptions
        /// </summary>
        /// <value></value>
        public DbSet<User> Users { get; set; }
        /// <summary>
        /// Subscriptions created by <see cref="Users"/>
        /// </summary>
        /// <value></value>
        public DbSet<Subscription> Subscriptions { get; set; }

        /// <summary>
        /// Creates a new instance of <see cref="SubsDbContext"/>
        /// </summary>
        /// <param name="options"></param>
        public SubsDbContext(DbContextOptions<SubsDbContext> options)
        : base(options)
        {
        }

        /// <summary>
        /// Configures additional relations and indexes
        /// </summary>
        /// <param name="modelBuilder"></param>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>(entity =>
            {
                entity.HasIndex(e => e.ExternalId).IsUnique();
            });

            modelBuilder.Entity<Device>(entity =>
            {
                entity.HasIndex(e => new { e.UserId, e.Name }).IsUnique();
            });
        }
    }
}