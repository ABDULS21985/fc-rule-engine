using FC.Engine.Application.Services;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FC.Engine.Infrastructure.Tests.Services;

public class FilingCalendarServiceTests
{
    // ── RAG color computation ──────────────────────────────────

    [Fact]
    public void RAG_Green_When_Submitted()
    {
        var today = new DateTime(2026, 3, 15);
        var deadline = new DateTime(2026, 3, 31);
        var periodStart = new DateTime(2026, 2, 28);

        var color = FilingCalendarService.ComputeRagColor(
            today, deadline, periodStart,
            hasSubmitted: true, inReview: false, status: "Completed");

        color.Should().Be(RagColor.Green);
    }

    [Fact]
    public void RAG_Green_When_Over_50_Percent_Time_Remaining()
    {
        // Period start: Jan 31, Deadline: Mar 2 (30 days)
        // Today: Feb 10 (20 days remaining = 67% remaining)
        var periodStart = new DateTime(2026, 1, 31);
        var deadline = new DateTime(2026, 3, 2);
        var today = new DateTime(2026, 2, 10);

        var color = FilingCalendarService.ComputeRagColor(
            today, deadline, periodStart,
            hasSubmitted: false, inReview: false, status: "Open");

        color.Should().Be(RagColor.Green);
    }

    [Fact]
    public void RAG_Amber_When_Under_50_Percent_Time_Remaining()
    {
        // Period start: Jan 31, Deadline: Mar 2 (30 days)
        // Today: Feb 20 (10 days remaining = 33% remaining)
        var periodStart = new DateTime(2026, 1, 31);
        var deadline = new DateTime(2026, 3, 2);
        var today = new DateTime(2026, 2, 20);

        var color = FilingCalendarService.ComputeRagColor(
            today, deadline, periodStart,
            hasSubmitted: false, inReview: false, status: "Open");

        color.Should().Be(RagColor.Amber);
    }

    [Fact]
    public void RAG_Amber_When_In_Review()
    {
        var periodStart = new DateTime(2026, 1, 31);
        var deadline = new DateTime(2026, 3, 2);
        var today = new DateTime(2026, 2, 5);

        var color = FilingCalendarService.ComputeRagColor(
            today, deadline, periodStart,
            hasSubmitted: false, inReview: true, status: "Open");

        color.Should().Be(RagColor.Amber);
    }

    [Fact]
    public void RAG_Red_When_Overdue()
    {
        var periodStart = new DateTime(2026, 1, 31);
        var deadline = new DateTime(2026, 3, 2);
        var today = new DateTime(2026, 3, 5);

        var color = FilingCalendarService.ComputeRagColor(
            today, deadline, periodStart,
            hasSubmitted: false, inReview: false, status: "Overdue");

        color.Should().Be(RagColor.Red);
    }

    [Fact]
    public void RAG_Red_When_Less_Than_7_Days_And_Not_Submitted()
    {
        var periodStart = new DateTime(2026, 1, 31);
        var deadline = new DateTime(2026, 3, 2);
        var today = new DateTime(2026, 2, 27); // 3 days remaining

        var color = FilingCalendarService.ComputeRagColor(
            today, deadline, periodStart,
            hasSubmitted: false, inReview: false, status: "DueSoon");

        color.Should().Be(RagColor.Red);
    }

    [Fact]
    public void RAG_Green_Even_When_Overdue_If_Submitted()
    {
        // Submitted returns always green regardless of deadline
        var periodStart = new DateTime(2026, 1, 31);
        var deadline = new DateTime(2026, 3, 2);
        var today = new DateTime(2026, 3, 10);

        var color = FilingCalendarService.ComputeRagColor(
            today, deadline, periodStart,
            hasSubmitted: true, inReview: false, status: "Overdue");

        color.Should().Be(RagColor.Green);
    }

    // ── Period formatting ──────────────────────────────────────

    [Fact]
    public void FormatPeriod_Monthly_Returns_Month_Year()
    {
        var period = new Domain.Entities.ReturnPeriod { Year = 2026, Month = 3, Frequency = "Monthly" };
        var label = FilingCalendarService.FormatPeriod(period);
        label.Should().Be("Mar 2026");
    }

    [Fact]
    public void FormatPeriod_Quarterly_Returns_Q_Year()
    {
        var period = new Domain.Entities.ReturnPeriod { Year = 2026, Month = 6, Quarter = 2, Frequency = "Quarterly" };
        var label = FilingCalendarService.FormatPeriod(period);
        label.Should().Be("Q2 2026");
    }

    [Fact]
    public void FormatPeriod_SemiAnnual_Returns_H_Year()
    {
        var period = new Domain.Entities.ReturnPeriod { Year = 2026, Month = 6, Frequency = "SemiAnnual" };
        var label = FilingCalendarService.FormatPeriod(period);
        label.Should().Be("H1 2026");
    }

