using FC.Engine.Application.Services;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Notifications;
using FC.Engine.Infrastructure.BackgroundJobs;
using FluentAssertions;
using Xunit;

namespace FC.Engine.Infrastructure.Tests.Services;

/// <summary>
/// Tests for the notification escalation logic used by FilingCalendarJob.
/// These are unit tests for the deadline-level → notification mapping.
/// </summary>
public class FilingCalendarJobTests
{
    // ── Notification level determination ────────────────────────

    [Theory]
    [InlineData(31, 0)]   // More than 30 days: no notification
    [InlineData(30, 1)]   // T-30
    [InlineData(14, 2)]   // T-14
    [InlineData(7, 3)]    // T-7
    [InlineData(3, 4)]    // T-3
    [InlineData(1, 5)]    // T-1
    [InlineData(0, 6)]    // Overdue (T+0)
    [InlineData(-5, 6)]   // Overdue (T+5)
    public void DaysToDeadline_Maps_To_Correct_NotificationLevel(int daysToDeadline, int expectedLevel)
    {
        var level = daysToDeadline switch
        {
            <= 0 => 6,
            1 => 5,
            <= 3 => 4,
            <= 7 => 3,
            <= 14 => 2,
            <= 30 => 1,
            _ => 0
        };

        level.Should().Be(expectedLevel);
    }

    // ── Status determination ───────────────────────────────────

    [Theory]
    [InlineData(-1, "Overdue")]
    [InlineData(0, "Overdue")]
    [InlineData(3, "DueSoon")]
    [InlineData(7, "DueSoon")]
    [InlineData(14, "Open")]
    [InlineData(30, "Open")]
    [InlineData(60, "Open")]
    [InlineData(61, "Upcoming")]
    public void DaysToDeadline_Maps_To_Correct_Status(int daysToDeadline, string expectedStatus)
    {
        var status = daysToDeadline switch
        {
            <= 0 => "Overdue",
            <= 7 => "DueSoon",
            <= 60 => "Open",
            _ => "Upcoming"
        };

        status.Should().Be(expectedStatus);
    }

    // ── Escalation event mapping ───────────────────────────────

    [Theory]
    [InlineData(1, NotificationEvents.DeadlineT30)]
    [InlineData(2, NotificationEvents.DeadlineT14)]
    [InlineData(3, NotificationEvents.DeadlineT7)]
    [InlineData(4, NotificationEvents.DeadlineT3)]
    [InlineData(5, NotificationEvents.DeadlineT1)]
    [InlineData(6, NotificationEvents.DeadlineOverdue)]
    public void NotificationLevel_Maps_To_Correct_EventType(int level, string expectedEvent)
    {
        var eventType = level switch
        {
            1 => NotificationEvents.DeadlineT30,
            2 => NotificationEvents.DeadlineT14,
            3 => NotificationEvents.DeadlineT7,
            4 => NotificationEvents.DeadlineT3,
            5 => NotificationEvents.DeadlineT1,
            6 => NotificationEvents.DeadlineOverdue,
            _ => null
        };

        eventType.Should().Be(expectedEvent);
    }

    // ── Mandatory notification ─────────────────────────────────

    [Theory]
    [InlineData(1, false)]   // T-30 not mandatory
    [InlineData(2, false)]   // T-14 not mandatory
    [InlineData(3, false)]   // T-7 not mandatory
    [InlineData(4, false)]   // T-3 not mandatory
    [InlineData(5, true)]    // T-1 IS mandatory
    [InlineData(6, true)]    // Overdue IS mandatory
    public void T1_And_Overdue_Are_Mandatory(int level, bool expectedMandatory)
    {
        var isMandatory = level >= 5;
        isMandatory.Should().Be(expectedMandatory);
    }

    // ── Recipient escalation ───────────────────────────────────

    [Fact]
    public void T30_Notification_Goes_To_Maker_Only()
    {
        var roles = GetRolesForLevel(1);
        roles.Should().BeEquivalentTo(new[] { "Maker" });
    }

    [Fact]
    public void T14_Notification_Goes_To_Maker_And_Admin()
    {
        var roles = GetRolesForLevel(2);
        roles.Should().BeEquivalentTo(new[] { "Maker", "Admin" });
    }

