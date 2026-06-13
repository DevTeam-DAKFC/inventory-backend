using Inventory.Api.Auth;

namespace Inventory.Api.Tests.Auth;

public class BcryptPasswordHasherTests
{
    private readonly BcryptPasswordHasher _hasher = new();

    [Fact]
    public void Hash_Does_Not_Equal_Raw_Password()
    {
        const string password = "a-strong-password";

        var hash = _hasher.Hash(password);

        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.NotEqual(password, hash);
    }

    [Fact]
    public void Verify_Returns_True_For_Correct_Password()
    {
        const string password = "a-strong-password";
        var hash = _hasher.Hash(password);

        Assert.True(_hasher.Verify(password, hash));
    }

    [Fact]
    public void Verify_Returns_False_For_Wrong_Password()
    {
        var hash = _hasher.Hash("a-strong-password");

        Assert.False(_hasher.Verify("wrong-password", hash));
    }
}
