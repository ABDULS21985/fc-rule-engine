using System.Security.Claims;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure;
using FC.Engine.Infrastructure.Auth;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Middleware;
using FC.Engine.Infrastructure.MultiTenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
const string RegulatorAuthScheme = "FC.Regulator.Auth";

builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<FC.Engine.Infrastructure.Charts.ChartJsInterop>();

builder.Services.AddAuthentication(RegulatorAuthScheme)
    .AddCookie(RegulatorAuthScheme, options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/account/logout";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.Name = "FC.Regulator.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
    });

builder.Services.AddAuthorization(options =>
{
    options.AddRegosPermissionPolicies();
    options.AddPolicy("RegulatorOnly", policy =>
        policy.RequireAuthenticatedUser()
            .RequireClaim("TenantType", TenantType.Regulator.ToString()));
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseTenantResolution();
app.UseTenantFavicon();
app.UseStaticFiles();
app.UseAuthentication();
app.UseTenantContext();
app.UseReConsent();
app.UseAuthorization();
app.UseAntiforgery();

app.MapPost("/account/login", async (
    HttpContext context,
    AuthService authService,
    MetadataDbContext db,
    IEntitlementService entitlementService) =>
{
    var form = await context.Request.ReadFormAsync();
    var username = form["username"].ToString().Trim();
    var password = form["password"].ToString();

    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
    {
        context.Response.Redirect("/login?error=invalid");
        return;
    }

    var (user, errorCode) = await authService.ValidateLogin(
        username,
        password,
        context.Connection.RemoteIpAddress?.ToString(),
        context.Request.Headers.UserAgent.ToString(),
        context.RequestAborted);

    if (user is null)
    {
        context.Response.Redirect($"/login?error={errorCode ?? "invalid"}");
        return;
    }

    if (!user.TenantId.HasValue)
    {
        context.Response.Redirect("/login?error=tenant-missing");
        return;
    }

    var tenantId = user.TenantId.Value;
    var tenant = await db.Tenants
        .AsNoTracking()
        .FirstOrDefaultAsync(t => t.TenantId == tenantId, context.RequestAborted);

    if (tenant is null || tenant.TenantType != TenantType.Regulator)
    {
        context.Response.Redirect("/login?error=not-regulator");
        return;
    }

    var entitlement = await entitlementService.ResolveEntitlements(tenantId, context.RequestAborted);
    var regulatorCodes = entitlement.ActiveModules
        .Select(x => x.RegulatorCode)
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    if (regulatorCodes.Count == 0)
    {
        context.Response.Redirect("/login?error=not-regulator");
        return;
    }

    var regulatorCode = regulatorCodes[0];

    var principal = await authService.BuildClaimsPrincipalWithPermissions(user, context.RequestAborted);
    if (principal.Identity is ClaimsIdentity identity)
    {
        identity.AddClaim(new Claim("TenantType", TenantType.Regulator.ToString()));
        identity.AddClaim(new Claim("RegulatorCode", regulatorCode));
    }

    await context.SignInAsync(
        RegulatorAuthScheme,
        principal,
        new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
        });

    context.Response.Redirect("/inbox");
});

app.MapGet("/account/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(RegulatorAuthScheme);
    context.Response.Redirect("/login");
});

app.MapGet("/workspace/{projectId:int}/report", async (
    int projectId,
    HttpContext context,
    ITenantContext tenantContext,
    IExaminationWorkspaceService workspaceService) =>
{
    if (!tenantContext.CurrentTenantId.HasValue)
    {
        return Results.Unauthorized();
    }

    var regulatorCode = context.User.FindFirst("RegulatorCode")?.Value;
    if (string.IsNullOrWhiteSpace(regulatorCode))
    {
        return Results.BadRequest(new { error = "Missing regulator context." });
    }

    var pdf = await workspaceService.GenerateReportPdf(
        tenantContext.CurrentTenantId.Value,
        regulatorCode,
        projectId,
        context.RequestAborted);

    var fileName = $"examination-report-{projectId}-{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";
    return Results.File(pdf, "application/pdf", fileName);
}).RequireAuthorization("RegulatorOnly");

app.MapGet("/workspace/{projectId:int}/intelligence-pack", async (
    int projectId,
    HttpContext context,
    ITenantContext tenantContext,
    IExaminationWorkspaceService workspaceService) =>
{
    if (!tenantContext.CurrentTenantId.HasValue)
    {
        return Results.Unauthorized();
    }

    var regulatorCode = context.User.FindFirst("RegulatorCode")?.Value;
    if (string.IsNullOrWhiteSpace(regulatorCode))
    {
        return Results.BadRequest(new { error = "Missing regulator context." });
    }

    var pdf = await workspaceService.GenerateIntelligencePackPdf(
        tenantContext.CurrentTenantId.Value,
        regulatorCode,
        projectId,
        context.RequestAborted);

    var fileName = $"intelligence-pack-{projectId}-{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";
    return Results.File(pdf, "application/pdf", fileName);
}).RequireAuthorization("RegulatorOnly");

app.MapGet("/workspace/{projectId:int}/evidence/{evidenceId:int}", async (
    int projectId,
    int evidenceId,
    ITenantContext tenantContext,
    IExaminationWorkspaceService workspaceService,
    HttpContext context) =>
{
    if (!tenantContext.CurrentTenantId.HasValue)
    {
        return Results.Unauthorized();
    }

    var file = await workspaceService.DownloadEvidence(
        tenantContext.CurrentTenantId.Value,
        projectId,
        evidenceId,
        context.RequestAborted);

    return file is null
        ? Results.NotFound()
        : Results.File(file.Content, file.ContentType, file.FileName);
}).RequireAuthorization("RegulatorOnly");

app.MapGet("/stress-test/report/pdf", async (
    HttpContext context,
    IStressTestService stressTestService) =>
{
    var regulatorCode = context.User.FindFirst("RegulatorCode")?.Value;
    if (string.IsNullOrWhiteSpace(regulatorCode))
    {
        return Results.BadRequest(new { error = "Missing regulator context." });
    }

    // Run a default stress test to generate the report
    var scenarioParam = context.Request.Query["scenario"].FirstOrDefault() ?? "NgfsOrderly";
    if (!Enum.TryParse<FC.Engine.Domain.Models.StressScenarioType>(scenarioParam, out var scenarioType))
    {
        scenarioType = FC.Engine.Domain.Models.StressScenarioType.NgfsOrderly;
    }

    var report = await stressTestService.RunStressTestAsync(
        regulatorCode,
        new FC.Engine.Domain.Models.StressTestRequest { ScenarioType = scenarioType },
        context.RequestAborted);

    var pdf = await stressTestService.GenerateReportPdfAsync(regulatorCode, report, context.RequestAborted);
    var fileName = $"stress-test-{scenarioType}-{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";
    return Results.File(pdf, "application/pdf", fileName);
}).RequireAuthorization("RegulatorOnly");

app.MapRazorComponents<FC.Engine.Regulator.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
