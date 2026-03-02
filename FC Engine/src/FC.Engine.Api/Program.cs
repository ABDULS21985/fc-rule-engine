using FC.Engine.Api.Endpoints;
using FC.Engine.Application.Services;
using FC.Engine.Infrastructure;
using Serilog;

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

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "FC Engine API", Version = "v1" });
});

var app = builder.Build();

// Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();

// Map endpoints
app.MapSubmissionEndpoints();
app.MapTemplateEndpoints();
app.MapSchemaEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithTags("Health");

app.Run();
