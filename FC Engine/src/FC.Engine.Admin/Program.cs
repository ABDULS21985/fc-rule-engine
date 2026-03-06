using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure;
using FC.Engine.Infrastructure.Auth;
using FC.Engine.Infrastructure.Middleware;
using FC.Engine.Infrastructure.MultiTenancy;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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
builder.Services.AddScoped<FC.Engine.Admin.Services.HelpService>();

// Platform Admin services
builder.Services.AddScoped<FC.Engine.Admin.Services.TenantManagementService>();
builder.Services.AddScoped<FC.Engine.Admin.Services.PlatformAdminService>();
builder.Services.AddScoped<FC.Engine.Admin.Services.JurisdictionManagementService>();

// Authentication — cookie-based for Blazor Server
builder.Services.AddAuthentication(AdminAuthScheme)
    .AddCookie(AdminAuthScheme, options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/login";
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
    options.AddPolicy("Authenticated", policy => policy.RequireAuthenticatedUser());
    options.AddPolicy("PlatformAdmin", policy => policy.RequireRole("PlatformAdmin"));
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();

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

        context.Response.Redirect("/");
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
            Username = user.Username
        }, context.RequestAborted);

        await mfaService.SendMfaCodeSms(user.Id, "PortalUser", context.RequestAborted);

        context.Response.Redirect($"/login?mfa=required&challenge={Uri.EscapeDataString(pendingChallenge)}");
        return;
    }

    if (mfaRequired && !mfaEnabled)
    {
        var setupPrincipal = await authService.BuildClaimsPrincipalWithPermissions(user, context.RequestAborted);
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
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

    context.Response.Redirect("/");
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

    if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 8)
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

    context.Response.Redirect(string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl);
});

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

        var performedBy = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? context.User.Identity?.Name
                          ?? "platform-admin";

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
        var performedBy = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? context.User.Identity?.Name
                          ?? "platform-admin";

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

app.MapRazorComponents<FC.Engine.Admin.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
