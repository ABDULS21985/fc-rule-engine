using System.Security.Claims;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Infrastructure;
using FC.Engine.Infrastructure.Auth;
using FC.Engine.Infrastructure.Middleware;
using FC.Engine.Infrastructure.MultiTenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
const string PortalAuthScheme = "FC.Portal.Auth";

// Serilog
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// Infrastructure (shared layer — DB, repos, caching, validators)
builder.Services.AddInfrastructure(builder.Configuration);

// Application services (only the subset needed by the FI Portal)
builder.Services.AddScoped<IngestionOrchestrator>();
builder.Services.AddScoped<ValidationOrchestrator>();
builder.Services.AddScoped<TemplateService>();
builder.Services.AddScoped<InstitutionAuthService>();

// UI services
builder.Services.AddScoped<FC.Engine.Portal.Services.ToastService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.DialogService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.DashboardService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.SubmissionService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.CalendarService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.TemplateBrowserService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.FormDataToXmlService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.UserSettingsService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.ApprovalService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.InstitutionManagementService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.NotificationService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.CrossSheetDashboardService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.ExportService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.DryRunValidationService>();

// Caching
builder.Services.AddMemoryCache();

// Authentication — cookie-based for Blazor Server (separate cookie from Admin)
builder.Services.AddAuthentication(PortalAuthScheme)
    .AddCookie(PortalAuthScheme, options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/account/logout";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(4);
        options.SlidingExpiration = true;
        options.Cookie.Name = "FC.Portal.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
    });

// Authorization policies
builder.Services.AddAuthorization(options =>
{
    options.AddRegosPermissionPolicies();

    options.AddPolicy("InstitutionUser", policy =>
        policy.RequireAuthenticatedUser());

    options.AddPolicy("InstitutionAdmin", policy =>
        policy.RequireClaim(ClaimTypes.Role, "Admin"));

    options.AddPolicy("InstitutionMaker", policy =>
        policy.RequireClaim(ClaimTypes.Role, "Maker", "Admin"));

    options.AddPolicy("InstitutionChecker", policy =>
        policy.RequireClaim(ClaimTypes.Role, "Checker", "Admin"));
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddControllers();

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
app.UseAuthorization();
app.UseAntiforgery();

// Login POST endpoint — authenticates institution users
app.MapPost("/account/login", async (
    HttpContext context,
    InstitutionAuthService authService,
    IInstitutionUserRepository institutionUserRepository,
    IMfaService mfaService,
    IMfaChallengeStore mfaChallengeStore) =>
{
    var form = await context.Request.ReadFormAsync();
    var challengeId = form["challengeId"].ToString().Trim();
    var returnUrl = form["returnUrl"].ToString().Trim();

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

        var challengedUser = await institutionUserRepository.GetById(challenge.UserId, context.RequestAborted);
        if (challengedUser is null || !challengedUser.IsActive)
        {
            context.Response.Redirect("/login?error=invalid");
            return;
        }

        await mfaChallengeStore.RemoveChallenge(challengeId, context.RequestAborted);

        var challengeIp = context.Connection.RemoteIpAddress?.ToString();
        await authService.RecordLogin(challengedUser.Id, challengeIp, context.RequestAborted);

        var challengePrincipal = await authService.BuildClaimsPrincipalWithPermissions(challengedUser, context.RequestAborted);
        await context.SignInAsync(
            PortalAuthScheme,
            challengePrincipal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(4),
            });

        if (challenge.MustChangePassword)
        {
            context.Response.Redirect("/change-password");
            return;
        }

        var challengeRedirect = string.IsNullOrWhiteSpace(challenge.ReturnUrl) ? "/" : challenge.ReturnUrl;
        context.Response.Redirect(challengeRedirect);
        return;
    }

    var username = form["username"].ToString().Trim();
    var password = form["password"].ToString();

    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
    {
        context.Response.Redirect("/login?error=invalid");
        return;
    }

    var (user, errorCode) = await authService.ValidateLogin(username, password);

    if (user is null)
    {
        context.Response.Redirect($"/login?error={errorCode}");
        return;
    }

    var mfaEnabled = await mfaService.IsMfaEnabled(user.Id, "InstitutionUser");
    var mfaRequired = await mfaService.IsMfaRequired(user.TenantId, user.Role.ToString());
    if (mfaEnabled)
    {
        var pendingChallengeId = await mfaChallengeStore.CreateChallenge(new MfaLoginChallenge
        {
            UserId = user.Id,
            UserType = "InstitutionUser",
            Username = user.Username,
            ReturnUrl = returnUrl,
            MustChangePassword = user.MustChangePassword
        }, context.RequestAborted);

        context.Response.Redirect($"/login?mfa=required&challenge={Uri.EscapeDataString(pendingChallengeId)}");
        return;
    }

    if (mfaRequired && !mfaEnabled)
    {
        var setupPrincipal = await authService.BuildClaimsPrincipalWithPermissions(user, context.RequestAborted);
        await context.SignInAsync(
            PortalAuthScheme,
            setupPrincipal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(4),
            });

        context.Response.Redirect("/account/mfa-setup?enroll=required");
        return;
    }

    // Record login
    var ipAddress = context.Connection.RemoteIpAddress?.ToString();
    await authService.RecordLogin(user.Id, ipAddress, context.RequestAborted);

    var principal = await authService.BuildClaimsPrincipalWithPermissions(user, context.RequestAborted);

    await context.SignInAsync(
        PortalAuthScheme,
        principal,
        new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(4),
        });

    // Check if must change password
    if (user.MustChangePassword)
    {
        context.Response.Redirect("/change-password");
        return;
    }

    var redirect = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
    context.Response.Redirect(redirect);
});

app.MapGet("/account/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(PortalAuthScheme);
    context.Response.Redirect("/login");
});

app.MapControllers();

app.MapRazorComponents<FC.Engine.Portal.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
