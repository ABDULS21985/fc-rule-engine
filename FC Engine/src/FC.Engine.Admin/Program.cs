using System.Security.Claims;
using FC.Engine.Application.Services;
using FC.Engine.Infrastructure;
using FC.Engine.Infrastructure.MultiTenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

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

// UI notification & dialog services
builder.Services.AddScoped<FC.Engine.Admin.Services.ToastService>();
builder.Services.AddScoped<FC.Engine.Admin.Services.DialogService>();

// Platform Admin services
builder.Services.AddScoped<FC.Engine.Admin.Services.TenantManagementService>();

// Authentication — cookie-based for Blazor Server
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
    });

// Authorization policies
builder.Services.AddAuthorization(options =>
{
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

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseTenantContext();
app.UseAntiforgery();

// Login endpoint — handles cookie auth outside of Blazor's interactive (SignalR) pipeline
app.MapPost("/account/login", async (HttpContext context, AuthService authService) =>
{
    var form = await context.Request.ReadFormAsync();
    var username = form["username"].ToString();
    var password = form["password"].ToString();
    var rememberMe = form["rememberMe"].ToString() == "true";
    var ipAddress = context.Connection.RemoteIpAddress?.ToString();

    var (user, errorCode) = await authService.ValidateLogin(username, password, ipAddress);
    if (user is null)
    {
        context.Response.Redirect($"/login?error={errorCode ?? "invalid"}");
        return;
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new(ClaimTypes.Name, user.Username),
        new("DisplayName", user.DisplayName),
        new(ClaimTypes.Email, user.Email),
        new(ClaimTypes.Role, user.Role.ToString())
    };

    // Add TenantId claim for multi-tenancy
    if (user.TenantId.HasValue)
    {
        claims.Add(new Claim("TenantId", user.TenantId.Value.ToString()));
    }
    else
    {
        // PortalUser with no TenantId is a PlatformAdmin
        claims.Add(new Claim("IsPlatformAdmin", "true"));
        claims.Add(new Claim(ClaimTypes.Role, "PlatformAdmin"));
    }

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);

    await context.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
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
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
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

// PlatformAdmin impersonation endpoint
app.MapGet("/platform/impersonate", (HttpContext context) =>
{
    // Only PlatformAdmin can impersonate
    if (!context.User.IsInRole("PlatformAdmin") && !context.User.HasClaim("IsPlatformAdmin", "true"))
    {
        context.Response.StatusCode = 403;
        return;
    }

    var tenantIdStr = context.Request.Query["tenantId"].ToString();
    if (Guid.TryParse(tenantIdStr, out var tenantId))
    {
        context.Response.Cookies.Append("ImpersonateTenantId", tenantId.ToString(), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddHours(2)
        });
    }
    context.Response.Redirect("/");
});

// Stop impersonation
app.MapGet("/platform/stop-impersonation", (HttpContext context) =>
{
    context.Response.Cookies.Delete("ImpersonateTenantId");
    context.Response.Redirect("/platform/tenants");
});

app.MapRazorComponents<FC.Engine.Admin.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
