using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Inventory.Api.Controllers;
using Inventory.Api.Contracts.Products;
using Inventory.Api.Data;
using Inventory.Api.Models;
using Inventory.Api.Products;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.Tests;

public class ProductImageEndpointTests : IClassFixture<InventoryApiFactory>, IAsyncLifetime
{
    private readonly InventoryApiFactory _factory;
    private readonly HttpClient _client;

    public ProductImageEndpointTests(InventoryApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/png", ".png")]
    [InlineData("image/webp", ".webp")]
    public async Task Post_Product_Image_Stores_And_Serves_Allowed_Image(
        string contentType,
        string extension)
    {
        var createdAt = DateTime.UtcNow.AddDays(-1);
        var product = CreateProduct(createdAt: createdAt);
        await SeedProductAsync(product);

        var response = await UploadAsync(product.Id.ToString(), contentType, [1, 2, 3, 4]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = await ReadJsonAsync(response);
        var root = payload.RootElement;
        var imageUrl = root.GetProperty("imageUrl").GetString();

        Assert.NotNull(imageUrl);
        Assert.StartsWith($"/uploads/products/{product.Id:D}-", imageUrl);
        Assert.EndsWith(extension, imageUrl);
        Assert.Equal(createdAt, root.GetProperty("createdAt").GetDateTime());
        Assert.True(root.GetProperty("updatedAt").GetDateTime() > createdAt);
        Assert.False(root.TryGetProperty("image", out _));
        Assert.DoesNotContain("base64", root.ToString(), StringComparison.OrdinalIgnoreCase);

        var filePath = GetFilePath(imageUrl!);
        Assert.True(File.Exists(filePath));

        var imageResponse = await _client.GetAsync(imageUrl);
        Assert.Equal(HttpStatusCode.OK, imageResponse.StatusCode);
        Assert.Equal(contentType, imageResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal([1, 2, 3, 4], await imageResponse.Content.ReadAsByteArrayAsync());

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var persisted = await dbContext.Products.SingleAsync(existing => existing.Id == product.Id);
        Assert.Equal(imageUrl, persisted.ImageUrl);
        Assert.Equal(createdAt, persisted.CreatedAt);
        Assert.NotNull(persisted.UpdatedAt);
    }

    [Fact]
    public async Task Post_Product_Image_Returns_404_For_Missing_Product()
    {
        var response = await UploadAsync(Guid.NewGuid().ToString(), "image/jpeg", [1]);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertErrorResponseAsync(response, "not_found", "productId");
    }

    [Fact]
    public async Task Post_Product_Image_Returns_400_For_Invalid_Guid()
    {
        var response = await UploadAsync("not-a-guid", "image/jpeg", [1]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorResponseAsync(response, "validation_error", "productId");
    }

    [Fact]
    public async Task Post_Product_Image_Returns_400_When_File_Is_Missing()
    {
        var product = CreateProduct();
        await SeedProductAsync(product);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("ignored"), "description");
        var response = await _client.PostAsync($"/products/{product.Id}/image", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorResponseAsync(response, "validation_error", "file");
    }

    [Fact]
    public async Task Post_Product_Image_Returns_400_For_Invalid_Form()
    {
        var product = CreateProduct();
        await SeedProductAsync(product);

        using var content = new StringContent("not-a-valid-multipart-form");
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=missing");
        var response = await _client.PostAsync($"/products/{product.Id}/image", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorResponseAsync(response, "validation_error", "body");
    }

    [Theory]
    [InlineData("image/jpeg", 0)]
    [InlineData("text/plain", 1)]
    [InlineData("image/png", (5 * 1024 * 1024) + 1)]
    public async Task Post_Product_Image_Returns_400_For_Invalid_File(string contentType, int size)
    {
        var product = CreateProduct();
        await SeedProductAsync(product);

        var response = await UploadAsync(product.Id.ToString(), contentType, new byte[size]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorResponseAsync(response, "validation_error", "file");
    }

    [Fact]
    public async Task Post_Product_Image_Replaces_Previous_Managed_Image()
    {
        var product = CreateProduct();
        await SeedProductAsync(product);

        var firstResponse = await UploadAsync(product.Id.ToString(), "image/jpeg", [1]);
        using var firstPayload = await ReadJsonAsync(firstResponse);
        var firstUrl = firstPayload.RootElement.GetProperty("imageUrl").GetString()!;
        var firstPath = GetFilePath(firstUrl);

        var secondResponse = await UploadAsync(product.Id.ToString(), "image/png", [2]);
        using var secondPayload = await ReadJsonAsync(secondResponse);
        var secondUrl = secondPayload.RootElement.GetProperty("imageUrl").GetString()!;

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.NotEqual(firstUrl, secondUrl);
        Assert.False(File.Exists(firstPath));
        Assert.True(File.Exists(GetFilePath(secondUrl)));
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync(secondUrl)).StatusCode);
    }

    [Fact]
    public async Task Post_Product_Image_Does_Not_Delete_File_For_Previous_External_Url()
    {
        var sentinelPath = Path.Combine(_factory.WebRootPath, "external-sentinel.jpg");
        Directory.CreateDirectory(_factory.WebRootPath);
        await File.WriteAllBytesAsync(sentinelPath, [9]);

        var product = CreateProduct(imageUrl: "https://images.example.com/external.jpg");
        await SeedProductAsync(product);

        var response = await UploadAsync(product.Id.ToString(), "image/webp", [1]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(File.Exists(sentinelPath));
    }

    [Fact]
    public async Task UploadProductImage_Deletes_New_File_When_Persistence_Fails()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase($"failing-image-upload-{Guid.NewGuid():N}")
            .Options;
        await using var dbContext = new FailingInventoryDbContext(options);
        var product = CreateProduct();
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync();
        dbContext.FailOnSave = true;

        var storage = new TrackingProductImageStorage();
        var controller = new ProductsController(dbContext, storage)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        var formFile = CreateFormFile("image/jpeg", [1]);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            controller.UploadProductImage(product.Id.ToString(), formFile, CancellationToken.None));

        Assert.Equal(storage.SavedUrl, Assert.Single(storage.DeletedUrls));
    }

    private async Task SeedProductAsync(Product product)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync();
    }

    private async Task<HttpResponseMessage> UploadAsync(string productId, string contentType, byte[] bytes)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "file", "../../unsafe-client-name.exe");

        return await _client.PostAsync($"/products/{productId}/image", content);
    }

