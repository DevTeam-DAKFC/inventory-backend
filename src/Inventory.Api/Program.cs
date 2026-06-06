using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Inventory.Api.Auth;
using Inventory.Api.Common.Errors;
using Inventory.Api.Data;
using Inventory.Api.Imports;
using Inventory.Api.InventoryMovements;
using Inventory.Api.Products;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
const string DevelopmentCorsPolicy = "DevelopmentCorsPolicy";

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var details = context.ModelState
            .Where(kv => kv.Value is { Errors.Count: > 0 })
            .SelectMany(kv => kv.Value!.Errors.Select(e => new FieldError(
                Field: string.IsNullOrWhiteSpace(kv.Key)
                    ? "body"
                    : JsonNamingPolicy.CamelCase.ConvertName(kv.Key),
                Message: string.IsNullOrWhiteSpace(e.ErrorMessage)
                    ? e.Exception?.Message ?? "Invalid value."
                    : e.ErrorMessage)))
            .ToList();

        var response = new ErrorResponse
        {
            Error = new ErrorBody
            {
                Code = "validation_error",
                Message = "The request contains invalid fields.",
                Details = details,
                RequestId = context.HttpContext.TraceIdentifier
            }
        };

        return new BadRequestObjectResult(response);
    };
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy(DevelopmentCorsPolicy, policy =>
        {
            policy
                .SetIsOriginAllowed(origin =>
                {
                    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                    {
                        return false;
                    }

                    return uri.Host is "localhost" or "127.0.0.1";
                })
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
    });
}

builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IProductImageStorage, LocalProductImageStorage>();

builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.SecretKey), "Jwt:SecretKey is required.")
    .Validate(o => Encoding.UTF8.GetByteCount(o.SecretKey) >= 32, "Jwt:SecretKey must be at least 32 bytes (256 bits) for HS256.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer), "Jwt:Issuer is required.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Audience), "Jwt:Audience is required.")
    .Validate(o => o.ExpiresInMinutes > 0, "Jwt:ExpiresInMinutes must be greater than zero.")
    .ValidateOnStart();

builder.Services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
builder.Services.AddSingleton<ITokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthRegistrationService, AuthRegistrationService>();
builder.Services.AddScoped<IAuthLoginService, AuthLoginService>();
builder.Services.AddScoped<IAuthCurrentUserService, AuthCurrentUserService>();
builder.Services.AddScoped<IInventoryMovementService, InventoryMovementService>();
builder.Services.AddScoped<IProductCsvImportService, ProductCsvImportService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearerOptions, jwtOptionsAccessor) =>
    {
        var jwt = jwtOptionsAccessor.Value;
        var keyBytes = Encoding.UTF8.GetBytes(jwt.SecretKey);

        bearerOptions.MapInboundClaims = false;
        bearerOptions.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = keyBytes.Length > 0 ? new SymmetricSecurityKey(keyBytes) : null,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        bearerOptions.Events = new JwtBearerEvents
        {
            OnChallenge = async challengeContext =>
            {
                // Replace the default empty 401 with the project's ErrorResponse envelope.
                // The body is intentionally generic and does not reveal whether the token
                // was missing, malformed, expired, or signed with the wrong key.
                challengeContext.HandleResponse();
                if (challengeContext.Response.HasStarted)
                {
                    return;
                }

                await AuthChallengeResponseWriter.WriteUnauthorizedAsync(
                    challengeContext.Response,
                    challengeContext.HttpContext.TraceIdentifier,
                    challengeContext.HttpContext.RequestAborted);
            }
        };
    });

builder.Services.AddAuthorization();

var webRootPath = builder.Environment.WebRootPath
    ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
Directory.CreateDirectory(webRootPath);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
    app.UseSwagger();
    app.UseSwaggerUI();

    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

    if (dbContext.Database.IsSqlServer())
    {
        await DevelopmentDataSeeder.SeedAsync(dbContext);
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(webRootPath)
});

if (app.Environment.IsDevelopment())
{
    app.UseCors(DevelopmentCorsPolicy);
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    status = "ok",
    service = "Inventory.Api",
    docs = "/scalar/v1",
    health = "/health"
}));

app.MapControllers();

app.Run();
