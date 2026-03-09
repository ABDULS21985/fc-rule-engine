# Comprehensive Fix Prompt — FC Engine E2E Test Failures

You are a senior .NET / Blazor engineer. Your job is to fix **all 6 issues** discovered during end-to-end testing of the FC Engine platform. Each fix includes the exact file path, the current broken code, and the precise change required. Apply every fix, verify no regressions, and rebuild the Docker containers.

---

## Environment Context

| Service | URL | Tech |
|---------|-----|------|
| API | http://localhost:5100 | ASP.NET Minimal API |
| Admin Portal | http://localhost:5001 | Blazor Server (.NET 8) |
| Institution Portal | http://localhost:5300 | Blazor Server (.NET 8) |
| SQL Server | localhost:1433 | MSSQL 2022 |
| Docker Compose | `FC Engine/docker/docker-compose.yml` | |

---

## Fix 1 — CRITICAL: Enable Server-Side Prerendering on Institution Portal

### Problem
The Institution Portal explicitly disables Blazor prerendering (`prerender: false`), causing the browser to receive an empty `<body>` with only `<script>` tags. No HTML content appears until the WebSocket circuit connects. If the circuit is slow or fails, users see a blank white page. The Admin Portal does NOT have this problem because it uses the default `RenderMode.InteractiveServer` (prerender: true).

### File to Edit
`FC Engine/src/FC.Engine.Portal/Components/App.razor`

### Current Code (lines 17 and 20)
```razor
<HeadOutlet @rendermode="new InteractiveServerRenderMode(prerender: false)" />
```
```razor
<Routes @rendermode="new InteractiveServerRenderMode(prerender: false)" />
```

### Required Change
```razor
<HeadOutlet @rendermode="RenderMode.InteractiveServer" />
```
```razor
<Routes @rendermode="RenderMode.InteractiveServer" />
```

This matches the Admin Portal's `App.razor` at `FC Engine/src/FC.Engine.Admin/Components/App.razor` lines 17 and 20, which already uses `RenderMode.InteractiveServer` (prerender defaults to `true`).

### Verification
After the change, `curl http://localhost:5300/login` should return HTML with visible `<input>` and `<button>` elements in the response body, not just empty Blazor component markers and `<script>` tags.

### Side Effects to Handle
When prerendering is enabled, any component that injects scoped services or uses `OnInitializedAsync` will execute **twice** (once during prerender, once when the circuit connects). Audit the portal's pages for:
- Components that call APIs in `OnInitializedAsync` — these should use `OnAfterRenderAsync(firstRender)` or check `RendererInfo.IsInteractive` to avoid duplicate calls
- Components that set `JSRuntime`-dependent state in `OnInitializedAsync` — JS interop is not available during prerender; guard with `if (RendererInfo.IsInteractive)` or move to `OnAfterRenderAsync`
- Authentication state — ensure the `CascadingAuthenticationState` and cookie-based auth still work during prerender (the Admin Portal already handles this, so follow its pattern)

If any specific component breaks due to double-execution, add `@rendermode="new InteractiveServerRenderMode(prerender: false)"` to **that specific component only**, not to the global `App.razor`.

---

## Fix 2 — CRITICAL: Stabilize Institution Portal Container

### Problem
The portal Docker container crashes after ~9–15 Playwright page navigations. Docker marks it `unhealthy`. Root causes: (a) no resource limits or restart policy in docker-compose, (b) no Blazor circuit limits configured, (c) no circuit disconnect monitoring in the portal (the Admin has one).

### Part A: Docker Compose Resource Limits and Restart Policy

**File:** `FC Engine/docker/docker-compose.yml`

**Current portal service definition (lines ~86–104):**
```yaml
  portal:
    build:
      context: ..
      dockerfile: docker/Dockerfile.portal
    depends_on:
      migrator:
        condition: service_completed_successfully
    ports:
      - "5300:8080"
    environment:
      - ConnectionStrings__FcEngine=Server=sqlserver;...
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8080
    healthcheck:
      test: curl -f http://localhost:8080/login || exit 1
      interval: 10s
      timeout: 3s
      retries: 5
      start_period: 15s
```

