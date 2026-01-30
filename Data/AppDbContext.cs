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
        public DbSet<ContractDet> ContractDets { get; set; }
        public DbSet<OnlineContract.Models.ContractStateLookup> ContractStates { get; set; }
        public DbSet<OnlineContract.Models.PasswordResetToken> PasswordResetTokens { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<ApprovalRule> ApprovalRules { get; set; }
        public DbSet<TaskItem> Tasks { get; set; }
        public DbSet<SchedulerProcess> SchedulerProcesses { get; set; }
        public DbSet<LookupSet> LookupSets { get; set; }
        public DbSet<Review> Reviews { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<EventLogView>()
                .HasNoKey()
        .ToFunction("fn_event_logs");

            // Map Store entity to dbo.store with snake_case columns
            modelBuilder.Entity<Store>(entity =>
            {
                entity.ToTable("store", "dbo", tb => tb.HasTrigger("TR_dbo_store"));
                entity.HasKey(e => e.StoreId).HasName("PK_store");
                entity.Property(e => e.StoreId).HasColumnName("store_id");
                entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(100);
                entity.Property(e => e.Address).HasColumnName("address").HasMaxLength(255);
                entity.Property(e => e.PhoneNumber).HasColumnName("phone_number").HasMaxLength(20);
                entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(100);
                entity.Property(e => e.WorkingHours).HasColumnName("working_hours").HasMaxLength(255);
                entity.Property(e => e.CreatedDt).HasColumnName("created_dt");
                entity.Property(e => e.UpdatedDt).HasColumnName("updated_dt");
                entity.Property(e => e.LastModifiedUserId).HasColumnName("last_modified_user_id");
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
                entity.Property(e => e.AmtMatched).HasColumnName("amt_matched").HasColumnType("decimal(18,2)");
                entity.Property(e => e.Stamp).HasColumnName("stamp");
                entity.Ignore(e => e.CustomerFullName);
            });

            // Map ContractDet entity to dbo.contract_det
            modelBuilder.Entity<ContractDet>(entity =>
            {
                entity.ToTable("contract_det", "dbo");
                entity.HasKey(e => e.Id).HasName("PK_contract_det");
                entity.Property(e => e.Id).HasColumnName("contract_det_id");
                entity.Property(e => e.ContractId).HasColumnName("contract_id");
                entity.Property(e => e.ProductVariantId).HasColumnName("product_variant_id");
                entity.Property(e => e.Quantity).HasColumnName("quantity");
                entity.Property(e => e.Amount).HasColumnName("amount").HasColumnType("decimal(18,2)");
                // Computed columns: EF must not attempt to INSERT/UPDATE these
                var amtTaxProp = entity.Property(e => e.AmtTax)
                    .HasColumnName("amt_tax")
                    .HasColumnType("decimal(18,2)")
                    .HasComputedColumnSql("([amount] * 0.20)", stored: true);
                amtTaxProp.Metadata.SetBeforeSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
                amtTaxProp.Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);

                var amtGrossProp = entity.Property(e => e.AmtGross)
                    .HasColumnName("amt_gross")
                    .HasColumnType("decimal(18,2)")
                    .HasComputedColumnSql("(([quantity] * [amount]))", stored: true);
                amtGrossProp.Metadata.SetBeforeSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
                amtGrossProp.Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
                entity.Property(e => e.ProductName).HasColumnName("product_name");
                entity.Property(e => e.Size).HasColumnName("size");
                entity.Property(e => e.Color).HasColumnName("color");
                entity.Property(e => e.ItemStateId).HasColumnName("item_state_id");
                entity.Property(e => e.InputDt).HasColumnName("input_dt");
                entity.Property(e => e.InputUserId).HasColumnName("input_user_id");
                entity.Property(e => e.LastModifiedById).HasColumnName("last_modified_by_id");
                entity.Property(e => e.LastUpdatedDt).HasColumnName("last_updated_dt");
                entity.Property(e => e.Stamp).HasColumnName("stamp");
                entity.Property(e => e.IsActive).HasColumnName("is_active");
                entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");
            });

            // Map ContractState lookup entity to dbo.contract_state
            modelBuilder.Entity<OnlineContract.Models.ContractStateLookup>(entity =>
            {
                entity.ToTable("contract_state", "dbo");
                entity.HasKey(e => e.LookupSetId).HasName("PK_contract_state_lookup");
                entity.Property(e => e.LookupSetId).HasColumnName("lookup_set_id");
                entity.Property(e => e.IsStartState).HasColumnName("is_start_state");
                entity.Property(e => e.IsEndState).HasColumnName("is_end_state");
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
                entity.Property(e => e.CreatedDt).HasColumnName("created_dt");
                entity.Property(e => e.ExpiryDt).HasColumnName("expiry_dt");
                entity.Property(e => e.IsUsed).HasColumnName("is_used");
                entity.Property(e => e.UsedDt).HasColumnName("used_dt");
            });

            // Payment entity mapping
            modelBuilder.Entity<Payment>(entity =>
            {
                entity.ToTable("payment", "dbo");
                entity.HasKey(e => e.PaymentId).HasName("PK_payment");
                entity.Property(e => e.PaymentId).HasColumnName("payment_id");
                entity.Property(e => e.ContractId).HasColumnName("contract_id");
                entity.Property(e => e.Provider).HasColumnName("provider");
                entity.Property(e => e.ExternalOrderId).HasColumnName("external_order_id");
                entity.Property(e => e.AmountGross).HasColumnName("amt_gross").HasColumnType("decimal(18,2)");
                entity.Property(e => e.Currency).HasColumnName("currency");
                entity.Property(e => e.Status).HasColumnName("status");
                entity.Property(e => e.CreatedDt).HasColumnName("created_dt");
                entity.Property(e => e.UpdatedDt).HasColumnName("updated_dt");
                entity.Property(e => e.Stamp).HasColumnName("stamp");
                entity.Property(e => e.TransactionId).HasColumnName("transaction_id");
                entity.Property(e => e.LastCallbackDt).HasColumnName("last_callback_dt");
                entity.Property(e => e.LastCallbackStatus).HasColumnName("last_callback_status");
                entity.HasIndex(e => e.ExternalOrderId).IsUnique();
            });

            // ApprovalRule mapping
            modelBuilder.Entity<ApprovalRule>(entity =>
            {
                entity.ToTable("approval_rule", "dbo");
                entity.HasKey(e => e.Id).HasName("PK_approval_rule");
                entity.Property(e => e.Id).HasColumnName("approval_rule_id");
                entity.Property(e => e.Name).HasColumnName("name");
                entity.Property(e => e.Description).HasColumnName("description");
                entity.Property(e => e.IsActive).HasColumnName("is_active");
                entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");
                entity.Property(e => e.TaskAssignedToId).HasColumnName("task_assigned_to_id");
                entity.Property(e => e.AmtThreshold).HasColumnName("amt_threshold").HasColumnType("decimal(18,2)");
                entity.Property(e => e.PctThreshold).HasColumnName("pct_threshold").HasColumnType("decimal(18,2)");
                entity.Property(e => e.ApprovalRuleContextId).HasColumnName("approval_rule_context_id");
                entity.Property(e => e.Stamp).HasColumnName("stamp");
            });

            // TaskItem mapping
            modelBuilder.Entity<TaskItem>(entity =>
            {
                entity.ToTable("task", "dbo");
                entity.HasKey(e => e.Id).HasName("PK_task");
                entity.Property(e => e.Id).HasColumnName("task_id");
                entity.Property(e => e.Subject).HasColumnName("subject");
                entity.Property(e => e.Comments).HasColumnName("comments");
                entity.Property(e => e.InputDt).HasColumnName("input_dt");
                entity.Property(e => e.AssignedToUserId).HasColumnName("assigned_to_user_id");
                entity.Property(e => e.InitiatedByUserId).HasColumnName("initiated_by_user_id");
                entity.Property(e => e.Priority).HasColumnName("priority");
                entity.Property(e => e.Status).HasColumnName("status");
                entity.Property(e => e.ReminderDt).HasColumnName("reminder_dt");
                entity.Property(e => e.ContractId).HasColumnName("contract_id");
                entity.Property(e => e.CompletedDt).HasColumnName("completed_dt");
                entity.Property(e => e.Stamp).HasColumnName("stamp");
            });

            // SchedulerProcess mapping
            modelBuilder.Entity<SchedulerProcess>(entity =>
            {
                entity.ToTable("scheduler_process", "dbo");
                entity.HasKey(e => e.Id).HasName("PK_scheduler_process");
                entity.Property(e => e.Id).HasColumnName("scheduler_process_id");
                entity.Property(e => e.Name).HasColumnName("name");
                entity.Property(e => e.Description).HasColumnName("description");
                entity.Property(e => e.LastEndDt).HasColumnName("last_end_dt");
                entity.Property(e => e.LastStartDt).HasColumnName("last_start_dt");
                entity.Property(e => e.NextRunDt).HasColumnName("next_run_dt");
                // Computed columns - must be marked as database-generated
                entity.Property(e => e.DurationSec).HasColumnName("duration_sec").ValueGeneratedOnAddOrUpdate();
                entity.Property(e => e.DurationFmt).HasColumnName("duration_fmt").ValueGeneratedOnAddOrUpdate();
                entity.Property(e => e.IsActive).HasColumnName("is_active");
                entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");
                entity.Property(e => e.Stamp).HasColumnName("stamp");
            });

            // LookupSet mapping
            modelBuilder.Entity<LookupSet>(entity =>
            {
                entity.ToTable("lookup_set", "dbo");
                entity.HasKey(e => e.LookupSetId).HasName("PK_lookup_set");
                entity.Property(e => e.LookupSetId).HasColumnName("lookup_set_id");
                entity.Property(e => e.SetName).HasColumnName("set_name");
                entity.Property(e => e.Value).HasColumnName("value");
            });
        }
    }
}