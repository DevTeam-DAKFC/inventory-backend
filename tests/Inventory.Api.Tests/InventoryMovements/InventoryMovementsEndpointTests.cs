using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Inventory.Api.Auth;
using Inventory.Api.Data;
using Inventory.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.Tests.InventoryMovements;

public class InventoryMovementsEndpointTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;
    private readonly HttpClient _client;

    public InventoryMovementsEndpointTests(InventoryApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Post_Incoming_Movement_Increases_Stock()
    {
        var scenario = await SeedScenarioAsync(initialStock: 5);
        var request = CreateAuthorizedRequest(HttpMethod.Post, "/inventory-movements", scenario.Token, new
        {
            productId = scenario.ProductId,
            branchId = scenario.BranchId,
            type = "incoming",
            quantity = 7,
            reason = "Restock"
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("incoming", body.GetProperty("type").GetString());
        Assert.Equal(5, body.GetProperty("previousStock").GetInt32());
        Assert.Equal(12, body.GetProperty("resultingStock").GetInt32());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var stock = await db.Stocks.SingleAsync(s => s.Id == scenario.StockId);
        Assert.Equal(12, stock.AvailableQuantity);
        Assert.NotNull(stock.LastMovementId);
        Assert.NotNull(stock.LastMovementAt);
        Assert.NotNull(stock.UpdatedAt);
    }

    [Fact]
    public async Task Post_Outgoing_Movement_Decreases_Stock()
    {
        var scenario = await SeedScenarioAsync(initialStock: 10);
        var request = CreateAuthorizedRequest(HttpMethod.Post, "/inventory-movements", scenario.Token, new
        {
            productId = scenario.ProductId,
            branchId = scenario.BranchId,
            type = "outgoing",
            quantity = 4,
            reason = "Sale"
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("outgoing", body.GetProperty("type").GetString());
        Assert.Equal(10, body.GetProperty("previousStock").GetInt32());
        Assert.Equal(6, body.GetProperty("resultingStock").GetInt32());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var stock = await db.Stocks.SingleAsync(s => s.Id == scenario.StockId);
        Assert.Equal(6, stock.AvailableQuantity);
    }

    [Fact]
    public async Task Post_Outgoing_Movement_Returns_422_When_Stock_Is_Insufficient()
    {
        var scenario = await SeedScenarioAsync(initialStock: 3);
        var request = CreateAuthorizedRequest(HttpMethod.Post, "/inventory-movements", scenario.Token, new
        {
            productId = scenario.ProductId,
            branchId = scenario.BranchId,
            type = "outgoing",
            quantity = 8,
            reason = "Sale"
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var error = body.GetProperty("error");
        Assert.Equal("insufficient_stock", error.GetProperty("code").GetString());
        Assert.Contains("Requested 8 but only 3", error.GetProperty("details")[0].GetProperty("message").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var stock = await db.Stocks.SingleAsync(s => s.Id == scenario.StockId);
        Assert.Equal(3, stock.AvailableQuantity);
        Assert.False(await db.InventoryMovements.AnyAsync(m => m.ProductId == scenario.ProductId));
    }

    [Fact]
    public async Task Post_Incoming_Movement_Creates_Stock_When_Product_Branch_Stock_Does_Not_Exist()
    {
        var scenario = await SeedScenarioWithoutStockAsync();
        var request = CreateAuthorizedRequest(HttpMethod.Post, "/inventory-movements", scenario.Token, new
        {
            productId = scenario.ProductId,
            branchId = scenario.BranchId,
            type = "incoming",
            quantity = 5,
            reason = "Initial stock"
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("incoming", body.GetProperty("type").GetString());
        Assert.Equal(0, body.GetProperty("previousStock").GetInt32());
        Assert.Equal(5, body.GetProperty("resultingStock").GetInt32());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var stock = await db.Stocks.SingleAsync(s =>
            s.ProductId == scenario.ProductId &&
            s.BranchId == scenario.BranchId);

        Assert.Equal(5, stock.AvailableQuantity);
        Assert.NotNull(stock.LastMovementId);
        Assert.NotNull(stock.LastMovementAt);
        Assert.NotNull(stock.UpdatedAt);
    }

    [Fact]
    public async Task Post_Outgoing_Movement_Returns_422_When_Stock_Does_Not_Exist()
    {
        var scenario = await SeedScenarioWithoutStockAsync();
        var request = CreateAuthorizedRequest(HttpMethod.Post, "/inventory-movements", scenario.Token, new
        {
            productId = scenario.ProductId,
            branchId = scenario.BranchId,
            type = "outgoing",
            quantity = 1,
            reason = "Sale"
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var error = body.GetProperty("error");
        Assert.Equal("insufficient_stock", error.GetProperty("code").GetString());
        Assert.Contains("Requested 1 but only 0", error.GetProperty("details")[0].GetProperty("message").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.False(await db.Stocks.AnyAsync(s =>
            s.ProductId == scenario.ProductId &&
            s.BranchId == scenario.BranchId));
        Assert.False(await db.InventoryMovements.AnyAsync(m => m.ProductId == scenario.ProductId));
    }

    [Fact]
    public async Task Get_List_Includes_Successful_Movement()
    {
        var scenario = await SeedScenarioAsync(initialStock: 1);
        var create = CreateAuthorizedRequest(HttpMethod.Post, "/inventory-movements", scenario.Token, new
        {
            productId = scenario.ProductId,
            branchId = scenario.BranchId,
            type = "incoming",
            quantity = 2,
            reason = "Restock"
        });
        var createResponse = await _client.SendAsync(create);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var movementId = created.GetProperty("id").GetString();

        var list = new HttpRequestMessage(
            HttpMethod.Get,
            $"/inventory-movements?productId={scenario.ProductId}&branchId={scenario.BranchId}&page=1&pageSize=20");
        list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", scenario.Token);

        var response = await _client.SendAsync(list);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("total").GetInt32() >= 1);
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(20, body.GetProperty("pageSize").GetInt32());
        Assert.Contains(
            body.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetString() == movementId);
    }

    [Fact]
    public async Task Get_By_Id_Returns_Movement_Detail()
    {
        var scenario = await SeedScenarioAsync(initialStock: 9);
        var create = CreateAuthorizedRequest(HttpMethod.Post, "/inventory-movements", scenario.Token, new
        {
            productId = scenario.ProductId,
            branchId = scenario.BranchId,
            type = "outgoing",
            quantity = 3,
            reason = "Sale",
            notes = "Counter sale"
        });
        var createResponse = await _client.SendAsync(create);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var movementId = created.GetProperty("id").GetString();

        var detail = new HttpRequestMessage(HttpMethod.Get, $"/inventory-movements/{movementId}");
        detail.Headers.Authorization = new AuthenticationHeaderValue("Bearer", scenario.Token);

        var response = await _client.SendAsync(detail);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(movementId, body.GetProperty("id").GetString());
        Assert.Equal(scenario.ProductId.ToString(), body.GetProperty("productId").GetString());
        Assert.Equal(scenario.BranchId.ToString(), body.GetProperty("branchId").GetString());
        Assert.Equal(scenario.UserId.ToString(), body.GetProperty("userId").GetString());
        Assert.Equal("outgoing", body.GetProperty("type").GetString());
        Assert.Equal(3, body.GetProperty("quantity").GetInt32());
        Assert.Equal(9, body.GetProperty("previousStock").GetInt32());
        Assert.Equal(6, body.GetProperty("resultingStock").GetInt32());
        Assert.Equal("Counter sale", body.GetProperty("notes").GetString());
    }

    [Theory]
    [InlineData("GET", "/inventory-movements")]
    [InlineData("POST", "/inventory-movements")]
    [InlineData("GET", "/inventory-movements/00000000-0000-0000-0000-000000000001")]
    public async Task Endpoints_Require_Authentication(string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new
            {
                productId = Guid.NewGuid(),
                branchId = Guid.NewGuid(),
                type = "incoming",
                quantity = 1,
                reason = "Restock"
            });
        }

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("unauthorized", body.GetProperty("error").GetProperty("code").GetString());
    }

    private async Task<MovementScenario> SeedScenarioAsync(int initialStock)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var now = DateTime.UtcNow;

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Name = "Inventory User",
            Email = $"movement-{Guid.NewGuid():N}@example.com",
            PasswordHash = "not-used",
            Role = UserRole.Collaborator,
            IsActive = true,
            CreatedAt = now
        };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Rice",
            Sku = $"SKU-{Guid.NewGuid():N}",
            Category = "Food",
            MinStock = 1,
            IsActive = true,
            CreatedAt = now
        };
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            Name = "Central",
            Address = "Main street",
            IsActive = true,
            CreatedAt = now
        };
        var stock = new Stock
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            BranchId = branch.Id,
            AvailableQuantity = initialStock,
            MinStock = 1,
            UpdatedAt = now
        };

        db.Users.Add(user);
        db.Products.Add(product);
        db.Branches.Add(branch);
        db.Stocks.Add(stock);
        await db.SaveChangesAsync();

        var token = tokens.CreateAccessToken(user).Value;
        return new MovementScenario(user.Id, product.Id, branch.Id, stock.Id, token);
    }

    private async Task<MovementScenarioWithoutStock> SeedScenarioWithoutStockAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var now = DateTime.UtcNow;

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Name = "Inventory User",
            Email = $"movement-{Guid.NewGuid():N}@example.com",
            PasswordHash = "not-used",
            Role = UserRole.Collaborator,
            IsActive = true,
            CreatedAt = now
        };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "New Product",
            Sku = $"SKU-{Guid.NewGuid():N}",
            Category = "Food",
            MinStock = 1,
            IsActive = true,
            CreatedAt = now
        };
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            Name = "Central",
            Address = "Main street",
            IsActive = true,
            CreatedAt = now
        };

        db.Users.Add(user);
        db.Products.Add(product);
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        var token = tokens.CreateAccessToken(user).Value;
        return new MovementScenarioWithoutStock(user.Id, product.Id, branch.Id, token);
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string url,
        string token,
        object body)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private sealed record MovementScenario(
        Guid UserId,
        Guid ProductId,
        Guid BranchId,
        Guid StockId,
        string Token);

    private sealed record MovementScenarioWithoutStock(
        Guid UserId,
        Guid ProductId,
        Guid BranchId,
        string Token);
}
