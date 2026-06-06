using Inventory.Api.Contracts.Common;
using Inventory.Api.Contracts.Errors;
using Inventory.Api.Contracts.Imports;
using Inventory.Api.Data;
using Inventory.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Imports;

public class ProductCsvImportService : IProductCsvImportService
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private static readonly string[] RequiredHeaders = ["name", "sku", "category", "minstock"];

    private readonly InventoryDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public ProductCsvImportService(InventoryDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<ImportProductsResult> ImportProductsAsync(
        Guid importedBy,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        var fileValidation = ValidateFile(file);
        if (fileValidation.Count > 0)
        {
            return new ImportProductsResult.ValidationFailed(fileValidation);
        }

        await using var stream = file!.OpenReadStream();
        using var reader = new StreamReader(stream);
        var rows = await ReadRowsAsync(reader, cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var batch = new ImportBatch
        {
            Id = Guid.NewGuid(),
            FileName = Path.GetFileName(file.FileName),
            Status = ImportStatus.Pending,
            ImportedById = importedBy,
            CreatedAt = now
        };

        if (rows.Count == 0)
        {
            batch.Status = ImportStatus.Failed;
            batch.CompletedAt = now;
            batch.InvalidRows = 1;
            batch.Errors.Add(CreateError(batch.Id, 1, "file", "CSV file is empty."));

            await SaveBatchAsync(batch, [], cancellationToken);
            return new ImportProductsResult.Success(ImportBatchResponse.FromEntity(batch));
        }

        var headerErrors = ValidateHeaders(rows[0], batch.Id);
        if (headerErrors.Count > 0)
        {
            batch.Status = ImportStatus.Failed;
            batch.CompletedAt = now;
            batch.InvalidRows = headerErrors.Count;

            foreach (var error in headerErrors)
            {
                batch.Errors.Add(error);
            }

            await SaveBatchAsync(batch, [], cancellationToken);
            return new ImportProductsResult.Success(ImportBatchResponse.FromEntity(batch));
        }

        var headers = BuildHeaderIndex(rows[0]);
        var dataRows = rows.Skip(1).Where(row => !IsEmptyRow(row.Values)).ToList();
        batch.TotalRows = dataRows.Count;

        var existingSkus = await _dbContext.Products
            .AsNoTracking()
            .Select(product => product.Sku)
            .ToListAsync(cancellationToken);
        var existingSkuSet = new HashSet<string>(existingSkus, StringComparer.OrdinalIgnoreCase);

        var existingBarcodes = await _dbContext.Products
            .AsNoTracking()
            .Where(product => product.Barcode != null)
            .Select(product => product.Barcode!)
            .ToListAsync(cancellationToken);
        var existingBarcodeSet = new HashSet<string>(existingBarcodes, StringComparer.OrdinalIgnoreCase);

        var csvSkuSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var csvBarcodeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var products = new List<Product>();

        foreach (var row in dataRows)
        {
            var rowErrors = ValidateProductRow(
                row,
                headers,
                existingSkuSet,
                existingBarcodeSet,
                csvSkuSet,
                csvBarcodeSet,
                out var product);

            if (rowErrors.Count > 0)
            {
                batch.InvalidRows++;
                foreach (var error in rowErrors)
                {
                    error.ImportBatchId = batch.Id;
                    batch.Errors.Add(error);
                }

                continue;
            }

            products.Add(product!);
            batch.ValidRows++;
        }

        batch.Status = batch.InvalidRows == batch.TotalRows && batch.TotalRows > 0
            ? ImportStatus.Failed
            : ImportStatus.Completed;
        batch.CompletedAt = now;

        await SaveBatchAsync(batch, products, cancellationToken);
        return new ImportProductsResult.Success(ImportBatchResponse.FromEntity(batch));
    }

    public async Task<ListImportBatchesResult> ListBatchesAsync(
        ImportBatchQueryParameters query,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidatePagination(query.Page, query.PageSize, out var page, out var pageSize);
        var importedBy = ParseOptionalGuid(query.ImportedBy, "importedBy", validationErrors);

        if (validationErrors.Count > 0)
        {
            return new ListImportBatchesResult.ValidationFailed(validationErrors);
        }

        var batches = _dbContext.ImportBatches.AsNoTracking().AsQueryable();

        if (importedBy.HasValue)
        {
            batches = batches.Where(batch => batch.ImportedById == importedBy.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            batches = ApplyStatusFilter(batches, query.Status.Trim());
        }

        var total = await batches.CountAsync(cancellationToken);
        var items = await batches
            .OrderByDescending(batch => batch.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(batch => ImportBatchResponse.FromEntity(batch))
            .ToListAsync(cancellationToken);

        return new ListImportBatchesResult.Success(new PaginatedResponse<ImportBatchResponse>(
            items,
            total,
            page,
            pageSize,
            page * pageSize < total));
    }

    public async Task<GetImportBatchResult> GetBatchAsync(
        string batchId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(batchId, out var parsedBatchId))
        {
            return new GetImportBatchResult.ValidationFailed(
                [new FieldError("batchId", "Batch id must be a valid GUID.")]);
        }

        var batch = await _dbContext.ImportBatches
            .AsNoTracking()
            .FirstOrDefaultAsync(importBatch => importBatch.Id == parsedBatchId, cancellationToken);

        return batch is null
            ? new GetImportBatchResult.NotFound()
            : new GetImportBatchResult.Success(ImportBatchResponse.FromEntity(batch));
    }

    public async Task<ListImportBatchErrorsResult> ListBatchErrorsAsync(
        string batchId,
        ImportBatchErrorQueryParameters query,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidatePagination(query.Page, query.PageSize, out var page, out var pageSize);
        if (!Guid.TryParse(batchId, out var parsedBatchId))
        {
            validationErrors.Add(new FieldError("batchId", "Batch id must be a valid GUID."));
        }

        if (validationErrors.Count > 0)
        {
            return new ListImportBatchErrorsResult.ValidationFailed(validationErrors);
        }

        var batchExists = await _dbContext.ImportBatches
            .AsNoTracking()
            .AnyAsync(batch => batch.Id == parsedBatchId, cancellationToken);

        if (!batchExists)
        {
            return new ListImportBatchErrorsResult.BatchNotFound();
        }

        var errors = _dbContext.ImportBatchErrors
            .AsNoTracking()
            .Where(error => error.ImportBatchId == parsedBatchId);

        var total = await errors.CountAsync(cancellationToken);
        var items = await errors
            .OrderBy(error => error.RowNumber)
            .ThenBy(error => error.Field)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(error => ImportBatchErrorResponse.FromEntity(error))
            .ToListAsync(cancellationToken);

        return new ListImportBatchErrorsResult.Success(new PaginatedResponse<ImportBatchErrorResponse>(
            items,
            total,
            page,
            pageSize,
            page * pageSize < total));
    }

    private async Task SaveBatchAsync(
        ImportBatch batch,
        IReadOnlyList<Product> products,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        _dbContext.ImportBatches.Add(batch);
        if (products.Count > 0)
        {
            _dbContext.Products.AddRange(products);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static List<FieldError> ValidateFile(IFormFile? file)
    {
        var errors = new List<FieldError>();
        if (file is null)
        {
            errors.Add(new FieldError("file", "CSV file is required."));
            return errors;
        }

        if (file.Length == 0)
        {
            errors.Add(new FieldError("file", "CSV file cannot be empty."));
        }

        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new FieldError("file", "File must use .csv extension."));
        }

        return errors;
    }

    private static async Task<List<CsvRow>> ReadRowsAsync(
        TextReader reader,
        CancellationToken cancellationToken)
    {
        var rows = new List<CsvRow>();
        var rowNumber = 1;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                rowNumber++;
                continue;
            }

            rows.Add(new CsvRow(rowNumber, ParseCsvLine(line)));
            rowNumber++;
        }

        return rows;
    }

    private static List<ImportBatchError> ValidateHeaders(CsvRow headerRow, Guid batchId)
    {
        var headers = headerRow.Values
            .Select(NormalizeHeader)
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return RequiredHeaders
            .Where(requiredHeader => !headers.Contains(requiredHeader))
            .Select(requiredHeader => CreateError(
                batchId,
                headerRow.RowNumber,
                requiredHeader,
                $"Missing required header '{requiredHeader}'."))
            .ToList();
    }

    private static Dictionary<string, int> BuildHeaderIndex(CsvRow headerRow)
    {
        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < headerRow.Values.Count; index++)
        {
            var header = NormalizeHeader(headerRow.Values[index]);
            if (!string.IsNullOrWhiteSpace(header) && !headers.ContainsKey(header))
            {
                headers[header] = index;
            }
        }

        return headers;
    }

    private static List<ImportBatchError> ValidateProductRow(
        CsvRow row,
        IReadOnlyDictionary<string, int> headers,
        HashSet<string> existingSkuSet,
        HashSet<string> existingBarcodeSet,
        HashSet<string> csvSkuSet,
        HashSet<string> csvBarcodeSet,
        out Product? product)
    {
        var errors = new List<ImportBatchError>();
        product = null;

        var name = GetValue(row, headers, "name");
        var sku = GetValue(row, headers, "sku");
        var barcode = TrimToNull(GetValue(row, headers, "barcode"));
        var category = GetValue(row, headers, "category");
        var description = TrimToNull(GetValue(row, headers, "description"));
        var imageUrl = TrimToNull(GetValue(row, headers, "imageurl"));
        var minStockText = GetValue(row, headers, "minstock");
        var isActiveText = TrimToNull(GetValue(row, headers, "isactive"));

        ValidateRequiredText(row, "name", name, 150, errors);
        ValidateRequiredText(row, "sku", sku, 100, errors);
        ValidateRequiredText(row, "category", category, 100, errors);
        ValidateOptionalText(row, "barcode", barcode, 32, errors);
        ValidateOptionalText(row, "description", description, 500, errors);
        ValidateOptionalText(row, "imageUrl", imageUrl, 1000, errors);

        if (!int.TryParse(minStockText, out var minStock) || minStock < 0)
        {
            errors.Add(CreateError(Guid.Empty, row.RowNumber, "minStock", "minStock must be a number greater than or equal to zero."));
        }

        var isActive = true;
        if (isActiveText is not null && !TryParseBoolean(isActiveText, out isActive))
        {
            errors.Add(CreateError(Guid.Empty, row.RowNumber, "isActive", "isActive must be true or false."));
        }

        if (!string.IsNullOrWhiteSpace(sku))
        {
            var normalizedSku = sku.Trim();
            if (existingSkuSet.Contains(normalizedSku))
            {
                errors.Add(CreateError(Guid.Empty, row.RowNumber, "sku", "SKU already exists."));
            }

            if (!csvSkuSet.Add(normalizedSku))
            {
                errors.Add(CreateError(Guid.Empty, row.RowNumber, "sku", "SKU is duplicated in the CSV file."));
            }
        }

        if (barcode is not null)
        {
            if (existingBarcodeSet.Contains(barcode))
            {
                errors.Add(CreateError(Guid.Empty, row.RowNumber, "barcode", "Barcode already exists."));
            }

            if (!csvBarcodeSet.Add(barcode))
            {
                errors.Add(CreateError(Guid.Empty, row.RowNumber, "barcode", "Barcode is duplicated in the CSV file."));
            }
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        product = new Product
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Sku = sku.Trim(),
            Barcode = barcode,
            Category = category.Trim(),
            Description = description,
            ImageUrl = imageUrl,
            MinStock = minStock,
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow
        };

        return errors;
    }

    private static List<FieldError> ValidatePagination(
        int requestedPage,
        int requestedPageSize,
        out int page,
        out int pageSize)
    {
        var errors = new List<FieldError>();
        page = requestedPage <= 0 ? DefaultPage : requestedPage;
        pageSize = requestedPageSize <= 0 ? DefaultPageSize : requestedPageSize;

        if (requestedPage < 1)
        {
            errors.Add(new FieldError("page", "Page must be greater than or equal to 1."));
        }

        if (requestedPageSize < 1 || requestedPageSize > MaxPageSize)
        {
            errors.Add(new FieldError("pageSize", $"Page size must be between 1 and {MaxPageSize}."));
        }

        return errors;
    }

    private static IQueryable<ImportBatch> ApplyStatusFilter(IQueryable<ImportBatch> query, string status) =>
        status.ToLowerInvariant() switch
        {
            "pending" => query.Where(batch => batch.Status == ImportStatus.Pending),
            "processing" => query.Where(batch => batch.Status == ImportStatus.Validated),
            "completed" => query.Where(batch => batch.Status == ImportStatus.Completed && batch.InvalidRows == 0),
            "completed_with_errors" => query.Where(batch => batch.Status == ImportStatus.Completed && batch.InvalidRows > 0),
            "failed" => query.Where(batch => batch.Status == ImportStatus.Failed),
            _ => query.Where(_ => false)
        };

    private static Guid? ParseOptionalGuid(
        string? value,
        string field,
        List<FieldError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Guid.TryParse(value, out var parsed))
        {
            return parsed;
        }

        errors.Add(new FieldError(field, $"{field} must be a valid GUID."));
        return null;
    }

    private static string GetValue(CsvRow row, IReadOnlyDictionary<string, int> headers, string header)
    {
        if (!headers.TryGetValue(header, out var index) || index >= row.Values.Count)
        {
            return string.Empty;
        }

        return row.Values[index].Trim();
    }

    private static void ValidateRequiredText(
        CsvRow row,
        string field,
        string value,
        int maxLength,
        List<ImportBatchError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(CreateError(Guid.Empty, row.RowNumber, field, $"{field} is required."));
            return;
        }

        if (value.Trim().Length > maxLength)
        {
            errors.Add(CreateError(Guid.Empty, row.RowNumber, field, $"{field} cannot exceed {maxLength} characters."));
        }
    }

    private static void ValidateOptionalText(
        CsvRow row,
        string field,
        string? value,
        int maxLength,
        List<ImportBatchError> errors)
    {
        if (value is not null && value.Length > maxLength)
        {
            errors.Add(CreateError(Guid.Empty, row.RowNumber, field, $"{field} cannot exceed {maxLength} characters."));
        }
    }

    private static bool TryParseBoolean(string value, out bool parsed)
    {
        if (bool.TryParse(value, out parsed))
        {
            return true;
        }

        if (value == "1")
        {
            parsed = true;
            return true;
        }

        if (value == "0")
        {
            parsed = false;
            return true;
        }

        return false;
    }

    private static bool IsEmptyRow(IReadOnlyList<string> values) =>
        values.All(string.IsNullOrWhiteSpace);

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeHeader(string value) =>
        value.Trim().Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    private static ImportBatchError CreateError(
        Guid batchId,
        int rowNumber,
        string field,
        string message) =>
        new()
        {
            Id = Guid.NewGuid(),
            ImportBatchId = batchId,
            RowNumber = rowNumber,
            Field = field,
            Message = message
        };

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var currentChar = line[index];
            if (currentChar == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (currentChar == ',' && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(currentChar);
        }

        values.Add(current.ToString());
        return values;
    }

    private sealed record CsvRow(int RowNumber, IReadOnlyList<string> Values);
}
