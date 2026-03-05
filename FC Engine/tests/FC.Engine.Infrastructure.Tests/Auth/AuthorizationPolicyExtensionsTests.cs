using FC.Engine.Domain.Security;
using FC.Engine.Infrastructure.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace FC.Engine.Infrastructure.Tests.Auth;

public class AuthorizationPolicyExtensionsTests
{
    [Fact]
    public void Permission_Check_On_API_Endpoint_Policy_Requires_SubmissionCreate()
    {
        var options = new AuthorizationOptions();
        options.AddRegosPermissionPolicies();

        var policy = options.GetPolicy("CanCreateSubmission");
        policy.Should().NotBeNull();

        var claimRequirement = policy!.Requirements.OfType<ClaimsAuthorizationRequirement>().Single();
        claimRequirement.ClaimType.Should().Be("perm");
        claimRequirement.AllowedValues.Should().Contain(PermissionCatalog.SubmissionCreate);
    }
}
