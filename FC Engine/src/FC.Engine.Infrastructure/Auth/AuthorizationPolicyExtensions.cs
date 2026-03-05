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
        options.AddPolicy("CanManageBilling", policy => policy.RequireClaim("perm", PermissionCatalog.BillingManage));
        options.AddPolicy("CanReadAudit", policy => policy.RequireClaim("perm", PermissionCatalog.AuditRead));
        options.AddPolicy("CanManageUsers", policy => policy.RequireClaim("perm", PermissionCatalog.UserCreate, PermissionCatalog.UserEdit));
    }

    public static string ToPermissionPolicyName(string permissionCode) => $"perm:{permissionCode}";
}
