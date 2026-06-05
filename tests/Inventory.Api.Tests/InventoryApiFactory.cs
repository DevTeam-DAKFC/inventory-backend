using Inventory.Api.Data;
using Inventory.Api.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Inventory.Api.Tests;

public class InventoryApiFactory : WebApplicationFactory<Program>
{
    private const string TestOrBearerSchemeName = "TestOrBearer";
    private const string TestUserRoleHeaderName = "X-Test-User-Role";
    private readonly string _databaseName = $"inventory-tests-{Guid.NewGuid():N}";
    private readonly string _webRootPath = Path.Combine(
        Path.GetTempPath(),
        $"inventory-api-tests-{Guid.NewGuid():N}");

    public string WebRootPath => _webRootPath;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseWebRoot(_webRootPath);
        builder.ConfigureLogging(logging => logging.ClearProviders());

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

            services.AddDbContext<InventoryDbContext>(options => options
                .UseInMemoryDatabase(_databaseName)
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

            services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestOrBearerSchemeName;
                    options.DefaultChallengeScheme = TestOrBearerSchemeName;
                    options.DefaultForbidScheme = TestOrBearerSchemeName;
                })
                .AddPolicyScheme(TestOrBearerSchemeName, displayName: null, options =>
                {
                    options.ForwardDefaultSelector = context =>
                        context.Request.Headers.ContainsKey(TestUserRoleHeaderName)
                            ? TestAuthenticationHandler.SchemeName
                            : JwtBearerDefaults.AuthenticationScheme;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    options => { });
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

    public async Task ResetDatabaseAsync(params Branch[] branches)
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

        if (branches.Length > 0)
        {
            dbContext.Branches.AddRange(branches);
            await dbContext.SaveChangesAsync();
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
