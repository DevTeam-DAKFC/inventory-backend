namespace Inventory.Api.Products;

public sealed class LocalProductImageStorage : IProductImageStorage
{
    public const string PublicPathPrefix = "/uploads/products/";

    private readonly string _storageDirectory;

    public LocalProductImageStorage(IWebHostEnvironment environment)
    {
        var webRootPath = environment.WebRootPath
            ?? Path.Combine(environment.ContentRootPath, "wwwroot");

        _storageDirectory = Path.GetFullPath(
            Path.Combine(webRootPath, "uploads", "products"));
    }

    public async Task<string> SaveAsync(
        Guid productId,
        Stream content,
        string extension,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_storageDirectory);

        var fileName = $"{productId:D}-{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(_storageDirectory, fileName);

        await using var destination = new FileStream(
            filePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        await content.CopyToAsync(destination, cancellationToken);

        return $"{PublicPathPrefix}{fileName}";
    }

    public void DeleteIfManaged(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl) ||
            !imageUrl.StartsWith(PublicPathPrefix, StringComparison.Ordinal))
        {
            return;
        }

        var fileName = imageUrl[PublicPathPrefix.Length..];
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName != Path.GetFileName(fileName))
        {
            return;
        }

        var filePath = Path.GetFullPath(Path.Combine(_storageDirectory, fileName));
        var expectedDirectory = Path.GetDirectoryName(filePath);

        if (!string.Equals(expectedDirectory, _storageDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        File.Delete(filePath);
    }
}
