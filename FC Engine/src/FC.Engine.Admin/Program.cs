using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure;
using FC.Engine.Infrastructure.Auth;
using FC.Engine.Infrastructure.Middleware;
using FC.Engine.Infrastructure.MultiTenancy;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
const string AdminAuthScheme = "FC.Admin.Auth";

// Serilog
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// Infrastructure
builder.Services.AddInfrastructure(builder.Configuration);

// Application services
builder.Services.AddScoped<TemplateService>();
builder.Services.AddScoped<TemplateVersioningService>();
builder.Services.AddScoped<FormulaService>();
builder.Services.AddScoped<FormulaSeedService>();
builder.Services.AddScoped<CrossSheetRuleSeedService>();
builder.Services.AddScoped<FormulaCatalogSeeder>();
builder.Services.AddScoped<SeedService>();
builder.Services.AddScoped<IngestionOrchestrator>();
builder.Services.AddScoped<ValidationOrchestrator>();
builder.Services.AddScoped<AuthService>();

// UI notification, dialog, command palette & sidebar services
builder.Services.AddScoped<FC.Engine.Admin.Services.ToastService>();
builder.Services.AddScoped<FC.Engine.Admin.Services.DialogService>();
builder.Services.AddScoped<FC.Engine.Admin.Services.CommandPaletteService>();
builder.Services.AddScoped<FC.Engine.Admin.Services.SidebarStateService>();
builder.Services.AddScoped<FC.Engine.Admin.Services.DataTableExportService>();
builder.Services.AddScoped<FC.Engine.Admin.Services.PlatformIntelligenceExportService>();
builder.Services.AddScoped<FC.Engine.Admin.Services.PlatformIntelligenceExportAuditService>();
builder.Services.AddSingleton<FC.Engine.Admin.Services.ITablePresetService, FC.Engine.Admin.Services.InMemoryTablePresetService>();
builder.Services.AddScoped<FC.Engine.Admin.Services.HelpService>();
builder.Services.AddScoped<FC.Engine.Admin.Services.DataCacheService>();
builder.Services.AddScoped<FC.Engine.Admin.Services.GlobalErrorService>();
builder.Services.AddScoped<FC.Engine.Admin.Services.SessionService>();
builder.Services.AddScoped<FC.Engine.Admin.Services.KeyboardShortcutService>();
builder.Services.AddScoped<FC.Engine.Admin.Services.HealthAlertService>();
builder.Services.AddScoped<FC.Engine.Admin.Services.RegulatorSessionService>();
builder.Services.AddScoped<FC.Engine.Admin.Services.PlatformIntelligenceService>();
builder.Services.AddScoped<FC.Engine.Admin.Services.IPlatformIntelligenceWorkspaceLoader>(sp => sp.GetRequiredService<FC.Engine.Admin.Services.PlatformIntelligenceService>());
builder.Services.AddScoped<FC.Engine.Admin.Services.DashboardBriefingPackBuilder>();
builder.Services.AddScoped<FC.Engine.Admin.Services.PlatformIntelligenceRefreshService>();
builder.Services.AddHostedService<FC.Engine.Admin.Services.PlatformIntelligenceRefreshJob>();
builder.Services.AddScoped<FC.Engine.Infrastructure.Charts.ChartJsInterop>();
builder.Services.AddScoped<IAuthorizationHandler, RegulatorTenantAccessHandler>();

// Scenario simulation engine — persisted (regulator-scoped, survives restart)
builder.Services.AddScoped<FC.Engine.Admin.Services.Scenarios.IScenarioEngine,
                           FC.Engine.Admin.Services.Scenarios.PersistedScenarioEngine>();
// Template catalogue is read-only static data; singleton is fine
builder.Services.AddSingleton<FC.Engine.Admin.Services.Scenarios.IScenarioTemplateService,
                              FC.Engine.Admin.Services.Scenarios.ScenarioTemplateService>();

// Capital Management
builder.Services.AddScoped<FC.Engine.Admin.Services.Capital.RwaOptimizationService>();
builder.Services.AddScoped<FC.Engine.Admin.Services.Capital.CapitalStackOptimizerService>();

// Compliance & Knowledge Graph
builder.Services.AddScoped<FC.Engine.Admin.Services.Compliance.ComplianceGraphService>();

// Sanctions & AML
builder.Services.AddScoped<FC.Engine.Admin.Services.Sanctions.ScreeningEngineService>();
builder.Services.AddScoped<FC.Engine.Admin.Services.Sanctions.AlertWorkflowService>();

// Operational Resilience
builder.Services.AddScoped<FC.Engine.Admin.Services.Resilience.ResilienceOrchestratorService>();

// Model Risk Management
builder.Services.AddScoped<FC.Engine.Admin.Services.ModelRisk.ModelGovernanceService>();

// Stakeholder Dashboards
builder.Services.AddScoped<FC.Engine.Admin.Services.Dashboards.StakeholderDashboardService>();

// Platform Admin services
builder.Services.AddScoped<FC.Engine.Admin.Services.TenantManagementService>();
builder.Services.AddScoped<FC.Engine.Admin.Services.PlatformAdminService>();
builder.Services.AddScoped<FC.Engine.Admin.Services.JurisdictionManagementService>();
builder.Services.AddScoped<FC.Engine.Admin.Services.JurisdictionContextService>();
builder.Services.AddScoped<FC.Engine.Admin.Services.RegulatoryCalendarImportService>();

// Authentication — cookie-based for Blazor Server
builder.Services.AddAuthentication(AdminAuthScheme)
    .AddCookie(AdminAuthScheme, options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/access-denied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Name = "FC.Admin.Auth";
    });

