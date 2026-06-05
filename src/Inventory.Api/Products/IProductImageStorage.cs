namespace Inventory.Api.Products;

public interface IProductImageStorage
{
    Task<string> SaveAsync(
        Guid productId,
        Stream content,
        string extension,
        CancellationToken cancellationToken);

    void DeleteIfManaged(string? imageUrl);
}