    [Fact]
    public void FormatPeriod_Annual_Returns_FY_Year()
    {
        var period = new Domain.Entities.ReturnPeriod { Year = 2026, Month = 12, Frequency = "Annual" };
        var label = FilingCalendarService.FormatPeriod(period);
        label.Should().Be("FY 2026");
    }

    // ── SLA tracking (spec-required tests) ─────────────────────

    [Fact]
    public async Task SLA_Record_Created_On_Submission()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(nameof(SLA_Record_Created_On_Submission));

        var module = new Module { Id = 1, ModuleCode = "TEST", ModuleName = "Test", DefaultFrequency = "Monthly" };
        db.Modules.Add(module);

        var period = new ReturnPeriod
        {
            TenantId = tenantId,
            ModuleId = module.Id,
            Year = 2026,
            Month = 3,
            Frequency = "Monthly",
            ReportingDate = new DateTime(2026, 3, 31),
            DeadlineDate = new DateTime(2026, 4, 30),
            IsOpen = true,
            CreatedAt = DateTime.UtcNow
        };
        db.ReturnPeriods.Add(period);
        await db.SaveChangesAsync();

        var submission = new Submission
        {
            TenantId = tenantId,
            InstitutionId = 1,
            ReturnPeriodId = period.Id,
            ReturnCode = "TEST-001",
            Status = Domain.Enums.SubmissionStatus.Accepted,
            SubmittedAt = new DateTime(2026, 4, 25),
            CreatedAt = DateTime.UtcNow
        };
        db.Submissions.Add(submission);
        await db.SaveChangesAsync();

        var sut = new FilingCalendarService(db, new DeadlineComputationService(), NullLogger<FilingCalendarService>.Instance);
        await sut.RecordSla(period.Id, submission.Id);

        var sla = await db.FilingSlaRecords.SingleAsync();
        sla.TenantId.Should().Be(tenantId);
        sla.ModuleId.Should().Be(module.Id);
        sla.PeriodId.Should().Be(period.Id);
        sla.SubmissionId.Should().Be(submission.Id);
        sla.DaysToDeadline.Should().Be(5);
        sla.OnTime.Should().BeTrue();
    }

    [Fact]
    public async Task SLA_Tracks_DaysToDeadline_Correctly()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(nameof(SLA_Tracks_DaysToDeadline_Correctly));

        var module = new Module { Id = 2, ModuleCode = "TEST2", ModuleName = "Test 2", DefaultFrequency = "Quarterly" };
        db.Modules.Add(module);

        var period = new ReturnPeriod
        {
            TenantId = tenantId,
            ModuleId = module.Id,
            Year = 2026,
            Month = 6,
            Quarter = 2,
            Frequency = "Quarterly",
            ReportingDate = new DateTime(2026, 6, 30),
            DeadlineDate = new DateTime(2026, 8, 14),
            IsOpen = true,
            CreatedAt = DateTime.UtcNow
        };
        db.ReturnPeriods.Add(period);
        await db.SaveChangesAsync();

        var submission = new Submission
        {
            TenantId = tenantId,
            InstitutionId = 1,
            ReturnPeriodId = period.Id,
            ReturnCode = "TEST-002",
            Status = Domain.Enums.SubmissionStatus.Accepted,
            SubmittedAt = new DateTime(2026, 8, 17),
            CreatedAt = DateTime.UtcNow
        };
        db.Submissions.Add(submission);
        await db.SaveChangesAsync();

        var sut = new FilingCalendarService(db, new DeadlineComputationService(), NullLogger<FilingCalendarService>.Instance);
        await sut.RecordSla(period.Id, submission.Id);

        var sla = await db.FilingSlaRecords.SingleAsync();
        sla.DaysToDeadline.Should().Be(-3);
        sla.OnTime.Should().BeFalse();
    }

    [Fact]
    public async Task Deadline_Override_Resets_Notification_Level()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(nameof(Deadline_Override_Resets_Notification_Level));

        var period = new ReturnPeriod
        {
            TenantId = tenantId,
            ModuleId = 1,
            Year = 2026,
            Month = 4,
            Frequency = "Monthly",
            ReportingDate = new DateTime(2026, 4, 30),
            DeadlineDate = new DateTime(2026, 5, 30),
            NotificationLevel = 4,
            Status = "DueSoon",
            IsOpen = true,
            CreatedAt = DateTime.UtcNow
        };
        db.ReturnPeriods.Add(period);
        await db.SaveChangesAsync();

        var sut = new FilingCalendarService(db, new DeadlineComputationService(), NullLogger<FilingCalendarService>.Instance);
        await sut.OverrideDeadline(tenantId, period.Id, new DateTime(2026, 6, 30), "Regulator extension", 42);

        var updated = await db.ReturnPeriods.SingleAsync();
        updated.NotificationLevel.Should().Be(0);
        updated.DeadlineOverrideDate.Should().Be(new DateTime(2026, 6, 30));
        updated.DeadlineOverrideBy.Should().Be(42);
        updated.DeadlineOverrideReason.Should().Be("Regulator extension");
    }

    private static MetadataDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new MetadataDbContext(options);
    }
}