// Authorization policies
builder.Services.AddAuthorization(options =>
{
    options.AddRegosPermissionPolicies();
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.AddPolicy("ApproverOrAdmin", policy => policy.RequireRole("Approver", "Admin"));
    // Note: "Authenticated" in the Admin portal context requires PlatformAdmin role.
    // This is intentional — all Admin portal API endpoints require platform-level access.
    options.AddPolicy("Authenticated", policy => policy.RequireRole("PlatformAdmin"));
    options.AddPolicy("PlatformAdmin", policy => policy.RequireRole("PlatformAdmin"));
    options.AddPolicy("RegulatorOnly", policy =>
        policy.RequireAuthenticatedUser()
            .AddRequirements(new RegulatorTenantAccessRequirement()));
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();

// Named HTTP client for the self-probe in PlatformAdminService health checks
builder.Services.AddHttpClient("ApiHealthProbe", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var baseUrl = config["HealthCheck:ApiBaseUrl"] ?? "http://localhost:5002";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(5);
});

// Blazor Server
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

// Login endpoint — handles cookie auth outside of Blazor's interactive (SignalR) pipeline
app.MapPost("/account/login", async (
    HttpContext context,
    AuthService authService,
    IPortalUserRepository portalUserRepository,
    IMfaService mfaService,
    IMfaChallengeStore mfaChallengeStore) =>
{
    var form = await context.Request.ReadFormAsync();
    var challengeId = form["challengeId"].ToString().Trim();
    var returnUrl = form["returnUrl"].ToString().Trim();

    // Validate returnUrl to prevent open redirect attacks
    static string SafeRedirect(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "/";
        // Only allow relative paths starting with /
        if (url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\"))
            return url;
        return "/";
    }

    if (!string.IsNullOrWhiteSpace(challengeId))
    {
        var challenge = await mfaChallengeStore.GetChallenge(challengeId, context.RequestAborted);
        if (challenge is null)
        {
            context.Response.Redirect("/login?error=mfa-expired");
            return;
        }

        var mfaCode = form["mfaCode"].ToString().Trim();
        var backupCode = form["backupCode"].ToString().Trim().ToUpperInvariant();
        var verified = false;
        if (!string.IsNullOrWhiteSpace(mfaCode))
        {
            verified = await mfaService.VerifyCode(challenge.UserId, mfaCode, challenge.UserType);
        }
        else if (!string.IsNullOrWhiteSpace(backupCode))
        {
            verified = await mfaService.VerifyBackupCode(challenge.UserId, backupCode, challenge.UserType);
        }

        if (!verified)
        {
            context.Response.Redirect($"/login?mfa=required&challenge={Uri.EscapeDataString(challengeId)}&error=mfa-invalid");
            return;
        }

        var challengedUser = await portalUserRepository.GetById(challenge.UserId, context.RequestAborted);
        if (challengedUser is null || !challengedUser.IsActive)
        {
            context.Response.Redirect("/login?error=invalid");
            return;
        }

        await mfaChallengeStore.RemoveChallenge(challengeId, context.RequestAborted);

        var principalWithMfa = await authService.BuildClaimsPrincipalWithPermissions(challengedUser, context.RequestAborted);
        await context.SignInAsync(
            AdminAuthScheme,
            principalWithMfa,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });

        // Prefer challenge's stored returnUrl (from original login), fall back to form's returnUrl
        var mfaRedirect = !string.IsNullOrWhiteSpace(challenge.ReturnUrl) ? challenge.ReturnUrl : returnUrl;
        context.Response.Redirect(SafeRedirect(mfaRedirect));
        return;
    }

    var username = form["username"].ToString();
    var password = form["password"].ToString();
    var rememberMe = form["rememberMe"].ToString() == "true";
    var ipAddress = context.Connection.RemoteIpAddress?.ToString();
    var userAgent = context.Request.Headers["User-Agent"].ToString();

    var (user, errorCode) = await authService.ValidateLogin(username, password, ipAddress, userAgent);
    if (user is null)
    {
        context.Response.Redirect($"/login?error={errorCode ?? "invalid"}");
        return;
    }

    var mfaEnabled = await mfaService.IsMfaEnabled(user.Id, "PortalUser");
    var mfaRequired = user.TenantId.HasValue
        ? await mfaService.IsMfaRequired(user.TenantId.Value, user.Role.ToString())
        : string.Equals(user.Role.ToString(), "Approver", StringComparison.OrdinalIgnoreCase);

    if (mfaEnabled)
    {
        var pendingChallenge = await mfaChallengeStore.CreateChallenge(new MfaLoginChallenge
        {
            UserId = user.Id,
            UserType = "PortalUser",
            Username = user.Username,
            ReturnUrl = returnUrl
        }, context.RequestAborted);

        await mfaService.SendMfaCodeSms(user.Id, "PortalUser", context.RequestAborted);

        context.Response.Redirect($"/login?mfa=required&challenge={Uri.EscapeDataString(pendingChallenge)}");
        return;
    }

    if (mfaRequired && !mfaEnabled)
    {
        var setupPrincipal = await authService.BuildClaimsPrincipalWithPermissions(user, context.RequestAborted);
        await context.SignInAsync(
            AdminAuthScheme,
            setupPrincipal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });

        context.Response.Redirect("/account/mfa-setup?enroll=required");
        return;
    }

    var principal = await authService.BuildClaimsPrincipalWithPermissions(user, context.RequestAborted);

    await context.SignInAsync(
        AdminAuthScheme,
        principal,
        new AuthenticationProperties
        {
            IsPersistent = rememberMe,
            ExpiresUtc = rememberMe
                ? DateTimeOffset.UtcNow.AddDays(14)
                : DateTimeOffset.UtcNow.AddHours(8)
        });

    context.Response.Redirect(SafeRedirect(returnUrl));
});

app.MapGet("/account/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(AdminAuthScheme);
    context.Response.Redirect("/login");
});

// Forgot password — generates a reset token
app.MapPost("/account/forgot-password", async (HttpContext context, AuthService authService) =>
{
    var form = await context.Request.ReadFormAsync();
    var email = form["email"].ToString().Trim();

    if (string.IsNullOrEmpty(email))
    {
        context.Response.Redirect("/forgot-password?error=missing");
        return;
    }

    var token = await authService.GeneratePasswordResetToken(email);

    if (token is not null)
    {
        // In production, send this via email. For now, log it.
        var resetUrl = $"{context.Request.Scheme}://{context.Request.Host}/reset-password?token={token}";
        Log.Information("Password reset link generated for {Email}: {ResetUrl}", email, resetUrl);
    }

    // Always show success message to prevent email enumeration
    context.Response.Redirect("/forgot-password?sent=true");
});

// Reset password — validates token and changes password
app.MapPost("/account/reset-password", async (HttpContext context, AuthService authService) =>
{
    var form = await context.Request.ReadFormAsync();
    var token = form["token"].ToString();
    var newPassword = form["newPassword"].ToString();
    var confirmPassword = form["confirmPassword"].ToString();

    if (string.IsNullOrEmpty(token))
    {
        context.Response.Redirect("/reset-password?error=invalid");
        return;
    }

    if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 12)
    {
        context.Response.Redirect($"/reset-password?token={token}&error=weak");
        return;
    }

    if (newPassword != confirmPassword)
    {
        context.Response.Redirect($"/reset-password?token={token}&error=mismatch");
        return;
    }

    var success = await authService.ResetPasswordWithToken(token, newPassword);
    if (!success)
    {
        context.Response.Redirect("/reset-password?error=expired");
        return;
    }

    context.Response.Redirect("/login?reset=success");
});

app.MapPost("/account/reconsent", async (HttpContext context, IConsentService consentService) =>
{
    if (context.User?.Identity?.IsAuthenticated != true)
    {
        context.Response.Redirect("/login");
        return;
    }

    var tenantClaim = context.User.FindFirst("TenantId")?.Value;
    var userIdClaim = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (!Guid.TryParse(tenantClaim, out var tenantId) || !int.TryParse(userIdClaim, out var userId))
    {
        context.Response.Redirect("/login?error=denied");
        return;
    }

    var form = await context.Request.ReadFormAsync();
    var returnUrl = form["returnUrl"].ToString();
    var allowCore = string.Equals(form["allowCore"], "on", StringComparison.OrdinalIgnoreCase);
    var allowMarketing = string.Equals(form["allowMarketing"], "on", StringComparison.OrdinalIgnoreCase);
    var allowAnalytics = string.Equals(form["allowAnalytics"], "on", StringComparison.OrdinalIgnoreCase);
    var ipAddress = context.Connection.RemoteIpAddress?.ToString();
    var userAgent = context.Request.Headers["User-Agent"].ToString();

    if (!allowCore)
    {
        var target = string.IsNullOrWhiteSpace(returnUrl) ? "/" : Uri.EscapeDataString(returnUrl);
        context.Response.Redirect($"/privacy/reconsent?error=core-required&returnUrl={target}");
        return;
    }

    var userType = context.User.HasClaim(c => c.Type == "InstitutionId") ? "InstitutionUser" : "PortalUser";
    var baseRequest = new ConsentCaptureRequest
    {
        TenantId = tenantId,
        UserId = userId,
        UserType = userType,
        ConsentGiven = true,
        ConsentMethod = "button_click",
        IpAddress = ipAddress,
        UserAgent = userAgent
    };

    baseRequest.ConsentType = ConsentType.Registration;
    await consentService.RecordConsent(baseRequest, context.RequestAborted);
    baseRequest.ConsentType = ConsentType.DataProcessing;
    await consentService.RecordConsent(baseRequest, context.RequestAborted);

    baseRequest.ConsentType = ConsentType.Marketing;
    baseRequest.ConsentGiven = allowMarketing;
    await consentService.RecordConsent(baseRequest, context.RequestAborted);

    baseRequest.ConsentType = ConsentType.Analytics;
    baseRequest.ConsentGiven = allowAnalytics;
    await consentService.RecordConsent(baseRequest, context.RequestAborted);

    context.Response.Redirect(SafeRedirectReconsent(returnUrl));
});