    private string GetFilePath(string imageUrl) =>
        Path.Combine(
            _factory.WebRootPath,
            imageUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    private static FormFile CreateFormFile(string contentType, byte[] bytes)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", "unsafe.exe")
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private static Product CreateProduct(DateTime? createdAt = null, string? imageUrl = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "Image Product",
            Sku = $"IMG-{Guid.NewGuid():N}",
            Category = "Images",
            ImageUrl = imageUrl,
            MinStock = 1,
            IsActive = true,
            CreatedAt = createdAt ?? DateTime.UtcNow
        };

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static async Task AssertErrorResponseAsync(
        HttpResponseMessage response,
        string expectedCode,
        string expectedField)
    {
        using var payload = await ReadJsonAsync(response);
        var error = payload.RootElement.GetProperty("error");

        Assert.Equal(expectedCode, error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("requestId").GetString()));
        Assert.Contains(
            error.GetProperty("details").EnumerateArray(),
            detail => detail.GetProperty("field").GetString() == expectedField);
    }

    private sealed class FailingInventoryDbContext(DbContextOptions<InventoryDbContext> options)
        : InventoryDbContext(options)
    {
        public bool FailOnSave { get; set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            FailOnSave
                ? throw new DbUpdateException("Expected persistence failure.")
                : base.SaveChangesAsync(cancellationToken);
    }

    private sealed class TrackingProductImageStorage : IProductImageStorage
    {
        public string SavedUrl { get; } = $"/uploads/products/{Guid.NewGuid():N}.jpg";
        public List<string?> DeletedUrls { get; } = [];

        public Task<string> SaveAsync(
            Guid productId,
            Stream content,
            string extension,
            CancellationToken cancellationToken) =>
            Task.FromResult(SavedUrl);

        public void DeleteIfManaged(string? imageUrl) => DeletedUrls.Add(imageUrl);
    }
}
