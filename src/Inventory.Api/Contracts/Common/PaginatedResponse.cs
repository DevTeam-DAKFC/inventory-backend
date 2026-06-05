namespace Inventory.Api.Contracts.Common;

public record PaginatedResponse<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Page,
    int PageSize,
    bool HasNextPage);