// Validate returnUrl for reconsent — same logic, different local function scope
static string SafeRedirectReconsent(string? url)
{
    if (string.IsNullOrWhiteSpace(url))
        return "/";
    if (url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\"))
        return url;
    return "/";
}

app.MapGet("/platform/tenants/export", async (
    HttpContext context,
    FC.Engine.Admin.Services.PlatformAdminService platformAdminService) =>
{
    if (!context.User.IsInRole("PlatformAdmin") && !context.User.HasClaim("IsPlatformAdmin", "true"))
    {
        return Results.Forbid();
    }

    var query = new FC.Engine.Admin.Services.PlatformTenantListQuery
    {
        Search = context.Request.Query["search"].ToString(),
        Status = context.Request.Query["status"].ToString(),
        PlanCode = context.Request.Query["plan"].ToString(),
        LicenceType = context.Request.Query["licence"].ToString(),
        OnlyNeedsReconciliation = string.Equals(context.Request.Query["needsReconciliation"], "true", StringComparison.OrdinalIgnoreCase),
        OnlyStaleReconciliation = string.Equals(context.Request.Query["staleReconciliation"], "true", StringComparison.OrdinalIgnoreCase),
        SortBy = string.IsNullOrWhiteSpace(context.Request.Query["sortBy"]) ? "name" : context.Request.Query["sortBy"].ToString(),
        SortDescending = string.Equals(context.Request.Query["sortDesc"], "true", StringComparison.OrdinalIgnoreCase)
    };

    if (int.TryParse(context.Request.Query["minModules"], out var minModules))
    {
        query.MinModuleCount = minModules;
    }

    var bytes = await platformAdminService.ExportTenantListExcel(query, context.RequestAborted);
    var fileName = $"platform-tenants-{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx";
    return Results.File(
        bytes,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        fileName);
});

// PlatformAdmin impersonation endpoint
app.MapGet("/platform/impersonate", async (
    HttpContext context,
    MetadataDbContext db,
    IAuditLogger auditLogger) =>
{
    if (!context.User.IsInRole("PlatformAdmin") && !context.User.HasClaim("IsPlatformAdmin", "true"))
    {
        context.Response.StatusCode = 403;
        return;
    }

    var tenantIdStr = context.Request.Query["tenantId"].ToString();
    if (Guid.TryParse(tenantIdStr, out var tenantId))
    {
        var tenantExists = await db.Tenants.AnyAsync(t => t.TenantId == tenantId, context.RequestAborted);
        if (!tenantExists)
        {
            context.Response.Redirect("/platform/tenants?error=tenant-not-found");
            return;
        }

        context.Response.Cookies.Append("ImpersonateTenantId", tenantId.ToString(), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddHours(2)
        });

        if (!FC.Engine.Admin.Utilities.UserIdentityResolver.TryResolveActor(context.User, out var performedBy))
        {
            context.Response.StatusCode = 401;
            return;
        }

        await auditLogger.Log(
            "Tenant",
            0,
            "ImpersonationStarted",
            null,
            new
            {
                IsPlatformAdmin = true,
                ImpersonatedTenantId = tenantId
            },
            performedBy,
            context.RequestAborted);
    }

    context.Response.Redirect("/");
});

// Stop impersonation
app.MapGet("/platform/stop-impersonation", async (
    HttpContext context,
    IAuditLogger auditLogger) =>
{
    if (!context.User.IsInRole("PlatformAdmin") && !context.User.HasClaim("IsPlatformAdmin", "true"))
    {
        context.Response.StatusCode = 403;
        return;
    }

    if (context.Request.Cookies.TryGetValue("ImpersonateTenantId", out var existingTenantId))
    {
        if (!FC.Engine.Admin.Utilities.UserIdentityResolver.TryResolveActor(context.User, out var performedBy))
        {
            context.Response.StatusCode = 401;
            return;
        }

        await auditLogger.Log(
            "Tenant",
            0,
            "ImpersonationStopped",
            null,
            new
            {
                IsPlatformAdmin = true,
                ImpersonatedTenantId = existingTenantId
            },
            performedBy,
            context.RequestAborted);
    }

    context.Response.Cookies.Delete("ImpersonateTenantId");
    context.Response.Redirect("/platform/tenants");
});

// Session ping — extends the sliding auth cookie; called by FCSession.ping() JS
app.MapPost("/api/session/ping", (HttpContext ctx) =>
    ctx.User.Identity?.IsAuthenticated == true
        ? Results.Ok(new { extended = true, utc = DateTimeOffset.UtcNow })
        : Results.Unauthorized())
    .RequireAuthorization();

app.MapGet("/api/intelligence/overview", async (
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var workspace = await intelligenceService.GetWorkspaceAsync(ct);
    return Results.Ok(new
    {
        workspace.GeneratedAt,
        workspace.Hero,
        workspace.Refresh,
        InstitutionCount = workspace.InstitutionScorecards.Count,
        InterventionCount = workspace.Interventions.Count,
        TimelineCount = workspace.ActivityTimeline.Count
    });
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/exports/activity", async (
    string? area,
    string? format,
    string? action,
    int? take,
    FC.Engine.Admin.Services.PlatformIntelligenceExportAuditService exportAuditService,
    CancellationToken ct) =>
{
    var rows = await exportAuditService.GetRecentExportsAsync(
        area,
        format,
        action,
        FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeTake(take, 25, 100),
        ct);

    return Results.Ok(new
    {
        Total = rows.Count,
        Rows = rows
    });
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/overview/export.csv", async (
    HttpContext context,
    FC.Engine.Admin.Services.PlatformIntelligenceExportService exportService,
    IAuditLogger auditLogger,
    CancellationToken ct) =>
{
    var file = await exportService.ExportOverviewCsvAsync(ct);
    await AuditIntelligenceExportAsync(context, auditLogger, "OverviewExported", "Overview", "csv", file, null, null, ct);
    return Results.File(file.Content, file.ContentType, file.FileName);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/overview/export.pdf", async (
    HttpContext context,
    FC.Engine.Admin.Services.PlatformIntelligenceExportService exportService,
    IAuditLogger auditLogger,
    CancellationToken ct) =>
{
    var file = await exportService.ExportOverviewPdfAsync(ct);
    await AuditIntelligenceExportAsync(context, auditLogger, "OverviewExported", "Overview", "pdf", file, null, null, ct);
    return Results.File(file.Content, file.ContentType, file.FileName);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/export-bundle.zip", async (
    string? lens,
    int? institutionId,
    HttpContext context,
    FC.Engine.Admin.Services.PlatformIntelligenceExportService exportService,
    IAuditLogger auditLogger,
    CancellationToken ct) =>
{
    if (!FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.TryNormalizeDashboardBriefingPackQuery(
            lens,
            institutionId,
            out var query,
            out var error))
    {
        return Results.BadRequest(new { error });
    }

    var file = await exportService.ExportBundleAsync(query.Lens, query.InstitutionId, ct);
    if (file is null)
    {
        return Results.NotFound();
    }

    await AuditIntelligenceExportAsync(
        context,
        auditLogger,
        "BundleExported",
        "Bundle",
        "zip",
        file,
        query.Lens,
        query.InstitutionId,
        ct);

    return Results.File(file.Content, file.ContentType, file.FileName);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/refresh/status", async (
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var snapshot = await intelligenceService.GetRefreshSnapshotAsync(ct);
    return Results.Ok(snapshot);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/refresh/runs", async (
    int? take,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var size = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeTake(take, 8, 50);
    var rows = await intelligenceService.GetRecentRefreshRunsAsync(size, ct);
    return Results.Ok(rows);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/refresh/freshness", async (
    string? status,
    string? area,
    int? take,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var snapshot = await intelligenceService.GetRefreshSnapshotAsync(ct);
    var normalizedStatus = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(status);
    var normalizedArea = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(area);
    var size = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeTake(take, 25, 100);

    var rows = snapshot.CatalogFreshness
        .Where(x => normalizedStatus is null || x.Status.Equals(normalizedStatus, StringComparison.OrdinalIgnoreCase))
        .Where(x => normalizedArea is null || x.Area.Equals(normalizedArea, StringComparison.OrdinalIgnoreCase))
        .Take(size)
        .ToList();

    return Results.Ok(new
    {
        snapshot.GeneratedAtUtc,
        snapshot.Status,
        snapshot.IsStale,
        Total = rows.Count,
        Rows = rows
    });
}).RequireAuthorization("Authenticated");

