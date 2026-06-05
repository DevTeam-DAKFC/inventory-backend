namespace Inventory.Api.Common.Pagination;

public class PaginatedResponse<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int Total { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required bool HasNextPage { get; init; }
}
