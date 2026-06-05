using Inventory.Api.Contracts.Errors;
using Inventory.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var details = context.ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .SelectMany(entry => entry.Value!.Errors.Select(error =>
                    new FieldError(string.IsNullOrWhiteSpace(entry.Key) ? "body" : entry.Key, string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? "The field is invalid."
                        : error.ErrorMessage)))
                .ToArray();

            return new BadRequestObjectResult(new ErrorResponse(new ErrorBody(
                "validation_error",
                "The request contains invalid fields.",
                details,
                context.HttpContext.TraceIdentifier)));
        };
    });
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
