using Inventory.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

public static class DevelopmentDataSeeder
{
    public static readonly Guid MainBranchId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid WarehouseBranchId = Guid.Parse("10000000-0000-0000-0000-000000000002");

    public static readonly Guid CoffeeProductId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    public static readonly Guid TeaProductId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    public static readonly Guid SugarProductId = Guid.Parse("20000000-0000-0000-0000-000000000003");

    public static readonly Guid MainCoffeeStockId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    public static readonly Guid WarehouseCoffeeStockId = Guid.Parse("30000000-0000-0000-0000-000000000002");
    public static readonly Guid MainTeaStockId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    public static readonly Guid WarehouseTeaStockId = Guid.Parse("30000000-0000-0000-0000-000000000004");
    public static readonly Guid MainSugarStockId = Guid.Parse("30000000-0000-0000-0000-000000000005");

    public static async Task SeedAsync(InventoryDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var mainBranch = await GetOrCreateBranchAsync(
            dbContext,
            MainBranchId,
            "Central Branch",
            "100 Main St",
            now,
            cancellationToken);
        var warehouseBranch = await GetOrCreateBranchAsync(
            dbContext,
            WarehouseBranchId,
            "Warehouse Branch",
            "200 Warehouse Ave",
            now,
            cancellationToken);

        var coffee = await GetOrCreateProductAsync(
            dbContext,
            CoffeeProductId,
            "Coffee Beans",
            "DEV-COF-001",
            "779000000001",
            "Pantry",
            "Development sample product",
            "https://example.test/images/coffee.png",
            5,
            now,
            cancellationToken);
        var tea = await GetOrCreateProductAsync(
            dbContext,
            TeaProductId,
            "Tea Bags",
            "DEV-TEA-001",
            "779000000002",
            "Pantry",
            "Development sample product",
            "https://example.test/images/tea.png",
            10,
            now,
            cancellationToken);
        var sugar = await GetOrCreateProductAsync(
            dbContext,
            SugarProductId,
            "Sugar Packets",
            "DEV-SUG-001",
            "779000000003",
            "Pantry",
            "Development sample product",
            "https://example.test/images/sugar.png",
            2,
            now,
            cancellationToken);

        await GetOrCreateStockAsync(dbContext, MainCoffeeStockId, coffee, mainBranch, 25, 5, now, cancellationToken);
        await GetOrCreateStockAsync(dbContext, WarehouseCoffeeStockId, coffee, warehouseBranch, 4, 5, now, cancellationToken);
        await GetOrCreateStockAsync(dbContext, MainTeaStockId, tea, mainBranch, 10, 10, now, cancellationToken);
        await GetOrCreateStockAsync(dbContext, WarehouseTeaStockId, tea, warehouseBranch, 35, 10, now, cancellationToken);
        await GetOrCreateStockAsync(dbContext, MainSugarStockId, sugar, mainBranch, 0, 2, now, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<Branch> GetOrCreateBranchAsync(
        InventoryDbContext dbContext,
        Guid id,
        string name,
        string address,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var branch = await dbContext.Branches.FirstOrDefaultAsync(
            b => b.Id == id || b.Name == name,
            cancellationToken);

        if (branch is not null)
        {
            return branch;
        }

        branch = new Branch
        {
            Id = id,
            Name = name,
            Address = address,
            IsActive = true,
            CreatedAt = now
        };
        dbContext.Branches.Add(branch);

        return branch;
    }

    private static async Task<Product> GetOrCreateProductAsync(
        InventoryDbContext dbContext,
        Guid id,
        string name,
        string sku,
        string barcode,
        string category,
        string description,
        string imageUrl,
        int minStock,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products.FirstOrDefaultAsync(
            p => p.Id == id || p.Sku == sku || p.Barcode == barcode,
            cancellationToken);

        if (product is not null)
        {
            return product;
        }

        product = new Product
        {
            Id = id,
            Name = name,
            Sku = sku,
            Barcode = barcode,
            Category = category,
            Description = description,
            ImageUrl = imageUrl,
            MinStock = minStock,
            IsActive = true,
            CreatedAt = now
        };
        dbContext.Products.Add(product);

        return product;
    }

    private static async Task<Stock> GetOrCreateStockAsync(
        InventoryDbContext dbContext,
        Guid id,
        Product product,
        Branch branch,
        int availableQuantity,
        int minStock,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var stock = await dbContext.Stocks.FirstOrDefaultAsync(
            s => s.Id == id || (s.ProductId == product.Id && s.BranchId == branch.Id),
            cancellationToken);

        if (stock is not null)
        {
            return stock;
        }

        stock = new Stock
        {
            Id = id,
            Product = product,
            Branch = branch,
            AvailableQuantity = availableQuantity,
            MinStock = minStock,
            UpdatedAt = now
        };
        dbContext.Stocks.Add(stock);

        return stock;
    }
}
