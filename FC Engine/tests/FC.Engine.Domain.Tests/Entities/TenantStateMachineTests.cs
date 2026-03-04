using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FC.Engine.Domain.Tests.Entities;

public class TenantStateMachineTests
{
    private static Tenant CreateTenant() =>
        Tenant.Create("Test Org", "test-org", TenantType.Institution, "test@example.com");

    // ── Factory ──

    [Fact]
    public void Create_ShouldInitWithPendingActivation()
    {
        var tenant = CreateTenant();

        tenant.TenantId.Should().NotBeEmpty();
        tenant.TenantName.Should().Be("Test Org");
        tenant.TenantSlug.Should().Be("test-org");
        tenant.TenantType.Should().Be(TenantType.Institution);
        tenant.Status.Should().Be(TenantStatus.PendingActivation);
        tenant.ContactEmail.Should().Be("test@example.com");
        tenant.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    // ── Valid Transitions ──

    [Fact]
    public void Activate_FromPending_ShouldSucceed()
    {
        var tenant = CreateTenant();

        tenant.Activate();

        tenant.Status.Should().Be(TenantStatus.Active);
    }

    [Fact]
    public void Suspend_FromActive_ShouldSucceed()
    {
        var tenant = CreateTenant();
        tenant.Activate();

        tenant.Suspend("Policy violation");

        tenant.Status.Should().Be(TenantStatus.Suspended);
    }

    [Fact]
    public void Reactivate_FromSuspended_ShouldSucceed()
    {
        var tenant = CreateTenant();
        tenant.Activate();
        tenant.Suspend("Maintenance");

        tenant.Reactivate();

        tenant.Status.Should().Be(TenantStatus.Active);
    }

    [Fact]
    public void Deactivate_FromActive_ShouldSucceed()
    {
        var tenant = CreateTenant();
        tenant.Activate();

        tenant.Deactivate();

        tenant.Status.Should().Be(TenantStatus.Deactivated);
        tenant.DeactivatedAt.Should().NotBeNull();
    }

    [Fact]
    public void Deactivate_FromSuspended_ShouldSucceed()
    {
        var tenant = CreateTenant();
        tenant.Activate();
        tenant.Suspend("Issue");

        tenant.Deactivate();

        tenant.Status.Should().Be(TenantStatus.Deactivated);
        tenant.DeactivatedAt.Should().NotBeNull();
    }

    [Fact]
    public void Archive_FromDeactivated_ShouldSucceed()
    {
        var tenant = CreateTenant();
        tenant.Activate();
        tenant.Deactivate();

        tenant.Archive();

        tenant.Status.Should().Be(TenantStatus.Archived);
    }

    // ── Full Lifecycle ──

    [Fact]
    public void FullLifecycle_PendingToArchived_ShouldSucceed()
    {
        var tenant = CreateTenant();

        tenant.Activate();
        tenant.Suspend("Review");
        tenant.Reactivate();
        tenant.Deactivate();
        tenant.Archive();

        tenant.Status.Should().Be(TenantStatus.Archived);
    }

    // ── Invalid Transitions ──

    [Fact]
    public void Activate_FromActive_ShouldThrow()
    {
        var tenant = CreateTenant();
        tenant.Activate();

        var act = () => tenant.Activate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Active*");
    }

    [Fact]
    public void Activate_FromSuspended_ShouldThrow()
    {
        var tenant = CreateTenant();
        tenant.Activate();
        tenant.Suspend("Test");

        var act = () => tenant.Activate();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Suspend_FromPending_ShouldThrow()
    {
        var tenant = CreateTenant();

        var act = () => tenant.Suspend("Test");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*PendingActivation*");
    }

    [Fact]
    public void Reactivate_FromActive_ShouldThrow()
    {
        var tenant = CreateTenant();
        tenant.Activate();

        var act = () => tenant.Reactivate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Active*");
    }

    [Fact]
    public void Reactivate_FromPending_ShouldThrow()
    {
        var tenant = CreateTenant();

        var act = () => tenant.Reactivate();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Deactivate_FromPending_ShouldThrow()
    {
        var tenant = CreateTenant();

        var act = () => tenant.Deactivate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*PendingActivation*");
    }

    [Fact]
    public void Deactivate_FromArchived_ShouldThrow()
    {
        var tenant = CreateTenant();
        tenant.Activate();
        tenant.Deactivate();
        tenant.Archive();

        var act = () => tenant.Deactivate();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Archive_FromActive_ShouldThrow()
    {
        var tenant = CreateTenant();
        tenant.Activate();

        var act = () => tenant.Archive();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Active*");
    }

    [Fact]
    public void Archive_FromPending_ShouldThrow()
    {
        var tenant = CreateTenant();

        var act = () => tenant.Archive();

        act.Should().Throw<InvalidOperationException>();
    }

    // ── UpdateDetails ──

    [Fact]
    public void UpdateDetails_ShouldUpdatePropertiesAndTimestamp()
    {
        var tenant = CreateTenant();
        var before = tenant.UpdatedAt;

        tenant.UpdateDetails("New Name", "new@example.com", "+234123", "Lagos");

        tenant.TenantName.Should().Be("New Name");
        tenant.ContactEmail.Should().Be("new@example.com");
        tenant.ContactPhone.Should().Be("+234123");
        tenant.Address.Should().Be("Lagos");
        tenant.UpdatedAt.Should().BeOnOrAfter(before);
    }

    // ── Defaults ──

    [Fact]
    public void Create_ShouldSetDefaultValues()
    {
        var tenant = CreateTenant();

        tenant.FiscalYearStartMonth.Should().Be(1);
        tenant.Timezone.Should().Be("Africa/Lagos");
        tenant.DefaultCurrency.Should().Be("NGN");
        tenant.MaxInstitutions.Should().Be(1);
        tenant.MaxUsersPerEntity.Should().Be(10);
        tenant.DeactivatedAt.Should().BeNull();
    }
}
