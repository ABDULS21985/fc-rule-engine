using System.Security.Claims;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure;
using FC.Engine.Infrastructure.Auth;
using FC.Engine.Infrastructure.DragDrop;
using FC.Engine.Infrastructure.Hubs;
using FC.Engine.Infrastructure.Middleware;
using FC.Engine.Infrastructure.MultiTenancy;
using FC.Engine.Infrastructure.Notifications;
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

// Infrastructure interop services
builder.Services.AddScoped<DragDropInterop>();
builder.Services.AddScoped<FC.Engine.Infrastructure.Charts.ChartJsInterop>();

// UI services
builder.Services.AddScoped<FC.Engine.Portal.Services.ToastService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.DialogService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.DashboardService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.SubmissionService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.SubmissionBatchPortalService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.CalendarService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.TemplateBrowserService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.FormDataToXmlService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.UserSettingsService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.ApprovalService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.WorkflowService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.InstitutionManagementService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.NotificationService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.DeadlineExtensionRequestService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.ModuleWorkspaceService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.PortalSubmissionLaunchService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.CrossSheetDashboardService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.ExportService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.DryRunValidationService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.ValidationHubService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.ReportBuilderService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.PartnerPortalService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.PartnerViewAsService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.OnboardingWizardService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.SandboxService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.KnowledgeBaseService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.OverdueAlertService>();
// IFormDataService is already registered in AddInfrastructure() — do not duplicate
builder.Services.AddScoped<FC.Engine.Portal.Services.TourService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.TourStateService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.PortalShortcutService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.OnboardingStateService>();
builder.Services.AddScoped<FC.Engine.Portal.Services.SubscriptionOverdueStateService>();
builder.Services.AddSingleton<FC.Engine.Portal.Services.IPresenceService, FC.Engine.Portal.Services.InMemoryPresenceService>();
builder.Services.AddSingleton<FC.Engine.Portal.Services.IAuditCommentService, FC.Engine.Portal.Services.InMemoryAuditCommentService>();

// HttpClient for cross-project API calls (e.g. BatchSubmissions → FC.Engine.Api)
var apiBaseUrl = builder.Configuration["EngineApi:BaseUrl"] ?? "http://localhost:5002";
builder.Services.AddHttpClient(string.Empty, client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
});

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
var signalRBuilder = builder.Services.AddSignalR();
var signalRSettings = builder.Configuration.GetSection(NotificationSettings.SectionName).Get<NotificationSettings>()?.SignalR;
if (signalRSettings?.RedisBackplane == true)
{
    var redisConnection = builder.Configuration.GetConnectionString("Redis");
    if (!string.IsNullOrWhiteSpace(redisConnection))
    {
        signalRBuilder.AddStackExchangeRedis(redisConnection);
    }
}

// Blazor Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<Microsoft.AspNetCore.Components.Server.CircuitOptions>(options =>
{
    options.DetailedErrors = builder.Environment.IsDevelopment();
    options.DisconnectedCircuitMaxRetained = 20;
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(2);
    options.MaxBufferedUnacknowledgedRenderBatches = 10;
});

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

// Login POST endpoint — authenticates institution users
app.MapPost("/account/login", async (
    HttpContext context,
    InstitutionAuthService authService,
    IInstitutionUserRepository institutionUserRepository,
    IMfaService mfaService,
    IMfaChallengeStore mfaChallengeStore) =>
{
    // Validate returnUrl to prevent open redirect attacks
    static string SafeRedirect(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "/";
        if (url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\"))
            return url;
        return "/";
    }

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

        var challengeRedirect = SafeRedirect(challenge.ReturnUrl);
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

        await mfaService.SendMfaCodeSms(user.Id, "InstitutionUser", context.RequestAborted);

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

    context.Response.Redirect(SafeRedirect(returnUrl));
});

app.MapGet("/account/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(PortalAuthScheme);
    context.Response.Redirect("/login");
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

// Open redirect prevention for reconsent
static string SafeRedirectReconsent(string? url)
{
    if (string.IsNullOrWhiteSpace(url))
        return "/";
    if (url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\"))
        return url;
    return "/";
}

app.MapGet("/exports/{exportRequestId:int}/download", async (
    int exportRequestId,
    HttpContext context,
    IExportEngine exportEngine,
    IExportRequestRepository exportRequestRepository,
    CancellationToken ct) =>
{
    if (context.User?.Identity?.IsAuthenticated != true)
    {
        return Results.Unauthorized();
    }

    var tenantClaim = context.User.FindFirst("TenantId")?.Value;
    if (!Guid.TryParse(tenantClaim, out var tenantId))
    {
        return Results.Forbid();
    }

    var request = await exportRequestRepository.GetById(exportRequestId, ct);
    if (request is null || request.TenantId != tenantId)
    {
        return Results.NotFound();
    }

    try
    {
        var stream = await exportEngine.DownloadExport(exportRequestId, tenantId, ct);
        var fileName = $"submission-{request.SubmissionId}-export-{request.Id}.{GetExtension(request.Format)}";
        return Results.File(stream, GetContentType(request.Format), fileName);
    }
    catch (FileNotFoundException)
    {
        return Results.NotFound();
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Forbid();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapGet("/complianceiq/conversations/{conversationId:guid}/export", async (
    Guid conversationId,
    HttpContext context,
    IComplianceIqService complianceIqService,
    ITenantContext tenantContext,
    CancellationToken ct) =>
{
    if (context.User?.Identity?.IsAuthenticated != true)
    {
        return Results.Unauthorized();
    }

    var tenantId = tenantContext.CurrentTenantId;
    if (!tenantId.HasValue)
    {
        var tenantClaim = context.User.FindFirst("TenantId")?.Value;
        if (!Guid.TryParse(tenantClaim, out var claimTenantId))
        {
            return Results.Forbid();
        }

        tenantId = claimTenantId;
    }

    var pdf = await complianceIqService.ExportConversationPdfAsync(conversationId, tenantId.Value, ct);
    var fileName = $"complianceiq-conversation-{conversationId:D}.pdf";
    return Results.File(pdf, "application/pdf", fileName);
}).RequireAuthorization("InstitutionUser");

app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");
app.MapHub<ReturnLockHub>("/hubs/returnlock");

app.MapRazorComponents<FC.Engine.Portal.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();

static string GetContentType(ExportFormat format) => format switch
{
    ExportFormat.Excel => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    ExportFormat.PDF => "application/pdf",
    ExportFormat.XML => "application/xml",
    ExportFormat.XBRL => "application/xbrl+xml",
    _ => "application/octet-stream"
};

static string GetExtension(ExportFormat format) => format switch
{
    ExportFormat.Excel => "xlsx",
    ExportFormat.PDF => "pdf",
    ExportFormat.XML => "xml",
    ExportFormat.XBRL => "xbrl",
    _ => "bin"
};
