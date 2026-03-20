using FC.Engine.Application.DTOs;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FluentAssertions;
using Moq;
using Xunit;

namespace FC.Engine.Infrastructure.Tests.Services;

public class TemplateServiceTests
{
    private readonly Mock<ITemplateRepository> _templateRepo = new();
    private readonly Mock<IAuditLogger> _audit = new();
    private readonly Mock<ITemplateMetadataCache> _cache = new();
    private readonly Mock<ISqlTypeMapper> _sqlTypeMapper = new();
    private readonly Mock<IEntitlementService> _entitlementService = new();
    private readonly Mock<ITenantContext> _tenantContext = new();

    private TemplateService CreateService() =>
        new(_templateRepo.Object, _audit.Object, _cache.Object, _sqlTypeMapper.Object, _entitlementService.Object, _tenantContext.Object);

    // ──────────────────────────────────────────────────
    // Helper: builds a valid CreateTemplateRequest
    // ──────────────────────────────────────────────────
    private static CreateTemplateRequest MakeCreateRequest(
        string returnCode = "MFCR 300",
        string name = "Statement of Financial Position",
        string? description = "Monthly return for SFP",
        ReturnFrequency frequency = ReturnFrequency.Monthly,
        StructuralCategory category = StructuralCategory.FixedRow,
        string createdBy = "admin") => new()
    {
        ReturnCode = returnCode,
        Name = name,
        Description = description,
        Frequency = frequency,
        StructuralCategory = category,
        CreatedBy = createdBy
    };

    // ──────────────────────────────────────────────────
    // Helper: builds a ReturnTemplate with a Draft version
    // ──────────────────────────────────────────────────
    private static ReturnTemplate MakeTemplate(
        int id = 1,
        string returnCode = "MFCR 300",
        string name = "Statement of Financial Position",
        TemplateStatus versionStatus = TemplateStatus.Draft,
        int versionId = 10,
        int versionNumber = 1)
    {
        var template = new ReturnTemplate
        {
            Id = id,
            ReturnCode = returnCode,
            Name = name,
            Description = "Monthly return for SFP",
            Frequency = ReturnFrequency.Monthly,
            StructuralCategory = StructuralCategory.FixedRow,
            PhysicalTableName = "mfcr_300",
            XmlRootElement = "MFCR300",
            XmlNamespace = "urn:cbn:dfis:fc:mfcr300",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "admin",
            UpdatedAt = DateTime.UtcNow,
            UpdatedBy = "admin"
        };

        var version = new TemplateVersion
        {
            Id = versionId,
            TemplateId = id,
            VersionNumber = versionNumber,
            Status = versionStatus,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "admin"
        };
        template.AddVersion(version);

        return template;
    }

    // ──────────────────────────────────────────────────
    // Helper: builds an AddFieldRequest
    // ──────────────────────────────────────────────────
    private static AddFieldRequest MakeFieldRequest(
        string fieldName = "cash_notes",
        string displayName = "Notes & Coins",
        string xmlElementName = "CashNotes",
        string? lineCode = "10110",
        string? sectionName = "Cash",
        int fieldOrder = 1,
        FieldDataType dataType = FieldDataType.Money,
        bool isRequired = true,
        bool isKeyField = false) => new()
    {
        FieldName = fieldName,
        DisplayName = displayName,
        XmlElementName = xmlElementName,
        LineCode = lineCode,
        SectionName = sectionName,
        FieldOrder = fieldOrder,
        DataType = dataType,
        IsRequired = isRequired,
        IsKeyField = isKeyField,
        MinValue = "0",
        MaxValue = "999999999",
        MaxLength = null,
        AllowedValues = null
    };

    // ══════════════════════════════════════════════════
    //  CreateTemplate
    // ══════════════════════════════════════════════════

