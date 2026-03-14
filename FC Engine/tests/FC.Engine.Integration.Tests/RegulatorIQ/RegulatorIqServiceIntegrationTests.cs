using System.Text;
using FC.Engine.Domain.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FC.Engine.Integration.Tests.RegulatorIQ;

[Collection("RegulatorIqIntegration")]
public sealed class RegulatorIqServiceIntegrationTests : IClassFixture<RegulatorIqFixture>
{
    private readonly RegulatorIqFixture _fixture;

    public RegulatorIqServiceIntegrationTests(RegulatorIqFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task QueryAsync_EntityProfile_PersistsTurnAndAuditTrail()
    {
        var service = _fixture.CreateOrchestrator();

        var result = await service.QueryAsync(new RegulatorIqQueryRequest
        {
            Query = "Give me a full profile of Access Bank",
            RegulatorCode = "CBN",
            RegulatorId = "examiner-001",
            UserRole = "Examiner"
        });

        result.TurnId.Should().NotBeNull();
        result.ConversationId.Should().NotBe(Guid.Empty);
        result.IntentCode.Should().Be("ENTITY_PROFILE");
        result.Response.EntitiesAccessed.Should().Contain(_fixture.AccessBankTenantId);
        result.Response.DataSourcesUsed.Should().Contain(new[] { "RG-07", "RG-32", "AI-01", "AI-04", "RG-12" });

        await using var db = _fixture.CreateDbContext();
        var turn = await db.ComplianceIqTurns
            .AsNoTracking()
            .SingleAsync(x => x.Id == result.TurnId!.Value);

        turn.ClassificationLevel.Should().Be("CONFIDENTIAL");
        turn.RegulatorAgency.Should().Be("CBN");
        turn.EntitiesAccessedJson.Should().Contain(_fixture.AccessBankTenantId.ToString());
        turn.DataSourcesUsed.Should().Contain("RG-07");

        var accessLog = await db.RegIqAccessLogs
            .AsNoTracking()
            .OrderByDescending(x => x.Id)
            .FirstAsync();

        accessLog.TurnId.Should().Be(result.TurnId);
        accessLog.ConversationId.Should().Be(result.ConversationId);
        accessLog.ClassificationLevel.Should().Be("CONFIDENTIAL");

        var auditActions = await db.AuditLog
            .AsNoTracking()
            .Where(x => x.TenantId == _fixture.CbnRegulatorTenantId)
            .OrderByDescending(x => x.Id)
            .Select(x => x.Action)
            .Take(5)
            .ToListAsync();

        auditActions.Should().Contain("REGIQ_ACCESS");
        auditActions.Should().Contain("REGULATORIQ_QUERY_PROCESSED");
    }

    [Fact]
    public async Task StartAndEndExaminationSessionAsync_UpdatesConversationFlags()
    {
        var service = _fixture.CreateOrchestrator();

        var conversationId = await service.StartExaminationSessionAsync("examiner-001", _fixture.AccessBankTenantId);

        await using (var db = _fixture.CreateDbContext())
        {
            var conversation = await db.ComplianceIqConversations
                .AsNoTracking()
                .SingleAsync(x => x.Id == conversationId);

            conversation.IsExaminationSession.Should().BeTrue();
            conversation.ExaminationTargetTenantId.Should().Be(_fixture.AccessBankTenantId);
            conversation.Scope.Should().Be("ENTITY");
        }

        await service.EndExaminationSessionAsync(conversationId);

        await using (var db = _fixture.CreateDbContext())
        {
            var conversation = await db.ComplianceIqConversations
                .AsNoTracking()
                .SingleAsync(x => x.Id == conversationId);

            conversation.IsExaminationSession.Should().BeFalse();
            conversation.ExaminationTargetTenantId.Should().BeNull();
            conversation.Scope.Should().Be("SECTOR");
        }
    }

    [Fact]
    public async Task SubmitFeedbackAsync_WritesComplianceIqFeedback()
    {
        var service = _fixture.CreateOrchestrator();
        var result = await service.QueryAsync(new RegulatorIqQueryRequest
        {
            Query = "Show filing status for Access Bank",
            RegulatorCode = "CBN",
            RegulatorId = "examiner-001",
            UserRole = "Examiner"
        });

        await service.SubmitFeedbackAsync(result.TurnId!.Value, 5, "Grounded and useful.");

        await using var db = _fixture.CreateDbContext();
        var feedback = await db.ComplianceIqFeedback
            .AsNoTracking()
            .SingleAsync(x => x.TurnId == result.TurnId.Value);

        feedback.Rating.Should().Be(5);
        feedback.FeedbackText.Should().Be("Grounded and useful.");
        feedback.UserId.Should().Be("examiner-001");
    }

    [Fact]
    public async Task QueryAsync_ThenGetConversationHistoryAsync_ReturnsTurnsInOrder()
    {
        var service = _fixture.CreateOrchestrator();

        var firstTurn = await service.QueryAsync(new RegulatorIqQueryRequest
        {
            Query = "Give me a full profile of Access Bank",
            RegulatorCode = "CBN",
            RegulatorId = "examiner-001",
            UserRole = "Examiner"
        });

        var secondTurn = await service.QueryAsync(new RegulatorIqQueryRequest
        {
            ConversationId = firstTurn.ConversationId,
            Query = "Show filing status for Access Bank",
            RegulatorCode = "CBN",
            RegulatorId = "examiner-001",
            UserRole = "Examiner"
        });

        var history = await service.GetConversationHistoryAsync(firstTurn.ConversationId);

        history.Should().HaveCount(2);
        history.Select(x => x.TurnNumber).Should().ContainInOrder(1, 2);
        history.Select(x => x.QueryText).Should().ContainInOrder(
            "Give me a full profile of Access Bank",
            "Show filing status for Access Bank");
        history.Single(x => x.Id == secondTurn.TurnId!.Value).IntentCode.Should().Be("FILING_STATUS");

        await using var db = _fixture.CreateDbContext();
        var conversation = await db.ComplianceIqConversations
            .AsNoTracking()
            .SingleAsync(x => x.Id == firstTurn.ConversationId);

        conversation.TurnCount.Should().Be(2);
    }

    [Fact]
    public async Task ExportConversationPdfAsync_ReturnsPdfBytes()
    {
        var service = _fixture.CreateOrchestrator();
        var result = await service.QueryAsync(new RegulatorIqQueryRequest
        {
            Query = "Compare Access Bank vs Zenith on CAR and NPL",
            RegulatorCode = "CBN",
            RegulatorId = "examiner-001",
            UserRole = "Examiner"
        });

        var pdf = await service.ExportConversationPdfAsync(result.ConversationId);

        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf.Take(4).ToArray()).Should().Be("%PDF");
    }

    [Fact]
    public async Task GenerateExaminationBriefingPdfAsync_ReturnsPdfBytes()
    {
        var service = _fixture.CreateOrchestrator();

        var pdf = await service.GenerateExaminationBriefingPdfAsync(_fixture.AccessBankTenantId, "CBN");

        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf.Take(4).ToArray()).Should().Be("%PDF");
    }
}
