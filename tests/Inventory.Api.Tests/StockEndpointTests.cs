using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventory.Api.Data;
using Inventory.Api.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.Tests;

public class StockEndpointTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;
    private readonly HttpClient _client;

    public StockEndpointTests(InventoryApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Get_Stock_Returns_All_Stock_With_Product_And_Branch_Data()
    {
        var data = await SeedStockAsync();

        var stocks = await _client.GetFromJsonAsync<JsonElement[]>("/stock");

        Assert.NotNull(stocks);
        Assert.Equal(3, stocks.Length);

        var firstBranchStock = stocks.Single(stock =>
            stock.GetProperty("id").GetGuid() == data.MainCoffeeStockId);
        Assert.Equal(data.CoffeeProductId, firstBranchStock.GetProperty("product").GetProperty("id").GetGuid());
        Assert.Equal("Coffee Beans", firstBranchStock.GetProperty("product").GetProperty("name").GetString());
        Assert.Equal(data.MainBranchId, firstBranchStock.GetProperty("branch").GetProperty("id").GetGuid());
        Assert.Equal("Main Branch", firstBranchStock.GetProperty("branch").GetProperty("name").GetString());
        Assert.Equal(4, firstBranchStock.GetProperty("availableQuantity").GetInt32());
        Assert.Equal(5, firstBranchStock.GetProperty("minStock").GetInt32());
    }

    [Fact]
    public async Task Get_Stock_Can_Filter_By_BranchId()
    {
        var data = await SeedStockAsync();

        var stocks = await _client.GetFromJsonAsync<JsonElement[]>($"/stock?branchId={data.MainBranchId}");

        Assert.NotNull(stocks);
        Assert.Equal(2, stocks.Length);
        Assert.All(stocks, stock =>
            Assert.Equal(data.MainBranchId, stock.GetProperty("branch").GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task Get_Stock_Can_Filter_By_ProductId()
    {
        var data = await SeedStockAsync();

        var stocks = await _client.GetFromJsonAsync<JsonElement[]>($"/stock?productId={data.CoffeeProductId}");

        Assert.NotNull(stocks);
        Assert.Equal(2, stocks.Length);
        Assert.All(stocks, stock =>
            Assert.Equal(data.CoffeeProductId, stock.GetProperty("product").GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task Get_Stock_Lookup_Returns_Product_Branch_Combination()
    {
        var data = await SeedStockAsync();

        var stock = await _client.GetFromJsonAsync<JsonElement>(
            $"/stock/lookup?productId={data.CoffeeProductId}&branchId={data.SecondaryBranchId}");

        Assert.Equal(data.SecondaryCoffeeStockId, stock.GetProperty("id").GetGuid());
        Assert.Equal(20, stock.GetProperty("availableQuantity").GetInt32());
        Assert.False(stock.GetProperty("isLowStock").GetBoolean());
    }

    [Fact]
    public async Task Get_Stock_Lookup_Returns_400_When_ProductId_Is_Missing()
    {
        var data = await SeedStockAsync();

        var response = await _client.GetAsync($"/stock/lookup?branchId={data.MainBranchId}");
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("missing_required_parameter", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Get_Stock_Lookup_Returns_400_When_BranchId_Is_Not_A_Guid()
    {
        var data = await SeedStockAsync();

        var response = await _client.GetAsync($"/stock/lookup?productId={data.CoffeeProductId}&branchId=not-a-guid");
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_guid", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Get_Stock_Lookup_Returns_404_When_Combination_Does_Not_Exist()
    {
        var data = await SeedStockAsync();

        var response = await _client.GetAsync(
            $"/stock/lookup?productId={data.TeaProductId}&branchId={data.SecondaryBranchId}");
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("stock_not_found", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Get_Stock_By_Id_Returns_Detail()
    {
        var data = await SeedStockAsync();

        var stock = await _client.GetFromJsonAsync<JsonElement>($"/stock/{data.MainCoffeeStockId}");

        Assert.Equal(data.MainCoffeeStockId, stock.GetProperty("id").GetGuid());
        Assert.Equal(data.CoffeeProductId, stock.GetProperty("product").GetProperty("id").GetGuid());
        Assert.Equal(data.MainBranchId, stock.GetProperty("branch").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Get_Stock_By_Id_Returns_404_When_Stock_Does_Not_Exist()
    {
        await SeedStockAsync();

        var response = await _client.GetAsync($"/stock/{Guid.NewGuid()}");
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("stock_not_found", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Get_Stock_By_Id_Returns_400_When_StockId_Is_Not_A_Guid()
    {
        await SeedStockAsync();

        var response = await _client.GetAsync("/stock/not-a-guid");
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_guid", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Get_Stock_Derives_Low_Stock_From_Available_Quantity_And_Min_Stock()
    {
        var data = await SeedStockAsync();

        var stock = await _client.GetFromJsonAsync<JsonElement>($"/stock/{data.MainCoffeeStockId}");

        Assert.Equal(4, stock.GetProperty("availableQuantity").GetInt32());
        Assert.Equal(5, stock.GetProperty("minStock").GetInt32());
        Assert.True(stock.GetProperty("isLowStock").GetBoolean());
    }

    [Fact]
    public async Task Get_Stock_Returns_400_When_Filter_Is_Not_A_Guid()
    {
        await SeedStockAsync();

        var response = await _client.GetAsync("/stock?branchId=not-a-guid");
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_guid", error.GetProperty("code").GetString());
    }

    private async Task<SeededStockData> SeedStockAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var mainBranchId = Guid.NewGuid();
        var secondaryBranchId = Guid.NewGuid();
        var coffeeProductId = Guid.NewGuid();
        var teaProductId = Guid.NewGuid();
        var mainCoffeeStockId = Guid.NewGuid();
        var secondaryCoffeeStockId = Guid.NewGuid();
        var mainTeaStockId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var mainBranch = new Branch
        {
            Id = mainBranchId,
            Name = "Main Branch",
            Address = "100 Main St",
            IsActive = true,
            CreatedAt = now
        };
        var secondaryBranch = new Branch
        {
            Id = secondaryBranchId,
            Name = "Secondary Branch",
            Address = "200 Market St",
            IsActive = true,
            CreatedAt = now
        };
        var coffee = new Product
        {
            Id = coffeeProductId,
            Name = "Coffee Beans",
            Sku = "COF-001",
            Barcode = "1234567890123",
            Category = "Pantry",
            ImageUrl = "https://example.test/coffee.png",
            MinStock = 5,
            IsActive = true,
            CreatedAt = now
        };
        var tea = new Product
        {
            Id = teaProductId,
            Name = "Tea Bags",
            Sku = "TEA-001",
            Category = "Pantry",
            MinStock = 3,
            IsActive = true,
            CreatedAt = now
        };

        dbContext.AddRange(
            mainBranch,
            secondaryBranch,
            coffee,
            tea,
            new Stock
            {
                Id = mainCoffeeStockId,
                Product = coffee,
                Branch = mainBranch,
                AvailableQuantity = 4,
                MinStock = 5,
                UpdatedAt = now
            },
            new Stock
            {
                Id = secondaryCoffeeStockId,
                Product = coffee,
                Branch = secondaryBranch,
                AvailableQuantity = 20,
                MinStock = 5,
                UpdatedAt = now
            },
            new Stock
            {
                Id = mainTeaStockId,
                Product = tea,
                Branch = mainBranch,
                AvailableQuantity = 8,
                MinStock = 3,
                UpdatedAt = now
            });

        await dbContext.SaveChangesAsync();

        return new SeededStockData(
            mainBranchId,
            secondaryBranchId,
            coffeeProductId,
            teaProductId,
            mainCoffeeStockId,
            secondaryCoffeeStockId,
            mainTeaStockId);
    }

    private sealed record SeededStockData(
        Guid MainBranchId,
        Guid SecondaryBranchId,
        Guid CoffeeProductId,
        Guid TeaProductId,
        Guid MainCoffeeStockId,
        Guid SecondaryCoffeeStockId,
        Guid MainTeaStockId);
}