app.MapPost("/api/intelligence/refresh/run", async (
    HttpContext context,
    FC.Engine.Admin.Services.PlatformIntelligenceRefreshService refreshService,
    IAuditLogger auditLogger) =>
{
    var result = await refreshService.RefreshAsync(context.RequestAborted);
    var performedBy = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? context.User.Identity?.Name
                      ?? "platform-intelligence-api";

    await auditLogger.Log(
        "PlatformIntelligence",
        0,
        "RefreshTriggered",
        null,
        new
        {
            result.GeneratedAt,
            result.DurationMilliseconds,
            result.InstitutionCount,
            result.InterventionCount,
            result.TimelineCount,
            result.DashboardPacksMaterialized
        },
        performedBy,
        context.RequestAborted);

    return Results.Ok(result);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/overview/interventions", async (
    string? domain,
    string? priority,
    string? ownerLane,
    int? take,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var workspace = await intelligenceService.GetWorkspaceAsync(ct);
    var normalizedDomain = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(domain);
    var normalizedPriority = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(priority);
    var normalizedOwnerLane = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(ownerLane);
    var size = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeTake(take, 25, 100);

    var rows = workspace.Interventions
        .Where(x => normalizedDomain is null || x.Domain.Equals(normalizedDomain, StringComparison.OrdinalIgnoreCase))
        .Where(x => normalizedPriority is null || x.Priority.Equals(normalizedPriority, StringComparison.OrdinalIgnoreCase))
        .Where(x => normalizedOwnerLane is null || x.OwnerLane.Equals(normalizedOwnerLane, StringComparison.OrdinalIgnoreCase))
        .Take(size)
        .ToList();

    return Results.Ok(new
    {
        workspace.OperationsCatalogMaterializedAt,
        Total = rows.Count,
        Rows = rows
    });
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/overview/timeline", async (
    string? domain,
    string? severity,
    int? institutionId,
    int? take,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var workspace = await intelligenceService.GetWorkspaceAsync(ct);
    var normalizedDomain = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(domain);
    var normalizedSeverity = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(severity);
    var size = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeTake(take, 25, 100);

    var rows = workspace.ActivityTimeline
        .Where(x => normalizedDomain is null || x.Domain.Equals(normalizedDomain, StringComparison.OrdinalIgnoreCase))
        .Where(x => normalizedSeverity is null || x.Severity.Equals(normalizedSeverity, StringComparison.OrdinalIgnoreCase))
        .Where(x => !institutionId.HasValue || x.InstitutionId == institutionId.Value)
        .Take(size)
        .ToList();

    return Results.Ok(new
    {
        workspace.OperationsCatalogMaterializedAt,
        Total = rows.Count,
        Rows = rows
    });
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/institutions/scorecards", async (
    string? priority,
    string? licenceType,
    int? take,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var workspace = await intelligenceService.GetWorkspaceAsync(ct);
    var normalizedPriority = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(priority);
    var normalizedLicenceType = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(licenceType);
    var size = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeTake(take, 25, 100);

    var rows = workspace.InstitutionScorecards
        .Where(x => normalizedPriority is null || x.Priority.Equals(normalizedPriority, StringComparison.OrdinalIgnoreCase))
        .Where(x => normalizedLicenceType is null || x.LicenceType.Equals(normalizedLicenceType, StringComparison.OrdinalIgnoreCase))
        .Take(size)
        .ToList();

    return Results.Ok(new
    {
        workspace.InstitutionCatalogMaterializedAt,
        Total = rows.Count,
        Rows = rows
    });
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/institutions/{institutionId:int}", async (
    int institutionId,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    if (institutionId <= 0)
    {
        return Results.BadRequest(new { error = "InstitutionId must be greater than zero." });
    }

    var detail = await intelligenceService.GetInstitutionIntelligenceDetailAsync(institutionId, ct);
    return detail is null ? Results.NotFound() : Results.Ok(detail);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/rollout/overview", async (
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var snapshot = await intelligenceService.GetMarketplaceRolloutSnapshotAsync(ct);
    return Results.Ok(snapshot);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/rollout/plan-coverage", async (
    string? moduleCode,
    string? planCode,
    string? signal,
    int? take,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var snapshot = await intelligenceService.GetMarketplaceRolloutSnapshotAsync(ct);
    var normalizedModuleCode = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(moduleCode);
    var normalizedPlanCode = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(planCode);
    var normalizedSignal = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(signal);
    var size = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeTake(take, 25, 100);

    var rows = snapshot.PlanCoverage
        .Where(x => normalizedModuleCode is null || x.ModuleCode.Equals(normalizedModuleCode, StringComparison.OrdinalIgnoreCase))
        .Where(x => normalizedPlanCode is null || x.PlanCode.Equals(normalizedPlanCode, StringComparison.OrdinalIgnoreCase))
        .Where(x => normalizedSignal is null || x.Signal.Equals(normalizedSignal, StringComparison.OrdinalIgnoreCase))
        .Take(size)
        .ToList();

    return Results.Ok(new
    {
        snapshot.CatalogMaterializedAt,
        Total = rows.Count,
        Rows = rows
    });
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/rollout/reconciliation-queue", async (
    string? state,
    string? signal,
    int? take,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var snapshot = await intelligenceService.GetMarketplaceRolloutSnapshotAsync(ct);
    var normalizedState = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(state);
    var normalizedSignal = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(signal);
    var size = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeTake(take, 25, 100);

    var rows = snapshot.ReconciliationQueue
        .Where(x => normalizedState is null || x.State.Equals(normalizedState, StringComparison.OrdinalIgnoreCase))
        .Where(x => normalizedSignal is null || x.Signal.Equals(normalizedSignal, StringComparison.OrdinalIgnoreCase))
        .Take(size)
        .ToList();

    return Results.Ok(new
    {
        snapshot.CatalogMaterializedAt,
        Total = rows.Count,
        Rows = rows
    });
}).RequireAuthorization("Authenticated");

app.MapPost("/api/intelligence/rollout/reconcile/{tenantId:guid}", async (
    Guid tenantId,
    HttpContext context,
    FC.Engine.Admin.Services.TenantManagementService tenantService,
    IAuditLogger auditLogger) =>
{
    if (tenantId == Guid.Empty)
    {
        return Results.BadRequest(new { error = "TenantId is required." });
    }

    var result = await tenantService.ReconcileTenantModulesAsync(tenantId, context.RequestAborted);
    var performedBy = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? context.User.Identity?.Name
                      ?? "platform-intelligence-api";

    await auditLogger.Log(
        "MarketplaceRollout",
        0,
        "RolloutTenantReconciliationRequested",
        null,
        new
        {
            TenantId = tenantId,
            result.ModulesCreated,
            result.ModulesReactivated,
            result.ModulesUpdated,
            result.ModulesDeactivated,
            result.TenantsTouched
        },
        performedBy,
        context.RequestAborted);

    return Results.Ok(result);
}).RequireAuthorization("PlatformAdmin");

app.MapPost("/api/intelligence/rollout/reconcile", async (
    FC.Engine.Admin.Services.RolloutReconciliationApiRequest request,
    HttpContext context,
    FC.Engine.Admin.Services.TenantManagementService tenantService,
    IAuditLogger auditLogger) =>
{
    if (!FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.TryNormalizeRolloutReconciliationRequest(
            request,
            out var tenantIds,
            out var error))
    {
        return Results.BadRequest(new { error });
    }

    var result = await tenantService.ReconcileTenantModulesAsync(tenantIds, context.RequestAborted);
    var performedBy = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? context.User.Identity?.Name
                      ?? "platform-intelligence-api";

    await auditLogger.Log(
        "MarketplaceRollout",
        0,
        "RolloutBatchReconciliationRequested",
        null,
        new
        {
            RequestedTenantCount = tenantIds.Count,
            result.ProcessedTenants,
            result.Reconciliation.ModulesCreated,
            result.Reconciliation.ModulesReactivated,
            result.Reconciliation.ModulesUpdated,
            result.Reconciliation.ModulesDeactivated
        },
        performedBy,
        context.RequestAborted);

    return Results.Ok(result);
}).RequireAuthorization("PlatformAdmin");

