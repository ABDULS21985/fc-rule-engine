using System.Security.Claims;

namespace FC.Engine.Admin.Utilities;

public static class UserIdentityResolver
{
    private static readonly string[] ActorClaimPriority =
    [
        ClaimTypes.NameIdentifier,
        ClaimTypes.Email,
        ClaimTypes.Name,
        "preferred_username",
        "username",
        "sub"
    ];

    public static string ResolveActor(ClaimsPrincipal? principal)
    {
        if (TryResolveActor(principal, out var actor))
        {
            return actor;
        }

        throw new InvalidOperationException("Unable to resolve the current authenticated user identity.");
    }

    public static bool TryResolveActor(ClaimsPrincipal? principal, out string actor)
    {
        actor = string.Empty;

        if (principal?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        foreach (var claimType in ActorClaimPriority)
        {
            var value = principal.FindFirstValue(claimType);
            if (!string.IsNullOrWhiteSpace(value))
            {
                actor = value.Trim();
                return true;
            }
        }

        return false;
    }
}
