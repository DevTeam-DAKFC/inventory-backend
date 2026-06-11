using Inventory.Api.Data;
using Inventory.Api.Models;
using Inventory.Api.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Tests.Notifications;

public class FirebaseFcmNotificationSenderTests
{
    [Fact]
    public async Task SendAsync_Does_Not_Call_Firebase_When_No_Tokens_Exist()
    {
        await using var db = CreateDbContext();
        var client = new RecordingFirebaseMessagingClient();
        var sender = new FirebaseFcmNotificationSender(db, client);

        await sender.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task SendAsync_Sends_Notification_To_All_Registered_Tokens()
    {
        await using var db = CreateDbContext();
        var user = CreateUser();
        db.Users.Add(user);
        db.NotificationTokens.AddRange(
            CreateToken(user.Id, "token-one"),
            CreateToken(user.Id, "token-two"));
        await db.SaveChangesAsync();

        var client = new RecordingFirebaseMessagingClient();
        var sender = new FirebaseFcmNotificationSender(db, client);
        var notification = CreateNotification();

        await sender.SendAsync(notification, CancellationToken.None);

        Assert.Equal(1, client.CallCount);
        Assert.Equal(["token-one", "token-two"], client.Tokens.OrderBy(token => token));
        Assert.Same(notification, client.Notification);
    }

    [Fact]
    public async Task SendAsync_Splits_More_Than_500_Tokens_Into_Multiple_Requests()
    {
        await using var db = CreateDbContext();
        var user = CreateUser();
        db.Users.Add(user);
        db.NotificationTokens.AddRange(
            Enumerable.Range(1, 501)
                .Select(index => CreateToken(user.Id, $"token-{index}")));
        await db.SaveChangesAsync();

        var client = new RecordingFirebaseMessagingClient();
        var sender = new FirebaseFcmNotificationSender(db, client);

        await sender.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(2, client.CallCount);
        Assert.Equal(501, client.TokenBatches.Sum(batch => batch.Count));
        Assert.All(client.TokenBatches, batch => Assert.InRange(batch.Count, 1, 500));
    }

    private static InventoryDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase($"fcm-sender-tests-{Guid.NewGuid():N}")
            .Options;
        return new InventoryDbContext(options);
    }

    private static FcmNotification CreateNotification() =>
        new(
            "Stock bajo detectado",
            "Rice tiene stock bajo en Central.",
            new Dictionary<string, string>
            {
                ["type"] = "low_stock",
                ["productId"] = Guid.NewGuid().ToString(),
                ["branchId"] = Guid.NewGuid().ToString()
            });

    private static AppUser CreateUser() =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "Notification User",
            Email = $"notification-{Guid.NewGuid():N}@example.com",
            PasswordHash = "unused",
            Role = UserRole.Collaborator,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

    private static NotificationToken CreateToken(Guid userId, string value) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = value,
            Platform = PlatformType.Android,
            CreatedAt = DateTime.UtcNow
        };

    private sealed class RecordingFirebaseMessagingClient : IFirebaseMessagingClient
    {
        public int CallCount { get; private set; }
        public IReadOnlyList<string> Tokens { get; private set; } = [];
        public List<IReadOnlyList<string>> TokenBatches { get; } = [];
        public FcmNotification? Notification { get; private set; }

        public Task SendMulticastAsync(
            IReadOnlyList<string> tokens,
            FcmNotification notification,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Tokens = tokens;
            TokenBatches.Add(tokens);
            Notification = notification;
            return Task.CompletedTask;
        }
    }
}