app.MapGet("/api/intelligence/dashboards/briefing-pack", async (
    string? lens,
    int? institutionId,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    FC.Engine.Admin.Services.DashboardBriefingPackBuilder briefingPackBuilder,
    CancellationToken ct) =>
{
    if (!FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.TryNormalizeDashboardBriefingPackQuery(
            lens,
            institutionId,
            out var query,
            out var error))
    {
        return Results.BadRequest(new { error });
    }

    var state = await intelligenceService.GetDashboardBriefingPackCatalogStateAsync(query.Lens, query.InstitutionId, ct);
    if (state.Sections.Count > 0)
    {
        return Results.Ok(state);
    }

    var workspace = await intelligenceService.GetWorkspaceAsync(ct);
    var screeningSession = await intelligenceService.GetSanctionsScreeningSessionStateAsync(ct);
    var workflowState = await intelligenceService.GetSanctionsWorkflowStateAsync(ct);
    var strDraftCatalog = await intelligenceService.GetSanctionsStrDraftCatalogStateAsync(ct);
    var sections = briefingPackBuilder.Build(workspace, query.Lens, query.InstitutionId, screeningSession, workflowState, strDraftCatalog);

    if (query.Lens == "executive" && sections.Count == 0)
    {
        return Results.NotFound();
    }

    state = await intelligenceService.MaterializeDashboardBriefingPackAsync(query.Lens, query.InstitutionId, sections, ct);
    return Results.Ok(state);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/dashboards/briefing-pack/export.csv", async (
    string? lens,
    int? institutionId,
    HttpContext context,
    FC.Engine.Admin.Services.PlatformIntelligenceExportService exportService,
    IAuditLogger auditLogger,
    CancellationToken ct) =>
{
    if (!FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.TryNormalizeDashboardBriefingPackQuery(
            lens,
            institutionId,
            out var query,
            out var error))
    {
        return Results.BadRequest(new { error });
    }

    var file = await exportService.ExportDashboardBriefingPackCsvAsync(query.Lens, query.InstitutionId, ct);
    if (file is null)
    {
        return Results.NotFound();
    }

    await AuditIntelligenceExportAsync(context, auditLogger, "DashboardBriefingPackExported", "Dashboards", "csv", file, query.Lens, query.InstitutionId, ct);
    return Results.File(file.Content, file.ContentType, file.FileName);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/dashboards/briefing-pack/export.pdf", async (
    string? lens,
    int? institutionId,
    HttpContext context,
    FC.Engine.Admin.Services.PlatformIntelligenceExportService exportService,
    IAuditLogger auditLogger,
    CancellationToken ct) =>
{
    if (!FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.TryNormalizeDashboardBriefingPackQuery(
            lens,
            institutionId,
            out var query,
            out var error))
    {
        return Results.BadRequest(new { error });
    }

    var file = await exportService.ExportDashboardBriefingPackPdfAsync(query.Lens, query.InstitutionId, ct);
    if (file is null)
    {
        return Results.NotFound();
    }

    await AuditIntelligenceExportAsync(context, auditLogger, "DashboardBriefingPackExported", "Dashboards", "pdf", file, query.Lens, query.InstitutionId, ct);
    return Results.File(file.Content, file.ContentType, file.FileName);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/knowledge/obligations", async (
    int? institutionId,
    string? status,
    string? regulatorCode,
    string? moduleCode,
    int? take,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var snapshot = await intelligenceService.GetKnowledgeGraphSnapshotAsync(ct);
    var normalizedStatus = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(status);
    var normalizedRegulator = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(regulatorCode);
    var normalizedModule = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(moduleCode);
    var size = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeTake(take, 50);

    var rows = snapshot.InstitutionObligations
        .Where(x => !institutionId.HasValue || x.InstitutionId == institutionId.Value)
        .Where(x => normalizedStatus is null || x.Status.Equals(normalizedStatus, StringComparison.OrdinalIgnoreCase))
        .Where(x => normalizedRegulator is null || x.RegulatorCode.Equals(normalizedRegulator, StringComparison.OrdinalIgnoreCase))
        .Where(x => normalizedModule is null || x.ModuleCode.Equals(normalizedModule, StringComparison.OrdinalIgnoreCase))
        .Take(size)
        .ToList();

    return Results.Ok(rows);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/knowledge/catalog", async (
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var state = await intelligenceService.GetKnowledgeGraphCatalogStateAsync(ct);
    return Results.Ok(new
    {
        state.MaterializedAt,
        state.NodeCount,
        state.EdgeCount,
        NodeTypes = state.NodeTypes,
        EdgeTypes = state.EdgeTypes
    });
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/knowledge/catalog/nodes", async (
    string? nodeType,
    string? regulatorCode,
    string? sourceReference,
    string? search,
    int? take,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var state = await intelligenceService.GetKnowledgeGraphCatalogStateAsync(ct);
    var normalizedNodeType = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(nodeType);
    var normalizedRegulatorCode = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(regulatorCode);
    var normalizedSourceReference = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(sourceReference);
    var normalizedSearch = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(search);
    var size = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeTake(take, 25, 100);

    var rows = state.Nodes
        .Where(x => normalizedNodeType is null || x.NodeType.Equals(normalizedNodeType, StringComparison.OrdinalIgnoreCase))
        .Where(x => normalizedRegulatorCode is null || string.Equals(x.RegulatorCode, normalizedRegulatorCode, StringComparison.OrdinalIgnoreCase))
        .Where(x => normalizedSourceReference is null || string.Equals(x.SourceReference, normalizedSourceReference, StringComparison.OrdinalIgnoreCase))
        .Where(x => normalizedSearch is null
            || x.NodeKey.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
            || x.DisplayName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(x.Code) && x.Code.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)))
        .Take(size)
        .ToList();

    return Results.Ok(new
    {
        state.MaterializedAt,
        Total = rows.Count,
        Rows = rows
    });
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/knowledge/impact-propagation", async (
    int? institutionId,
    string? signal,
    int? take,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var snapshot = await intelligenceService.GetKnowledgeGraphSnapshotAsync(ct);
    var normalizedSignal = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(signal);
    var size = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeTake(take, 25);

    HashSet<string>? institutionReturns = null;
    if (institutionId.HasValue)
    {
        institutionReturns = snapshot.InstitutionObligations
            .Where(x => x.InstitutionId == institutionId.Value)
            .Select(x => x.ReturnCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    var rows = snapshot.ImpactPropagation
        .Where(x => institutionReturns is null || institutionReturns.Contains(x.PrimaryReturnCode))
        .Where(x => normalizedSignal is null || x.Signal.Equals(normalizedSignal, StringComparison.OrdinalIgnoreCase))
        .Take(size)
        .ToList();

    return Results.Ok(rows);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/knowledge/catalog/edges", async (
    string? edgeType,
    string? regulatorCode,
    string? sourceNodeKey,
    string? targetNodeKey,
    int? take,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var state = await intelligenceService.GetKnowledgeGraphCatalogStateAsync(ct);
    var normalizedEdgeType = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(edgeType);
    var normalizedRegulatorCode = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(regulatorCode);
    var normalizedSourceNodeKey = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(sourceNodeKey);
    var normalizedTargetNodeKey = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(targetNodeKey);
    var size = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeTake(take, 25, 100);

    var rows = state.Edges
        .Where(x => normalizedEdgeType is null || x.EdgeType.Equals(normalizedEdgeType, StringComparison.OrdinalIgnoreCase))
        .Where(x => normalizedRegulatorCode is null || string.Equals(x.RegulatorCode, normalizedRegulatorCode, StringComparison.OrdinalIgnoreCase))
        .Where(x => normalizedSourceNodeKey is null || x.SourceNodeKey.Equals(normalizedSourceNodeKey, StringComparison.OrdinalIgnoreCase))
        .Where(x => normalizedTargetNodeKey is null || x.TargetNodeKey.Equals(normalizedTargetNodeKey, StringComparison.OrdinalIgnoreCase))
        .Take(size)
        .ToList();

    return Results.Ok(new
    {
        state.MaterializedAt,
        Total = rows.Count,
        Rows = rows
    });
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/knowledge/dossier", async (
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var snapshot = await intelligenceService.GetKnowledgeGraphSnapshotAsync(ct);
    return Results.Ok(snapshot.DossierPack);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/knowledge/dossier/export.csv", async (
    HttpContext context,
    FC.Engine.Admin.Services.PlatformIntelligenceExportService exportService,
    IAuditLogger auditLogger,
    CancellationToken ct) =>
{
    var file = await exportService.ExportKnowledgeDossierCsvAsync(ct);
    await AuditIntelligenceExportAsync(context, auditLogger, "KnowledgeDossierExported", "Knowledge", "csv", file, null, null, ct);
    return Results.File(file.Content, file.ContentType, file.FileName);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/knowledge/navigator", async (
    string? key,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    if (!FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.TryNormalizeKnowledgeNavigatorKey(
            key,
            out var normalizedKey,
            out var error))
    {
        return Results.BadRequest(new { error });
    }

    var detail = await intelligenceService.GetKnowledgeNavigatorDetailAsync(normalizedKey, ct);
    return detail is null ? Results.NotFound() : Results.Ok(detail);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/resilience/overview", async (
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var snapshot = await intelligenceService.GetResilienceSnapshotAsync(ct);
    return Results.Ok(snapshot);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/resilience/pack", async (
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var snapshot = await intelligenceService.GetResilienceSnapshotAsync(ct);
    return Results.Ok(snapshot.ReturnPack);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/resilience/pack/export.csv", async (
    HttpContext context,
    FC.Engine.Admin.Services.PlatformIntelligenceExportService exportService,
    IAuditLogger auditLogger,
    CancellationToken ct) =>
{
    var file = await exportService.ExportResiliencePackCsvAsync(ct);
    await AuditIntelligenceExportAsync(context, auditLogger, "ResiliencePackExported", "Resilience", "csv", file, null, null, ct);
    return Results.File(file.Content, file.ContentType, file.FileName);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/resilience/incidents", async (
    int? take,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var snapshot = await intelligenceService.GetResilienceSnapshotAsync(ct);
    var size = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeTake(take, 12, 50);
    return Results.Ok(snapshot.IncidentTimelines.Take(size).ToList());
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/resilience/self-assessment", async (
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var state = await intelligenceService.GetResilienceAssessmentStateAsync(ct);
    return Results.Ok(state);
}).RequireAuthorization("Authenticated");

