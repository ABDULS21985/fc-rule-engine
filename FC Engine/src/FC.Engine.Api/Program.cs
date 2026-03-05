using FC.Engine.Api.Endpoints;
using FC.Engine.Api.Middleware;
using FC.Engine.Application.Services;
using FC.Engine.Infrastructure;
using FC.Engine.Infrastructure.Auth;
using FC.Engine.Infrastructure.MultiTenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// Infrastructure (EF Core, Dapper, repositories, caching, validation, audit)
builder.Services.AddInfrastructure(builder.Configuration);

// Application services
builder.Services.AddScoped<IngestionOrchestrator>();
builder.Services.AddScoped<ValidationOrchestrator>();
builder.Services.AddScoped<TemplateService>();
builder.Services.AddScoped<TemplateVersioningService>();
builder.Services.AddScoped<FormulaService>();
builder.Services.AddScoped<SeedService>();
builder.Services.AddScoped<FormulaSeedService>();
builder.Services.AddScoped<CrossSheetRuleSeedService>();
builder.Services.AddScoped<FormulaCatalogSeeder>();
builder.Services.AddScoped<InstitutionAuthService>();

var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();
var jwtSigningKey = LoadRsaPublicSecurityKey(jwtSettings.SigningKeyPath);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwtSigningKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization(options => { options.AddRegosPermissionPolicies(); });

// Swagger
builder.Services.AddHttpContextAccessor();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "FC Engine API", Version = "v1" });
    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Name = "X-Api-Key",
        Type = SecuritySchemeType.ApiKey,
        Description = "API key for authentication"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT access token"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" }
            },
            Array.Empty<string>()
        },
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseAuthentication();

// Dual auth middleware (JWT Bearer first, then API key fallback).
app.UseApiKeyAuth();
app.UseAuthorization();

// Tenant context resolution for API requests
app.UseTenantContext();

// Map endpoints
app.MapAuthEndpoints();
app.MapSubmissionEndpoints();
app.MapTemplateEndpoints();
app.MapSchemaEndpoints();
app.MapFilingCalendarEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithTags("Health");

app.Run();

static SecurityKey LoadRsaPublicSecurityKey(string keyPath)
{
    if (string.IsNullOrWhiteSpace(keyPath))
    {
        throw new InvalidOperationException("Jwt:SigningKeyPath is required for API authentication.");
    }

    if (!File.Exists(keyPath))
    {
        throw new FileNotFoundException($"JWT signing key file was not found at path '{keyPath}'.");
    }

    var pem = File.ReadAllText(keyPath);
    var rsa = RSA.Create();
    rsa.ImportFromPem(pem);
    return new RsaSecurityKey(rsa.ExportParameters(false));
}
