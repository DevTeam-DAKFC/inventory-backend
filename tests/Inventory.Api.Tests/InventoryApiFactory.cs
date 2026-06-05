using Inventory.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Inventory.Api.Tests;

public class InventoryApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"inventory-tests-{Guid.NewGuid():N}";
    private readonly string _webRootPath = Path.Combine(
        Path.GetTempPath(),
        $"inventory-api-tests-{Guid.NewGuid():N}");

    public string WebRootPath => _webRootPath;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseWebRoot(_webRootPath);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Server=test;Database=InventoryDbTest;User Id=sa;Password=unused;Encrypt=False;TrustServerCertificate=True;",
                ["Jwt:SecretKey"] =
                    "test-only-secret-key-must-be-at-least-32-bytes-long-xxxxxxxxxxxx",
                ["Jwt:Issuer"] = "inventory-api-tests",
                ["Jwt:Audience"] = "inventory-mobile-tests",
                ["Jwt:ExpiresInMinutes"] = "60"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<InventoryDbContext>();
            services.RemoveAll<DbContextOptions<InventoryDbContext>>();
            services.RemoveAll<DbContextOptions>();

            RemoveDbContextOptionsConfigurations(services);

            services.AddDbContext<InventoryDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(_webRootPath))
        {
            Directory.Delete(_webRootPath, recursive: true);
        }
    }

    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var uploadsPath = Path.Combine(_webRootPath, "uploads");
        if (Directory.Exists(uploadsPath))
        {
            Directory.Delete(uploadsPath, recursive: true);
        }
    }

    private static void RemoveDbContextOptionsConfigurations(
        IServiceCollection services)
    {
        var descriptors = services
            .Where(service =>
                service.ServiceType.FullName?.Contains(
                    "IDbContextOptionsConfiguration",
                    StringComparison.Ordinal) == true)
            .ToArray();

        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }
    }
}
