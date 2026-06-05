using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Inventory.Api.Data;
using Inventory.Api.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.Tests;

public class ProductEndpointTests : IClassFixture<InventoryApiFactory>, IAsyncLifetime
{
    private readonly InventoryApiFactory _factory;
    private readonly HttpClient _client;

    public ProductEndpointTests(InventoryApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Get_Products_Returns_Default_Paginated_Response_Without_Stock_Quantities()
    {
        await SeedProductsAsync(
            CreateProduct(name: "Coffee", sku: "COF-001", barcode: "111", category: "Grocery"),
            CreateProduct(name: "Tea", sku: "TEA-001", barcode: "222", category: "Grocery"));

        var response = await _client.GetAsync("/products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await ReadJsonAsync(response);
        Assert.Equal(2, payload.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(1, payload.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(20, payload.RootElement.GetProperty("pageSize").GetInt32());
        Assert.False(payload.RootElement.GetProperty("hasNextPage").GetBoolean());

        var item = payload.RootElement.GetProperty("items")[0];
        AssertProductDoesNotExposeStockQuantities(item);
    }

    [Theory]
    [InlineData("Arabica", "Arabica Beans")]
    [InlineData("SKU-SEARCH", "Sku Match")]
    [InlineData("789456123", "Barcode Match")]
    public async Task Get_Products_Searches_By_Name_Sku_And_Barcode(string query, string expectedName)
    {
        await SeedProductsAsync(
            CreateProduct(name: "Arabica Beans", sku: "COF-001", barcode: "111", category: "Grocery"),
            CreateProduct(name: "Sku Match", sku: "SKU-SEARCH", barcode: "222", category: "Tools"),
            CreateProduct(name: "Barcode Match", sku: "BAR-001", barcode: "789456123", category: "Hardware"));

        var response = await _client.GetAsync($"/products?q={Uri.EscapeDataString(query)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await ReadJsonAsync(response);
        var item = Assert.Single(payload.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(expectedName, item.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Get_Products_Filters_By_Category_IsActive_LowStock_And_Combines_Filters()
    {
        var lowStockProduct = CreateProduct(name: "Low Stock", sku: "LOW-001", category: "Grocery", minStock: 5);
        var enoughStockProduct = CreateProduct(name: "Enough Stock", sku: "ENO-001", category: "Grocery", minStock: 5);
        var inactiveProduct = CreateProduct(name: "Inactive Low", sku: "INA-001", category: "Grocery", minStock: 5, isActive: false);
        var otherCategoryProduct = CreateProduct(name: "Hardware Low", sku: "HAR-001", category: "Hardware", minStock: 5);
        var multiStockProduct = CreateProduct(name: "Multi Stock Low", sku: "MUL-001", category: "Grocery", minStock: 5);
        var noStockProduct = CreateProduct(name: "No Stock", sku: "NOS-001", category: "Grocery", minStock: 5);

        var branchId = Guid.NewGuid();
        var secondBranchId = Guid.NewGuid();
        await SeedProductsAsync(
            new[] { lowStockProduct, enoughStockProduct, inactiveProduct, otherCategoryProduct, multiStockProduct, noStockProduct },
            new[] { CreateBranch(branchId), CreateBranch(secondBranchId, "Secondary Branch") },
            new[]
            {
                CreateStock(lowStockProduct.Id, branchId, availableQuantity: 5),
                CreateStock(enoughStockProduct.Id, branchId, availableQuantity: 6),
                CreateStock(inactiveProduct.Id, branchId, availableQuantity: 3),
                CreateStock(otherCategoryProduct.Id, branchId, availableQuantity: 1),
                CreateStock(multiStockProduct.Id, branchId, availableQuantity: 8),
                CreateStock(multiStockProduct.Id, secondBranchId, availableQuantity: 4)
            });

        var categoryResponse = await _client.GetAsync("/products?category=Grocery");
        var categoryPayload = await ReadJsonAsync(categoryResponse);
        Assert.Equal(5, categoryPayload.RootElement.GetProperty("total").GetInt32());

        var activeResponse = await _client.GetAsync("/products?isActive=false");
        var activePayload = await ReadJsonAsync(activeResponse);
        var inactiveItem = Assert.Single(activePayload.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("Inactive Low", inactiveItem.GetProperty("name").GetString());

        var lowStockResponse = await _client.GetAsync("/products?lowStockOnly=true");
        var lowStockPayload = await ReadJsonAsync(lowStockResponse);
        var lowStockNames = lowStockPayload.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains("Low Stock", lowStockNames);
        Assert.Contains("Inactive Low", lowStockNames);
        Assert.Contains("Hardware Low", lowStockNames);
        Assert.Contains("Multi Stock Low", lowStockNames);
        Assert.DoesNotContain("Enough Stock", lowStockNames);
        Assert.DoesNotContain("No Stock", lowStockNames);

        var combinedResponse = await _client.GetAsync("/products?category=Grocery&isActive=true&lowStockOnly=true");
        var combinedPayload = await ReadJsonAsync(combinedResponse);
        var combinedNames = combinedPayload.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .ToArray();
        Assert.Equal(2, combinedNames.Length);
        Assert.Contains("Low Stock", combinedNames);
        Assert.Contains("Multi Stock Low", combinedNames);
    }

    [Fact]
    public async Task Get_Products_Respects_Page_And_PageSize()
    {
        await SeedProductsAsync(
            CreateProduct(name: "Alpha", sku: "A-001"),
            CreateProduct(name: "Bravo", sku: "B-001"),
            CreateProduct(name: "Charlie", sku: "C-001"));

        var response = await _client.GetAsync("/products?page=2&pageSize=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await ReadJsonAsync(response);
        Assert.Equal(3, payload.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(2, payload.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(1, payload.RootElement.GetProperty("pageSize").GetInt32());
        Assert.True(payload.RootElement.GetProperty("hasNextPage").GetBoolean());

        var item = Assert.Single(payload.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("Bravo", item.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("/products?page=0", "page")]
    [InlineData("/products?pageSize=0", "pageSize")]
    [InlineData("/products?pageSize=101", "pageSize")]
    public async Task Get_Products_Returns_Validation_Error_For_Invalid_Pagination(string url, string expectedField)
    {
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorResponseAsync(response, "validation_error", expectedField);
    }

    [Theory]
    [InlineData("/products?page=abc", "page")]
    [InlineData("/products?pageSize=abc", "pageSize")]
    [InlineData("/products?isActive=abc", "isActive")]
    [InlineData("/products?lowStockOnly=abc", "lowStockOnly")]
    public async Task Get_Products_Returns_ModelBinding_Error_For_Invalid_Query_Types(string url, string expectedField)
    {
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorResponseAsync(response, "validation_error", expectedField);
    }

    [Fact]
    public async Task Post_Products_Creates_Product_And_Does_Not_Create_Stock()
    {
        var request = new
        {
            name = "  Created Product  ",
            sku = "  SKU-CREATED  ",
            barcode = "  BAR-CREATED  ",
            category = "  Grocery  ",
            description = "  Description  ",
            imageUrl = "https://example.com/product.png",
            minStock = 3
        };

        var response = await _client.PostAsJsonAsync("/products", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await ReadJsonAsync(response);
        var productId = payload.RootElement.GetProperty("id").GetGuid();
        Assert.Equal($"/products/{productId}", response.Headers.Location!.AbsolutePath);
        Assert.Equal("Created Product", payload.RootElement.GetProperty("name").GetString());
        Assert.Equal("SKU-CREATED", payload.RootElement.GetProperty("sku").GetString());
        Assert.Equal("BAR-CREATED", payload.RootElement.GetProperty("barcode").GetString());
        Assert.Equal("Grocery", payload.RootElement.GetProperty("category").GetString());
        Assert.Equal("Description", payload.RootElement.GetProperty("description").GetString());
        Assert.Equal("https://example.com/product.png", payload.RootElement.GetProperty("imageUrl").GetString());
        Assert.Equal(3, payload.RootElement.GetProperty("minStock").GetInt32());
        Assert.True(payload.RootElement.GetProperty("isActive").GetBoolean());
        Assert.True(payload.RootElement.GetProperty("createdAt").GetDateTime() > DateTime.MinValue);
        Assert.Equal(JsonValueKind.Null, payload.RootElement.GetProperty("updatedAt").ValueKind);
        AssertProductDoesNotExposeStockQuantities(payload.RootElement);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Empty(dbContext.Stocks);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Post_Products_Uses_Expected_IsActive_Value(bool? isActive)
    {
        var request = new Dictionary<string, object?>
        {
            ["name"] = "Active Product",
            ["sku"] = $"ACTIVE-{Guid.NewGuid()}",
            ["category"] = "Grocery",
            ["minStock"] = 1
        };

        if (isActive.HasValue)
        {
            request["isActive"] = isActive.Value;
        }

        var response = await _client.PostAsJsonAsync("/products", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await ReadJsonAsync(response);
        Assert.Equal(isActive ?? true, payload.RootElement.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Post_Products_Allows_Null_Barcode_And_Multiple_Products_Without_Barcode()
    {
        var first = await _client.PostAsJsonAsync("/products", new
        {
            name = "No Barcode 1",
            sku = "NO-BAR-1",
            category = "Grocery",
            minStock = 1
        });
        var second = await _client.PostAsJsonAsync("/products", new
        {
            name = "No Barcode 2",
            sku = "NO-BAR-2",
            barcode = (string?)null,
            category = "Grocery",
            minStock = 1
        });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task Post_Products_Normalizes_Empty_Optional_Text_Fields_To_Null()
    {
        var response = await _client.PostAsJsonAsync("/products", new
        {
            name = "Optional Empty Product",
            sku = "OPTIONAL-EMPTY",
            barcode = "   ",
            category = "Grocery",
            description = "",
            imageUrl = "   ",
            minStock = 1
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Null, payload.RootElement.GetProperty("barcode").ValueKind);
        Assert.Equal(JsonValueKind.Null, payload.RootElement.GetProperty("description").ValueKind);
        Assert.Equal(JsonValueKind.Null, payload.RootElement.GetProperty("imageUrl").ValueKind);

        var second = await _client.PostAsJsonAsync("/products", new
        {
            name = "Optional Empty Product 2",
            sku = "OPTIONAL-EMPTY-2",
            barcode = "",
            category = "Grocery",
            minStock = 1
        });

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Theory]
    [InlineData("{", "$")]
    [InlineData("", "body")]
    [InlineData("null", "body")]
    [InlineData("""{"name":"Name","sku":"SKU","category":"Grocery","minStock":"abc"}""", "$.minStock")]
    public async Task Post_Products_Returns_ErrorResponse_For_Invalid_Body(string body, string expectedField)
    {
        var response = await _client.PostAsync(
            "/products",
            new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorResponseAsync(response, "validation_error", expectedField);
    }

    [Theory]
    [MemberData(nameof(InvalidCreateRequests))]
    public async Task Post_Products_Returns_Validation_Error_For_Invalid_Request(object request, string expectedField)
    {
        var response = await _client.PostAsJsonAsync("/products", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorResponseAsync(response, "validation_error", expectedField);
    }

    public static IEnumerable<object[]> InvalidCreateRequests()
    {
        yield return new object[] { new { sku = "SKU", category = "Grocery", minStock = 1 }, "name" };
        yield return new object[] { new { name = "   ", sku = "SKU", category = "Grocery", minStock = 1 }, "name" };
        yield return new object[] { new { name = "Name", category = "Grocery", minStock = 1 }, "sku" };
        yield return new object[] { new { name = "Name", sku = "SKU", minStock = 1 }, "category" };
        yield return new object[] { new { name = "Name", sku = "SKU", category = "Grocery", minStock = -1 }, "minStock" };
        yield return new object[] { new { name = new string('a', 151), sku = "SKU", category = "Grocery", minStock = 1 }, "name" };
        yield return new object[] { new { name = "Name", sku = new string('a', 101), category = "Grocery", minStock = 1 }, "sku" };
        yield return new object[] { new { name = "Name", sku = "SKU", barcode = new string('a', 33), category = "Grocery", minStock = 1 }, "barcode" };
        yield return new object[] { new { name = "Name", sku = "SKU", category = new string('a', 101), minStock = 1 }, "category" };
        yield return new object[] { new { name = "Name", sku = "SKU", category = "Grocery", description = new string('a', 501), minStock = 1 }, "description" };
        yield return new object[] { new { name = "Name", sku = "SKU", category = "Grocery", imageUrl = new string('a', 1001), minStock = 1 }, "imageUrl" };
        yield return new object[] { new { name = "Name", sku = "SKU", category = "Grocery", imageUrl = "not-a-uri", minStock = 1 }, "imageUrl" };
    }

    [Fact]
    public async Task Post_Products_Returns_Conflict_For_Duplicate_Sku_And_Barcode()
    {
        await SeedProductsAsync(CreateProduct(name: "Existing", sku: "DUP-SKU", barcode: "DUP-BAR"));

        var duplicateSku = await _client.PostAsJsonAsync("/products", new
        {
            name = "Duplicate Sku",
            sku = "DUP-SKU",
            category = "Grocery",
            minStock = 1
        });
        var duplicateBarcode = await _client.PostAsJsonAsync("/products", new
        {
            name = "Duplicate Barcode",
            sku = "UNIQUE-SKU",
            barcode = "DUP-BAR",
            category = "Grocery",
            minStock = 1
        });

        Assert.Equal(HttpStatusCode.Conflict, duplicateSku.StatusCode);
        await AssertErrorResponseAsync(duplicateSku, "conflict", "sku");

        Assert.Equal(HttpStatusCode.Conflict, duplicateBarcode.StatusCode);
        await AssertErrorResponseAsync(duplicateBarcode, "conflict", "barcode");
    }

    [Fact]
    public async Task Get_Product_By_Id_Returns_Existing_Product_Including_Inactive_Without_Stock_Quantities()
    {
        var product = CreateProduct(name: "Inactive Product", sku: "INACTIVE-001", isActive: false);
        product.CreatedAt = DateTime.SpecifyKind(product.CreatedAt, DateTimeKind.Unspecified);
        product.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        await SeedProductsAsync(product);

        var response = await _client.GetAsync($"/products/{product.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await ReadJsonAsync(response);
        Assert.Equal(product.Id, payload.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("Inactive Product", payload.RootElement.GetProperty("name").GetString());
        Assert.False(payload.RootElement.GetProperty("isActive").GetBoolean());
        Assert.EndsWith("Z", payload.RootElement.GetProperty("createdAt").GetString());
        Assert.EndsWith("Z", payload.RootElement.GetProperty("updatedAt").GetString());
        AssertProductDoesNotExposeStockQuantities(payload.RootElement);
    }

    [Fact]
    public async Task Get_Product_By_Id_Returns_NotFound_Error_For_Unknown_Product()
    {
        var response = await _client.GetAsync($"/products/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertErrorResponseAsync(response, "not_found", "productId");
    }

    [Fact]
    public async Task Get_Product_By_Id_Returns_Validation_Error_For_Invalid_Guid()
    {
        var response = await _client.GetAsync("/products/not-a-guid");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorResponseAsync(response, "validation_error", "productId");
    }

    [Theory]
    [MemberData(nameof(SingleFieldPatchRequests))]
    public async Task Patch_Product_Updates_Single_Field_And_Conserves_Omitted_Fields(
        object request,
        string expectedField,
        object expectedValue)
    {
        var createdAt = DateTime.UtcNow.AddDays(-1);
        var product = CreateProduct(name: "Original Name", sku: "ORIGINAL-SKU", barcode: "ORIGINAL-BAR", category: "Original", minStock: 5);
        product.Description = "Original Description";
        product.ImageUrl = "https://example.com/original.png";
        product.CreatedAt = createdAt;
        await SeedProductsAsync(product);

        var response = await PatchAsJsonAsync($"/products/{product.Id}", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await ReadJsonAsync(response);
        AssertJsonValue(payload.RootElement, expectedField, expectedValue);
        Assert.Equal(createdAt, payload.RootElement.GetProperty("createdAt").GetDateTime());
        Assert.True(payload.RootElement.GetProperty("updatedAt").GetDateTime() > createdAt);

        if (expectedField != "name")
        {
            Assert.Equal("Original Name", payload.RootElement.GetProperty("name").GetString());
        }

        if (expectedField != "sku")
        {
            Assert.Equal("ORIGINAL-SKU", payload.RootElement.GetProperty("sku").GetString());
        }

        if (expectedField != "category")
        {
            Assert.Equal("Original", payload.RootElement.GetProperty("category").GetString());
        }
    }

    public static IEnumerable<object[]> SingleFieldPatchRequests()
    {
        yield return new object[] { new { name = "Updated Name" }, "name", "Updated Name" };
        yield return new object[] { new { sku = "UPDATED-SKU" }, "sku", "UPDATED-SKU" };
        yield return new object[] { new { category = "Updated Category" }, "category", "Updated Category" };
        yield return new object[] { new { minStock = 9 }, "minStock", 9 };
        yield return new object[] { new { barcode = "UPDATED-BAR" }, "barcode", "UPDATED-BAR" };
    }

    [Fact]
    public async Task Patch_Product_Updates_Multiple_Fields_Trims_Outer_Spaces_And_Keeps_Internal_Spaces()
    {
        var createdAt = DateTime.UtcNow.AddDays(-1);
        var product = CreateProduct(name: "Original", sku: "SKU-ORIGINAL", barcode: "BAR-ORIGINAL", category: "Original", minStock: 1);
        product.CreatedAt = createdAt;
        await SeedProductsAsync(product);

        var response = await PatchAsJsonAsync($"/products/{product.Id}", new
        {
            name = "  Updated  Product  ",
            sku = "  SKU-UPDATED  ",
            category = "  Updated  Category  ",
            description = "  Updated  Description  ",
            imageUrl = "  https://example.com/updated.png  ",
            minStock = 4
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await ReadJsonAsync(response);
        Assert.Equal("Updated  Product", payload.RootElement.GetProperty("name").GetString());
        Assert.Equal("SKU-UPDATED", payload.RootElement.GetProperty("sku").GetString());
        Assert.Equal("Updated  Category", payload.RootElement.GetProperty("category").GetString());
        Assert.Equal("Updated  Description", payload.RootElement.GetProperty("description").GetString());
        Assert.Equal("https://example.com/updated.png", payload.RootElement.GetProperty("imageUrl").GetString());
        Assert.Equal(4, payload.RootElement.GetProperty("minStock").GetInt32());
        Assert.Equal("BAR-ORIGINAL", payload.RootElement.GetProperty("barcode").GetString());
        Assert.Equal(createdAt, payload.RootElement.GetProperty("createdAt").GetDateTime());
        Assert.True(payload.RootElement.GetProperty("updatedAt").GetDateTime() > createdAt);
        AssertProductDoesNotExposeStockQuantities(payload.RootElement);
    }

    [Fact]
    public async Task Patch_Product_Clears_Optional_Fields_With_Null_Empty_And_Spaces()
    {
        var product = CreateProduct(name: "Original", sku: "SKU-OPTIONAL", barcode: "BAR-OPTIONAL", category: "Original", minStock: 1);
        product.Description = "Description";
        product.ImageUrl = "https://example.com/image.png";
        await SeedProductsAsync(product);

        var clearWithNull = await PatchAsJsonAsync($"/products/{product.Id}", new
        {
            barcode = (string?)null,
            description = (string?)null,
            imageUrl = (string?)null
        });

        Assert.Equal(HttpStatusCode.OK, clearWithNull.StatusCode);
        var nullPayload = await ReadJsonAsync(clearWithNull);
        Assert.Equal(JsonValueKind.Null, nullPayload.RootElement.GetProperty("barcode").ValueKind);
        Assert.Equal(JsonValueKind.Null, nullPayload.RootElement.GetProperty("description").ValueKind);
        Assert.Equal(JsonValueKind.Null, nullPayload.RootElement.GetProperty("imageUrl").ValueKind);

        var clearWithEmpty = await PatchAsJsonAsync($"/products/{product.Id}", new
        {
            barcode = "",
            description = "   ",
            imageUrl = ""
        });

        Assert.Equal(HttpStatusCode.OK, clearWithEmpty.StatusCode);
        var emptyPayload = await ReadJsonAsync(clearWithEmpty);
        Assert.Equal(JsonValueKind.Null, emptyPayload.RootElement.GetProperty("barcode").ValueKind);
        Assert.Equal(JsonValueKind.Null, emptyPayload.RootElement.GetProperty("description").ValueKind);
        Assert.Equal(JsonValueKind.Null, emptyPayload.RootElement.GetProperty("imageUrl").ValueKind);
    }

    [Fact]
    public async Task Patch_Product_Conserves_Optional_Fields_When_Omitted()
    {
        var product = CreateProduct(name: "Original", sku: "SKU-CONSERVE", barcode: "BAR-CONSERVE", category: "Original", minStock: 1);
        product.Description = "Description";
        product.ImageUrl = "https://example.com/image.png";
        await SeedProductsAsync(product);

        var response = await PatchAsJsonAsync($"/products/{product.Id}", new { name = "Updated" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await ReadJsonAsync(response);
        Assert.Equal("BAR-CONSERVE", payload.RootElement.GetProperty("barcode").GetString());
        Assert.Equal("Description", payload.RootElement.GetProperty("description").GetString());
        Assert.Equal("https://example.com/image.png", payload.RootElement.GetProperty("imageUrl").GetString());
    }

    [Fact]
    public async Task Patch_Product_Uses_AspNetCore_CaseInsensitive_Json_Property_Convention()
    {
        var product = CreateProduct(name: "Original", sku: "SKU-CASE");
        await SeedProductsAsync(product);

        var response = await PatchRawAsync($"/products/{product.Id}", """{"Name":"Updated Case","MinStock":6}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await ReadJsonAsync(response);
        Assert.Equal("Updated Case", payload.RootElement.GetProperty("name").GetString());
        Assert.Equal(6, payload.RootElement.GetProperty("minStock").GetInt32());
    }

    [Theory]
    [InlineData("{}", "body")]
    [InlineData("null", "body")]
    [InlineData("""{"isActive":false}""", "isActive")]
    public async Task Patch_Product_Returns_Validation_Error_For_Empty_Null_Or_Non_Updatable_Body(string body, string expectedField)
    {
        var product = CreateProduct(name: "Original", sku: "SKU-BODY");
        await SeedProductsAsync(product);

        var response = await PatchRawAsync($"/products/{product.Id}", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorResponseAsync(response, "validation_error", expectedField);
    }

    [Fact]
    public async Task Patch_Product_Returns_Validation_Error_For_Empty_Request_Body()
    {
        var product = CreateProduct(name: "Original", sku: "SKU-EMPTY-BODY");
        await SeedProductsAsync(product);

        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/products/{product.Id}")
        {
            Content = new StringContent("", Encoding.UTF8, "application/json")
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorResponseAsync(response, "validation_error", "body");
    }

    [Fact]
    public async Task Patch_Product_Returns_NotFound_And_Invalid_Guid_Errors()
    {
        var notFound = await PatchAsJsonAsync($"/products/{Guid.NewGuid()}", new { name = "Updated" });
        var invalidGuid = await PatchAsJsonAsync("/products/not-a-guid", new { name = "Updated" });

        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        await AssertErrorResponseAsync(notFound, "not_found", "productId");

        Assert.Equal(HttpStatusCode.BadRequest, invalidGuid.StatusCode);
        await AssertErrorResponseAsync(invalidGuid, "validation_error", "productId");
    }

    [Theory]
    [MemberData(nameof(InvalidPatchRequests))]
    public async Task Patch_Product_Returns_Validation_Error_For_Invalid_Request(string body, string expectedField)
    {
        var product = CreateProduct(name: "Original", sku: "SKU-INVALID-PATCH", category: "Original");
        await SeedProductsAsync(product);

        var response = await PatchRawAsync($"/products/{product.Id}", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorResponseAsync(response, "validation_error", expectedField);
    }

    public static IEnumerable<object[]> InvalidPatchRequests()
    {
        yield return new object[] { """{"name":""}""", "name" };
        yield return new object[] { """{"name":null}""", "name" };
        yield return new object[] { """{"sku":"   "}""", "sku" };
        yield return new object[] { """{"sku":null}""", "sku" };
        yield return new object[] { """{"category":null}""", "category" };
        yield return new object[] { """{"category":"   "}""", "category" };
        yield return new object[] { $$"""{"name":"{{new string('a', 151)}}"}""", "name" };
        yield return new object[] { $$"""{"sku":"{{new string('a', 101)}}"}""", "sku" };
        yield return new object[] { $$"""{"barcode":"{{new string('a', 33)}}"}""", "barcode" };
        yield return new object[] { $$"""{"category":"{{new string('a', 101)}}"}""", "category" };
        yield return new object[] { $$"""{"description":"{{new string('a', 501)}}"}""", "description" };
        yield return new object[] { $$"""{"imageUrl":"{{new string('a', 1001)}}"}""", "imageUrl" };
        yield return new object[] { """{"minStock":-1}""", "minStock" };
        yield return new object[] { """{"imageUrl":"not-a-uri"}""", "imageUrl" };
        yield return new object[] { """{"minStock":null}""", "minStock" };
        yield return new object[] { """{"minStock":1.5}""", "minStock" };
        yield return new object[] { """{"minStock":"abc"}""", "minStock" };
        yield return new object[] { """{"MinStock":"abc"}""", "minStock" };
        yield return new object[] { """{"minStock":true}""", "minStock" };
        yield return new object[] { """{"minStock":{}}""", "minStock" };
        yield return new object[] { """{"minStock":[]}""", "minStock" };
        yield return new object[] { """{"id":"4ea15cc4-7f4d-4062-8c4a-2213be43a511"}""", "id" };
        yield return new object[] { """{"createdAt":"2026-01-01T00:00:00Z"}""", "createdAt" };
        yield return new object[] { """{"updatedAt":"2026-01-01T00:00:00Z"}""", "updatedAt" };
        yield return new object[] { """{"stock":1}""", "stock" };
        yield return new object[] { """{"quantity":1}""", "quantity" };
        yield return new object[] { """{"currentStock":1}""", "currentStock" };
        yield return new object[] { """{"availableQuantity":1}""", "availableQuantity" };
        yield return new object[] { "{", "$" };
    }

    [Fact]
    public async Task Patch_Product_Handles_Sku_And_Barcode_Conflicts_And_Allows_Current_Values()
    {
        var first = CreateProduct(name: "First", sku: "FIRST-SKU", barcode: "FIRST-BAR");
        var second = CreateProduct(name: "Second", sku: "SECOND-SKU", barcode: "SECOND-BAR");
        await SeedProductsAsync(first, second);

        var duplicateSku = await PatchAsJsonAsync($"/products/{first.Id}", new { sku = "SECOND-SKU" });
        var sameSku = await PatchAsJsonAsync($"/products/{first.Id}", new { sku = "FIRST-SKU" });
        var duplicateBarcode = await PatchAsJsonAsync($"/products/{first.Id}", new { barcode = "SECOND-BAR" });
        var sameBarcode = await PatchAsJsonAsync($"/products/{first.Id}", new { barcode = "FIRST-BAR" });
        var clearBarcode = await PatchAsJsonAsync($"/products/{first.Id}", new { barcode = (string?)null });
        var secondClearBarcode = await PatchAsJsonAsync($"/products/{second.Id}", new { barcode = "   " });

        Assert.Equal(HttpStatusCode.Conflict, duplicateSku.StatusCode);
        await AssertErrorResponseAsync(duplicateSku, "conflict", "sku");

        Assert.Equal(HttpStatusCode.OK, sameSku.StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, duplicateBarcode.StatusCode);
        await AssertErrorResponseAsync(duplicateBarcode, "conflict", "barcode");

        Assert.Equal(HttpStatusCode.OK, sameBarcode.StatusCode);
        Assert.Equal(HttpStatusCode.OK, clearBarcode.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondClearBarcode.StatusCode);
    }

    [Fact]
    public async Task Patch_Product_Deactivate_Active_Product_Returns_NoContent_And_Preserves_Stock()
    {
        var product = CreateProduct(name: "Active Product", sku: "ACTIVE-DEACTIVATE", minStock: 1);
        var branchId = Guid.NewGuid();
        var stock = CreateStock(product.Id, branchId, availableQuantity: 7);
        await SeedProductsAsync(
            new[] { product },
            new[] { CreateBranch(branchId) },
            new[] { stock });

        var response = await _client.PatchAsync($"/products/{product.Id}/deactivate", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var updatedProduct = await dbContext.Products.FindAsync(product.Id);
        var persistedStock = await dbContext.Stocks.FindAsync(stock.Id);

        Assert.NotNull(updatedProduct);
        Assert.False(updatedProduct!.IsActive);
        Assert.NotNull(updatedProduct.UpdatedAt);
        Assert.NotNull(persistedStock);
        Assert.Equal(7, persistedStock!.AvailableQuantity);
        Assert.Equal(product.Id, persistedStock.ProductId);

        var detail = await _client.GetAsync($"/products/{product.Id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var detailPayload = await ReadJsonAsync(detail);
        Assert.False(detailPayload.RootElement.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Patch_Product_Deactivate_Inactive_Product_Is_Idempotent()
    {
        var updatedAt = DateTime.UtcNow.AddDays(-1);
        var product = CreateProduct(name: "Inactive Product", sku: "INACTIVE-DEACTIVATE", isActive: false);
        product.UpdatedAt = updatedAt;
        await SeedProductsAsync(product);

        var response = await _client.PatchAsync($"/products/{product.Id}/deactivate", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var persisted = await dbContext.Products.FindAsync(product.Id);

        Assert.NotNull(persisted);
        Assert.False(persisted!.IsActive);
        Assert.Equal(updatedAt, persisted.UpdatedAt);
    }

    [Fact]
    public async Task Patch_Product_Deactivate_Returns_NotFound_And_Invalid_Guid_Errors()
    {
        var notFound = await _client.PatchAsync($"/products/{Guid.NewGuid()}/deactivate", content: null);
        var invalidGuid = await _client.PatchAsync("/products/not-a-guid/deactivate", content: null);

        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        await AssertErrorResponseAsync(notFound, "not_found", "productId");

        Assert.Equal(HttpStatusCode.BadRequest, invalidGuid.StatusCode);
        await AssertErrorResponseAsync(invalidGuid, "validation_error", "productId");
    }

    private async Task SeedProductsAsync(params Product[] products) =>
        await SeedProductsAsync(products, Array.Empty<Branch>(), Array.Empty<Stock>());

    private async Task SeedProductsAsync(Product[] products, Branch[] branches, Stock[] stocks)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        dbContext.Products.AddRange(products);
        dbContext.Branches.AddRange(branches);
        dbContext.Stocks.AddRange(stocks);
        await dbContext.SaveChangesAsync();
    }

    private static Product CreateProduct(
        string name,
        string sku,
        string? barcode = null,
        string category = "Default",
        int minStock = 1,
        bool isActive = true) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Sku = sku,
            Barcode = barcode,
            Category = category,
            MinStock = minStock,
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow
        };

    private static Branch CreateBranch(Guid id, string name = "Main Branch") =>
        new()
        {
            Id = id,
            Name = name,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

    private static Stock CreateStock(Guid productId, Guid branchId, int availableQuantity) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            BranchId = branchId,
            AvailableQuantity = availableQuantity,
            MinStock = 1
        };

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }

    private async Task<HttpResponseMessage> PatchAsJsonAsync(string url, object request) =>
        await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = JsonContent.Create(request)
        });

    private async Task<HttpResponseMessage> PatchRawAsync(string url, string body) =>
        await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });

    private static async Task AssertErrorResponseAsync(HttpResponseMessage response, string expectedCode, string expectedField)
    {
        var payload = await ReadJsonAsync(response);
        var error = payload.RootElement.GetProperty("error");

        Assert.Equal(expectedCode, error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("requestId").GetString()));

        var details = error.GetProperty("details").EnumerateArray().ToArray();
        Assert.Contains(details, detail => detail.GetProperty("field").GetString() == expectedField);
        Assert.All(details, detail => Assert.False(string.IsNullOrWhiteSpace(detail.GetProperty("message").GetString())));
    }

    private static void AssertProductDoesNotExposeStockQuantities(JsonElement product)
    {
        Assert.False(product.TryGetProperty("stock", out _));
        Assert.False(product.TryGetProperty("quantity", out _));
        Assert.False(product.TryGetProperty("currentStock", out _));
        Assert.False(product.TryGetProperty("availableQuantity", out _));
    }

    private static void AssertJsonValue(JsonElement element, string propertyName, object expectedValue)
    {
        var property = element.GetProperty(propertyName);
        switch (expectedValue)
        {
            case int intValue:
                Assert.Equal(intValue, property.GetInt32());
                break;
            case string stringValue:
                Assert.Equal(stringValue, property.GetString());
                break;
            default:
                throw new InvalidOperationException($"Unexpected expected value type {expectedValue.GetType()}.");
        }
    }
}