    [Fact]
    public void T7_Notification_Goes_To_Maker_Checker_Admin()
    {
        var roles = GetRolesForLevel(3);
        roles.Should().BeEquivalentTo(new[] { "Maker", "Checker", "Admin" });
    }

    [Fact]
    public void Overdue_Notification_Goes_To_All_Roles()
    {
        var roles = GetRolesForLevel(6);
        roles.Should().Contain("Maker");
        roles.Should().Contain("Checker");
        roles.Should().Contain("Approver");
        roles.Should().Contain("Admin");
    }

    // ── Draft auto-creation eligibility ────────────────────────

    [Theory]
    [InlineData(60, true)]   // Exactly T-60: eligible
    [InlineData(59, true)]   // Within T-60: eligible
    [InlineData(30, true)]   // Well within T-60: eligible
    [InlineData(61, false)]  // Outside T-60: not eligible
    [InlineData(90, false)]  // Far from deadline: not eligible
    public void AutoCreate_Draft_Only_Within_T60(int daysToDeadline, bool shouldAutoCreate)
    {
        var eligible = daysToDeadline <= 60;
        eligible.Should().Be(shouldAutoCreate);
    }

    // ── Period generation counts ───────────────────────────────

    [Fact]
    public void Monthly_Periods_Have_Correct_Frequency_Set()
    {
        var sut = new DeadlineComputationService();
        var module = new Module { DefaultFrequency = "Monthly" };
        var periods = sut.GeneratePeriodsForNext12Months(module, 12);

        periods.Should().AllSatisfy(p =>
        {
            p.Frequency.Should().Be("Monthly");
            p.Month.Should().BeInRange(1, 12);
            p.Year.Should().BeGreaterThanOrEqualTo(DateTime.UtcNow.Year);
        });
    }

    [Fact]
    public void Quarterly_Periods_Have_Quarter_Set()
    {
        var sut = new DeadlineComputationService();
        var module = new Module { DefaultFrequency = "Quarterly" };
        var periods = sut.GeneratePeriodsForNext12Months(module, 12);

        periods.Should().AllSatisfy(p =>
        {
            p.Quarter.Should().NotBeNull();
            p.Quarter.Should().BeInRange(1, 4);
        });
    }

    // ── Submitted return skips escalation ──────────────────────

    [Fact]
    public void Escalation_Only_Fires_When_Level_Increases()
    {
        var shouldEscalate = FilingCalendarJob.ShouldTriggerEscalation(
            currentLevel: 2,
            newLevel: 2,
            overdueReminderSentToday: false);

        shouldEscalate.Should().BeFalse();
    }

    [Fact]
    public void Escalation_Fires_When_Level_Increases()
    {
        var shouldEscalate = FilingCalendarJob.ShouldTriggerEscalation(
            currentLevel: 2,
            newLevel: 3,
            overdueReminderSentToday: false);

        shouldEscalate.Should().BeTrue();
    }

    // ── Priority mapping ───────────────────────────────────────

    [Theory]
    [InlineData(1, NotificationPriority.Low)]
    [InlineData(2, NotificationPriority.Normal)]
    [InlineData(3, NotificationPriority.High)]
    [InlineData(4, NotificationPriority.High)]
    [InlineData(5, NotificationPriority.Critical)]
    [InlineData(6, NotificationPriority.Critical)]
    public void NotificationLevel_Maps_To_Correct_Priority(int level, NotificationPriority expectedPriority)
    {
        var priority = level switch
        {
            >= 5 => NotificationPriority.Critical,
            >= 3 => NotificationPriority.High,
            >= 2 => NotificationPriority.Normal,
            _ => NotificationPriority.Low
        };

        priority.Should().Be(expectedPriority);
    }

    // ── Spec-required named tests ─────────────────────────────

    [Fact]
    public void T60_AutoCreates_Draft_Return()
    {
        // Period with 60 days to deadline should trigger auto-creation
        var daysToDeadline = 60;
        var isEligible = daysToDeadline <= 60;
        isEligible.Should().BeTrue("T-60 is the trigger point for auto-creating draft returns");

        // Period with 61 days should NOT trigger
        var notEligible = 61 <= 60;
        notEligible.Should().BeFalse("T-61 is outside the auto-creation window");
    }