app.MapPost("/api/intelligence/resilience/self-assessment", async (
    FC.Engine.Admin.Services.ResilienceAssessmentApiRequest request,
    HttpContext context,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    IAuditLogger auditLogger) =>
{
    if (!FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.TryNormalizeResilienceAssessmentRequest(
            request,
            out var command,
            out var error))
    {
        return Results.BadRequest(new { error });
    }

    await intelligenceService.RecordResilienceAssessmentResponseAsync(command, context.RequestAborted);
    var performedBy = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? context.User.Identity?.Name
                      ?? "platform-intelligence-api";

    await auditLogger.Log(
        "ResilienceAssessment",
        0,
        "SelfAssessmentResponseRecorded",
        null,
        new
        {
            command.QuestionId,
            command.Domain,
            command.Score,
            command.AnsweredAtUtc
        },
        performedBy,
        context.RequestAborted);

    return Results.Ok(command);
}).RequireAuthorization("Authenticated");

app.MapDelete("/api/intelligence/resilience/self-assessment", async (
    HttpContext context,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    IAuditLogger auditLogger) =>
{
    await intelligenceService.ResetResilienceAssessmentAsync(context.RequestAborted);
    var performedBy = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? context.User.Identity?.Name
                      ?? "platform-intelligence-api";

    await auditLogger.Log(
        "ResilienceAssessment",
        0,
        "SelfAssessmentReset",
        null,
        new { ResetAtUtc = DateTime.UtcNow },
        performedBy,
        context.RequestAborted);

    return Results.Ok(new { reset = true, utc = DateTimeOffset.UtcNow });
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/model-risk/overview", async (
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var snapshot = await intelligenceService.GetModelRiskSnapshotAsync(ct);
    return Results.Ok(snapshot);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/model-risk/catalog", async (
    string? owner,
    string? tier,
    string? returnHint,
    int? take,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var state = await intelligenceService.GetModelInventoryCatalogStateAsync(ct);
    var normalizedOwner = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(owner);
    var normalizedTier = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(tier);
    var normalizedReturnHint = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(returnHint);
    var size = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeTake(take, 25, 100);

    var rows = state.Definitions
        .Where(x => normalizedOwner is null || x.Owner.Equals(normalizedOwner, StringComparison.OrdinalIgnoreCase))
        .Where(x => normalizedTier is null || x.Tier.Equals(normalizedTier, StringComparison.OrdinalIgnoreCase))
        .Where(x => normalizedReturnHint is null || x.ReturnHint.Contains(normalizedReturnHint, StringComparison.OrdinalIgnoreCase))
        .Take(size)
        .ToList();

    return Results.Ok(new
    {
        state.MaterializedAt,
        Total = rows.Count,
        Rows = rows
    });
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/model-risk/pack", async (
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var snapshot = await intelligenceService.GetModelRiskSnapshotAsync(ct);
    return Results.Ok(snapshot.ReturnPack);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/model-risk/pack/export.csv", async (
    HttpContext context,
    FC.Engine.Admin.Services.PlatformIntelligenceExportService exportService,
    IAuditLogger auditLogger,
    CancellationToken ct) =>
{
    var file = await exportService.ExportModelRiskPackCsvAsync(ct);
    await AuditIntelligenceExportAsync(context, auditLogger, "ModelRiskPackExported", "Model Risk", "csv", file, null, null, ct);
    return Results.File(file.Content, file.ContentType, file.FileName);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/model-risk/inventory", async (
    int? take,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var snapshot = await intelligenceService.GetModelRiskSnapshotAsync(ct);
    var size = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeTake(take, 25, 100);
    return Results.Ok(snapshot.Inventory.Take(size).ToList());
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/model-risk/backtesting", async (
    int? take,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var snapshot = await intelligenceService.GetModelRiskSnapshotAsync(ct);
    var size = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeTake(take, 25, 100);
    return Results.Ok(snapshot.Backtesting.Take(size).ToList());
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/model-risk/monitoring", async (
    int? take,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var snapshot = await intelligenceService.GetModelRiskSnapshotAsync(ct);
    var size = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeTake(take, 25, 100);
    return Results.Ok(snapshot.MonitoringSummary.Take(size).ToList());
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/model-risk/approval-queue", async (
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var snapshot = await intelligenceService.GetModelRiskSnapshotAsync(ct);
    return Results.Ok(snapshot.ApprovalQueue);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/model-risk/approval-workflow", async (
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var state = await intelligenceService.GetModelApprovalWorkflowStateAsync(ct);
    return Results.Ok(state);
}).RequireAuthorization("Authenticated");

app.MapPost("/api/intelligence/model-risk/approval-workflow", async (
    FC.Engine.Admin.Services.ModelApprovalStageApiRequest request,
    HttpContext context,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    IAuditLogger auditLogger) =>
{
    if (!FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.TryNormalizeModelApprovalStageRequest(
            request,
            out var command,
            out var error))
    {
        return Results.BadRequest(new { error });
    }

    await intelligenceService.RecordModelApprovalStageAsync(command, context.RequestAborted);
    var performedBy = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? context.User.Identity?.Name
                      ?? "platform-intelligence-api";

    await auditLogger.Log(
        "ModelRisk",
        0,
        "ModelApprovalStageChanged",
        null,
        new
        {
            command.WorkflowKey,
            command.ModelCode,
            command.PreviousStage,
            command.Stage,
            command.ChangedAtUtc
        },
        performedBy,
        context.RequestAborted);

    return Results.Ok(command);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/capital/scenario", async (
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var state = await intelligenceService.GetCapitalPlanningScenarioStateAsync(ct);
    return state is null ? Results.NotFound() : Results.Ok(state);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/capital/overview", async (
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var snapshot = await intelligenceService.GetCapitalSnapshotAsync(ct);
    return Results.Ok(snapshot);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/capital/pack", async (
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var snapshot = await intelligenceService.GetCapitalSnapshotAsync(ct);
    return Results.Ok(snapshot.ReturnPack);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/capital/action-catalog", async (
    string? lever,
    string? code,
    int? take,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var state = await intelligenceService.GetCapitalActionCatalogStateAsync(ct);
    var normalizedLever = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(lever);
    var normalizedCode = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(code);
    var size = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeTake(take, 25, 100);

    var rows = state.Templates
        .Where(x => normalizedLever is null || x.PrimaryLever.Equals(normalizedLever, StringComparison.OrdinalIgnoreCase))
        .Where(x => normalizedCode is null || x.Code.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase))
        .Take(size)
        .ToList();

    return Results.Ok(new
    {
        state.MaterializedAt,
        Total = rows.Count,
        Rows = rows
    });
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/capital/pack/export.csv", async (
    HttpContext context,
    FC.Engine.Admin.Services.PlatformIntelligenceExportService exportService,
    IAuditLogger auditLogger,
    CancellationToken ct) =>
{
    var file = await exportService.ExportCapitalPackCsvAsync(ct);
    await AuditIntelligenceExportAsync(context, auditLogger, "CapitalPackExported", "Capital", "csv", file, null, null, ct);
    return Results.File(file.Content, file.ContentType, file.FileName);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/capital/scenario/history", async (
    int? take,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var size = take.GetValueOrDefault(8);
    size = Math.Clamp(size, 1, 50);
    var history = await intelligenceService.GetCapitalPlanningScenarioHistoryAsync(size, ct);
    return Results.Ok(history);
}).RequireAuthorization("Authenticated");