**Add to the portal service:**
```yaml
  portal:
    restart: unless-stopped
    deploy:
      resources:
        limits:
          memory: 512M
          cpus: '1.0'
        reservations:
          memory: 256M
    # ... rest stays the same
```

Also add `restart: unless-stopped` to the `admin` and `api` services for consistency.

### Part B: Configure Blazor Circuit Options

**File:** `FC Engine/src/FC.Engine.Portal/Program.cs`

After the line (approximately line 122):
```csharp
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
```

Add circuit configuration:
```csharp
builder.Services.Configure<Microsoft.AspNetCore.Components.Server.CircuitOptions>(options =>
{
    options.DetailedErrors = builder.Environment.IsDevelopment();
    options.DisconnectedCircuitMaxRetained = 20;
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(2);
    options.MaxBufferedUnacknowledgedRenderBatches = 10;
});
```

This limits the number of zombie circuits retained in memory and reduces the retention window. Without this, every disconnected browser tab keeps its circuit alive indefinitely, consuming memory until the container OOMs.

### Part C: Add Circuit Disconnect Monitoring (Port from Admin)

The Admin Portal has circuit disconnect handling in:
- `FC Engine/src/FC.Engine.Admin/Components/Shared/SessionExpiredModal.razor` (line 151+)
- `FC Engine/src/FC.Engine.Admin/wwwroot/js/session.js` (lines 143–191)

Port equivalent circuit reconnection logic to the Portal:
1. Create `FC Engine/src/FC.Engine.Portal/Components/Shared/CircuitHandler.razor` (or add to existing layout) that monitors the Blazor circuit state
2. Add a reconnection UI overlay so users see "Reconnecting..." instead of a frozen page
3. Use the `Blazor.reconnect` API in the portal's JS to handle `components-reconnect-modal` gracefully

At minimum, add this to the Portal's `App.razor` or main layout:
```html
<div id="components-reconnect-modal" style="display:none">
    <div class="reconnect-overlay">
        <p>Connection lost. Reconnecting...</p>
    </div>
</div>
```

And in the portal's JS, add a reconnection handler:
```javascript
Blazor.addEventListener('enhancedload', () => {
    // Handle reconnection
});
```

### Part D: Add GC Tuning to Portal Dockerfile

**File:** `FC Engine/docker/Dockerfile.portal`

In the final stage `ENTRYPOINT` or via an `ENV` instruction, add:
```dockerfile
ENV DOTNET_GCConserveMemory=9
ENV DOTNET_GCHeapHardLimit=0x1C000000
```

This tells the .NET GC to be more aggressive about collecting memory (conserve level 9) and sets a hard heap limit of ~448MB, leaving room for native allocations within the 512MB container limit.

---

## Fix 3 — MEDIUM: API Middleware Skip List Alignment

### Problem
The `ApiKeyMiddleware` skip list is correct for the current routes, but it's missing the `/error` endpoint and lacks documentation about the required `/api/v1/` prefix.

### File to Edit
`FC Engine/src/FC.Engine.Api/Middleware/ApiKeyMiddleware.cs`

### Current Code (lines 30–36)
```csharp
var path = context.Request.Path.Value ?? "";
if (path.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWith("/health/", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWith("/metrics", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWith("/api/v1/auth/login", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWith("/api/v1/auth/refresh", StringComparison.OrdinalIgnoreCase))
```

### Required Change
```csharp
var path = context.Request.Path.Value ?? "";
if (path.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWith("/health/", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWith("/metrics", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
    path.Equals("/error", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWith("/api/v1/auth/login", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWith("/api/v1/auth/refresh", StringComparison.OrdinalIgnoreCase))
```

### Additional: Add OpenAPI/Swagger Documentation
In `FC Engine/src/FC.Engine.Api/Endpoints/AuthEndpoints.cs`, add XML summary comments to the login endpoint so Swagger documents that the base URL is `/api/v1/auth/login`:
```csharp
group.MapPost("/login", async (...) => { ... })
    .AllowAnonymous()
    .WithSummary("Authenticate and obtain JWT tokens")
    .WithDescription("POST /api/v1/auth/login — Accepts email and password, returns access and refresh tokens.");
```

