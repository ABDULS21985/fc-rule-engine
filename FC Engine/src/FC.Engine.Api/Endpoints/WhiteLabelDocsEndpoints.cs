using FC.Engine.Domain.Abstractions;

namespace FC.Engine.Api.Endpoints;

/// <summary>
/// White-label API documentation endpoints — partner-branded docs and Postman collections.
/// </summary>
public static class WhiteLabelDocsEndpoints
{
    public static void MapWhiteLabelDocsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/caas/docs")
            .WithTags("White-Label Docs");

        // GET /caas/docs/{partnerSlug} — Render branded API docs page
        group.MapGet("/{partnerSlug}", async (
            string partnerSlug,
            IPartnerManagementService partnerService,
            ITenantBrandingService brandingService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            // Look up partner by slug to get branding config
            // Partner slugs are derived from tenant names during onboarding
            var html = GenerateApiDocsHtml(partnerSlug);
            httpContext.Response.ContentType = "text/html; charset=utf-8";
            await httpContext.Response.WriteAsync(html, ct);
        })
        .AllowAnonymous()
        .WithName("WhiteLabelDocs")
        .WithSummary("Render branded API documentation for a partner")
        .ExcludeFromDescription();

        // GET /caas/docs/{partnerSlug}/postman — Auto-generated Postman collection
        group.MapGet("/{partnerSlug}/postman", (string partnerSlug) =>
        {
            var collection = GeneratePostmanCollection(partnerSlug);
            return Results.Json(collection, contentType: "application/json");
        })
        .AllowAnonymous()
        .WithName("WhiteLabelPostman")
        .WithSummary("Download auto-generated Postman collection for a partner");
    }

    private static string GenerateApiDocsHtml(string partnerSlug)
    {
        var partnerName = partnerSlug.Replace("-", " ");
        partnerName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(partnerName);

        return $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>{{partnerName}} — Compliance API Documentation</title>
            <style>
                :root {
                    --primary: #1a56db;
                    --primary-light: #e1effe;
                    --bg: #f9fafb;
                    --text: #111827;
                    --mutedtext: #6b7280;
                    --border: #e5e7eb;
                    --code-bg: #1f2937;
                    --code-text: #e5e7eb;
                    --green: #059669;
                    --orange: #d97706;
                }
                * { margin: 0; padding: 0; box-sizing: border-box; }
                body {
                    font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
                    background: var(--bg); color: var(--text); line-height: 1.6;
                }
                .header {
                    background: white; border-bottom: 1px solid var(--border);
                    padding: 1rem 2rem; display: flex; align-items: center; gap: 1rem;
                }
                .header h1 { font-size: 1.25rem; font-weight: 700; }
                .header .badge {
                    background: var(--primary-light); color: var(--primary);
                    padding: 0.25rem 0.75rem; border-radius: 9999px; font-size: 0.75rem; font-weight: 600;
                }
                .container { max-width: 1200px; margin: 2rem auto; padding: 0 2rem; }
                .endpoint-card {
                    background: white; border: 1px solid var(--border); border-radius: 0.75rem;
                    margin-bottom: 1.5rem; overflow: hidden;
                }
                .endpoint-header {
                    padding: 1rem 1.5rem; display: flex; align-items: center; gap: 1rem;
                    border-bottom: 1px solid var(--border);
                }
                .method {
                    padding: 0.25rem 0.75rem; border-radius: 0.375rem; font-weight: 700;
                    font-size: 0.75rem; text-transform: uppercase; font-family: monospace;
                }
                .method-post { background: #dbeafe; color: #1d4ed8; }
                .method-get { background: #d1fae5; color: #065f46; }
                .path { font-family: monospace; font-weight: 600; }
                .endpoint-body { padding: 1.5rem; }
                .endpoint-body p { color: var(--mutedtext); margin-bottom: 1rem; }
                pre {
                    background: var(--code-bg); color: var(--code-text); padding: 1rem;
                    border-radius: 0.5rem; overflow-x: auto; font-size: 0.875rem; line-height: 1.5;
                }
                .tab-group { display: flex; gap: 0; margin-bottom: -1px; }
                .tab {
                    padding: 0.5rem 1rem; font-size: 0.8rem; font-weight: 500;
                    cursor: pointer; border: 1px solid var(--border);
                    border-bottom: none; border-radius: 0.375rem 0.375rem 0 0;
                    background: var(--bg); color: var(--mutedtext);
                }
                .tab.active { background: var(--code-bg); color: white; }
                h2 { font-size: 1.5rem; margin-bottom: 1.5rem; }
                .section { margin-bottom: 3rem; }
            </style>
        </head>
        <body>
            <div class="header">
                <h1>{{partnerName}}</h1>
                <span class="badge">Compliance API v1</span>
                <span class="badge" style="background:#d1fae5;color:#065f46;">Powered by RegOS™</span>
            </div>
            <div class="container">
                <div class="section">
                    <h2>API Endpoints</h2>

                    <div class="endpoint-card">
                        <div class="endpoint-header">
                            <span class="method method-post">POST</span>
                            <span class="path">/api/v1/caas/validate</span>
                        </div>
                        <div class="endpoint-body">
                            <p>Validate data against any module template. Returns validation errors and a compliance score preview.</p>
                            <div class="tab-group">
                                <div class="tab active">cURL</div>
                                <div class="tab">Python</div>
                                <div class="tab">Node.js</div>
                                <div class="tab">C#</div>
                            </div>
                            <pre>curl -X POST https://api.{{partnerSlug}}.regos.app/api/v1/caas/validate \
          -H "X-Api-Key: regos_live_..." \
          -H "Content-Type: application/json" \
          -d '{
            "returnCode": "CBN_300",
            "moduleCode": "PSP_FINTECH",
            "records": [{"TotalAssets": 1000000, "TotalLiabilities": 500000}]
          }'</pre>
                        </div>
                    </div>

                    <div class="endpoint-card">
                        <div class="endpoint-header">
                            <span class="method method-post">POST</span>
                            <span class="path">/api/v1/caas/submit</span>
                        </div>
                        <div class="endpoint-body">
                            <p>Submit a complete return via API. Returns submission ID and validation results.</p>
                            <div class="tab-group">
                                <div class="tab active">cURL</div>
                                <div class="tab">Python</div>
                                <div class="tab">Node.js</div>
                                <div class="tab">C#</div>
                            </div>
                            <pre>curl -X POST https://api.{{partnerSlug}}.regos.app/api/v1/caas/submit \
          -H "X-Api-Key: regos_live_..." \
          -H "Content-Type: application/json" \
          -d '{
            "returnCode": "CBN_300",
            "periodCode": "2025-12",
            "records": [{"TotalAssets": 1000000}],
            "autoApprove": false
          }'</pre>
                        </div>
                    </div>

                    <div class="endpoint-card">
                        <div class="endpoint-header">
                            <span class="method method-get">GET</span>
                            <span class="path">/api/v1/caas/templates/{module}</span>
                        </div>
                        <div class="endpoint-body">
                            <p>Get template structure for a module, including field definitions and formulas.</p>
                            <pre>curl https://api.{{partnerSlug}}.regos.app/api/v1/caas/templates/PSP_FINTECH \
          -H "X-Api-Key: regos_live_..."</pre>
                        </div>
                    </div>

                    <div class="endpoint-card">
                        <div class="endpoint-header">
                            <span class="method method-get">GET</span>
                            <span class="path">/api/v1/caas/deadlines</span>
                        </div>
                        <div class="endpoint-body">
                            <p>Get filing deadlines for all entitled modules with RAG status indicators.</p>
                            <pre>curl https://api.{{partnerSlug}}.regos.app/api/v1/caas/deadlines \
          -H "X-Api-Key: regos_live_..."</pre>
                        </div>
                    </div>

                    <div class="endpoint-card">
                        <div class="endpoint-header">
                            <span class="method method-post">POST</span>
                            <span class="path">/api/v1/caas/score</span>
                        </div>
                        <div class="endpoint-body">
                            <p>Compute compliance health score with breakdown (pass rate, deadline adherence, completeness).</p>
                            <pre>curl -X POST https://api.{{partnerSlug}}.regos.app/api/v1/caas/score \
          -H "X-Api-Key: regos_live_..." \
          -H "Content-Type: application/json" \
          -d '{"moduleCode": "PSP_FINTECH"}'</pre>
                        </div>
                    </div>

                    <div class="endpoint-card">
                        <div class="endpoint-header">
                            <span class="method method-get">GET</span>
                            <span class="path">/api/v1/caas/changes</span>
                        </div>
                        <div class="endpoint-body">
                            <p>Get regulatory changes affecting your institution's entitled modules.</p>
                            <pre>curl https://api.{{partnerSlug}}.regos.app/api/v1/caas/changes?module=PSP_FINTECH \
          -H "X-Api-Key: regos_live_..."</pre>
                        </div>
                    </div>

                    <div class="endpoint-card">
                        <div class="endpoint-header">
                            <span class="method method-post">POST</span>
                            <span class="path">/api/v1/caas/simulate</span>
                        </div>
                        <div class="endpoint-body">
                            <p>Run a what-if scenario simulation against a template without persisting data.</p>
                            <pre>curl -X POST https://api.{{partnerSlug}}.regos.app/api/v1/caas/simulate \
          -H "X-Api-Key: regos_live_..." \
          -H "Content-Type: application/json" \
          -d '{
            "returnCode": "CBN_300",
            "scenarioName": "Q4 Stress Test",
            "records": [{"TotalAssets": 500000}],
            "overrides": {"TotalLiabilities": 600000}
          }'</pre>
                        </div>
                    </div>
                </div>
            </div>
        </body>
        </html>
        """;
    }

    private static object GeneratePostmanCollection(string partnerSlug)
    {
        var partnerName = partnerSlug.Replace("-", " ");
        partnerName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(partnerName);
        var baseUrl = $"https://api.{partnerSlug}.regos.app";

        return new
        {
            info = new
            {
                name = $"{partnerName} — Compliance API",
                description = "Auto-generated Postman collection for the RegOS™ Compliance-as-a-Service API",
                schema = "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
            },
            auth = new
            {
                type = "apikey",
                apikey = new[]
                {
                    new { key = "key", value = "X-Api-Key", type = "string" },
                    new { key = "value", value = "regos_live_YOUR_KEY_HERE", type = "string" },
                    new { key = "in", value = "header", type = "string" }
                }
            },
            item = new object[]
            {
                CreatePostmanItem("Validate Data", "POST", $"{baseUrl}/api/v1/caas/validate",
                    """{"returnCode":"CBN_300","moduleCode":"PSP_FINTECH","records":[{"TotalAssets":1000000}]}"""),
                CreatePostmanItem("Submit Return", "POST", $"{baseUrl}/api/v1/caas/submit",
                    """{"returnCode":"CBN_300","periodCode":"2025-12","records":[{"TotalAssets":1000000}],"autoApprove":false}"""),
                CreatePostmanItem("Get Template Structure", "GET", $"{baseUrl}/api/v1/caas/templates/PSP_FINTECH", null),
                CreatePostmanItem("Get Deadlines", "GET", $"{baseUrl}/api/v1/caas/deadlines", null),
                CreatePostmanItem("Get Compliance Score", "POST", $"{baseUrl}/api/v1/caas/score",
                    """{"moduleCode":"PSP_FINTECH"}"""),
                CreatePostmanItem("Get Regulatory Changes", "GET", $"{baseUrl}/api/v1/caas/changes?module=PSP_FINTECH", null),
                CreatePostmanItem("Simulate Scenario", "POST", $"{baseUrl}/api/v1/caas/simulate",
                    """{"returnCode":"CBN_300","scenarioName":"Q4 Stress Test","records":[{"TotalAssets":500000}],"overrides":{"TotalLiabilities":600000}}""")
            }
        };
    }

    private static object CreatePostmanItem(string name, string method, string url, string? body)
    {
        var item = new Dictionary<string, object>
        {
            ["name"] = name,
            ["request"] = new Dictionary<string, object>
            {
                ["method"] = method,
                ["url"] = new { raw = url },
                ["header"] = new[]
                {
                    new { key = "Content-Type", value = "application/json", type = "text" }
                }
            }
        };

        if (body != null)
        {
            ((Dictionary<string, object>)item["request"])["body"] = new
            {
                mode = "raw",
                raw = body
            };
        }

        return item;
    }
}