app.MapPost("/api/intelligence/capital/scenario", async (
    FC.Engine.Admin.Services.CapitalPlanningScenarioApiRequest request,
    HttpContext context,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    IAuditLogger auditLogger) =>
{
    if (!FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.TryNormalizeCapitalScenarioRequest(
            request,
            out var command,
            out var error))
    {
        return Results.BadRequest(new { error });
    }

    var state = await intelligenceService.RecordCapitalPlanningScenarioAsync(command, context.RequestAborted);
    var performedBy = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? context.User.Identity?.Name
                      ?? "platform-intelligence-api";

    await auditLogger.Log(
        "CapitalPlanning",
        0,
        "CapitalScenarioSaved",
        null,
        new
        {
            state.CurrentCarPercent,
            state.TargetCarPercent,
            state.CurrentRwaBn,
            state.CapitalActionBn,
            state.RwaOptimisationPercent,
            state.SavedAtUtc
        },
        performedBy,
        context.RequestAborted);

    return Results.Ok(state);
}).RequireAuthorization("Authenticated");

app.MapPost("/api/intelligence/sanctions/screen", async (
    FC.Engine.Admin.Services.SanctionsBatchScreeningApiRequest request,
    HttpContext context,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    IAuditLogger auditLogger) =>
{
    if (!FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.TryNormalizeBatchScreeningRequest(
            request,
            out var command,
            out var error))
    {
        return Results.BadRequest(new { error });
    }

    var run = await intelligenceService.ScreenSubjectsAsync(command.Subjects, command.Threshold, context.RequestAborted);
    var performedBy = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? context.User.Identity?.Name
                      ?? "platform-intelligence-api";

    await auditLogger.Log(
        "Sanctions",
        0,
        "BatchScreeningRunRequested",
        null,
        new
        {
            SubjectCount = command.Subjects.Count,
            ThresholdPercent = run.ThresholdPercent,
            MatchCount = run.MatchCount
        },
        performedBy,
        context.RequestAborted);

    return Results.Ok(run);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/sanctions/overview", async (
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var snapshot = await intelligenceService.GetSanctionsSnapshotAsync(ct);
    return Results.Ok(snapshot);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/sanctions/catalog/sources", async (
    string? status,
    int? take,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var state = await intelligenceService.GetSanctionsCatalogStateAsync(ct);
    var normalizedStatus = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(status);
    var size = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeTake(take, 25, 100);

    var rows = state.Sources
        .Where(x => normalizedStatus is null || x.Status.Equals(normalizedStatus, StringComparison.OrdinalIgnoreCase))
        .Take(size)
        .ToList();

    return Results.Ok(new
    {
        state.MaterializedAt,
        Total = rows.Count,
        Rows = rows
    });
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/sanctions/catalog/entries", async (
    string? sourceCode,
    string? category,
    string? riskLevel,
    string? search,
    int? take,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var state = await intelligenceService.GetSanctionsCatalogStateAsync(ct);
    var normalizedSourceCode = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(sourceCode);
    var normalizedCategory = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(category);
    var normalizedRiskLevel = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(riskLevel);
    var normalizedSearch = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(search);
    var size = FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.NormalizeTake(take, 25, 100);

    var rows = state.Entries
        .Where(x => normalizedSourceCode is null || x.SourceCode.Equals(normalizedSourceCode, StringComparison.OrdinalIgnoreCase))
        .Where(x => normalizedCategory is null || x.Category.Equals(normalizedCategory, StringComparison.OrdinalIgnoreCase))
        .Where(x => normalizedRiskLevel is null || x.RiskLevel.Equals(normalizedRiskLevel, StringComparison.OrdinalIgnoreCase))
        .Where(x => normalizedSearch is null
            || x.PrimaryName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
            || x.Aliases.Any(alias => alias.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)))
        .Take(size)
        .ToList();

    return Results.Ok(new
    {
        state.MaterializedAt,
        Total = rows.Count,
        Rows = rows
    });
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/sanctions/pack", async (
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var snapshot = await intelligenceService.GetSanctionsSnapshotAsync(ct);
    return Results.Ok(snapshot.ReturnPack);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/sanctions/workflow", async (
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var state = await intelligenceService.GetSanctionsWorkflowStateAsync(ct);
    return Results.Ok(state);
}).RequireAuthorization("Authenticated");

app.MapPost("/api/intelligence/sanctions/workflow", async (
    FC.Engine.Admin.Services.SanctionsWorkflowDecisionApiRequest request,
    HttpContext context,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    IAuditLogger auditLogger) =>
{
    if (!FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.TryNormalizeSanctionsWorkflowDecisionRequest(
            request,
            out var command,
            out var error))
    {
        return Results.BadRequest(new { error });
    }

    await intelligenceService.RecordSanctionsDecisionAsync(command, context.RequestAborted);
    var performedBy = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? context.User.Identity?.Name
                      ?? "platform-intelligence-api";

    await auditLogger.Log(
        "Sanctions",
        0,
        "SanctionsWorkflowDecisionRecorded",
        null,
        new
        {
            command.MatchKey,
            command.Subject,
            command.SourceCode,
            command.PreviousDecision,
            command.Decision,
            command.ReviewedAtUtc
        },
        performedBy,
        context.RequestAborted);

    return Results.Ok(command);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/sanctions/session", async (
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var state = await intelligenceService.GetSanctionsScreeningSessionStateAsync(ct);
    return Results.Ok(state);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/sanctions/str-drafts", async (
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    CancellationToken ct) =>
{
    var state = await intelligenceService.GetSanctionsStrDraftCatalogStateAsync(ct);
    return Results.Ok(state);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/intelligence/sanctions/pack/export.csv", async (
    HttpContext context,
    FC.Engine.Admin.Services.PlatformIntelligenceExportService exportService,
    IAuditLogger auditLogger,
    CancellationToken ct) =>
{
    var file = await exportService.ExportSanctionsPackCsvAsync(ct);
    await AuditIntelligenceExportAsync(context, auditLogger, "SanctionsPackExported", "Sanctions", "csv", file, null, null, ct);
    return Results.File(file.Content, file.ContentType, file.FileName);
}).RequireAuthorization("Authenticated");

app.MapPost("/api/intelligence/sanctions/transactions/screen", async (
    FC.Engine.Admin.Services.SanctionsTransactionScreeningApiRequest request,
    HttpContext context,
    FC.Engine.Admin.Services.PlatformIntelligenceService intelligenceService,
    IAuditLogger auditLogger) =>
{
    if (!FC.Engine.Admin.Services.PlatformIntelligenceApiRequestMapper.TryNormalizeTransactionScreeningRequest(
            request,
            out var command,
            out var error))
    {
        return Results.BadRequest(new { error });
    }

    var result = await intelligenceService.ScreenTransactionAsync(command, context.RequestAborted);
    var performedBy = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? context.User.Identity?.Name
                      ?? "platform-intelligence-api";

    await auditLogger.Log(
        "Sanctions",
        0,
        "TransactionScreeningRunRequested",
        null,
        new
        {
            result.TransactionReference,
            result.ControlDecision,
            result.RequiresStrDraft,
            result.ThresholdPercent
        },
        performedBy,
        context.RequestAborted);

    return Results.Ok(result);
}).RequireAuthorization("Authenticated");

// ── Regulator API Endpoints ──────────────────────────────────────────────────