    [Fact]
    public async Task CreateTemplate_Success_CreatesTemplateWithDraftVersion()
    {
        // Arrange
        var request = MakeCreateRequest();

        _templateRepo.Setup(r => r.ExistsByReturnCode(request.ReturnCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _templateRepo.Setup(r => r.Add(It.IsAny<ReturnTemplate>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _audit.Setup(a => a.Log(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        var result = await service.CreateTemplate(request);

        // Assert
        result.Should().NotBeNull();
        result.ReturnCode.Should().Be("MFCR 300");
        result.Name.Should().Be("Statement of Financial Position");
        result.Description.Should().Be("Monthly return for SFP");
        result.Frequency.Should().Be("Monthly");
        result.StructuralCategory.Should().Be("FixedRow");

        // Verify template was persisted
        _templateRepo.Verify(
            r => r.Add(It.Is<ReturnTemplate>(t =>
                t.ReturnCode == "MFCR 300" &&
                t.Name == "Statement of Financial Position" &&
                t.Versions.Count == 1 &&
                t.Versions[0].Status == TemplateStatus.Draft &&
                t.Versions[0].VersionNumber == 1),
            It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify audit was called
        _audit.Verify(
            a => a.Log("ReturnTemplate", It.IsAny<int>(), "Created", null, It.IsAny<ReturnTemplate>(), "admin", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateTemplate_DuplicateReturnCode_ThrowsInvalidOperationException()
    {
        // Arrange
        var request = MakeCreateRequest();

        _templateRepo.Setup(r => r.ExistsByReturnCode(request.ReturnCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateService();

        // Act
        var act = () => service.CreateTemplate(request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*MFCR 300*already exists*");

        // Verify Add was never called
        _templateRepo.Verify(
            r => r.Add(It.IsAny<ReturnTemplate>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Verify audit was never called
        _audit.Verify(
            a => a.Log(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateTemplate_SetsCorrectPhysicalTableNameAndXmlProperties()
    {
        // Arrange
        var request = MakeCreateRequest(returnCode: "MFCR 300");

        _templateRepo.Setup(r => r.ExistsByReturnCode(request.ReturnCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _templateRepo.Setup(r => r.Add(It.IsAny<ReturnTemplate>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _audit.Setup(a => a.Log(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ReturnTemplate? capturedTemplate = null;
        _templateRepo.Setup(r => r.Add(It.IsAny<ReturnTemplate>(), It.IsAny<CancellationToken>()))
            .Callback<ReturnTemplate, CancellationToken>((t, _) => capturedTemplate = t)
            .Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        await service.CreateTemplate(request);

        // Assert — verify that ReturnCode.Parse generated the correct derived properties
        capturedTemplate.Should().NotBeNull();
        capturedTemplate!.PhysicalTableName.Should().Be("mfcr_300");
        capturedTemplate.XmlRootElement.Should().Be("MFCR300");
        capturedTemplate.XmlNamespace.Should().Be("urn:cbn:dfis:fc:mfcr300");
    }

    [Fact]
    public async Task CreateTemplate_SetsCreatedByAndTimestamps()
    {
        // Arrange
        var request = MakeCreateRequest(createdBy: "john.doe");

        _templateRepo.Setup(r => r.ExistsByReturnCode(request.ReturnCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        ReturnTemplate? capturedTemplate = null;
        _templateRepo.Setup(r => r.Add(It.IsAny<ReturnTemplate>(), It.IsAny<CancellationToken>()))
            .Callback<ReturnTemplate, CancellationToken>((t, _) => capturedTemplate = t)
            .Returns(Task.CompletedTask);
        _audit.Setup(a => a.Log(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var before = DateTime.UtcNow;
        var service = CreateService();

        // Act
        await service.CreateTemplate(request);

        var after = DateTime.UtcNow;

        // Assert
        capturedTemplate.Should().NotBeNull();
        capturedTemplate!.CreatedBy.Should().Be("john.doe");
        capturedTemplate.UpdatedBy.Should().Be("john.doe");
        capturedTemplate.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        capturedTemplate.UpdatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public async Task CreateTemplate_DraftVersionCreatedBy_MatchesRequestCreatedBy()
    {
        // Arrange
        var request = MakeCreateRequest(createdBy: "seed-user");

        _templateRepo.Setup(r => r.ExistsByReturnCode(request.ReturnCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        ReturnTemplate? capturedTemplate = null;
        _templateRepo.Setup(r => r.Add(It.IsAny<ReturnTemplate>(), It.IsAny<CancellationToken>()))
            .Callback<ReturnTemplate, CancellationToken>((t, _) => capturedTemplate = t)
            .Returns(Task.CompletedTask);
        _audit.Setup(a => a.Log(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        await service.CreateTemplate(request);

        // Assert — the auto-created draft version should carry the same CreatedBy
        capturedTemplate.Should().NotBeNull();
        var draftVersion = capturedTemplate!.Versions.Single();
        draftVersion.Status.Should().Be(TemplateStatus.Draft);
        draftVersion.VersionNumber.Should().Be(1);
        draftVersion.CreatedBy.Should().Be("seed-user");
    }

    [Fact]
    public async Task CreateTemplate_UsesCurrentTenantAndRequestedModule()
    {
        var request = MakeCreateRequest();
        var tenantId = Guid.NewGuid();
        request.ModuleId = 42;

        _tenantContext.SetupGet(t => t.CurrentTenantId).Returns(tenantId);
        _templateRepo.Setup(r => r.ExistsByReturnCode(request.ReturnCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        ReturnTemplate? capturedTemplate = null;
        _templateRepo.Setup(r => r.Add(It.IsAny<ReturnTemplate>(), It.IsAny<CancellationToken>()))
            .Callback<ReturnTemplate, CancellationToken>((t, _) => capturedTemplate = t)
            .Returns(Task.CompletedTask);
        _audit.Setup(a => a.Log(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();

        await service.CreateTemplate(request);

        capturedTemplate.Should().NotBeNull();
        capturedTemplate!.TenantId.Should().Be(tenantId);
        capturedTemplate.ModuleId.Should().Be(42);
    }

    [Theory]
    [InlineData("MFCR 300", "mfcr_300", "MFCR300", "urn:cbn:dfis:fc:mfcr300")]
    [InlineData("QFCR 100", "qfcr_100", "QFCR100", "urn:cbn:dfis:fc:qfcr100")]
    [InlineData("SFCR 200", "sfcr_200", "SFCR200", "urn:cbn:dfis:fc:sfcr200")]
    public async Task CreateTemplate_VariousReturnCodes_GeneratesCorrectDerivedValues(
        string returnCode, string expectedTable, string expectedXmlRoot, string expectedXmlNamespace)
    {
        // Arrange
        var request = MakeCreateRequest(returnCode: returnCode);

        _templateRepo.Setup(r => r.ExistsByReturnCode(request.ReturnCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        ReturnTemplate? capturedTemplate = null;
        _templateRepo.Setup(r => r.Add(It.IsAny<ReturnTemplate>(), It.IsAny<CancellationToken>()))
            .Callback<ReturnTemplate, CancellationToken>((t, _) => capturedTemplate = t)
            .Returns(Task.CompletedTask);
        _audit.Setup(a => a.Log(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        await service.CreateTemplate(request);

        // Assert
        capturedTemplate.Should().NotBeNull();
        capturedTemplate!.PhysicalTableName.Should().Be(expectedTable);
        capturedTemplate.XmlRootElement.Should().Be(expectedXmlRoot);
        capturedTemplate.XmlNamespace.Should().Be(expectedXmlNamespace);
    }

    // ══════════════════════════════════════════════════
    //  GetTemplateDetail
    // ══════════════════════════════════════════════════

    [Fact]
    public async Task GetTemplateDetail_NonExistentReturnCode_ReturnsNull()
    {
        // Arrange
        _templateRepo.Setup(r => r.GetByReturnCode("MFCR 999", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReturnTemplate?)null);

        var service = CreateService();

        // Act
        var result = await service.GetTemplateDetail("MFCR 999");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetTemplateDetail_ExistingTemplate_ReturnsFullDetailWithVersionsAndFields()
    {
        // Arrange — build template with a published version containing fields and item codes
        var template = new ReturnTemplate
        {
            Id = 5,
            ReturnCode = "MFCR 300",
            Name = "Statement of Financial Position",
            Description = "Monthly SFP return",
            Frequency = ReturnFrequency.Monthly,
            StructuralCategory = StructuralCategory.FixedRow,
            PhysicalTableName = "mfcr_300",
            XmlRootElement = "MFCR300",
            XmlNamespace = "urn:cbn:dfis:fc:mfcr300",
            CreatedAt = new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            CreatedBy = "admin",
            UpdatedAt = DateTime.UtcNow,
            UpdatedBy = "admin"
        };

        var publishedVersion = new TemplateVersion
        {
            Id = 20,
            TemplateId = 5,
            VersionNumber = 1,
            Status = TemplateStatus.Published,
            PublishedAt = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            ApprovedBy = "supervisor",
            ChangeSummary = "Initial release",
            CreatedAt = new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            CreatedBy = "admin"
        };
        publishedVersion.AddField(new TemplateField
        {
            Id = 100,
            FieldName = "cash_notes",
            DisplayName = "Notes",
            XmlElementName = "CashNotes",
            LineCode = "10110",
            SectionName = "Cash",
            FieldOrder = 1,
            DataType = FieldDataType.Money,
            SqlType = "DECIMAL(18,2)",
            IsRequired = true,
            IsKeyField = false
        });
        publishedVersion.AddField(new TemplateField
        {
            Id = 101,
            FieldName = "cash_coins",
            DisplayName = "Coins",
            XmlElementName = "CashCoins",
            LineCode = "10120",
            SectionName = "Cash",
            FieldOrder = 2,
            DataType = FieldDataType.Money,
            SqlType = "DECIMAL(18,2)",
            IsRequired = true,
            IsKeyField = false
        });
        publishedVersion.AddItemCode(new TemplateItemCode
        {
            Id = 50,
            ItemCode = "10110",
            ItemDescription = "Notes & Coins",
            SortOrder = 1
        });

        var draftVersion = new TemplateVersion
        {
            Id = 21,
            TemplateId = 5,
            VersionNumber = 2,
            Status = TemplateStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "admin"
        };

        template.AddVersion(publishedVersion);
        template.AddVersion(draftVersion);

        _templateRepo.Setup(r => r.GetByReturnCode("MFCR 300", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var service = CreateService();

        // Act
        var result = await service.GetTemplateDetail("MFCR 300");

        // Assert — top-level DTO properties
        result.Should().NotBeNull();
        result!.Id.Should().Be(5);
        result.ReturnCode.Should().Be("MFCR 300");
        result.Name.Should().Be("Statement of Financial Position");
        result.Description.Should().Be("Monthly SFP return");
        result.Frequency.Should().Be("Monthly");
        result.StructuralCategory.Should().Be("FixedRow");
        result.PhysicalTableName.Should().Be("mfcr_300");
        result.XmlRootElement.Should().Be("MFCR300");
        result.XmlNamespace.Should().Be("urn:cbn:dfis:fc:mfcr300");
        result.PublishedVersionId.Should().Be(20);
        result.PublishedVersionNumber.Should().Be(1);
        result.FieldCount.Should().Be(2);

        // Assert — versions (ordered descending by version number)
        result.Versions.Should().HaveCount(2);
        result.Versions[0].VersionNumber.Should().Be(2); // Draft (v2) should appear first
        result.Versions[0].Status.Should().Be("Draft");
        result.Versions[1].VersionNumber.Should().Be(1); // Published (v1) second
        result.Versions[1].Status.Should().Be("Published");

        // Assert — fields on published version
        var publishedDto = result.Versions.Single(v => v.Status == "Published");
        publishedDto.FieldCount.Should().Be(2);
        publishedDto.Fields.Should().HaveCount(2);
        publishedDto.Fields[0].FieldName.Should().Be("cash_notes");
        publishedDto.Fields[0].DataType.Should().Be("Money");
        publishedDto.Fields[0].SqlType.Should().Be("DECIMAL(18,2)");
        publishedDto.Fields[0].LineCode.Should().Be("10110");
        publishedDto.Fields[1].FieldName.Should().Be("cash_coins");

        // Assert — item codes
        publishedDto.ItemCodes.Should().HaveCount(1);
        publishedDto.ItemCodes[0].ItemCode.Should().Be("10110");
        publishedDto.ItemCodes[0].ItemName.Should().Be("Notes & Coins");
        publishedDto.ItemCodes[0].SortOrder.Should().Be(1);
    }

    [Fact]
    public async Task GetTemplateDetail_NoPublishedVersion_PublishedFieldsAreNull()
    {
        // Arrange — template only has a draft version, no published
        var template = MakeTemplate(versionStatus: TemplateStatus.Draft, versionId: 10);

        _templateRepo.Setup(r => r.GetByReturnCode("MFCR 300", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var service = CreateService();

        // Act
        var result = await service.GetTemplateDetail("MFCR 300");

        // Assert
        result.Should().NotBeNull();
        result!.PublishedVersionId.Should().BeNull();
        result.PublishedVersionNumber.Should().BeNull();
        result.FieldCount.Should().Be(0);
    }

    // ══════════════════════════════════════════════════
    //  GetAllTemplates
    // ══════════════════════════════════════════════════

    [Fact]
    public async Task GetAllTemplates_NoTemplatesExist_ReturnsEmptyList()
    {
        // Arrange
        _templateRepo.Setup(r => r.GetAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReturnTemplate>());

        var service = CreateService();

        // Act
        var result = await service.GetAllTemplates();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllTemplates_MultipleTemplates_ReturnsMappedDtos()
    {
        // Arrange
        var template1 = new ReturnTemplate
        {
            Id = 1,
            ReturnCode = "MFCR 300",
            Name = "Statement of Financial Position",
            Frequency = ReturnFrequency.Monthly,
            StructuralCategory = StructuralCategory.FixedRow,
            PhysicalTableName = "mfcr_300",
            CreatedAt = DateTime.UtcNow
        };

        var template2 = new ReturnTemplate
        {
            Id = 2,
            ReturnCode = "QFCR 100",
            Name = "Quarterly Capital Adequacy",
            Description = "Quarterly return",
            Frequency = ReturnFrequency.Quarterly,
            StructuralCategory = StructuralCategory.MultiRow,
            PhysicalTableName = "qfcr_100",
            CreatedAt = DateTime.UtcNow
        };
        // Add a published version to template2 so we can verify published-version mapping
        var publishedVersion = new TemplateVersion
        {
            Id = 30,
            TemplateId = 2,
            VersionNumber = 1,
            Status = TemplateStatus.Published,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "admin"
        };
        publishedVersion.AddField(new TemplateField
        {
            FieldName = "capital_ratio",
            DisplayName = "Capital Ratio",
            DataType = FieldDataType.Percentage,
            SqlType = "DECIMAL(5,2)",
            FieldOrder = 1
        });
        template2.AddVersion(publishedVersion);

        _templateRepo.Setup(r => r.GetAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReturnTemplate> { template1, template2 });

        var service = CreateService();

        // Act
        var result = await service.GetAllTemplates();

        // Assert
        result.Should().HaveCount(2);

        // First template — no published version
        var dto1 = result.Single(t => t.ReturnCode == "MFCR 300");
        dto1.Id.Should().Be(1);
        dto1.Name.Should().Be("Statement of Financial Position");
        dto1.Frequency.Should().Be("Monthly");
        dto1.StructuralCategory.Should().Be("FixedRow");
        dto1.PhysicalTableName.Should().Be("mfcr_300");
        dto1.PublishedVersionId.Should().BeNull();
        dto1.PublishedVersionNumber.Should().BeNull();
        dto1.FieldCount.Should().Be(0);

        // Second template — has published version with 1 field
        var dto2 = result.Single(t => t.ReturnCode == "QFCR 100");
        dto2.Id.Should().Be(2);
        dto2.Name.Should().Be("Quarterly Capital Adequacy");
        dto2.Description.Should().Be("Quarterly return");
        dto2.Frequency.Should().Be("Quarterly");
        dto2.StructuralCategory.Should().Be("MultiRow");
        dto2.PublishedVersionId.Should().Be(30);
        dto2.PublishedVersionNumber.Should().Be(1);
        dto2.FieldCount.Should().Be(1);
    }

    // ══════════════════════════════════════════════════
    //  AddFieldToVersion
    // ══════════════════════════════════════════════════

    [Fact]
    public async Task AddFieldToVersion_DraftVersion_SuccessfullyAddsField()
    {
        // Arrange
        var template = MakeTemplate(id: 1, versionId: 10, versionStatus: TemplateStatus.Draft);
        var fieldRequest = MakeFieldRequest();

        _templateRepo.Setup(r => r.GetById(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _templateRepo.Setup(r => r.Update(It.IsAny<ReturnTemplate>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _audit.Setup(a => a.Log(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _sqlTypeMapper.Setup(m => m.MapToSqlType(FieldDataType.Money, null))
            .Returns("DECIMAL(18,2)");

        var service = CreateService();

        // Act
        await service.AddFieldToVersion(1, 10, fieldRequest, "admin");

        // Assert — field was added to the version
        var version = template.GetVersion(10);
        version.Fields.Should().HaveCount(1);

        var field = version.Fields[0];
        field.FieldName.Should().Be("cash_notes");
        field.DisplayName.Should().Be("Notes & Coins");
        field.XmlElementName.Should().Be("CashNotes");
        field.LineCode.Should().Be("10110");
        field.SectionName.Should().Be("Cash");
        field.FieldOrder.Should().Be(1);
        field.DataType.Should().Be(FieldDataType.Money);
        field.SqlType.Should().Be("DECIMAL(18,2)");
        field.IsRequired.Should().BeTrue();
        field.IsKeyField.Should().BeFalse();
        field.MinValue.Should().Be("0");
        field.MaxValue.Should().Be("999999999");

        // Verify repo was updated
        _templateRepo.Verify(r => r.Update(template, It.IsAny<CancellationToken>()), Times.Once);

        // Verify audit
        _audit.Verify(a => a.Log("TemplateField", It.IsAny<int>(), "Added", null, It.IsAny<TemplateField>(), "admin", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddFieldToVersion_TemplateNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        _templateRepo.Setup(r => r.GetById(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReturnTemplate?)null);

        var service = CreateService();

        // Act
        var act = () => service.AddFieldToVersion(999, 1, MakeFieldRequest(), "admin");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*999*not found*");

        // Verify no update or audit occurred
        _templateRepo.Verify(r => r.Update(It.IsAny<ReturnTemplate>(), It.IsAny<CancellationToken>()), Times.Never);
        _audit.Verify(
            a => a.Log(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(TemplateStatus.Published)]
    [InlineData(TemplateStatus.Review)]
    public async Task AddFieldToVersion_NonDraftVersion_ThrowsInvalidOperationException(TemplateStatus status)
    {
        // Arrange
        var template = MakeTemplate(id: 1, versionId: 10, versionStatus: status);

        _templateRepo.Setup(r => r.GetById(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var service = CreateService();

        // Act
        var act = () => service.AddFieldToVersion(1, 10, MakeFieldRequest(), "admin");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Draft*");

        // Verify no update or audit occurred
        _templateRepo.Verify(r => r.Update(It.IsAny<ReturnTemplate>(), It.IsAny<CancellationToken>()), Times.Never);
        _audit.Verify(
            a => a.Log(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AddFieldToVersion_DeprecatedVersion_ThrowsInvalidOperationException()
    {
        // Arrange
        var template = MakeTemplate(id: 1, versionId: 10, versionStatus: TemplateStatus.Deprecated);

        _templateRepo.Setup(r => r.GetById(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var service = CreateService();

        // Act
        var act = () => service.AddFieldToVersion(1, 10, MakeFieldRequest(), "admin");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Draft*");
    }

    [Theory]
    [InlineData(FieldDataType.Text, "NVARCHAR(500)")]
    [InlineData(FieldDataType.Integer, "INT")]
    [InlineData(FieldDataType.Money, "DECIMAL(18,2)")]
    [InlineData(FieldDataType.Decimal, "DECIMAL(18,4)")]
    [InlineData(FieldDataType.Percentage, "DECIMAL(5,2)")]
    [InlineData(FieldDataType.Date, "DATE")]
    [InlineData(FieldDataType.Boolean, "BIT")]
    public async Task AddFieldToVersion_MapsDataTypeToSqlTypeCorrectly(FieldDataType dataType, string expectedSqlType)
    {
        // Arrange
        var template = MakeTemplate(id: 1, versionId: 10, versionStatus: TemplateStatus.Draft);
        var fieldRequest = MakeFieldRequest(dataType: dataType);

        _templateRepo.Setup(r => r.GetById(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _templateRepo.Setup(r => r.Update(It.IsAny<ReturnTemplate>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _audit.Setup(a => a.Log(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _sqlTypeMapper.Setup(m => m.MapToSqlType(dataType, null))
            .Returns(expectedSqlType);

        var service = CreateService();

        // Act
        await service.AddFieldToVersion(1, 10, fieldRequest, "admin");

        // Assert
        var field = template.GetVersion(10).Fields.Single();
        field.DataType.Should().Be(dataType);
        field.SqlType.Should().Be(expectedSqlType);

        // Verify mapper was called with the correct data type
        _sqlTypeMapper.Verify(m => m.MapToSqlType(dataType, null), Times.Once);
    }

    [Fact]
    public async Task AddFieldToVersion_AllFieldPropertiesMappedCorrectly()
    {
        // Arrange
        var template = MakeTemplate(id: 1, versionId: 10, versionStatus: TemplateStatus.Draft);
        var fieldRequest = new AddFieldRequest
        {
            FieldName = "institution_code",
            DisplayName = "Institution Code",
            XmlElementName = "InstitutionCode",
            LineCode = "50010",
            SectionName = "Identifiers",
            FieldOrder = 1,
            DataType = FieldDataType.Text,
            IsRequired = true,
            IsKeyField = true,
            MinValue = null,
            MaxValue = null,
            MaxLength = 20,
            AllowedValues = "DMB,MFB,PMB"
        };

        _templateRepo.Setup(r => r.GetById(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _templateRepo.Setup(r => r.Update(It.IsAny<ReturnTemplate>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _audit.Setup(a => a.Log(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _sqlTypeMapper.Setup(m => m.MapToSqlType(FieldDataType.Text, null))
            .Returns("NVARCHAR(500)");

        var service = CreateService();

        // Act
        await service.AddFieldToVersion(1, 10, fieldRequest, "operator");

        // Assert
        var field = template.GetVersion(10).Fields.Single();
        field.FieldName.Should().Be("institution_code");
        field.DisplayName.Should().Be("Institution Code");
        field.XmlElementName.Should().Be("InstitutionCode");
        field.LineCode.Should().Be("50010");
        field.SectionName.Should().Be("Identifiers");
        field.FieldOrder.Should().Be(1);
        field.DataType.Should().Be(FieldDataType.Text);
        field.SqlType.Should().Be("NVARCHAR(500)");
        field.IsRequired.Should().BeTrue();
        field.IsKeyField.Should().BeTrue();
        field.MinValue.Should().BeNull();
        field.MaxValue.Should().BeNull();
        field.MaxLength.Should().Be(20);
        field.AllowedValues.Should().Be("DMB,MFB,PMB");
    }

    [Fact]
    public async Task AddFieldToVersion_SetsTemplateVersionIdOnField()
    {
        // Arrange
        var template = MakeTemplate(id: 1, versionId: 10, versionStatus: TemplateStatus.Draft);

        _templateRepo.Setup(r => r.GetById(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _templateRepo.Setup(r => r.Update(It.IsAny<ReturnTemplate>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _audit.Setup(a => a.Log(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _sqlTypeMapper.Setup(m => m.MapToSqlType(It.IsAny<FieldDataType>(), null))
            .Returns("DECIMAL(18,2)");

        var service = CreateService();

        // Act
        await service.AddFieldToVersion(1, 10, MakeFieldRequest(), "admin");

        // Assert — AddField on TemplateVersion sets the TemplateVersionId
        var field = template.GetVersion(10).Fields.Single();
        field.TemplateVersionId.Should().Be(10);
    }

    [Fact]
    public async Task AddFieldToVersion_MultipleFields_AllAddedCorrectly()
    {
        // Arrange
        var template = MakeTemplate(id: 1, versionId: 10, versionStatus: TemplateStatus.Draft);

        _templateRepo.Setup(r => r.GetById(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _templateRepo.Setup(r => r.Update(It.IsAny<ReturnTemplate>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _audit.Setup(a => a.Log(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _sqlTypeMapper.Setup(m => m.MapToSqlType(It.IsAny<FieldDataType>(), null))
            .Returns("DECIMAL(18,2)");

        var service = CreateService();

        // Act — add two fields sequentially
        var request1 = MakeFieldRequest(fieldName: "cash_notes", fieldOrder: 1);
        var request2 = MakeFieldRequest(fieldName: "cash_coins", displayName: "Coins", fieldOrder: 2);

        await service.AddFieldToVersion(1, 10, request1, "admin");
        await service.AddFieldToVersion(1, 10, request2, "admin");

        // Assert
        var version = template.GetVersion(10);
        version.Fields.Should().HaveCount(2);
        version.Fields[0].FieldName.Should().Be("cash_notes");
        version.Fields[1].FieldName.Should().Be("cash_coins");

        // Verify repo update was called twice
        _templateRepo.Verify(r => r.Update(template, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task AddFieldToVersion_AuditLogCalledWithCorrectPerformedBy()
    {
        // Arrange
        var template = MakeTemplate(id: 1, versionId: 10, versionStatus: TemplateStatus.Draft);

        _templateRepo.Setup(r => r.GetById(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _templateRepo.Setup(r => r.Update(It.IsAny<ReturnTemplate>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _audit.Setup(a => a.Log(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _sqlTypeMapper.Setup(m => m.MapToSqlType(It.IsAny<FieldDataType>(), null))
            .Returns("DECIMAL(18,2)");

        var service = CreateService();

        // Act
        await service.AddFieldToVersion(1, 10, MakeFieldRequest(), "supervisor.jones");

        // Assert
        _audit.Verify(
            a => a.Log(
                "TemplateField",
                It.IsAny<int>(),
                "Added",
                null,
                It.IsAny<TemplateField>(),
                "supervisor.jones",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateFieldInVersion_DraftVersion_UpdatesExistingField()
    {
        var template = MakeTemplate(id: 1, versionId: 10, versionStatus: TemplateStatus.Draft);
        template.GetVersion(10).AddField(new TemplateField
        {
            Id = 55,
            FieldName = "cash_notes",
            DisplayName = "Notes",
            XmlElementName = "CashNotes",
            DataType = FieldDataType.Money,
            SqlType = "DECIMAL(18,2)",
            FieldOrder = 1
        });

        var request = MakeFieldRequest(
            fieldName: "cash_balance",
            displayName: "Cash Balance",
            xmlElementName: "CashBalance",
            fieldOrder: 2,
            dataType: FieldDataType.Decimal);

        _templateRepo.Setup(r => r.GetById(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _templateRepo.Setup(r => r.Update(It.IsAny<ReturnTemplate>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _audit.Setup(a => a.Log(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _sqlTypeMapper.Setup(m => m.MapToSqlType(FieldDataType.Decimal, null))
            .Returns("DECIMAL(18,4)");

        var service = CreateService();

        await service.UpdateFieldInVersion(1, 10, 55, request, "editor");

        var field = template.GetVersion(10).GetField(55);
        field.FieldName.Should().Be("cash_balance");
        field.DisplayName.Should().Be("Cash Balance");
        field.XmlElementName.Should().Be("CashBalance");
        field.FieldOrder.Should().Be(2);
        field.DataType.Should().Be(FieldDataType.Decimal);
        field.SqlType.Should().Be("DECIMAL(18,4)");

        _templateRepo.Verify(r => r.Update(template, It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.Log("TemplateField", 55, "Updated", It.IsAny<object>(), It.IsAny<object>(), "editor", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveFieldFromVersion_DraftVersion_RemovesField()
    {
        var template = MakeTemplate(id: 1, versionId: 10, versionStatus: TemplateStatus.Draft);
        template.GetVersion(10).AddField(new TemplateField
        {
            Id = 55,
            FieldName = "cash_notes",
            DisplayName = "Notes",
            XmlElementName = "CashNotes",
            DataType = FieldDataType.Money,
            SqlType = "DECIMAL(18,2)",
            FieldOrder = 1
        });

        _templateRepo.Setup(r => r.GetById(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _templateRepo.Setup(r => r.Update(It.IsAny<ReturnTemplate>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _audit.Setup(a => a.Log(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();

        await service.RemoveFieldFromVersion(1, 10, 55, "editor");

        template.GetVersion(10).Fields.Should().BeEmpty();
        _templateRepo.Verify(r => r.Update(template, It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.Log("TemplateField", 55, "Removed", It.IsAny<object>(), null, "editor", It.IsAny<CancellationToken>()), Times.Once);
    }
}
