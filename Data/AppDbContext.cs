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
        public DbSet<Contract> Contracts { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<EventLogView>()
                .HasNoKey()
        .ToFunction("fn_event_logs");

            // Map Store entity to dbo.stores with snake_case columns
            modelBuilder.Entity<Store>(entity =>
            {
                entity.ToTable("stores", "dbo", tb => tb.HasTrigger("TR_dbo_stores"));
                entity.HasKey(e => e.StoreId).HasName("PK_stores");
                entity.Property(e => e.StoreId).HasColumnName("store_id");
                entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(100);
                entity.Property(e => e.Address).HasColumnName("address").HasMaxLength(255);
                entity.Property(e => e.Phone_Number).HasColumnName("phone_number").HasMaxLength(20);
                entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(100);
                entity.Property(e => e.Working_Hours).HasColumnName("working_hours").HasMaxLength(255);
                entity.Property(e => e.Created_At).HasColumnName("created_at");
                entity.Property(e => e.Updated_At).HasColumnName("updated_at");
                entity.Property(e => e.Last_Modified_User_Id).HasColumnName("last_modified_user_id");
            });

            // Map Contract entity to dbo.contract with snake_case columns
            modelBuilder.Entity<Contract>(entity =>
            {
                entity.ToTable("contract", "dbo");
                entity.HasKey(e => e.Id).HasName("PK_contract");
                entity.Property(e => e.Id).HasColumnName("contract_id");
                entity.Property(e => e.EntryDate).HasColumnName("input_dt");
                entity.Property(e => e.InputUserId).HasColumnName("input_user_id");
                entity.Property(e => e.ContractState).HasColumnName("contract_state");
                entity.Property(e => e.LastModifiedById).HasColumnName("last_modified_by_id");
                entity.Property(e => e.LastUpdatedDt).HasColumnName("last_updated_dt");
                entity.Property(e => e.Stamp).HasColumnName("stamp");
                entity.Ignore(e => e.CustomerFullName);
            });
        }
    }
}