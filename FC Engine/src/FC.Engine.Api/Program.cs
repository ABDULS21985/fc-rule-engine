using System.Threading.RateLimiting;
using Asp.Versioning;
using FC.Engine.Api.Endpoints;
using FC.Engine.Api.Metrics;
using FC.Engine.Api.Middleware;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Infrastructure;
using FC.Engine.Infrastructure.Auth;
using FC.Engine.Infrastructure.MultiTenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Prometheus;
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

// ── API Versioning (RG-15) ──
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(),
        new HeaderApiVersionReader("X-Api-Version"));
});

// ── Per-Tenant Rate Limiting (RG-15) ──
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("PerTenantPolicy", context =>
    {
        var tenantId = context.Items.TryGetValue("TenantId", out var tid)
            ? tid?.ToString() ?? "anonymous"
            : "anonymous";

        var resolver = context.RequestServices.GetRequiredService<IRateLimitResolver>();
        var planTier = resolver.GetTenantTier(tenantId);

        var limit = planTier switch
        {
            "STARTER" => 100,
            "PROFESSIONAL" => 500,
            "ENTERPRISE" => 2000,
            "GROUP" => 5000,
            "REGULATOR" => 10000,
            "WHITE_LABEL" => 10000,
            _ => 50
        };

        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: tenantId,
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = limit,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 10
            });
    });

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        context.HttpContext.Response.Headers["Retry-After"] = "60";

        var tenantId = context.HttpContext.Items.TryGetValue("TenantId", out var tid)
            ? tid?.ToString() ?? "unknown"
            : "unknown";
        var resolver = context.HttpContext.RequestServices.GetRequiredService<IRateLimitResolver>();
        var planCode = resolver.GetTenantTier(tenantId);
        RegosMetrics.RateLimitHits.WithLabels(tenantId, planCode).Inc();

        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "Rate limit exceeded",
            retryAfter = 60,
            limit = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                ? retryAfter.TotalSeconds : 60
        }, token);
    };
});

// ── Health Checks (RG-15) ──
var connectionString = builder.Configuration.GetConnectionString("FcEngine")
    ?? throw new InvalidOperationException("Connection string 'FcEngine' not found");

var healthChecksBuilder = builder.Services.AddHealthChecks()
    .AddSqlServer(connectionString, name: "sqlserver", tags: new[] { "ready" });

var redisConfig = builder.Configuration.GetSection("HealthChecks:Redis");
if (redisConfig.GetValue<bool>("Enabled"))
{
    var redisConnStr = redisConfig["ConnectionString"];
    if (!string.IsNullOrWhiteSpace(redisConnStr))
        healthChecksBuilder.AddRedis(redisConnStr, name: "redis", tags: new[] { "ready" });
}

var rabbitConfig = builder.Configuration.GetSection("HealthChecks:RabbitMQ");
if (rabbitConfig.GetValue<bool>("Enabled"))
{
    var rabbitConnStr = rabbitConfig["ConnectionString"];
    if (!string.IsNullOrWhiteSpace(rabbitConnStr))
        healthChecksBuilder.AddRabbitMQ(new Uri(rabbitConnStr), name: "rabbitmq", tags: new[] { "ready" });
}

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

// ═══════════════════════════════════════════════════════════════════
// Middleware pipeline — order matters (RG-15)
// ═══════════════════════════════════════════════════════════════════

// 1. Exception handler (outermost)
app.UseExceptionHandler("/error");
app.Map("/error", () => Results.Problem());

// 2. Request ID generation
app.UseRequestId();

// 3. Security headers
app.UseSecurityHeaders();

// 4. Prometheus HTTP metrics
app.UseHttpMetrics();

// 5. Swagger (dev only)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 6. Serilog request logging
app.UseSerilogRequestLogging();

// 7. Authentication (JWT Bearer)
app.UseAuthentication();

// 8. Dual auth middleware (JWT Bearer first, then API key fallback)
app.UseApiKeyAuth();

// 9. Tenant context resolution
app.UseTenantContext();

// 10. Rate limiting (after tenant resolution so we know the tenant)
app.UseRateLimiter();

// 11. Structured request logging (after all context available)
app.UseStructuredRequestLogging();

// 12. Authorization + entitlement
app.UseAuthorization();

// ═══════════════════════════════════════════════════════════════════
// Health & infrastructure endpoints (no auth, no versioning)
// ═══════════════════════════════════════════════════════════════════

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false // Liveness: always healthy if app is running
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.TotalMilliseconds,
                error = e.Value.Exception?.Message
            })
        });
    }
});

// Backward-compatible simple health endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithTags("Health");

// Prometheus metrics scrape endpoint
app.MapMetrics("/metrics");

// ═══════════════════════════════════════════════════════════════════
// Versioned API endpoints (RG-15)
// ═══════════════════════════════════════════════════════════════════

var v1 = app.MapGroup("/api/v1")
    .RequireRateLimiting("PerTenantPolicy");

v1.MapAuthEndpoints();
v1.MapSubmissionEndpoints("v1");
v1.MapDataFeedEndpoints();
v1.MapTemplateEndpoints();
v1.MapSchemaEndpoints("v1");
v1.MapFilingCalendarEndpoints();
v1.MapPrivacyEndpoints();
v1.MapHistoricalMigrationEndpoints();

// v2 endpoints (future — same implementations for now, diverge later)
var v2 = app.MapGroup("/api/v2")
    .RequireRateLimiting("PerTenantPolicy");

v2.MapSchemaEndpoints("v2");
v2.MapSubmissionEndpoints("v2");

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
