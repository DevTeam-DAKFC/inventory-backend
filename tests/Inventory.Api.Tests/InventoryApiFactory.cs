using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Inventory.Api.Tests;

public class InventoryApiFactory : WebApplicationFactory<Program>
{
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
    }
}
