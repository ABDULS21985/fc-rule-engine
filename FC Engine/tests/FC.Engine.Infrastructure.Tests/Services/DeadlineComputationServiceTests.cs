using FC.Engine.Application.Services;
using FC.Engine.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace FC.Engine.Infrastructure.Tests.Services;

public class DeadlineComputationServiceTests
{
    private readonly DeadlineComputationService _sut = new();

    // ── Period generation ──────────────────────────────────────

    [Fact]
    public void Monthly_Module_Generates_12_Periods_Per_Year()
    {
        var module = CreateModule("Monthly");

        var periods = _sut.GeneratePeriodsForNext12Months(module, 12);

        periods.Should().HaveCount(12);
        periods.Should().AllSatisfy(p => p.Frequency.Should().Be("Monthly"));
    }

    [Fact]
    public void Quarterly_Module_Generates_4_Periods_Per_Year()
    {
        var module = CreateModule("Quarterly");

        var periods = _sut.GeneratePeriodsForNext12Months(module, 12);

        periods.Should().HaveCount(4);
        periods.Should().AllSatisfy(p =>
        {
            p.Frequency.Should().Be("Quarterly");
            p.Quarter.Should().BeInRange(1, 4);
        });
    }

    [Fact]
    public void Annual_Module_Generates_1_Period_Per_Year()
    {
        var module = CreateModule("Annual");

        var periods = _sut.GeneratePeriodsForNext12Months(module, 12);

        periods.Should().HaveCount(1);
        periods.Should().AllSatisfy(p => p.Frequency.Should().Be("Annual"));
    }

    [Fact]
    public void SemiAnnual_Module_Generates_2_Periods()
    {
        var module = CreateModule("SemiAnnual");

        var periods = _sut.GeneratePeriodsForNext12Months(module, 12);

        periods.Should().HaveCount(2);
    }

    // ── Deadline computation ───────────────────────────────────

    [Fact]
    public void Deadline_30_Days_After_Monthly_Period_End()
    {
        var module = CreateModule("Monthly");
        var period = new ReturnPeriod { Year = 2026, Month = 3, Frequency = "Monthly" };

        var deadline = _sut.ComputeDeadline(module, period);

        // March 31 + 30 days = April 30
        deadline.Should().Be(new DateTime(2026, 4, 30));
    }

    [Fact]
    public void Deadline_45_Days_After_Quarterly_Period_End()
    {
        var module = CreateModule("Quarterly");
        var period = new ReturnPeriod { Year = 2026, Month = 3, Quarter = 1, Frequency = "Quarterly" };

        var deadline = _sut.ComputeDeadline(module, period);

        // Q1 ends March 31 + 45 days = May 15
        deadline.Should().Be(new DateTime(2026, 5, 15));
    }

    [Fact]
    public void Deadline_60_Days_After_SemiAnnual_Period_End()
    {
        var module = CreateModule("SemiAnnual");
        var period = new ReturnPeriod { Year = 2026, Month = 6, Frequency = "SemiAnnual" };

        var deadline = _sut.ComputeDeadline(module, period);

        // June 30 + 60 days = August 29
        deadline.Should().Be(new DateTime(2026, 8, 29));
    }

    [Fact]
    public void Deadline_90_Days_After_Annual_Period_End()
    {
        var module = CreateModule("Annual");
        var period = new ReturnPeriod { Year = 2026, Month = 12, Frequency = "Annual" };

        var deadline = _sut.ComputeDeadline(module, period);

        // Dec 31 + 90 days = March 31, 2027
        deadline.Should().Be(new DateTime(2027, 3, 31));
    }

    [Fact]
    public void Custom_DeadlineOffsetDays_Overrides_Default()
    {
        var module = CreateModule("Monthly");
        module.DeadlineOffsetDays = 15; // Custom: 15 days instead of default 30
        var period = new ReturnPeriod { Year = 2026, Month = 1, Frequency = "Monthly" };

        var deadline = _sut.ComputeDeadline(module, period);

        // Jan 31 + 15 days = Feb 15
        deadline.Should().Be(new DateTime(2026, 2, 15));
    }

    [Fact]
    public void Q2_Deadline_Uses_June_End()
    {
        var module = CreateModule("Quarterly");
        var period = new ReturnPeriod { Year = 2026, Month = 6, Quarter = 2, Frequency = "Quarterly" };

        var deadline = _sut.ComputeDeadline(module, period);

        // Q2 ends June 30 + 45 days = August 14
        deadline.Should().Be(new DateTime(2026, 8, 14));
    }

    // ── Period end date ────────────────────────────────────────

    [Theory]
    [InlineData("Monthly", 2026, 2, null, 2026, 2, 28)]
    [InlineData("Monthly", 2024, 2, null, 2024, 2, 29)] // Leap year
    [InlineData("Quarterly", 2026, null, 1, 2026, 3, 31)]
    [InlineData("Quarterly", 2026, null, 3, 2026, 9, 30)]
    [InlineData("Annual", 2026, null, null, 2026, 12, 31)]
    public void GetPeriodEndDate_Returns_Correct_Date(
        string frequency, int year, int? month, int? quarter,
        int expectedYear, int expectedMonth, int expectedDay)
    {
        var result = DeadlineComputationService.GetPeriodEndDate(frequency, year, month, quarter);

        result.Should().Be(new DateTime(expectedYear, expectedMonth, expectedDay));
    }

    // ── Helpers ────────────────────────────────────────────────

    private static Module CreateModule(string frequency) => new()
    {
        Id = 1,
        ModuleCode = "TEST",
        ModuleName = "Test Module",
        DefaultFrequency = frequency,
        IsActive = true
    };
}
