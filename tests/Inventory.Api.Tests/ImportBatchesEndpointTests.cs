using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Inventory.Api.Auth;
using Inventory.Api.Data;
using Inventory.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.Tests;

public class ImportBatchesEndpointTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;
    private readonly HttpClient _client;

    public ImportBatchesEndpointTests(InventoryApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Post_Products_Requires_Authentication()
    {
        await _factory.ResetDatabaseAsync();

        using var content = CreateCsvContent(
            """
            name,sku,category,minStock
            Rice,CSV-AUTH-001,Food,5
            """);

        var response = await _client.PostAsync("/import-batches/products", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("unauthorized", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Post_Products_With_Valid_Csv_Creates_Products_And_Completed_Batch()
    {
        var token = await ResetAndCreateTokenAsync();
        using var content = CreateCsvContent(
            """
            name,sku,barcode,category,description,imageUrl,minStock,isActive
            Rice 1kg,CSV-VALID-001,700000000001,Food,White rice,,5,true
            Beans 900g,CSV-VALID-002,700000000002,Food,Black beans,,3,false
            """);

        var response = await SendAuthorizedAsync(HttpMethod.Post, "/import-batches/products", token, content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("completed", body.GetProperty("status").GetString());
        Assert.Equal(2, body.GetProperty("totalRows").GetInt32());
        Assert.Equal(2, body.GetProperty("processedRows").GetInt32());
        Assert.Equal(2, body.GetProperty("importedRows").GetInt32());
        Assert.Equal(0, body.GetProperty("failedRows").GetInt32());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.True(await db.Products.AnyAsync(product => product.Sku == "CSV-VALID-001"));
        var inactiveProduct = await db.Products.SingleAsync(product => product.Sku == "CSV-VALID-002");
        Assert.False(inactiveProduct.IsActive);
    }

    [Fact]
    public async Task Post_Products_With_Missing_Headers_Creates_Failed_Batch_With_Errors()
    {
        var token = await ResetAndCreateTokenAsync();
        using var content = CreateCsvContent(
            """
            name,category
            Rice,Food
            """);

        var response = await SendAuthorizedAsync(HttpMethod.Post, "/import-batches/products", token, content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("failed", body.GetProperty("status").GetString());
        Assert.Equal(0, body.GetProperty("totalRows").GetInt32());
        Assert.True(body.GetProperty("failedRows").GetInt32() >= 1);

        var batchId = body.GetProperty("id").GetString();
        var errors = await GetAuthorizedJsonAsync($"/import-batches/{batchId}/errors", token);
        Assert.True(errors.GetProperty("total").GetInt32() >= 1);
        Assert.Contains(
            errors.GetProperty("items").EnumerateArray(),
            error => error.GetProperty("field").GetString() == "sku");
    }

    [Fact]
    public async Task Post_Products_With_Invalid_Row_Imports_Valid_Rows_And_Stores_Errors()
    {
        var token = await ResetAndCreateTokenAsync();
        using var content = CreateCsvContent(
            """
            name,sku,category,minStock
            Cooking Oil 1L,CSV-MIXED-001,Grocery,4
            ,CSV-MIXED-002,,bad-number
            """);

        var response = await SendAuthorizedAsync(HttpMethod.Post, "/import-batches/products", token, content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("completed_with_errors", body.GetProperty("status").GetString());
        Assert.Equal(2, body.GetProperty("totalRows").GetInt32());
        Assert.Equal(2, body.GetProperty("processedRows").GetInt32());
        Assert.Equal(1, body.GetProperty("importedRows").GetInt32());
        Assert.Equal(1, body.GetProperty("failedRows").GetInt32());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.True(await db.Products.AnyAsync(product => product.Sku == "CSV-MIXED-001"));
        Assert.False(await db.Products.AnyAsync(product => product.Sku == "CSV-MIXED-002"));

        var batchId = body.GetProperty("id").GetString();
        var errors = await GetAuthorizedJsonAsync($"/import-batches/{batchId}/errors", token);
        Assert.True(errors.GetProperty("total").GetInt32() >= 1);
        Assert.Contains(
            errors.GetProperty("items").EnumerateArray(),
            error => error.GetProperty("rowNumber").GetInt32() == 3);
    }

    [Fact]
    public async Task Post_Products_With_Duplicate_Sku_Stores_Row_Error()
    {
        var token = await ResetAndCreateTokenAsync(seedExistingProduct: true);
        using var content = CreateCsvContent(
            """
            name,sku,barcode,category,minStock
            Existing Rice,CSV-EXISTING-001,800000000001,Food,2
            New Beans,CSV-UNIQUE-001,800000000002,Food,2
            """);

        var response = await SendAuthorizedAsync(HttpMethod.Post, "/import-batches/products", token, content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("completed_with_errors", body.GetProperty("status").GetString());
        Assert.Equal(1, body.GetProperty("importedRows").GetInt32());
        Assert.Equal(1, body.GetProperty("failedRows").GetInt32());

        var batchId = body.GetProperty("id").GetString();
        var errors = await GetAuthorizedJsonAsync($"/import-batches/{batchId}/errors", token);
        Assert.Contains(
            errors.GetProperty("items").EnumerateArray(),
            error =>
                error.GetProperty("field").GetString() == "sku"
                && error.GetProperty("message").GetString()!.Contains("already exists", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Get_ImportBatches_Returns_Paginated_Batches()
    {
        var token = await ResetAndCreateTokenAsync();
        var batchId = await CreateSuccessfulImportAsync(token, "CSV-LIST-001");

        var body = await GetAuthorizedJsonAsync("/import-batches?page=1&pageSize=20", token);

        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(20, body.GetProperty("pageSize").GetInt32());
        Assert.True(body.GetProperty("total").GetInt32() >= 1);
        Assert.Contains(
            body.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetString() == batchId);
    }

    [Fact]
    public async Task Get_ImportBatch_Returns_Detail()
    {
        var token = await ResetAndCreateTokenAsync();
        var batchId = await CreateSuccessfulImportAsync(token, "CSV-DETAIL-001");

        var body = await GetAuthorizedJsonAsync($"/import-batches/{batchId}", token);

        Assert.Equal(batchId, body.GetProperty("id").GetString());
        Assert.Equal("completed", body.GetProperty("status").GetString());
        Assert.Equal(1, body.GetProperty("totalRows").GetInt32());
        Assert.Equal(1, body.GetProperty("importedRows").GetInt32());
    }

    [Fact]
    public async Task Get_ImportBatchErrors_Returns_Paginated_Errors()
    {
        var token = await ResetAndCreateTokenAsync();
        using var content = CreateCsvContent(
            """
            name,sku,category,minStock
            ,CSV-ERRORS-001,,invalid
            """);
        var response = await SendAuthorizedAsync(HttpMethod.Post, "/import-batches/products", token, content);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var batchId = created.GetProperty("id").GetString();

        var body = await GetAuthorizedJsonAsync($"/import-batches/{batchId}/errors?page=1&pageSize=2", token);

        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(2, body.GetProperty("pageSize").GetInt32());
        Assert.True(body.GetProperty("total").GetInt32() >= 1);
        Assert.All(
            body.GetProperty("items").EnumerateArray(),
            item => Assert.Equal("validation_error", item.GetProperty("code").GetString()));
    }

    private async Task<string> CreateSuccessfulImportAsync(string token, string sku)
    {
        using var content = CreateCsvContent(
            $"""
            name,sku,category,minStock
            Imported Product,{sku},Food,1
            """);

        var response = await SendAuthorizedAsync(HttpMethod.Post, "/import-batches/products", token, content);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetString()!;
    }

    private async Task<string> ResetAndCreateTokenAsync(bool seedExistingProduct = false)
    {
        await _factory.ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var now = DateTime.UtcNow;
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Name = "Import User",
            Email = $"import-{Guid.NewGuid():N}@example.com",
            PasswordHash = "not-used",
            Role = UserRole.Collaborator,
            IsActive = true,
            CreatedAt = now
        };

        db.Users.Add(user);

        if (seedExistingProduct)
        {
            db.Products.Add(new Product
            {
                Id = Guid.NewGuid(),
                Name = "Existing Rice",
                Sku = "CSV-EXISTING-001",
                Barcode = "799999999999",
                Category = "Food",
                MinStock = 1,
                IsActive = true,
                CreatedAt = now
            });
        }

        await db.SaveChangesAsync();

        return tokens.CreateAccessToken(user).Value;
    }

    private static MultipartFormDataContent CreateCsvContent(string csv)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", "products.csv");
        return content;
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpMethod method,
        string url,
        string token,
        HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await _client.SendAsync(request);
    }

    private async Task<JsonElement> GetAuthorizedJsonAsync(string url, string token)
    {
        using var response = await SendAuthorizedAsync(HttpMethod.Get, url, token);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
