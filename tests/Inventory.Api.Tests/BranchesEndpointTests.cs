using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventory.Api.Dtos;
using Inventory.Api.Models;

namespace Inventory.Api.Tests;

public class BranchesEndpointTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;

    public BranchesEndpointTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_Branches_Returns_Active_Branches_By_Default()
    {
        var activeBranch = CreateBranch("Central", isActive: true);
        var inactiveBranch = CreateBranch("Closed", isActive: false);
        await _factory.ResetDatabaseAsync(activeBranch, inactiveBranch);
        var client = CreateClient(UserRole.Admin);

        var branches = await client.GetFromJsonAsync<List<BranchResponse>>("/branches");

        Assert.NotNull(branches);
        var branch = Assert.Single(branches);
        Assert.Equal(activeBranch.Id, branch.Id);
    }

    [Fact]
    public async Task Get_Branches_Supports_Active_Filter_For_Admin()
    {
        var activeBranch = CreateBranch("Central", isActive: true);
        var inactiveBranch = CreateBranch("Closed", isActive: false);
        await _factory.ResetDatabaseAsync(activeBranch, inactiveBranch);
        var client = CreateClient(UserRole.Admin);

        var branches = await client.GetFromJsonAsync<List<BranchResponse>>("/branches?active=false");

        Assert.NotNull(branches);
        var branch = Assert.Single(branches);
        Assert.Equal(inactiveBranch.Id, branch.Id);
        Assert.False(branch.IsActive);
    }

    [Fact]
    public async Task Get_Branches_Does_Not_Return_Inactive_Branches_To_Collaborators()
    {
        var activeBranch = CreateBranch("Central", isActive: true);
        var inactiveBranch = CreateBranch("Closed", isActive: false);
        await _factory.ResetDatabaseAsync(activeBranch, inactiveBranch);
        var client = CreateClient(UserRole.Collaborator);

        var branches = await client.GetFromJsonAsync<List<BranchResponse>>("/branches?active=false");

        Assert.NotNull(branches);
        var branch = Assert.Single(branches);
        Assert.Equal(activeBranch.Id, branch.Id);
    }

    [Fact]
    public async Task Post_Branches_Creates_Branch_As_Admin()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient(UserRole.Admin);

        var response = await client.PostAsJsonAsync("/branches", new BranchCreateRequest
        {
            Name = "North Warehouse",
            Address = "  Main Street  "
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var branch = await response.Content.ReadFromJsonAsync<BranchResponse>();
        Assert.NotNull(branch);
        Assert.Equal("North Warehouse", branch.Name);
        Assert.Equal("Main Street", branch.Address);
        Assert.True(branch.IsActive);
    }

    [Fact]
    public async Task Post_Branches_Rejects_Missing_Required_Fields()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient(UserRole.Admin);

        var response = await client.PostAsJsonAsync("/branches", new BranchCreateRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorResponseAsync(response, "validation_error", "name");
    }

    [Fact]
    public async Task Post_Branches_Rejects_Collaborator()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient(UserRole.Collaborator);

        var response = await client.PostAsJsonAsync("/branches", new BranchCreateRequest { Name = "North" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Branches_Rejects_Unauthenticated_User()
    {
        await _factory.ResetDatabaseAsync();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/branches", new BranchCreateRequest { Name = "North" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Branch_Returns_Branch_Detail()
    {
        var branch = CreateBranch("Central", isActive: true);
        await _factory.ResetDatabaseAsync(branch);
        var client = CreateClient(UserRole.Collaborator);

        var response = await client.GetAsync($"/branches/{branch.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<BranchResponse>();
        Assert.NotNull(payload);
        Assert.Equal(branch.Id, payload.Id);
    }

    [Fact]
    public async Task Get_Branch_Returns_404_When_Not_Found()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient(UserRole.Admin);

        var response = await client.GetAsync($"/branches/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertErrorResponseAsync(response, "not_found", "branchId");
    }

    [Fact]
    public async Task Patch_Branches_Updates_Branch_As_Admin()
    {
        var branch = CreateBranch("Central", isActive: true);
        await _factory.ResetDatabaseAsync(branch);
        var client = CreateClient(UserRole.Admin);

        var response = await client.PatchAsJsonAsync($"/branches/{branch.Id}", new BranchUpdateRequest
        {
            Name = "Updated Central",
            Address = ""
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<BranchResponse>();
        Assert.NotNull(payload);
        Assert.Equal("Updated Central", payload.Name);
        Assert.Null(payload.Address);
        Assert.NotNull(payload.UpdatedAt);
    }

    [Fact]
    public async Task Patch_Branches_Rejects_Collaborator()
    {
        var branch = CreateBranch("Central", isActive: true);
        await _factory.ResetDatabaseAsync(branch);
        var client = CreateClient(UserRole.Collaborator);

        var response = await client.PatchAsJsonAsync($"/branches/{branch.Id}", new BranchUpdateRequest
        {
            Name = "Updated"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Patch_Branches_Deactivate_Deactivates_Branch_As_Admin()
    {
        var branch = CreateBranch("Central", isActive: true);
        await _factory.ResetDatabaseAsync(branch);
        var client = CreateClient(UserRole.Admin);

        var response = await client.PatchAsync($"/branches/{branch.Id}/deactivate", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<BranchResponse>();
        Assert.NotNull(payload);
        Assert.False(payload.IsActive);
        Assert.NotNull(payload.UpdatedAt);
    }

    [Fact]
    public async Task Patch_Branches_Activate_Activates_Branch_As_Admin()
    {
        var branch = CreateBranch("Central", isActive: false);
        await _factory.ResetDatabaseAsync(branch);
        var client = CreateClient(UserRole.Admin);

        var response = await client.PatchAsync($"/branches/{branch.Id}/activate", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<BranchResponse>();
        Assert.NotNull(payload);
        Assert.True(payload.IsActive);
        Assert.NotNull(payload.UpdatedAt);
    }

    [Fact]
    public async Task Patch_Branches_Activate_Active_Branch_Is_Idempotent()
    {
        var branch = CreateBranch("Central", isActive: true);
        await _factory.ResetDatabaseAsync(branch);
        var client = CreateClient(UserRole.Admin);

        var response = await client.PatchAsync($"/branches/{branch.Id}/activate", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<BranchResponse>();
        Assert.NotNull(payload);
        Assert.True(payload.IsActive);
    }

    [Fact]
    public async Task Patch_Branches_Activate_Rejects_Collaborator()
    {
        var branch = CreateBranch("Central", isActive: false);
        await _factory.ResetDatabaseAsync(branch);
        var client = CreateClient(UserRole.Collaborator);

        var response = await client.PatchAsync($"/branches/{branch.Id}/activate", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Patch_Branches_Activate_Returns_404_When_Not_Found()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient(UserRole.Admin);

        var response = await client.PatchAsync($"/branches/{Guid.NewGuid()}/activate", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertErrorResponseAsync(response, "not_found", "branchId");
    }

    private HttpClient CreateClient(UserRole role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Test-User-Role", role.ToString());
        return client;
    }

    private static Branch CreateBranch(string name, bool isActive) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Address = $"{name} address",
        IsActive = isActive,
        CreatedAt = DateTime.UtcNow
    };

    private static async Task AssertErrorResponseAsync(
        HttpResponseMessage response,
        string expectedCode,
        string expectedField)
    {
        var content = await response.Content.ReadAsStringAsync();
        using var payload = JsonDocument.Parse(content);
        var error = payload.RootElement.GetProperty("error");

        Assert.Equal(expectedCode, error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("requestId").GetString()));

        var details = error.GetProperty("details").EnumerateArray().ToArray();
        Assert.Contains(details, detail => detail.GetProperty("field").GetString() == expectedField);
    }
}
