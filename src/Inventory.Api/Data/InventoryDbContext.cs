using Inventory.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

public class InventoryDbContext : DbContext
{
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options)
        : base(options)
    {
    }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Stock> Stocks => Set<Stock>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<NotificationToken> NotificationTokens => Set<NotificationToken>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportBatchError> ImportBatchErrors => Set<ImportBatchError>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>(b =>
        {
            b.ToTable("users");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
            b.Property(x => x.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
            b.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();
            b.Property(x => x.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(50).IsRequired();
            b.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            b.HasIndex(x => x.Email).IsUnique().HasDatabaseName("UX_users_email");
        });

        modelBuilder.Entity<Branch>(b =>
        {
            b.ToTable("branches");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
            b.Property(x => x.Address).HasColumnName("address").HasMaxLength(300);
            b.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<Product>(b =>
        {
            b.ToTable("products", t => t.HasCheckConstraint("CK_products_min_stock", "[min_stock] >= 0"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
            b.Property(x => x.Sku).HasColumnName("sku").HasMaxLength(100).IsRequired();
            b.Property(x => x.Barcode).HasColumnName("barcode").HasMaxLength(32);
            b.Property(x => x.Category).HasColumnName("category").HasMaxLength(100).IsRequired();
            b.Property(x => x.Description).HasColumnName("description").HasMaxLength(500);
            b.Property(x => x.ImageUrl).HasColumnName("image_url").HasMaxLength(1000);
            b.Property(x => x.MinStock).HasColumnName("min_stock").IsRequired();
            b.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            b.HasIndex(x => x.Sku).IsUnique().HasDatabaseName("UX_products_sku");
            b.HasIndex(x => x.Barcode)
                .IsUnique()
                .HasDatabaseName("UX_products_barcode_filtered")
                .HasFilter("[barcode] IS NOT NULL");
        });

        modelBuilder.Entity<Stock>(b =>
        {
            b.ToTable("stocks", t =>
            {
                t.HasCheckConstraint("CK_stocks_available_quantity", "[available_quantity] >= 0");
                t.HasCheckConstraint("CK_stocks_min_stock", "[min_stock] >= 0");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.ProductId).HasColumnName("product_id").IsRequired();
            b.Property(x => x.BranchId).HasColumnName("branch_id").IsRequired();
            b.Property(x => x.AvailableQuantity).HasColumnName("available_quantity").IsRequired();
            b.Property(x => x.MinStock).HasColumnName("min_stock").IsRequired();
            b.Property(x => x.LastMovementId).HasColumnName("last_movement_id");
            b.Property(x => x.LastMovementAt).HasColumnName("last_movement_at");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            b.HasOne(x => x.Product)
                .WithMany(p => p.Stocks)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Branch)
                .WithMany(br => br.Stocks)
                .HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.LastMovement)
                .WithMany()
                .HasForeignKey(x => x.LastMovementId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(x => new { x.ProductId, x.BranchId })
                .IsUnique()
                .HasDatabaseName("UX_stocks_product_id_branch_id");
        });

        modelBuilder.Entity<InventoryMovement>(b =>
        {
            b.ToTable("inventory_movements", t =>
            {
                t.HasCheckConstraint("CK_inventory_movements_quantity", "[quantity] > 0");
                t.HasCheckConstraint("CK_inventory_movements_previous_stock", "[previous_stock] >= 0");
                t.HasCheckConstraint("CK_inventory_movements_resulting_stock", "[resulting_stock] >= 0");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.ProductId).HasColumnName("product_id").IsRequired();
            b.Property(x => x.BranchId).HasColumnName("branch_id").IsRequired();
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            b.Property(x => x.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(50).IsRequired();
            b.Property(x => x.Quantity).HasColumnName("quantity").IsRequired();
            b.Property(x => x.PreviousStock).HasColumnName("previous_stock").IsRequired();
            b.Property(x => x.ResultingStock).HasColumnName("resulting_stock").IsRequired();
            b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(200).IsRequired();
            b.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(500);
            b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

            b.HasOne(x => x.Product)
                .WithMany(p => p.InventoryMovements)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Branch)
                .WithMany(br => br.InventoryMovements)
                .HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.User)
                .WithMany(u => u.InventoryMovements)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(x => x.ProductId).HasDatabaseName("IX_inventory_movements_product_id");
            b.HasIndex(x => x.BranchId).HasDatabaseName("IX_inventory_movements_branch_id");
            b.HasIndex(x => x.UserId).HasDatabaseName("IX_inventory_movements_user_id");
            b.HasIndex(x => x.CreatedAt).HasDatabaseName("IX_inventory_movements_created_at");
        });

        modelBuilder.Entity<NotificationToken>(b =>
        {
            b.ToTable("notification_tokens");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            b.Property(x => x.Token).HasColumnName("token").HasMaxLength(500).IsRequired();
            b.Property(x => x.Platform).HasColumnName("platform").HasConversion<string>().HasMaxLength(50).IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            b.HasOne(x => x.User)
                .WithMany(u => u.NotificationTokens)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(x => x.Token).IsUnique().HasDatabaseName("UX_notification_tokens_token");
        });

        modelBuilder.Entity<ImportBatch>(b =>
        {
            b.ToTable("import_batches", t =>
            {
                t.HasCheckConstraint("CK_import_batches_total_rows", "[total_rows] >= 0");
                t.HasCheckConstraint("CK_import_batches_valid_rows", "[valid_rows] >= 0");
                t.HasCheckConstraint("CK_import_batches_invalid_rows", "[invalid_rows] >= 0");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(255).IsRequired();
            b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(50).IsRequired();
            b.Property(x => x.TotalRows).HasColumnName("total_rows").IsRequired();
            b.Property(x => x.ValidRows).HasColumnName("valid_rows").IsRequired();
            b.Property(x => x.InvalidRows).HasColumnName("invalid_rows").IsRequired();
            b.Property(x => x.ImportedById).HasColumnName("imported_by").IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            b.Property(x => x.CompletedAt).HasColumnName("completed_at");

            b.HasOne(x => x.ImportedBy)
                .WithMany(u => u.ImportBatches)
                .HasForeignKey(x => x.ImportedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ImportBatchError>(b =>
        {
            b.ToTable("import_batch_errors", t =>
                t.HasCheckConstraint("CK_import_batch_errors_row_number", "[row_number] > 0"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.ImportBatchId).HasColumnName("import_batch_id").IsRequired();
            b.Property(x => x.RowNumber).HasColumnName("row_number").IsRequired();
            b.Property(x => x.Field).HasColumnName("field").HasMaxLength(100).IsRequired();
            b.Property(x => x.Message).HasColumnName("message").HasMaxLength(500).IsRequired();

            b.HasOne(x => x.ImportBatch)
                .WithMany(ib => ib.Errors)
                .HasForeignKey(x => x.ImportBatchId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(x => x.ImportBatchId).HasDatabaseName("IX_import_batch_errors_import_batch_id");
        });
    }
}
