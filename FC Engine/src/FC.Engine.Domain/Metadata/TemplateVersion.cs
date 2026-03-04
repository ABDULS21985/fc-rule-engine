using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Metadata;

public class TemplateVersion
{
    public int Id { get; set; }
    public Guid? TenantId { get; set; }
    public int TemplateId { get; set; }
    public int VersionNumber { get; set; }
    public TemplateStatus Status { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public string? ChangeSummary { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? DdlScript { get; set; }
    public string? RollbackScript { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;

    private readonly List<TemplateField> _fields = new();
    public IReadOnlyList<TemplateField> Fields => _fields.AsReadOnly();

    private readonly List<TemplateItemCode> _itemCodes = new();
    public IReadOnlyList<TemplateItemCode> ItemCodes => _itemCodes.AsReadOnly();

    private readonly List<IntraSheetFormula> _intraSheetFormulas = new();
    public IReadOnlyList<IntraSheetFormula> IntraSheetFormulas => _intraSheetFormulas.AsReadOnly();

    public void AddField(TemplateField field)
    {
        field.TemplateVersionId = Id;
        _fields.Add(field);
    }

    public void AddItemCode(TemplateItemCode itemCode)
    {
        itemCode.TemplateVersionId = Id;
        _itemCodes.Add(itemCode);
    }

    public void AddFormula(IntraSheetFormula formula)
    {
        formula.TemplateVersionId = Id;
        _intraSheetFormulas.Add(formula);
    }

    public void SubmitForReview()
    {
        if (Status != TemplateStatus.Draft)
            throw new InvalidOperationException("Only Draft versions can be submitted for review");
        Status = TemplateStatus.Review;
    }

    public void Publish(DateTime publishedAt, string approvedBy)
    {
        if (Status != TemplateStatus.Review)
            throw new InvalidOperationException("Only versions in Review status can be published");
        Status = TemplateStatus.Published;
        PublishedAt = publishedAt;
        ApprovedBy = approvedBy;
        ApprovedAt = publishedAt;
        EffectiveFrom = DateTime.UtcNow.Date;
    }

    public void Deprecate()
    {
        Status = TemplateStatus.Deprecated;
        EffectiveTo = DateTime.UtcNow.Date;
    }

    public void SetDdlScript(string forwardSql, string rollbackSql)
    {
        DdlScript = forwardSql;
        RollbackScript = rollbackSql;
    }

    public void SetFields(IEnumerable<TemplateField> fields)
    {
        _fields.Clear();
        _fields.AddRange(fields);
    }

    public void SetItemCodes(IEnumerable<TemplateItemCode> itemCodes)
    {
        _itemCodes.Clear();
        _itemCodes.AddRange(itemCodes);
    }

    public void SetFormulas(IEnumerable<IntraSheetFormula> formulas)
    {
        _intraSheetFormulas.Clear();
        _intraSheetFormulas.AddRange(formulas);
    }
}