---

## Fix 4 — HIGH: `api_access` Entitlement — `all_features` Wildcard Not Honored

### Problem
The `EntitlementService.HasFeatureAccess()` method checks whether the tenant's feature list contains the exact string `"api_access"`. The default tenant's subscription plan has `"all_features"` in its feature list, which is intended as a wildcard (the `SubscriptionPlan.HasFeature()` method correctly treats it as one). But `EntitlementService` does NOT check for the wildcard, so `api_access` returns `false` even though the plan has `all_features`.

The API login endpoint at `AuthEndpoints.cs:32` calls `entitlementService.HasFeatureAccess(user.TenantId, "api_access", ct)` which returns `false`, causing a **403 Forbidden** even though credentials are valid.

### File to Edit
`FC Engine/src/FC.Engine.Infrastructure/Services/EntitlementService.cs`

### Current Code (lines 145–149)
```csharp
public async Task<bool> HasFeatureAccess(Guid tenantId, string featureCode, CancellationToken ct = default)
{
    var entitlement = await ResolveEntitlements(tenantId, ct);
    return entitlement.Features.Contains(featureCode, StringComparer.OrdinalIgnoreCase);
}
```

### Required Change
```csharp
public async Task<bool> HasFeatureAccess(Guid tenantId, string featureCode, CancellationToken ct = default)
{
    var entitlement = await ResolveEntitlements(tenantId, ct);
    return entitlement.Features.Contains(featureCode, StringComparer.OrdinalIgnoreCase)
        || entitlement.Features.Contains("all_features", StringComparer.OrdinalIgnoreCase);
}
```

### Also Audit Other Callers
Search the codebase for all calls to `HasFeatureAccess` and `entitlement.Features.Contains` to ensure they all benefit from this fix. The `HasFeatureAccess` method is the central point, so fixing it here should cover all callers. But verify there are no other places that directly check `entitlement.Features` without going through `HasFeatureAccess`.

### Verification
After fixing, `POST /api/v1/auth/login` with `{"email":"admin@fc001.com","password":"Admin@123"}` should return **200 OK** with `accessToken` and `refreshToken`, not 403.

---

## Fix 5 — HIGH: Regulatory Calendar `InvalidOperationException`

### Problem
The `/platform/regulatory-calendar` page throws a JS interop `InvalidOperationException` when the "Download Template" button is clicked (and the exception leaks into the pre-rendered HTML). The code calls `JS.InvokeVoidAsync("downloadFile", ...)` but no global `downloadFile` function exists. The only download function is `window.dataTable.downloadFile` in `data-table.js`, and it expects base64-encoded content, not raw text.

### File to Edit
`FC Engine/src/FC.Engine.Admin/Components/Pages/Platform/RegulatoryCalendarImport.razor`

### Current Code (lines 289–293)
```csharp
private async Task DownloadTemplate()
{
    var csv = "ReturnCode,Period,Deadline\nBSL001,2025-03,2025-04-07\nBSL002,2025-03,2025-04-15\n";
    await JS.InvokeVoidAsync("downloadFile", "regulatory-calendar-template.csv", csv, "text/csv");
}
```

### Required Change
```csharp
private async Task DownloadTemplate()
{
    var csv = "ReturnCode,Period,Deadline\nBSL001,2025-03,2025-04-07\nBSL002,2025-03,2025-04-15\n";
    var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(csv));
    await JS.InvokeVoidAsync("dataTable.downloadFile", "regulatory-calendar-template.csv", "text/csv", base64);
}
```

Changes:
1. Function name: `"downloadFile"` → `"dataTable.downloadFile"` (match the actual namespace in `data-table.js`)
2. Argument order: `(fileName, content, contentType)` → `(fileName, contentType, base64Content)` (match the actual function signature)
3. Encoding: raw CSV string → Base64-encoded string (match what `dataTable.downloadFile` expects)

