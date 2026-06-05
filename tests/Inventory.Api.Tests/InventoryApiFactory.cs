using Inventory.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Inventory.Api.Tests;

public class InventoryApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"inventory-tests-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Server=test;Database=InventoryDbTest;User Id=sa;Password=unused;Encrypt=False;TrustServerCertificate=True;",
                ["Jwt:SecretKey"] = "test-only-secret-key-must-be-at-least-32-bytes-long-xxxxxxxxxxxx",
                ["Jwt:Issuer"] = "inventory-api-tests",
                ["Jwt:Audience"] = "inventory-mobile-tests",
                ["Jwt:ExpiresInMinutes"] = "60"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<InventoryDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<InventoryDbContext>();

            var inMemoryProvider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            services.AddDbContext<InventoryDbContext>(options => options
                .UseInMemoryDatabase(_databaseName)
                .UseInternalServiceProvider(inMemoryProvider));
        });
    }
}