app.MapGet("/regulator/workspace/{projectId:int}/report", async (
    int projectId,
    HttpContext context,
    ITenantContext tenantContext,
    ITenantAccessContextResolver tenantAccessContextResolver,
    IExaminationWorkspaceService workspaceService) =>
{
    if (!tenantContext.CurrentTenantId.HasValue)
        return Results.Unauthorized();

    var accessContext = await tenantAccessContextResolver.TryResolveAsync(
        tenantContext.CurrentTenantId.Value,
        context.User,
        context.RequestAborted);
    if (accessContext?.TenantType != TenantType.Regulator || string.IsNullOrWhiteSpace(accessContext.RegulatorCode))
        return Results.BadRequest(new { error = "Missing regulator context." });

    var pdf = await workspaceService.GenerateReportPdf(
        tenantContext.CurrentTenantId.Value, accessContext.RegulatorCode, projectId, context.RequestAborted);
    var fileName = $"examination-report-{projectId}-{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";
    return Results.File(pdf, "application/pdf", fileName);
}).RequireAuthorization("RegulatorOnly");

app.MapGet("/regulator/workspace/{projectId:int}/intelligence-pack", async (
    int projectId,
    HttpContext context,
    ITenantContext tenantContext,
    ITenantAccessContextResolver tenantAccessContextResolver,
    IExaminationWorkspaceService workspaceService) =>
{
    if (!tenantContext.CurrentTenantId.HasValue)
        return Results.Unauthorized();

    var accessContext = await tenantAccessContextResolver.TryResolveAsync(
        tenantContext.CurrentTenantId.Value,
        context.User,
        context.RequestAborted);
    if (accessContext?.TenantType != TenantType.Regulator || string.IsNullOrWhiteSpace(accessContext.RegulatorCode))
        return Results.BadRequest(new { error = "Missing regulator context." });

    var pdf = await workspaceService.GenerateIntelligencePackPdf(
        tenantContext.CurrentTenantId.Value, accessContext.RegulatorCode, projectId, context.RequestAborted);
    var fileName = $"intelligence-pack-{projectId}-{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";
    return Results.File(pdf, "application/pdf", fileName);
}).RequireAuthorization("RegulatorOnly");

app.MapGet("/regulator/workspace/{projectId:int}/evidence/{evidenceId:int}", async (
    int projectId,
    int evidenceId,
    ITenantContext tenantContext,
    IExaminationWorkspaceService workspaceService,
    HttpContext context) =>
{
    if (!tenantContext.CurrentTenantId.HasValue)
        return Results.Unauthorized();

    var file = await workspaceService.DownloadEvidence(
        tenantContext.CurrentTenantId.Value, projectId, evidenceId, context.RequestAborted);
    return file is null
        ? Results.NotFound()
        : Results.File(file.Content, file.ContentType, file.FileName);
}).RequireAuthorization("RegulatorOnly");

app.MapGet("/regulator/stress-test/report/pdf", async (
    HttpContext context,
    ITenantContext tenantContext,
    ITenantAccessContextResolver tenantAccessContextResolver,
    IStressTestService stressTestService) =>
{
    if (!tenantContext.CurrentTenantId.HasValue)
        return Results.Unauthorized();

    var accessContext = await tenantAccessContextResolver.TryResolveAsync(
        tenantContext.CurrentTenantId.Value,
        context.User,
        context.RequestAborted);
    if (accessContext?.TenantType != TenantType.Regulator || string.IsNullOrWhiteSpace(accessContext.RegulatorCode))
        return Results.BadRequest(new { error = "Missing regulator context." });

    var scenarioParam = context.Request.Query["scenario"].FirstOrDefault() ?? "NgfsOrderly";
    if (!Enum.TryParse<StressScenarioType>(scenarioParam, out var scenarioType))
        scenarioType = StressScenarioType.NgfsOrderly;

    var report = await stressTestService.RunStressTestAsync(
        accessContext.RegulatorCode,
        new StressTestRequest { ScenarioType = scenarioType },
        context.RequestAborted);
    var pdf = await stressTestService.GenerateReportPdfAsync(accessContext.RegulatorCode, report, context.RequestAborted);
    var fileName = $"stress-test-{scenarioType}-{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";
    return Results.File(pdf, "application/pdf", fileName);
}).RequireAuthorization("RegulatorOnly");

app.MapGet("/regulator/complianceiq/conversations/{conversationId:guid}/export", async (
    Guid conversationId,
    HttpContext context,
    ITenantContext tenantContext,
    ITenantAccessContextResolver tenantAccessContextResolver,
    IComplianceIqService complianceIqService) =>
{
    if (!tenantContext.CurrentTenantId.HasValue)
    {
        return Results.Unauthorized();
    }

    var accessContext = await tenantAccessContextResolver.TryResolveAsync(
        tenantContext.CurrentTenantId.Value,
        context.User,
        context.RequestAborted);
    if (accessContext?.TenantType != TenantType.Regulator)
    {
        return Results.Forbid();
    }

    var pdf = await complianceIqService.ExportConversationPdfAsync(
        conversationId,
        accessContext.TenantId,
        context.RequestAborted);
    var fileName = $"complianceiq-regulator-conversation-{conversationId:D}.pdf";
    return Results.File(pdf, "application/pdf", fileName);
}).RequireAuthorization("RegulatorOnly");

app.MapGet("/regulator/regulatoriq/conversations/{conversationId:guid}/export", async (
    Guid conversationId,
    HttpContext context,
    ITenantContext tenantContext,
    ITenantAccessContextResolver tenantAccessContextResolver,
    IRegulatorIqService regulatorIqService) =>
{
    if (!tenantContext.CurrentTenantId.HasValue)
    {
        return Results.Unauthorized();
    }

    var accessContext = await tenantAccessContextResolver.TryResolveAsync(
        tenantContext.CurrentTenantId.Value,
        context.User,
        context.RequestAborted);
    if (accessContext?.TenantType != TenantType.Regulator)
    {
        return Results.Forbid();
    }

    var pdf = await regulatorIqService.ExportConversationPdfAsync(conversationId, context.RequestAborted);
    var fileName = $"regulatoriq-conversation-{conversationId:D}.pdf";
    return Results.File(pdf, "application/pdf", fileName);
}).RequireAuthorization("RegulatorOnly");

app.MapGet("/regulator/regulatoriq/entities/{targetTenantId:guid}/briefing/export", async (
    Guid targetTenantId,
    HttpContext context,
    ITenantContext tenantContext,
    ITenantAccessContextResolver tenantAccessContextResolver,
    IRegulatorIqService regulatorIqService) =>
{
    if (!tenantContext.CurrentTenantId.HasValue)
    {
        return Results.Unauthorized();
    }

    var accessContext = await tenantAccessContextResolver.TryResolveAsync(
        tenantContext.CurrentTenantId.Value,
        context.User,
        context.RequestAborted);
    if (accessContext?.TenantType != TenantType.Regulator || string.IsNullOrWhiteSpace(accessContext.RegulatorCode))
    {
        return Results.Forbid();
    }

    var pdf = await regulatorIqService.GenerateExaminationBriefingPdfAsync(targetTenantId, accessContext.RegulatorCode, context.RequestAborted);
    var fileName = $"regulatoriq-examination-briefing-{targetTenantId:D}.pdf";
    return Results.File(pdf, "application/pdf", fileName);
}).RequireAuthorization("RegulatorOnly");

static async Task AuditIntelligenceExportAsync(
    HttpContext context,
    IAuditLogger auditLogger,
    string action,
    string area,
    string format,
    FC.Engine.Admin.Services.IntelligenceExportFile file,
    string? lens,
    int? institutionId,
    CancellationToken ct)
{
    await auditLogger.Log(
        "PlatformIntelligence",
        0,
        action,
        null,
        new
        {
            Area = area,
            Format = format,
            file.FileName,
            Lens = lens,
            InstitutionId = institutionId,
            SizeBytes = file.Content.Length,
            ExportedAtUtc = DateTime.UtcNow
        },
        ResolveApiPerformedBy(context),
        ct);
}

static string ResolveApiPerformedBy(HttpContext context) =>
    context.User.FindFirstValue(ClaimTypes.NameIdentifier)
    ?? context.User.Identity?.Name
    ?? "platform-intelligence-api";

// Lightweight self-probe endpoint used by PlatformAdminService.CheckServiceHealthAsync
app.MapGet("/api/ping", () => Results.Ok(new { status = "ok", utc = DateTimeOffset.UtcNow }));

app.MapRazorComponents<FC.Engine.Admin.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program
{
}