### Also Check
Look at the `dataTable.downloadFile` implementation in `FC Engine/src/FC.Engine.Admin/wwwroot/js/data-table.js` line 167 to confirm the exact function signature. It should be:
```javascript
downloadFile: function(fileName, contentType, base64Content) { ... }
```

If the signature differs, adjust the Razor call accordingly.

---

## Fix 6 — MEDIUM: Self-Host Google Fonts for Reliability

### Problem
Both the Admin and Portal load Google Fonts (Inter and Plus Jakarta Sans) from `fonts.googleapis.com`. In Docker containers without internet access, or during E2E tests in network-restricted environments, this causes rendering timeouts and screenshot failures.

### Files to Edit
- `FC Engine/src/FC.Engine.Admin/Components/App.razor` (lines 13–16)
- `FC Engine/src/FC.Engine.Portal/Components/App.razor` (lines 13–16)

### Steps

**Step 1:** Download the font files. Create directories:
```
FC Engine/src/FC.Engine.Admin/wwwroot/fonts/
FC Engine/src/FC.Engine.Portal/wwwroot/fonts/
```

Download Inter (wght 400, 500, 600, 700) and Plus Jakarta Sans (wght 400, 500, 600, 700) as `.woff2` files from Google Fonts.

**Step 2:** Create a local font CSS file at `wwwroot/css/fonts.css` in both projects:
```css
/* Inter */
@font-face {
    font-family: 'Inter';
    font-style: normal;
    font-weight: 400;
    font-display: swap;
    src: url('../fonts/inter-400.woff2') format('woff2');
}
@font-face {
    font-family: 'Inter';
    font-style: normal;
    font-weight: 500;
    font-display: swap;
    src: url('../fonts/inter-500.woff2') format('woff2');
}
@font-face {
    font-family: 'Inter';
    font-style: normal;
    font-weight: 600;
    font-display: swap;
    src: url('../fonts/inter-600.woff2') format('woff2');
}
@font-face {
    font-family: 'Inter';
    font-style: normal;
    font-weight: 700;
    font-display: swap;
    src: url('../fonts/inter-700.woff2') format('woff2');
}

/* Plus Jakarta Sans */
@font-face {
    font-family: 'Plus Jakarta Sans';
    font-style: normal;
    font-weight: 400;
    font-display: swap;
    src: url('../fonts/plus-jakarta-sans-400.woff2') format('woff2');
}
@font-face {
    font-family: 'Plus Jakarta Sans';
    font-style: normal;
    font-weight: 500;
    font-display: swap;
    src: url('../fonts/plus-jakarta-sans-500.woff2') format('woff2');
}
@font-face {
    font-family: 'Plus Jakarta Sans';
    font-style: normal;
    font-weight: 600;
    font-display: swap;
    src: url('../fonts/plus-jakarta-sans-600.woff2') format('woff2');
}
@font-face {
    font-family: 'Plus Jakarta Sans';
    font-style: normal;
    font-weight: 700;
    font-display: swap;
    src: url('../fonts/plus-jakarta-sans-700.woff2') format('woff2');
}
```

**Step 3:** In both `App.razor` files, replace the Google Fonts block:

**Current (lines 13–16):**
```html
<link rel="preconnect" href="https://fonts.googleapis.com" />
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&family=Plus+Jakarta+Sans:wght@400;500;600;700&display=swap" media="print" onload="this.media='all'" />
<noscript><link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&family=Plus+Jakarta+Sans:wght@400;500;600;700&display=swap" /></noscript>
```

**Replace with:**
```html
<link rel="stylesheet" href="css/fonts.css" />
```

### Alternative (Quick Fix)
If self-hosting is too much work right now, at minimum add a CSS fallback so the page renders immediately with system fonts while waiting for Google Fonts:
```css
body {
    font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
}
```
And in both `App.razor`, change the font loading to use `rel="preload"`:
```html
<link rel="preload" href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&family=Plus+Jakarta+Sans:wght@400;500;600;700&display=swap" as="style" onload="this.rel='stylesheet'" />
```

---

