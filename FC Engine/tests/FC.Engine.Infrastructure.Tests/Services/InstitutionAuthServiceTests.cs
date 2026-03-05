using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FluentAssertions;
using Moq;
using System.Security.Claims;

namespace FC.Engine.Infrastructure.Tests.Services;

public class InstitutionAuthServiceTests
{
    [Fact]
    public void Existing_Portal_Cookie_Auth_Still_Works_After_JWT_Addition()
    {
        var userRepo = new Mock<IInstitutionUserRepository>();
        var institutionRepo = new Mock<IInstitutionRepository>();
        var tenantContext = new Mock<ITenantContext>();
        var entitlementService = new Mock<IEntitlementService>();
        var permissionService = new Mock<IPermissionService>();

        var sut = new InstitutionAuthService(
            userRepo.Object,
            institutionRepo.Object,
            tenantContext.Object,
            entitlementService.Object,
            permissionService.Object);

        var tenantId = Guid.NewGuid();
        var user = new InstitutionUser
        {
            Id = 9,
            TenantId = tenantId,
            InstitutionId = 101,
            Username = "portal.user",
            Email = "portal.user@test.local",
            DisplayName = "Portal User",
            Role = InstitutionRole.Maker,
            Institution = new Institution { Id = 101, InstitutionName = "Demo Institution", TenantId = tenantId }
        };

        var principal = sut.BuildClaimsPrincipal(user);

        principal.Identity.Should().NotBeNull();
        principal.Identity!.AuthenticationType.Should().Be("FC.Portal.Auth");
        principal.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be("9");
        principal.FindFirst("TenantId")!.Value.Should().Be(tenantId.ToString());
        principal.FindFirst("InstitutionId")!.Value.Should().Be("101");
    }
}
