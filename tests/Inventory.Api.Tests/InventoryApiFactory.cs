using Inventory.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Inventory.Api.Tests;

public class InventoryApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"InventoryApiTests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Server=test;Database=InventoryDbTest;User Id=sa;Password=unused;Encrypt=False;TrustServerCertificate=True;"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<InventoryDbContext>();
            services.RemoveAll<DbContextOptions<InventoryDbContext>>();
            RemoveDbContextOptionsConfigurations(services);

            services.AddDbContext<InventoryDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
        });
    }

    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();
    }

    private static void RemoveDbContextOptionsConfigurations(IServiceCollection services)
    {
        var descriptors = services
            .Where(service => service.ServiceType.FullName?.Contains("IDbContextOptionsConfiguration", StringComparison.Ordinal) == true)
            .ToArray();

        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }
    }
}
