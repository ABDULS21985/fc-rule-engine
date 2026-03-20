using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FluentAssertions;
using Moq;
using Xunit;

namespace FC.Engine.Infrastructure.Tests.Services;

public class TemplateVersioningServiceTests
{
    private readonly Mock<ITemplateRepository> _templateRepo = new();
    private readonly Mock<IDdlEngine> _ddlEngine = new();
    private readonly Mock<IDdlMigrationExecutor> _migrationExecutor = new();
    private readonly Mock<ITemplateMetadataCache> _cache = new();
    private readonly Mock<IXsdGenerator> _xsdGenerator = new();
    private readonly Mock<IAuditLogger> _audit = new();

    private TemplateVersioningService CreateService() => new(
        _templateRepo.Object,
        _ddlEngine.Object,
        _migrationExecutor.Object,
        _cache.Object,
        _xsdGenerator.Object,
        _audit.Object);

    private static ReturnTemplate CreateTemplate(int id = 1, string returnCode = "MFCR 300")
    {
        return new ReturnTemplate
        {
            Id = id,
            ReturnCode = returnCode,
            Name = "Statement of Financial Position",
            PhysicalTableName = "mfcr_300",
            Frequency = ReturnFrequency.Monthly,
            StructuralCategory = StructuralCategory.FixedRow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "system"
        };
    }

    private static TemplateVersion CreateVersion(
        int id = 1,
        int templateId = 1,
        int versionNumber = 1,
        TemplateStatus status = TemplateStatus.Published)
    {
        return new TemplateVersion
        {
            Id = id,
            TemplateId = templateId,
            VersionNumber = versionNumber,
            Status = status,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "system"
        };
    }

