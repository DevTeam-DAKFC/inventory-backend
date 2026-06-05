using Inventory.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
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
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<DbContextOptions<InventoryDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<InventoryDbContext>>();
            services.AddDbContext<InventoryDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
        });
    }
}
