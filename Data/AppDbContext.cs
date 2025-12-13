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
        public DbSet<Store> Stores { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<EventLogView>()
                .HasNoKey()
        .ToFunction("fn_event_logs");

            // Map Store entity to dbo.stores with snake_case columns
            modelBuilder.Entity<Store>(entity =>
            {
                entity.ToTable("stores", "dbo");
                entity.HasKey(e => e.StoreId).HasName("PK_stores");
                entity.Property(e => e.StoreId).HasColumnName("store_id");
                entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(100);
                entity.Property(e => e.Address).HasColumnName("address").HasMaxLength(255);
                entity.Property(e => e.Phone_Number).HasColumnName("phone_number").HasMaxLength(20);
                entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(100);
                entity.Property(e => e.Working_Hours).HasColumnName("working_hours").HasMaxLength(255);
                entity.Property(e => e.Created_At).HasColumnName("created_at");
                entity.Property(e => e.Updated_At).HasColumnName("updated_at");
            });
        }
    }
}