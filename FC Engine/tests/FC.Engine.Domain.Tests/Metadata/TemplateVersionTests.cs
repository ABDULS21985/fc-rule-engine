using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FluentAssertions;

namespace FC.Engine.Domain.Tests.Metadata;

public class TemplateVersionTests
{
    [Fact]
    public void SubmitForReview_DraftVersion_ShouldTransitionToReview()
    {
        var version = CreateDraftVersion();
        version.SubmitForReview();

        version.Status.Should().Be(TemplateStatus.Review);
    }

    [Fact]
    public void SubmitForReview_NonDraftVersion_ShouldThrow()
    {
        var version = CreateDraftVersion();
        version.SubmitForReview();

        var act = () => version.SubmitForReview();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Draft*");
    }

    [Fact]
    public void Publish_ReviewVersion_ShouldTransitionToPublished()
    {
        var version = CreateDraftVersion();
        version.SubmitForReview();
        var publishTime = DateTime.UtcNow;

        version.Publish(publishTime, "admin");

        version.Status.Should().Be(TemplateStatus.Published);
        version.PublishedAt.Should().Be(publishTime);
        version.ApprovedBy.Should().Be("admin");
        version.EffectiveFrom.Should().NotBeNull();
    }

    [Fact]
    public void Publish_DraftVersion_ShouldThrow()
    {
        var version = CreateDraftVersion();

        var act = () => version.Publish(DateTime.UtcNow, "admin");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Review*");
    }

    [Fact]
    public void Deprecate_ShouldSetStatusAndEffectiveTo()
    {
        var version = CreateDraftVersion();
        version.SubmitForReview();
        version.Publish(DateTime.UtcNow, "admin");

        version.Deprecate();

        version.Status.Should().Be(TemplateStatus.Deprecated);
        version.EffectiveTo.Should().NotBeNull();
    }

    [Fact]
    public void AddField_ShouldAppendToFieldsList()
    {
        var version = CreateDraftVersion();
        var field = new TemplateField
        {
            FieldName = "cash_notes",
            DisplayName = "Cash Notes",
            DataType = FieldDataType.Money,
            SqlType = "NUMERIC(20,2)"
        };

        version.AddField(field);

        version.Fields.Should().HaveCount(1);
        version.Fields[0].FieldName.Should().Be("cash_notes");
    }

    [Fact]
    public void AddFormula_ShouldAppendToFormulasList()
    {
        var version = CreateDraftVersion();
        var formula = new IntraSheetFormula
        {
            RuleCode = "SUM-001",
            FormulaType = FormulaType.Sum,
            TargetFieldName = "total_assets"
        };

        version.AddFormula(formula);

        version.IntraSheetFormulas.Should().HaveCount(1);
        version.IntraSheetFormulas[0].RuleCode.Should().Be("SUM-001");
    }

    [Fact]
    public void SetDdlScript_ShouldStoreScripts()
    {
        var version = CreateDraftVersion();
        version.SetDdlScript("CREATE TABLE...", "DROP TABLE...");

        version.DdlScript.Should().Be("CREATE TABLE...");
        version.RollbackScript.Should().Be("DROP TABLE...");
    }

    [Fact]
    public void SetFields_ShouldReplaceAllFields()
    {
        var version = CreateDraftVersion();
        version.AddField(new TemplateField { FieldName = "old" });

        version.SetFields(new[]
        {
            new TemplateField { FieldName = "new1" },
            new TemplateField { FieldName = "new2" }
        });

        version.Fields.Should().HaveCount(2);
        version.Fields.Select(f => f.FieldName).Should().Contain("new1", "new2");
    }

    private static TemplateVersion CreateDraftVersion()
    {
        return new TemplateVersion
        {
            Id = 1,
            TemplateId = 1,
            VersionNumber = 1,
            Status = TemplateStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "system"
        };
    }
}

public class ReturnTemplateTests
{
    [Fact]
    public void CreateDraftVersion_ShouldCreateVersionOne()
    {
        var template = new ReturnTemplate
        {
            Id = 1,
            ReturnCode = "MFCR 300",
            Name = "MFCR 300",
            StructuralCategory = StructuralCategory.FixedRow,
            PhysicalTableName = "mfcr_300"
        };

        var version = template.CreateDraftVersion("admin");

        version.VersionNumber.Should().Be(1);
        version.Status.Should().Be(TemplateStatus.Draft);
        version.CreatedBy.Should().Be("admin");
        template.Versions.Should().HaveCount(1);
    }

    [Fact]
    public void CreateDraftVersion_SecondTime_ShouldIncrementVersion()
    {
        var template = new ReturnTemplate { Id = 1, ReturnCode = "MFCR 300" };
        template.CreateDraftVersion("admin");
        var v2 = template.CreateDraftVersion("admin");

        v2.VersionNumber.Should().Be(2);
        template.Versions.Should().HaveCount(2);
    }

    [Fact]
    public void CurrentPublishedVersion_ShouldReturnPublishedVersion()
    {
        var template = new ReturnTemplate { Id = 1, ReturnCode = "MFCR 300" };
        var version = template.CreateDraftVersion("admin");
        version.SubmitForReview();
        version.Publish(DateTime.UtcNow, "admin");

        template.CurrentPublishedVersion.Should().BeSameAs(version);
    }

    [Fact]
    public void CurrentPublishedVersion_NonePublished_ShouldReturnNull()
    {
        var template = new ReturnTemplate { Id = 1, ReturnCode = "MFCR 300" };
        template.CreateDraftVersion("admin");

        template.CurrentPublishedVersion.Should().BeNull();
    }
}
