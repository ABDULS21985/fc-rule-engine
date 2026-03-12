using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Portal.Services;

public class KnowledgeBaseService
{
    private readonly IDbContextFactory<MetadataDbContext> _dbFactory;

    public KnowledgeBaseService(IDbContextFactory<MetadataDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<IReadOnlyList<KnowledgeBaseArticleView>> Search(
        string? query,
        string? moduleCode,
        string? category,
        int take = 100,
        CancellationToken ct = default)
    {
        await EnsureSeedData(ct);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var normalizedQuery = (query ?? string.Empty).Trim();
        var normalizedModule = Normalize(moduleCode);
        var normalizedCategory = Normalize(category);

        var articlesQuery = db.KnowledgeBaseArticles
            .AsNoTracking()
            .Where(x => x.IsPublished);

        if (!string.IsNullOrWhiteSpace(normalizedModule))
        {
            articlesQuery = articlesQuery.Where(x => x.ModuleCode == normalizedModule || x.ModuleCode == null);
        }

        if (!string.IsNullOrWhiteSpace(normalizedCategory))
        {
            articlesQuery = articlesQuery.Where(x => x.Category == normalizedCategory);
        }

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            articlesQuery = articlesQuery.Where(x =>
                x.Title.Contains(normalizedQuery)
                || x.Content.Contains(normalizedQuery)
                || (x.Tags != null && x.Tags.Contains(normalizedQuery)));
        }

        var rows = await articlesQuery
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Title)
            .Take(Math.Clamp(take, 1, 250))
            .Select(x => new KnowledgeBaseArticleView
            {
                Id = x.Id,
                Title = x.Title,
                Content = x.Content,
                Category = x.Category,
                ModuleCode = x.ModuleCode,
                Tags = x.Tags,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync(ct);

        return rows;
    }

    public async Task<IReadOnlyList<string>> GetModules(CancellationToken ct = default)
    {
        await EnsureSeedData(ct);

        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.KnowledgeBaseArticles
            .AsNoTracking()
            .Where(x => x.IsPublished && x.ModuleCode != null)
            .Select(x => x.ModuleCode!)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetCategories(CancellationToken ct = default)
    {
        await EnsureSeedData(ct);

        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.KnowledgeBaseArticles
            .AsNoTracking()
            .Where(x => x.IsPublished)
            .Select(x => x.Category)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);
    }

    private async Task EnsureSeedData(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (await db.KnowledgeBaseArticles.AnyAsync(ct))
        {
            return;
        }

        var now = DateTime.UtcNow;
        var seed = new List<KnowledgeBaseArticle>
        {
            new()
            {
                Title = "Getting Started with RegOS Onboarding",
                Category = "getting_started",
                Content = """
                    Use the onboarding wizard to configure your institution in eight steps.
                    Complete profile, licence type, modules, plan, users, workflow, and branding before go-live.
                    """,
                Tags = "[\"onboarding\",\"wizard\",\"setup\"]",
                DisplayOrder = 1,
                CreatedAt = now
            },
            new()
            {
                Title = "How to Submit Your First Return",
                Category = "module_guide",
                Content = """
                    Navigate to Submit Return, choose template and period, then upload XML or use manual data entry.
                    Resolve validation errors and submit for maker-checker approval where enabled.
                    """,
                Tags = "[\"submission\",\"validation\",\"maker-checker\"]",
                DisplayOrder = 2,
                CreatedAt = now
            },
            new()
            {
                Title = "Understanding Validation Errors",
                Category = "faq",
                Content = """
                    Validation errors stop submission and must be fixed.
                    Warnings do not block submission but should be reviewed before final approval.
                    """,
                Tags = "[\"validation\",\"errors\",\"warnings\"]",
                DisplayOrder = 3,
                CreatedAt = now
            },
            new()
            {
                Title = "NFIU goAML Field Mapping Tips",
                Category = "module_guide",
                ModuleCode = "NFIU_AML",
                Content = """
                    Ensure report codes, reporting entity identifiers, and transaction narratives match goAML schema expectations.
                    Always verify namespace and date formats before exporting.
                    """,
                Tags = "[\"NFIU\",\"goAML\",\"xml\"]",
                DisplayOrder = 4,
                CreatedAt = now
            },
            new()
            {
                Title = "Troubleshooting Bulk Upload",
                Category = "troubleshooting",
                Content = """
                    Download the latest template first, keep headers unchanged, and use validation dropdowns.
                    If upload fails, review the generated error workbook and correct highlighted cells.
                    """,
                Tags = "[\"bulk upload\",\"excel\",\"csv\"]",
                DisplayOrder = 5,
                CreatedAt = now
            }
        };

        db.KnowledgeBaseArticles.AddRange(seed);
        await db.SaveChangesAsync(ct);
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}

public class KnowledgeBaseArticleView
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? ModuleCode { get; set; }
    public string? Tags { get; set; }
    public DateTime CreatedAt { get; set; }
}
