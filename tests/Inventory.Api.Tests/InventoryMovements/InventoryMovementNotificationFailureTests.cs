using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Inventory.Api.Auth;
using Inventory.Api.Data;
using Inventory.Api.Models;
using Inventory.Api.Notifications;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Inventory.Api.Tests.InventoryMovements;

public class InventoryMovementNotificationFailureTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;

    public InventoryMovementNotificationFailureTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_Movement_Commits_When_Notification_Sender_Fails()
    {
        await _factory.ResetDatabaseAsync();
        using var app = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFcmNotificationSender>();
                services.AddScoped<IFcmNotificationSender, ThrowingSender>();
            }));
        using var client = app.CreateClient();
        var scenario = await SeedScenarioAsync(app.Services);
        var request = new HttpRequestMessage(HttpMethod.Post, "/inventory-movements")
        {
            Content = JsonContent.Create(new
            {
                productId = scenario.ProductId,
                branchId = scenario.BranchId,
                type = "outgoing",
                quantity = 1,
                reason = "Sale"
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", scenario.AccessToken);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var stock = await db.Stocks.SingleAsync(existing => existing.Id == scenario.StockId);
        Assert.Equal(5, stock.AvailableQuantity);
        Assert.True(await db.InventoryMovements.AnyAsync(
            movement => movement.ProductId == scenario.ProductId && movement.BranchId == scenario.BranchId));
    }

    private static async Task<Scenario> SeedScenarioAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var now = DateTime.UtcNow;
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Name = "Inventory User",
            Email = $"movement-alert-{Guid.NewGuid():N}@example.com",
            PasswordHash = "unused",
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
            MinStock = 5,
            IsActive = true,
            CreatedAt = now
        };
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            Name = "Central",
            IsActive = true,
            CreatedAt = now
        };
        var stock = new Stock
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            BranchId = branch.Id,
            AvailableQuantity = 6,
            MinStock = 5,
            UpdatedAt = now
        };

        db.AddRange(user, product, branch, stock);
        await db.SaveChangesAsync();

        return new Scenario(
            product.Id,
            branch.Id,
            stock.Id,
            tokenService.CreateAccessToken(user).Value);
    }

    private sealed record Scenario(
        Guid ProductId,
        Guid BranchId,
        Guid StockId,
        string AccessToken);

    private sealed class ThrowingSender : IFcmNotificationSender
    {
        public Task SendAsync(FcmNotification notification, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Firebase unavailable.");
    }
}
