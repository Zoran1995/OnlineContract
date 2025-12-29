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
        public DbSet<Product> Products { get; set; }
        public DbSet<ProductVariant> ProductVariants { get; set; }
        public DbSet<ProductInventory> ProductInventories { get; set; }
        public DbSet<Note> Notes { get; set; }
        public DbSet<OnlineContract.Models.PasswordResetToken> PasswordResetTokens { get; set; }

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
                entity.Property(e => e.DeliveredDt).HasColumnName("delivered_dt");
                entity.Property(e => e.WrittenOffDt).HasColumnName("written_off_dt");
                entity.Property(e => e.RejectedDt).HasColumnName("rejected_dt");
                entity.Property(e => e.CancelledDt).HasColumnName("cancelled_dt");
                entity.Property(e => e.Amount).HasColumnName("amount").HasColumnType("decimal(18,2)");
                entity.Property(e => e.Stamp).HasColumnName("stamp");
                entity.Ignore(e => e.CustomerFullName);
            });

            modelBuilder.Entity<Product>(entity =>
            {
                entity.ToTable("product", "dbo");
                entity.HasKey(e => e.Id).HasName("PK_product");
                entity.Property(e => e.Id).HasColumnName("product_id");
                entity.Property(e => e.Name).HasColumnName("name");
                entity.Property(e => e.InputDt).HasColumnName("input_dt");
                entity.Property(e => e.InputUserId).HasColumnName("input_user_id");
                entity.Property(e => e.LastModifiedById).HasColumnName("last_modified_by_id");
                entity.Property(e => e.LastUpdatedDt).HasColumnName("last_updated_dt");
                entity.Property(e => e.IsActive).HasColumnName("is_active");
                entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");
                entity.Property(e => e.Stamp).HasColumnName("stamp");
            });

            modelBuilder.Entity<ProductVariant>(entity =>
            {
                entity.ToTable("product_variant", "dbo");
                entity.HasKey(e => e.Id).HasName("PK_product_variant");
                entity.Property(e => e.Id).HasColumnName("product_variant_id");
                entity.Property(e => e.ProductId).HasColumnName("product_id");
                entity.Property(e => e.Size).HasColumnName("size");
                entity.Property(e => e.Color).HasColumnName("color");
                entity.Property(e => e.Amount).HasColumnName("amount").HasColumnType("decimal(18,2)");
                entity.Property(e => e.InputDt).HasColumnName("input_dt");
                entity.Property(e => e.InputUserId).HasColumnName("input_user_id");
                entity.Property(e => e.LastModifiedById).HasColumnName("last_modified_by_id");
                entity.Property(e => e.LastUpdatedDt).HasColumnName("last_updated_dt");
                entity.Property(e => e.IsActive).HasColumnName("is_active");
                entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");
                entity.Property(e => e.Stamp).HasColumnName("stamp");
                // Computed columns in DB: must not be included in INSERT/UPDATE
                var sizeKeyProp = entity.Property(e => e.SizeKey)
                    .HasColumnName("size_key")
                    .ValueGeneratedOnAddOrUpdate();
                sizeKeyProp.Metadata.SetBeforeSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
                sizeKeyProp.Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);

                var colorKeyProp = entity.Property(e => e.ColorKey)
                    .HasColumnName("color_key")
                    .ValueGeneratedOnAddOrUpdate();
                colorKeyProp.Metadata.SetBeforeSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
                colorKeyProp.Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
                entity.Property(e => e.PhotoFileName).HasColumnName("photo_file_name");
            });

            modelBuilder.Entity<ProductInventory>(entity =>
            {
                entity.ToTable("product_inventory", "dbo");
                entity.HasKey(e => e.Id).HasName("PK_product_inventory");
                entity.Property(e => e.Id).HasColumnName("product_inventory_id");
                entity.Property(e => e.ProductVariantId).HasColumnName("product_variant_id");
                entity.Property(e => e.StoreId).HasColumnName("store_id");
                entity.Property(e => e.QtyOnHand).HasColumnName("qty_on_hand");
                entity.Property(e => e.InputDt).HasColumnName("input_dt");
                entity.Property(e => e.InputUserId).HasColumnName("input_user_id");
                entity.Property(e => e.LastModifiedById).HasColumnName("last_modified_by_id");
                entity.Property(e => e.LastUpdatedDt).HasColumnName("last_updated_dt");
                entity.Property(e => e.IsActive).HasColumnName("is_active");
                entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");
                entity.Property(e => e.Stamp).HasColumnName("stamp");
            });

            modelBuilder.Entity<Note>(entity =>
            {
                entity.ToTable("note", "dbo");
                entity.HasKey(e => e.Id).HasName("PK_note");
                entity.Property(e => e.Id).HasColumnName("note_id");
                entity.Property(e => e.ProductId).HasColumnName("product_id");
                entity.Property(e => e.ContractId).HasColumnName("contract_id");
                entity.Property(e => e.Comment).HasColumnName("comment");
                entity.Property(e => e.IsMain).HasColumnName("is_main");
                entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");
                entity.Property(e => e.InputDt).HasColumnName("input_dt");
                entity.Property(e => e.InputUserId).HasColumnName("input_user_id");
                entity.Property(e => e.LastModifiedById).HasColumnName("last_modified_by_id");
                entity.Property(e => e.LastUpdatedDt).HasColumnName("last_updated_dt");
                entity.Property(e => e.Stamp).HasColumnName("stamp");
            });

            modelBuilder.Entity<OnlineContract.Models.PasswordResetToken>(entity =>
            {
                entity.ToTable("password_reset_token", "dbo");
                entity.HasKey(e => e.Id).HasName("PK_password_reset_token");
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.Token).HasColumnName("token").HasMaxLength(200);
                entity.Property(e => e.UserId).HasColumnName("user_id");
                entity.Property(e => e.Code).HasColumnName("code").HasMaxLength(50);
                entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(150);
                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
                entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
                entity.Property(e => e.Used).HasColumnName("used");
                entity.Property(e => e.UsedAt).HasColumnName("used_at");
            });
        }
    }
}