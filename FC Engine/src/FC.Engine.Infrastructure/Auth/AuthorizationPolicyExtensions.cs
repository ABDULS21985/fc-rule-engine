using FC.Engine.Domain.Security;
using Microsoft.AspNetCore.Authorization;

namespace FC.Engine.Infrastructure.Auth;

public static class AuthorizationPolicyExtensions
{
    public static void AddRegosPermissionPolicies(this AuthorizationOptions options)
    {
        foreach (var permission in PermissionCatalog.All)
        {
            options.AddPolicy(ToPermissionPolicyName(permission), policy => policy.RequireClaim("perm", permission));
        }

        options.AddPolicy("CanCreateSubmission", policy => policy.RequireClaim("perm", PermissionCatalog.SubmissionCreate));
        options.AddPolicy("CanApproveSubmission", policy => policy.RequireClaim("perm", PermissionCatalog.SubmissionApprove));
        // Plural aliases — referenced by PrivacyEndpoints
        options.AddPolicy("CanViewSubmissions", policy => policy.RequireClaim("perm", PermissionCatalog.SubmissionRead));
        options.AddPolicy("CanApproveSubmissions", policy => policy.RequireClaim("perm", PermissionCatalog.SubmissionApprove));
        options.AddPolicy("CanManageBilling", policy => policy.RequireClaim("perm", PermissionCatalog.BillingManage));
        options.AddPolicy("CanReadAudit", policy => policy.RequireClaim("perm", PermissionCatalog.AuditRead));
        options.AddPolicy("CanManageUsers", policy => policy.RequireClaim("perm", PermissionCatalog.UserCreate, PermissionCatalog.UserEdit));
        // Template management policies
        options.AddPolicy("CanEditTemplates", policy => policy.RequireClaim("perm", PermissionCatalog.TemplateEdit));
        options.AddPolicy("CanPublishTemplates", policy => policy.RequireClaim("perm", PermissionCatalog.TemplatePublish));
        options.AddPolicy("CanReadTemplates", policy => policy.RequireClaim("perm", PermissionCatalog.TemplateRead));
        // Direct regulatory submission (RG-34)
        options.AddPolicy("CanDirectSubmit", policy => policy.RequireClaim("perm", PermissionCatalog.SubmissionDirectSubmit));
        options.AddPolicy("CanViewDirectStatus", policy => policy.RequireClaim("perm", PermissionCatalog.SubmissionDirectStatus));
        // Compliance Health Score (RG-32)
        options.AddPolicy("CanViewComplianceHealth", policy => policy.RequireClaim("perm", PermissionCatalog.ComplianceHealthView));
        options.AddPolicy("CanAdminComplianceHealth", policy => policy.RequireClaim("perm", PermissionCatalog.ComplianceHealthAdmin));
        // Platform admin
        options.AddPolicy("PlatformAdmin", policy => policy.RequireClaim("perm", PermissionCatalog.AdminPlatform));
    }

    public static string ToPermissionPolicyName(string permissionCode) => $"perm:{permissionCode}";
}
