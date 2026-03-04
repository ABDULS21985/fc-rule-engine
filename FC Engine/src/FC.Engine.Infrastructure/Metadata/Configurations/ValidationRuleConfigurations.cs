using FC.Engine.Domain.Metadata;
using FC.Engine.Domain.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public class IntraSheetFormulaConfiguration : IEntityTypeConfiguration<IntraSheetFormula>
{
    public void Configure(EntityTypeBuilder<IntraSheetFormula> builder)
    {
        builder.ToTable("intra_sheet_formulas", "meta");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.RuleCode).HasMaxLength(50).IsRequired();
        builder.Property(f => f.RuleName).HasMaxLength(255).IsRequired();
        builder.Property(f => f.FormulaType).HasMaxLength(30).IsRequired()
            .HasConversion<string>();
        builder.Property(f => f.TargetFieldName).HasMaxLength(128).IsRequired();
        builder.Property(f => f.TargetLineCode).HasMaxLength(20);
        builder.Property(f => f.OperandFields).IsRequired();
        builder.Property(f => f.CustomExpression).HasMaxLength(1000);
        builder.Property(f => f.ToleranceAmount).HasColumnType("decimal(20,2)");
        builder.Property(f => f.TolerancePercent).HasColumnType("decimal(10,4)");
        builder.Property(f => f.Severity).HasMaxLength(10).IsRequired()
            .HasConversion<string>();
        builder.Property(f => f.ErrorMessage).HasMaxLength(500);
        builder.Property(f => f.CreatedBy).HasMaxLength(100).IsRequired();

        builder.HasIndex(f => new { f.TemplateVersionId, f.RuleCode }).IsUnique();
    }
}

public class CrossSheetRuleConfiguration : IEntityTypeConfiguration<CrossSheetRule>
{
    public void Configure(EntityTypeBuilder<CrossSheetRule> builder)
    {
        builder.ToTable("cross_sheet_rules", "meta");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.RuleCode).HasMaxLength(50).IsRequired();
        builder.Property(r => r.RuleName).HasMaxLength(255).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(1000);
        builder.Property(r => r.Severity).HasMaxLength(10).IsRequired()
            .HasConversion<string>();
        builder.Property(r => r.CreatedBy).HasMaxLength(100).IsRequired();

        builder.HasIndex(r => r.RuleCode).IsUnique();
        builder.HasIndex(r => r.TenantId);

        builder.HasMany(r => r.Operands)
            .WithOne()
            .HasForeignKey(o => o.RuleId);

        builder.HasOne(r => r.Expression)
            .WithOne()
            .HasForeignKey<CrossSheetRuleExpression>(e => e.RuleId);
    }
}

public class CrossSheetRuleOperandConfiguration : IEntityTypeConfiguration<CrossSheetRuleOperand>
{
    public void Configure(EntityTypeBuilder<CrossSheetRuleOperand> builder)
    {
        builder.ToTable("cross_sheet_rule_operands", "meta");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.OperandAlias).HasMaxLength(10).IsRequired();
        builder.Property(o => o.TemplateReturnCode).HasMaxLength(20).IsRequired();
        builder.Property(o => o.FieldName).HasMaxLength(128).IsRequired();
        builder.Property(o => o.LineCode).HasMaxLength(20);
        builder.Property(o => o.AggregateFunction).HasMaxLength(20);
        builder.Property(o => o.FilterItemCode).HasMaxLength(20);

        builder.HasIndex(o => new { o.RuleId, o.OperandAlias }).IsUnique();
    }
}

public class CrossSheetRuleExpressionConfiguration : IEntityTypeConfiguration<CrossSheetRuleExpression>
{
    public void Configure(EntityTypeBuilder<CrossSheetRuleExpression> builder)
    {
        builder.ToTable("cross_sheet_rule_expressions", "meta");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Expression).HasMaxLength(1000).IsRequired();
        builder.Property(e => e.ToleranceAmount).HasColumnType("decimal(20,2)");
        builder.Property(e => e.TolerancePercent).HasColumnType("decimal(10,4)");
        builder.Property(e => e.ErrorMessage).HasMaxLength(500);

        builder.HasIndex(e => e.RuleId).IsUnique();
    }
}

public class BusinessRuleConfiguration : IEntityTypeConfiguration<BusinessRule>
{
    public void Configure(EntityTypeBuilder<BusinessRule> builder)
    {
        builder.ToTable("business_rules", "meta");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.RuleCode).HasMaxLength(50).IsRequired();
        builder.Property(r => r.RuleName).HasMaxLength(255).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(1000);
        builder.Property(r => r.RuleType).HasMaxLength(30).IsRequired();
        builder.Property(r => r.Expression).HasMaxLength(1000);
        builder.Property(r => r.Severity).HasMaxLength(10).IsRequired()
            .HasConversion<string>();
        builder.Property(r => r.CreatedBy).HasMaxLength(100).IsRequired();

        builder.HasIndex(r => r.RuleCode).IsUnique();
        builder.HasIndex(r => r.TenantId);
    }
}
