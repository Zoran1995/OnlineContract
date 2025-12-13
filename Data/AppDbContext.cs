using Microsoft.EntityFrameworkCore;
using OnlineContract.Models;

namespace OnlineContract.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<AxUser> AxUsers { get; set; }
        public DbSet<EventLog> EventLogs { get; set; }
        public DbSet<EventLogView> EventLogViews { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<EventLogView>()
                .HasNoKey()
        .ToFunction("fn_event_logs");
        }
    }
}