    private void SetupTemplateRepoGetById(ReturnTemplate? template)
    {
        _templateRepo
            .Setup(r => r.GetById(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
    }

    private void SetupTemplateRepoUpdate()
    {
        _templateRepo
            .Setup(r => r.Update(It.IsAny<ReturnTemplate>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupAuditLog()
    {
        _audit
            .Setup(a => a.Log(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<object?>(),
                It.IsAny<object?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    // ─────────────────────────────────────────────────────────────────
    // CreateNewDraftVersion
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateNewDraftVersion_NoPublishedVersion_CreatesDraftWithNoClonedData()
    {
        // Arrange - template with no versions (no published version to clone from)
        var template = CreateTemplate();
        SetupTemplateRepoGetById(template);
        SetupTemplateRepoUpdate();
        SetupAuditLog();

        var service = CreateService();

        // Act
        var result = await service.CreateNewDraftVersion(1, "analyst");

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(TemplateStatus.Draft);
        result.VersionNumber.Should().Be(1);
        result.CreatedBy.Should().Be("analyst");
        result.Fields.Should().BeEmpty();
        result.ItemCodes.Should().BeEmpty();
        result.IntraSheetFormulas.Should().BeEmpty();

        template.Versions.Should().HaveCount(1);
        template.Versions[0].Should().BeSameAs(result);

        _templateRepo.Verify(r => r.Update(template, It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.Log(
            "TemplateVersion",
            It.IsAny<int>(),
            "DraftCreated",
            null,
            It.IsAny<object>(),
            "analyst",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateNewDraftVersion_WithPublishedVersion_ClonesFieldsItemCodesAndFormulas()
    {
        // Arrange - template with a published version containing fields, item codes, and formulas
        var template = CreateTemplate();
        var publishedVersion = CreateVersion(id: 1, templateId: 1, versionNumber: 1, status: TemplateStatus.Published);

        publishedVersion.AddField(new TemplateField
        {
            FieldName = "cash_notes",
            DisplayName = "Notes",
            LineCode = "10110",
            DataType = FieldDataType.Decimal,
            SqlType = "decimal(18,2)"
        });
        publishedVersion.AddField(new TemplateField
        {
            FieldName = "cash_coins",
            DisplayName = "Coins",
            LineCode = "10120",
            DataType = FieldDataType.Decimal,
            SqlType = "decimal(18,2)"
        });

        publishedVersion.AddItemCode(new TemplateItemCode
        {
            ItemCode = "10110",
            ItemDescription = "Notes",
            SortOrder = 1,
            IsTotalRow = false
        });
        publishedVersion.AddItemCode(new TemplateItemCode
        {
            ItemCode = "10120",
            ItemDescription = "Coins",
            SortOrder = 2,
            IsTotalRow = false
        });

        publishedVersion.AddFormula(new IntraSheetFormula
        {
            RuleCode = "SUM_CASH",
            RuleName = "Total Cash",
            FormulaType = FormulaType.Sum,
            TargetFieldName = "total_cash",
            TargetLineCode = "10140",
            OperandFields = "[\"cash_notes\",\"cash_coins\"]",
            IsActive = true
        });

        template.AddVersion(publishedVersion);

        SetupTemplateRepoGetById(template);
        SetupTemplateRepoUpdate();
        SetupAuditLog();

        var service = CreateService();

        // Act
        var result = await service.CreateNewDraftVersion(1, "analyst");

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(TemplateStatus.Draft);
        result.VersionNumber.Should().Be(2);

        // Fields should be cloned (same count and data, but different instances)
        result.Fields.Should().HaveCount(2);
        result.Fields[0].FieldName.Should().Be("cash_notes");
        result.Fields[1].FieldName.Should().Be("cash_coins");
        result.Fields[0].Should().NotBeSameAs(publishedVersion.Fields[0]);

        // Item codes should be cloned
        result.ItemCodes.Should().HaveCount(2);
        result.ItemCodes[0].ItemCode.Should().Be("10110");
        result.ItemCodes[1].ItemCode.Should().Be("10120");
        result.ItemCodes[0].Should().NotBeSameAs(publishedVersion.ItemCodes[0]);

        // Formulas should be cloned
        result.IntraSheetFormulas.Should().HaveCount(1);
        result.IntraSheetFormulas[0].RuleCode.Should().Be("SUM_CASH");
        result.IntraSheetFormulas[0].TargetFieldName.Should().Be("total_cash");
        result.IntraSheetFormulas[0].Should().NotBeSameAs(publishedVersion.IntraSheetFormulas[0]);

        // Published version should still have its original data
        publishedVersion.Fields.Should().HaveCount(2);
        publishedVersion.ItemCodes.Should().HaveCount(2);
        publishedVersion.IntraSheetFormulas.Should().HaveCount(1);

        _templateRepo.Verify(r => r.Update(template, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateNewDraftVersion_TemplateNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        SetupTemplateRepoGetById(null);
        var service = CreateService();

        // Act
        var act = () => service.CreateNewDraftVersion(999, "analyst");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*999*not found*");
    }

    [Fact]
    public async Task CreateNewDraftVersion_WhenDraftAlreadyExists_ThrowsInvalidOperationException()
    {
        _templateRepo
            .Setup(r => r.HasExistingDraft(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateService();

        var act = () => service.CreateNewDraftVersion(1, "analyst");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*active draft or review version*");

        _templateRepo.Verify(r => r.GetById(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateNewDraftVersion_AssignsIncrementingVersionNumber()
    {
        // Arrange - template already has version 1 (published) and version 2 (deprecated)
        var template = CreateTemplate();
        template.AddVersion(CreateVersion(id: 1, templateId: 1, versionNumber: 1, status: TemplateStatus.Deprecated));
        template.AddVersion(CreateVersion(id: 2, templateId: 1, versionNumber: 2, status: TemplateStatus.Published));

        SetupTemplateRepoGetById(template);
        SetupTemplateRepoUpdate();
        SetupAuditLog();

        var service = CreateService();

        // Act
        var result = await service.CreateNewDraftVersion(1, "analyst");

        // Assert
        result.VersionNumber.Should().Be(3);
    }

    // ─────────────────────────────────────────────────────────────────
    // SubmitForReview
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitForReview_DraftWithFields_TransitionsToReview()
    {
        // Arrange
        var template = CreateTemplate();
        var draftVersion = CreateVersion(id: 2, templateId: 1, versionNumber: 2, status: TemplateStatus.Draft);
        draftVersion.AddField(new TemplateField
        {
            FieldName = "cash_notes",
            DisplayName = "Notes",
            LineCode = "10110",
            DataType = FieldDataType.Decimal,
            SqlType = "decimal(18,2)"
        });
        template.AddVersion(draftVersion);

        SetupTemplateRepoGetById(template);
        SetupTemplateRepoUpdate();
        SetupAuditLog();

        var service = CreateService();

        // Act
        await service.SubmitForReview(1, 2, "reviewer");

        // Assert
        draftVersion.Status.Should().Be(TemplateStatus.Review);

        _templateRepo.Verify(r => r.Update(template, It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.Log(
            "TemplateVersion",
            2,
            "SubmittedForReview",
            null,
            null,
            "reviewer",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitForReview_NoFields_ThrowsInvalidOperationException()
    {
        // Arrange - draft version with no fields
        var template = CreateTemplate();
        var draftVersion = CreateVersion(id: 2, templateId: 1, versionNumber: 2, status: TemplateStatus.Draft);
        template.AddVersion(draftVersion);

        SetupTemplateRepoGetById(template);

        var service = CreateService();

        // Act
        var act = () => service.SubmitForReview(1, 2, "reviewer");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no fields*");
    }

    [Fact]
    public async Task SubmitForReview_TemplateNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        SetupTemplateRepoGetById(null);
        var service = CreateService();

        // Act
        var act = () => service.SubmitForReview(999, 1, "reviewer");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*999*not found*");
    }

    [Fact]
    public async Task SubmitForReview_DoesNotPersistOrAudit_WhenValidationFails()
    {
        // Arrange - draft version with no fields
        var template = CreateTemplate();
        var draftVersion = CreateVersion(id: 2, templateId: 1, versionNumber: 2, status: TemplateStatus.Draft);
        template.AddVersion(draftVersion);

        SetupTemplateRepoGetById(template);

        var service = CreateService();

        // Act
        var act = () => service.SubmitForReview(1, 2, "reviewer");
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Assert - should not have called Update or Audit
        _templateRepo.Verify(r => r.Update(It.IsAny<ReturnTemplate>(), It.IsAny<CancellationToken>()), Times.Never);
        _audit.Verify(a => a.Log(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<string>(),
            It.IsAny<object?>(),
            It.IsAny<object?>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────
    // PreviewDdl
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PreviewDdl_NoPreviousPublishedVersion_GeneratesCreateTable()
    {
        // Arrange - only one draft version, no published/deprecated versions
        var template = CreateTemplate();
        var draftVersion = CreateVersion(id: 1, templateId: 1, versionNumber: 1, status: TemplateStatus.Draft);
        draftVersion.AddField(new TemplateField
        {
            FieldName = "cash_notes",
            DisplayName = "Notes",
            DataType = FieldDataType.Decimal,
            SqlType = "decimal(18,2)"
        });
        template.AddVersion(draftVersion);

        var expectedDdl = new DdlScript(
            "CREATE TABLE mfcr_300 (cash_notes decimal(18,2));",
            "DROP TABLE mfcr_300;");

        _ddlEngine
            .Setup(d => d.GenerateCreateTable(template, draftVersion))
            .Returns(expectedDdl);

        SetupTemplateRepoGetById(template);

        var service = CreateService();

        // Act
        var result = await service.PreviewDdl(1, 1);

        // Assert
        result.Should().Be(expectedDdl);
        result.ForwardSql.Should().Contain("CREATE TABLE");

        _ddlEngine.Verify(d => d.GenerateCreateTable(template, draftVersion), Times.Once);
        _ddlEngine.Verify(
            d => d.GenerateAlterTable(It.IsAny<ReturnTemplate>(), It.IsAny<TemplateVersion>(), It.IsAny<TemplateVersion>()),
            Times.Never);
    }

    [Fact]
    public async Task PreviewDdl_PreviousPublishedVersionExists_GeneratesAlterTable()
    {
        // Arrange - published v1 and draft v2
        var template = CreateTemplate();
        var publishedVersion = CreateVersion(id: 1, templateId: 1, versionNumber: 1, status: TemplateStatus.Published);
        publishedVersion.AddField(new TemplateField
        {
            FieldName = "cash_notes",
            DisplayName = "Notes",
            DataType = FieldDataType.Decimal,
            SqlType = "decimal(18,2)"
        });
        template.AddVersion(publishedVersion);

        var draftVersion = CreateVersion(id: 2, templateId: 1, versionNumber: 2, status: TemplateStatus.Draft);
        draftVersion.AddField(new TemplateField
        {
            FieldName = "cash_notes",
            DisplayName = "Notes",
            DataType = FieldDataType.Decimal,
            SqlType = "decimal(18,2)"
        });
        draftVersion.AddField(new TemplateField
        {
            FieldName = "cash_coins",
            DisplayName = "Coins",
            DataType = FieldDataType.Decimal,
            SqlType = "decimal(18,2)"
        });
        template.AddVersion(draftVersion);

        var expectedDdl = new DdlScript(
            "ALTER TABLE mfcr_300 ADD cash_coins decimal(18,2);",
            "ALTER TABLE mfcr_300 DROP COLUMN cash_coins;");

        _ddlEngine
            .Setup(d => d.GenerateAlterTable(template, publishedVersion, draftVersion))
            .Returns(expectedDdl);

        SetupTemplateRepoGetById(template);

        var service = CreateService();

        // Act
        var result = await service.PreviewDdl(1, 2);

        // Assert
        result.Should().Be(expectedDdl);
        result.ForwardSql.Should().Contain("ALTER TABLE");

        _ddlEngine.Verify(d => d.GenerateAlterTable(template, publishedVersion, draftVersion), Times.Once);
        _ddlEngine.Verify(
            d => d.GenerateCreateTable(It.IsAny<ReturnTemplate>(), It.IsAny<TemplateVersion>()),
            Times.Never);
    }

    [Fact]
    public async Task PreviewDdl_TemplateNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        SetupTemplateRepoGetById(null);
        var service = CreateService();

        // Act
        var act = () => service.PreviewDdl(999, 1);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*999*not found*");
    }

    [Fact]
    public async Task PreviewDdl_DeprecatedPreviousVersion_TreatedAsPreviousPublished()
    {
        // Arrange - deprecated v1 (was once published) and draft v2
        var template = CreateTemplate();
        var deprecatedVersion = CreateVersion(id: 1, templateId: 1, versionNumber: 1, status: TemplateStatus.Deprecated);
        template.AddVersion(deprecatedVersion);

        var draftVersion = CreateVersion(id: 2, templateId: 1, versionNumber: 2, status: TemplateStatus.Draft);
        template.AddVersion(draftVersion);

        var expectedDdl = new DdlScript("ALTER TABLE ...", "ROLLBACK ...");

        _ddlEngine
            .Setup(d => d.GenerateAlterTable(template, deprecatedVersion, draftVersion))
            .Returns(expectedDdl);

        SetupTemplateRepoGetById(template);

        var service = CreateService();

        // Act
        var result = await service.PreviewDdl(1, 2);

        // Assert - should use AlterTable because deprecated counts as a previous published version
        result.Should().Be(expectedDdl);
        _ddlEngine.Verify(d => d.GenerateAlterTable(template, deprecatedVersion, draftVersion), Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────
    // Publish
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Publish_FirstVersion_CreateTableExecutedAndPublished()
    {
        // Arrange - single review version, no previous published
        var template = CreateTemplate();
        var reviewVersion = CreateVersion(id: 1, templateId: 1, versionNumber: 1, status: TemplateStatus.Review);
        reviewVersion.AddField(new TemplateField
        {
            FieldName = "cash_notes",
            DisplayName = "Notes",
            DataType = FieldDataType.Decimal,
            SqlType = "decimal(18,2)"
        });
        template.AddVersion(reviewVersion);

        var ddl = new DdlScript(
            "CREATE TABLE mfcr_300 (cash_notes decimal(18,2));",
            "DROP TABLE mfcr_300;");

        _ddlEngine
            .Setup(d => d.GenerateCreateTable(template, reviewVersion))
            .Returns(ddl);

        _migrationExecutor
            .Setup(m => m.Execute(1, null, 1, ddl, "approver", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationResult(true, null));

        SetupTemplateRepoGetById(template);
        SetupTemplateRepoUpdate();
        SetupAuditLog();

        var service = CreateService();

        // Act
        await service.Publish(1, 1, "approver");

        // Assert
        reviewVersion.Status.Should().Be(TemplateStatus.Published);
        reviewVersion.ApprovedBy.Should().Be("approver");
        reviewVersion.PublishedAt.Should().NotBeNull();
        reviewVersion.DdlScript.Should().Be(ddl.ForwardSql);
        reviewVersion.RollbackScript.Should().Be(ddl.RollbackSql);

        template.UpdatedBy.Should().Be("approver");

        _ddlEngine.Verify(d => d.GenerateCreateTable(template, reviewVersion), Times.Once);
        _migrationExecutor.Verify(m => m.Execute(1, null, 1, ddl, "approver", It.IsAny<CancellationToken>()), Times.Once);
        _templateRepo.Verify(r => r.Update(template, It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(c => c.Invalidate(template.TenantId, "MFCR 300"), Times.Once);
        _xsdGenerator.Verify(x => x.InvalidateCache(template.TenantId, "MFCR 300"), Times.Once);
        _audit.Verify(a => a.Log(
            "TemplateVersion",
            1,
            "Published",
            null,
            It.IsAny<object>(),
            "approver",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_WithPreviousPublished_AlterTableExecutedAndOldVersionDeprecated()
    {
        // Arrange - published v1 and review v2
        var template = CreateTemplate();

        var publishedVersion = CreateVersion(id: 1, templateId: 1, versionNumber: 1, status: TemplateStatus.Published);
        publishedVersion.AddField(new TemplateField
        {
            FieldName = "cash_notes",
            DisplayName = "Notes",
            DataType = FieldDataType.Decimal,
            SqlType = "decimal(18,2)"
        });
        template.AddVersion(publishedVersion);

        var reviewVersion = CreateVersion(id: 2, templateId: 1, versionNumber: 2, status: TemplateStatus.Review);
        reviewVersion.AddField(new TemplateField
        {
            FieldName = "cash_notes",
            DisplayName = "Notes",
            DataType = FieldDataType.Decimal,
            SqlType = "decimal(18,2)"
        });
        reviewVersion.AddField(new TemplateField
        {
            FieldName = "cash_coins",
            DisplayName = "Coins",
            DataType = FieldDataType.Decimal,
            SqlType = "decimal(18,2)"
        });
        template.AddVersion(reviewVersion);

        var ddl = new DdlScript(
            "ALTER TABLE mfcr_300 ADD cash_coins decimal(18,2);",
            "ALTER TABLE mfcr_300 DROP COLUMN cash_coins;");

        _ddlEngine
            .Setup(d => d.GenerateAlterTable(template, publishedVersion, reviewVersion))
            .Returns(ddl);

        _migrationExecutor
            .Setup(m => m.Execute(1, 1, 2, ddl, "approver", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationResult(true, null));

        SetupTemplateRepoGetById(template);
        SetupTemplateRepoUpdate();
        SetupAuditLog();

        var service = CreateService();

        // Act
        await service.Publish(1, 2, "approver");

        // Assert - new version is published
        reviewVersion.Status.Should().Be(TemplateStatus.Published);
        reviewVersion.ApprovedBy.Should().Be("approver");
        reviewVersion.PublishedAt.Should().NotBeNull();
        reviewVersion.DdlScript.Should().Be(ddl.ForwardSql);
        reviewVersion.RollbackScript.Should().Be(ddl.RollbackSql);

        // Assert - old version is deprecated
        publishedVersion.Status.Should().Be(TemplateStatus.Deprecated);
        publishedVersion.EffectiveTo.Should().NotBeNull();

        // Assert - template metadata updated
        template.UpdatedBy.Should().Be("approver");

        // Assert - caches invalidated
        _cache.Verify(c => c.Invalidate(template.TenantId, "MFCR 300"), Times.Once);
        _xsdGenerator.Verify(x => x.InvalidateCache(template.TenantId, "MFCR 300"), Times.Once);

        _ddlEngine.Verify(d => d.GenerateAlterTable(template, publishedVersion, reviewVersion), Times.Once);
        _migrationExecutor.Verify(m => m.Execute(1, 1, 2, ddl, "approver", It.IsAny<CancellationToken>()), Times.Once);
        _templateRepo.Verify(r => r.Update(template, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_DdlExecutionFails_ThrowsInvalidOperationException()
    {
        // Arrange
        var template = CreateTemplate();
        var reviewVersion = CreateVersion(id: 1, templateId: 1, versionNumber: 1, status: TemplateStatus.Review);
        reviewVersion.AddField(new TemplateField
        {
            FieldName = "cash_notes",
            DisplayName = "Notes",
            DataType = FieldDataType.Decimal,
            SqlType = "decimal(18,2)"
        });
        template.AddVersion(reviewVersion);

        var ddl = new DdlScript("CREATE TABLE mfcr_300 (...);", "DROP TABLE mfcr_300;");

        _ddlEngine
            .Setup(d => d.GenerateCreateTable(template, reviewVersion))
            .Returns(ddl);

        _migrationExecutor
            .Setup(m => m.Execute(1, null, 1, ddl, "approver", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationResult(false, "Duplicate table name"));

        SetupTemplateRepoGetById(template);

        var service = CreateService();

        // Act
        var act = () => service.Publish(1, 1, "approver");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*DDL execution failed*Duplicate table name*");
    }

    [Fact]
    public async Task Publish_DdlExecutionFails_DoesNotPublishOrDeprecateOrInvalidateCaches()
    {
        // Arrange
        var template = CreateTemplate();

        var publishedVersion = CreateVersion(id: 1, templateId: 1, versionNumber: 1, status: TemplateStatus.Published);
        template.AddVersion(publishedVersion);

        var reviewVersion = CreateVersion(id: 2, templateId: 1, versionNumber: 2, status: TemplateStatus.Review);
        reviewVersion.AddField(new TemplateField
        {
            FieldName = "cash_notes",
            DisplayName = "Notes",
            DataType = FieldDataType.Decimal,
            SqlType = "decimal(18,2)"
        });
        template.AddVersion(reviewVersion);

        var ddl = new DdlScript("ALTER TABLE ...", "ROLLBACK ...");

        _ddlEngine
            .Setup(d => d.GenerateAlterTable(template, publishedVersion, reviewVersion))
            .Returns(ddl);

        _migrationExecutor
            .Setup(m => m.Execute(1, 1, 2, ddl, "approver", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationResult(false, "Migration error"));

        SetupTemplateRepoGetById(template);

        var service = CreateService();

        // Act
        var act = () => service.Publish(1, 2, "approver");
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Assert - version statuses should not change
        reviewVersion.Status.Should().Be(TemplateStatus.Review);
        publishedVersion.Status.Should().Be(TemplateStatus.Published);

        // Assert - no persistence or cache invalidation
        _templateRepo.Verify(r => r.Update(It.IsAny<ReturnTemplate>(), It.IsAny<CancellationToken>()), Times.Never);
        _cache.Verify(c => c.Invalidate(It.IsAny<Guid?>(), It.IsAny<string>()), Times.Never);
        _xsdGenerator.Verify(x => x.InvalidateCache(It.IsAny<Guid?>(), It.IsAny<string>()), Times.Never);
        _audit.Verify(a => a.Log(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<string>(),
            It.IsAny<object?>(),
            It.IsAny<object?>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Publish_TemplateNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        SetupTemplateRepoGetById(null);
        var service = CreateService();

        // Act
        var act = () => service.Publish(999, 1, "approver");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*999*not found*");
    }

    [Fact]
    public async Task Publish_StoresDdlScriptOnVersion()
    {
        // Arrange
        var template = CreateTemplate();
        var reviewVersion = CreateVersion(id: 1, templateId: 1, versionNumber: 1, status: TemplateStatus.Review);
        reviewVersion.AddField(new TemplateField
        {
            FieldName = "cash_notes",
            DisplayName = "Notes",
            DataType = FieldDataType.Decimal,
            SqlType = "decimal(18,2)"
        });
        template.AddVersion(reviewVersion);

        var forwardSql = "CREATE TABLE mfcr_300 (cash_notes decimal(18,2));";
        var rollbackSql = "DROP TABLE mfcr_300;";
        var ddl = new DdlScript(forwardSql, rollbackSql);

        _ddlEngine
            .Setup(d => d.GenerateCreateTable(template, reviewVersion))
            .Returns(ddl);

        _migrationExecutor
            .Setup(m => m.Execute(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<DdlScript>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationResult(true, null));

        SetupTemplateRepoGetById(template);
        SetupTemplateRepoUpdate();
        SetupAuditLog();

        var service = CreateService();

        // Act
        await service.Publish(1, 1, "approver");

        // Assert
        reviewVersion.DdlScript.Should().Be(forwardSql);
        reviewVersion.RollbackScript.Should().Be(rollbackSql);
    }

    [Fact]
    public async Task Publish_UpdatesTemplateTimestampAndUser()
    {
        // Arrange
        var template = CreateTemplate();
        var beforePublish = template.UpdatedAt;

        var reviewVersion = CreateVersion(id: 1, templateId: 1, versionNumber: 1, status: TemplateStatus.Review);
        reviewVersion.AddField(new TemplateField
        {
            FieldName = "field1",
            DisplayName = "Field 1",
            DataType = FieldDataType.Decimal,
            SqlType = "decimal(18,2)"
        });
        template.AddVersion(reviewVersion);

        var ddl = new DdlScript("CREATE TABLE ...", "DROP TABLE ...");

        _ddlEngine
            .Setup(d => d.GenerateCreateTable(template, reviewVersion))
            .Returns(ddl);

        _migrationExecutor
            .Setup(m => m.Execute(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<DdlScript>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationResult(true, null));

        SetupTemplateRepoGetById(template);
        SetupTemplateRepoUpdate();
        SetupAuditLog();

        var service = CreateService();

        // Act
        await service.Publish(1, 1, "approver");

        // Assert
        template.UpdatedBy.Should().Be("approver");
        template.UpdatedAt.Should().BeOnOrAfter(beforePublish);
    }

    [Fact]
    public async Task Publish_InvalidatesBothMetadataCacheAndXsdCache()
    {
        // Arrange
        var template = CreateTemplate(returnCode: "MFCR 500");
        var reviewVersion = CreateVersion(id: 1, templateId: 1, versionNumber: 1, status: TemplateStatus.Review);
        reviewVersion.AddField(new TemplateField
        {
            FieldName = "field1",
            DisplayName = "Field 1",
            DataType = FieldDataType.Decimal,
            SqlType = "decimal(18,2)"
        });
        template.AddVersion(reviewVersion);

        var ddl = new DdlScript("CREATE TABLE ...", "DROP TABLE ...");

        _ddlEngine
            .Setup(d => d.GenerateCreateTable(template, reviewVersion))
            .Returns(ddl);

        _migrationExecutor
            .Setup(m => m.Execute(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<DdlScript>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationResult(true, null));

        SetupTemplateRepoGetById(template);
        SetupTemplateRepoUpdate();
        SetupAuditLog();

        var service = CreateService();

        // Act
        await service.Publish(1, 1, "approver");

        // Assert - both caches invalidated with correct return code
        _cache.Verify(c => c.Invalidate(template.TenantId, "MFCR 500"), Times.Once);
        _xsdGenerator.Verify(x => x.InvalidateCache(template.TenantId, "MFCR 500"), Times.Once);
    }

    [Fact]
    public async Task Publish_FirstVersion_DoesNotAttemptToDeprecateAnything()
    {
        // Arrange - only one version in review, no previous published
        var template = CreateTemplate();
        var reviewVersion = CreateVersion(id: 1, templateId: 1, versionNumber: 1, status: TemplateStatus.Review);
        reviewVersion.AddField(new TemplateField
        {
            FieldName = "field1",
            DisplayName = "Field 1",
            DataType = FieldDataType.Decimal,
            SqlType = "decimal(18,2)"
        });
        template.AddVersion(reviewVersion);

        var ddl = new DdlScript("CREATE TABLE ...", "DROP TABLE ...");

        _ddlEngine
            .Setup(d => d.GenerateCreateTable(template, reviewVersion))
            .Returns(ddl);

        _migrationExecutor
            .Setup(m => m.Execute(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<DdlScript>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationResult(true, null));

        SetupTemplateRepoGetById(template);
        SetupTemplateRepoUpdate();
        SetupAuditLog();

        var service = CreateService();

        // Act
        await service.Publish(1, 1, "approver");

        // Assert - the single version should be Published (not Deprecated)
        template.Versions.Should().HaveCount(1);
        template.Versions[0].Status.Should().Be(TemplateStatus.Published);
    }

    [Fact]
    public async Task Publish_PassesCorrectVersionNumbersToMigrationExecutor()
    {
        // Arrange - published v3 and review v4
        var template = CreateTemplate();

        var publishedVersion = CreateVersion(id: 10, templateId: 1, versionNumber: 3, status: TemplateStatus.Published);
        template.AddVersion(publishedVersion);

        var reviewVersion = CreateVersion(id: 11, templateId: 1, versionNumber: 4, status: TemplateStatus.Review);
        reviewVersion.AddField(new TemplateField
        {
            FieldName = "field1",
            DisplayName = "Field 1",
            DataType = FieldDataType.Decimal,
            SqlType = "decimal(18,2)"
        });
        template.AddVersion(reviewVersion);

        var ddl = new DdlScript("ALTER TABLE ...", "ROLLBACK ...");

        _ddlEngine
            .Setup(d => d.GenerateAlterTable(template, publishedVersion, reviewVersion))
            .Returns(ddl);

        _migrationExecutor
            .Setup(m => m.Execute(1, 3, 4, ddl, "approver", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationResult(true, null));

        SetupTemplateRepoGetById(template);
        SetupTemplateRepoUpdate();
        SetupAuditLog();

        var service = CreateService();

        // Act
        await service.Publish(1, 11, "approver");

        // Assert - migration executor called with templateId=1, versionFrom=3, versionTo=4
        _migrationExecutor.Verify(
            m => m.Execute(1, 3, 4, ddl, "approver", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