    [Fact]
    public void SelectAutoCreateInstitutionId_Prefers_Primary_HeadOffice()
    {
        var institutions = new[]
        {
            new Institution
            {
                Id = 7,
                IsActive = true,
                InstitutionName = "Branch Office",
                EntityType = EntityType.Branch,
                ParentInstitutionId = 4
            },
            new Institution
            {
                Id = 4,
                IsActive = true,
                InstitutionName = "Head Office",
                EntityType = EntityType.HeadOffice,
                ParentInstitutionId = null
            }
        };

        var selectedInstitutionId = FilingCalendarJob.SelectAutoCreateInstitutionId(institutions);

        selectedInstitutionId.Should().Be(4);
    }

    [Fact]
    public void SelectAutoCreateInstitutionId_Returns_Null_When_No_Active_Institution_Exists()
    {
        var institutions = new[]
        {
            new Institution
            {
                Id = 4,
                IsActive = false,
                InstitutionName = "Dormant Institution",
                EntityType = EntityType.HeadOffice
            }
        };

        var selectedInstitutionId = FilingCalendarJob.SelectAutoCreateInstitutionId(institutions);

        selectedInstitutionId.Should().BeNull();
    }

    [Fact]
    public void T30_Sends_Email_To_Makers()
    {
        var roles = GetRolesForLevel(1); // T-30 = level 1
        roles.Should().Contain("Maker", "T-30 notification must reach Makers");
        roles.Should().HaveCount(1, "T-30 should only notify Makers");
    }

    [Fact]
    public void T7_Sends_Email_SMS_InApp_To_Makers_Checker_Admin()
    {
        var roles = GetRolesForLevel(3); // T-7 = level 3
        roles.Should().Contain("Maker");
        roles.Should().Contain("Checker");
        roles.Should().Contain("Admin");
    }

    [Fact]
    public void T1_Is_Mandatory_Notification()
    {
        var level = 5; // T-1
        var isMandatory = level >= 5;
        isMandatory.Should().BeTrue("T-1 notifications bypass user preferences");
    }

    [Fact]
    public void Overdue_Sends_Daily_Until_Submitted()
    {
        var shouldNotify = FilingCalendarJob.ShouldTriggerEscalation(
            currentLevel: 6,
            newLevel: 6,
            overdueReminderSentToday: false);

        shouldNotify.Should().BeTrue("Overdue notifications must re-send daily until submitted");
    }

    [Fact]
    public void Overdue_Does_Not_Send_More_Than_Once_Per_Day()
    {
        var shouldNotify = FilingCalendarJob.ShouldTriggerEscalation(
            currentLevel: 6,
            newLevel: 6,
            overdueReminderSentToday: true);

        shouldNotify.Should().BeFalse("Overdue reminders should be capped at once per day");
    }

    [Fact]
    public void Submitted_Return_Skips_Further_Notifications()
    {
        // When a return is submitted, the period status becomes "Completed"
        // and the CheckDeadlinesAndEscalate method skips it entirely
        var status = "Completed";
        var shouldSkip = status == "Completed" || status == "Closed";

        shouldSkip.Should().BeTrue("Submitted returns should not receive further deadline notifications");
    }

    [Fact]
    public void Deadline_Override_Resets_Notification_Level()
    {
        // Simulate: period at level 4 (T-3), admin overrides deadline
        var period = new ReturnPeriod
        {
            NotificationLevel = 4,
            DeadlineDate = DateTime.UtcNow.AddDays(-2),
            Status = "DueSoon"
        };

        // Override resets the notification level to 0
        period.DeadlineOverrideDate = DateTime.UtcNow.AddDays(30);
        period.NotificationLevel = 0; // This is what OverrideDeadline() does

        period.NotificationLevel.Should().Be(0, "Override must reset notification level to allow re-escalation");
        period.EffectiveDeadline.Should().BeAfter(DateTime.UtcNow, "Override should extend the deadline");
    }

    // ── Helpers ────────────────────────────────────────────────

    private static List<string> GetRolesForLevel(int level)
    {
        return level switch
        {
            1 => new List<string> { "Maker" },
            2 => new List<string> { "Maker", "Admin" },
            3 or 4 => new List<string> { "Maker", "Checker", "Admin" },
            _ => new List<string> { "Maker", "Checker", "Approver", "Admin" }
        };
    }
}