## Post-Fix Verification Checklist

After applying all 6 fixes:

### 1. Rebuild Docker Containers
```bash
cd "FC Engine/docker"
docker compose build portal admin api
docker compose up -d
```

### 2. Verify Fix 1 (Portal Prerendering)
```bash
curl -s http://localhost:5300/login | grep -c '<input'
# Should return 2+ (username and password inputs in the pre-rendered HTML)
```

### 3. Verify Fix 2 (Portal Stability)
```bash
# Run the full Phase 3 E2E tests — all 43 tests should pass without container crash
cd e2e-tests
npx playwright test --project=Phase3-PortalPages
```

### 4. Verify Fix 3 (Middleware Skip)
```bash
curl -s http://localhost:5100/error
# Should not return {"error":"Invalid or missing authentication credentials"}
```

### 5. Verify Fix 4 (API Access Entitlement)
```bash
curl -s http://localhost:5100/api/v1/auth/login \
  -X POST -H "Content-Type: application/json" \
  -d '{"email":"admin@fc001.com","password":"Admin@123"}'
# Should return 200 with {"accessToken":"...","refreshToken":"...","expiresIn":...,"tokenType":"Bearer"}
```

Then use the token:
```bash
TOKEN=$(curl -s http://localhost:5100/api/v1/auth/login \
  -X POST -H "Content-Type: application/json" \
  -d '{"email":"admin@fc001.com","password":"Admin@123"}' | jq -r .accessToken)

curl -s http://localhost:5100/templates/ \
  -H "Authorization: Bearer $TOKEN" | head -200
# Should return 200 with template list JSON
```

### 6. Verify Fix 5 (Regulatory Calendar)
- Navigate to `http://localhost:5001/platform/regulatory-calendar` in a browser
- The page should render without any `InvalidOperationException` in the HTML
- Click "Download Template" — a CSV file should download

### 7. Verify Fix 6 (Fonts)
```bash
curl -s http://localhost:5001/css/fonts.css | head -5
# Should return @font-face declarations

curl -s http://localhost:5300/css/fonts.css | head -5
# Same
```

### 8. Full E2E Re-run
```bash
cd e2e-tests
npx playwright test
# Target: 128/128 tests passing, 0 failed
```

---

## File Summary

| # | File | Change |
|---|------|--------|
| 1 | `FC Engine/src/FC.Engine.Portal/Components/App.razor` | `prerender: false` → `RenderMode.InteractiveServer` |
| 2a | `FC Engine/docker/docker-compose.yml` | Add `restart: unless-stopped` + resource limits to portal |
| 2b | `FC Engine/src/FC.Engine.Portal/Program.cs` | Add `CircuitOptions` configuration |
| 2c | `FC Engine/docker/Dockerfile.portal` | Add GC tuning env vars |
| 3 | `FC Engine/src/FC.Engine.Api/Middleware/ApiKeyMiddleware.cs` | Add `/error` to skip list |
| 4 | `FC Engine/src/FC.Engine.Infrastructure/Services/EntitlementService.cs` | Add `all_features` wildcard to `HasFeatureAccess()` |
| 5 | `FC Engine/src/FC.Engine.Admin/Components/Pages/Platform/RegulatoryCalendarImport.razor` | Fix JS interop: function name, arg order, base64 encoding |
| 6a | `FC Engine/src/FC.Engine.Admin/Components/App.razor` | Replace Google Fonts CDN with local `css/fonts.css` |
| 6b | `FC Engine/src/FC.Engine.Portal/Components/App.razor` | Same as 6a |
| 6c | `FC Engine/src/FC.Engine.Admin/wwwroot/css/fonts.css` | New file — `@font-face` declarations |
| 6d | `FC Engine/src/FC.Engine.Portal/wwwroot/css/fonts.css` | New file — same as 6c |
| 6e | `FC Engine/src/FC.Engine.Admin/wwwroot/fonts/*.woff2` | New files — font binaries |
| 6f | `FC Engine/src/FC.Engine.Portal/wwwroot/fonts/*.woff2` | New files — font binaries |
