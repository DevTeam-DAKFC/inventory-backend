using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Tests;

public class ProductQueryTranslationTests
{
    [Fact]
    public void Product_List_Query_With_Search_Filters_LowStock_And_Pagination_Is_Translatable_By_SqlServer()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer("Server=(local);Database=TranslationOnly;User Id=sa;Password=unused;Encrypt=False;TrustServerCertificate=True;")
            .Options;

        using var dbContext = new InventoryDbContext(options);

        var query = dbContext.Products
            .AsNoTracking()
            .Where(product =>
                product.Name.Contains("needle") ||
                product.Sku.Contains("needle") ||
                (product.Barcode != null && product.Barcode.Contains("needle")))
            .Where(product => product.Category == "Grocery")
            .Where(product => product.IsActive)
            .Where(product => product.Stocks.Any(stock => stock.AvailableQuantity <= product.MinStock))
            .OrderBy(product => product.Name)
            .ThenBy(product => product.Id)
            .Skip(20)
            .Take(20)
            .Select(product => new
            {
                product.Id,
                product.Name,
                product.Sku,
                product.Barcode,
                product.Category,
                product.MinStock
            });

        var sql = query.ToQueryString();

        Assert.Contains("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FETCH", sql, StringComparison.OrdinalIgnoreCase);
    }
}
