using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FluentAssertions;

namespace FC.Engine.Infrastructure.Tests.Services;

public class SeedServiceTests
{
    [Fact]
    public async Task SeedFromSchema_SeedsSpecialFcAndReportTables()
    {
        var schemaPath = Path.GetTempFileName();
        var repo = new RecordingTemplateRepository();
        var sut = new SeedService(repo);

        await File.WriteAllTextAsync(schemaPath, """
            CREATE TABLE fc_car_1 (
                id SERIAL PRIMARY KEY,
                submission_id INT NOT NULL,
                item_code VARCHAR(20),
                item_description VARCHAR(255),
                asset_value NUMERIC(20,2),
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE fc_acr (
                id SERIAL PRIMARY KEY,
                submission_id INT NOT NULL,
                capital_funds NUMERIC(20,2),
                adjusted_capital_ratio NUMERIC(10,4),
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE fc_car_2 (
                id SERIAL PRIMARY KEY,
                submission_id INT NOT NULL,
                item_code VARCHAR(20),
                total_qualifying_capital NUMERIC(20,2),
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE fc_fhr (
                id SERIAL PRIMARY KEY,
                submission_id INT NOT NULL,
                indicator_name VARCHAR(255),
                indicator_value NUMERIC(20,4),
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE fc_cvr (
                id SERIAL PRIMARY KEY,
                submission_id INT NOT NULL,
                contravention_type VARCHAR(255),
                penalty_amount NUMERIC(20,2),
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE fc_rating (
                id SERIAL PRIMARY KEY,
                submission_id INT NOT NULL,
                composite_rating NUMERIC(10,4),
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE consol (
                id SERIAL PRIMARY KEY,
                reporting_date DATE NOT NULL,
                return_code VARCHAR(20),
                item_code VARCHAR(20),
                consolidated_amount NUMERIC(20,2),
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE npl (
                id SERIAL PRIMARY KEY,
                submission_id INT NOT NULL,
                serial_no INT,
                customer_code VARCHAR(50),
                total_outstanding NUMERIC(20,2),
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE reports_kri (
                id SERIAL PRIMARY KEY,
                reporting_date DATE,
                serial_no INT,
                indicator_name VARCHAR(255),
                computed_value NUMERIC(20,4),
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE sheet3_top10_rankings (
                id SERIAL PRIMARY KEY,
                reporting_date DATE,
                ranking_category VARCHAR(100),
                rank_position INT,
                institution_name VARCHAR(255),
                amount NUMERIC(20,2),
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            );
            """);

        try
        {
            var result = await sut.SeedFromSchema(schemaPath, "test-user");

            result.Errors.Should().BeEmpty();
            result.Created.Should().BeEquivalentTo(
            [
                "FC CAR 1",
                "FC CAR 2",
                "FC ACR",
                "FC FHR",
                "FC CVR",
                "FC RATING",
                "CONSOL",
                "NPL",
                "REPORTS",
                "SHEET3"
            ]);

            repo.Templates.Should().HaveCount(10);
            repo.Templates.Should().ContainSingle(t => t.ReturnCode == "FC CAR 1" && t.StructuralCategory == StructuralCategory.ItemCoded);
            repo.Templates.Should().ContainSingle(t => t.ReturnCode == "FC CAR 2" && t.StructuralCategory == StructuralCategory.ItemCoded);
            repo.Templates.Should().ContainSingle(t => t.ReturnCode == "FC FHR" && t.StructuralCategory == StructuralCategory.MultiRow);
            repo.Templates.Should().ContainSingle(t => t.ReturnCode == "FC CVR" && t.StructuralCategory == StructuralCategory.MultiRow);
            repo.Templates.Should().ContainSingle(t => t.ReturnCode == "CONSOL" && t.Frequency == ReturnFrequency.Computed);
            repo.Templates.Should().ContainSingle(t => t.ReturnCode == "NPL" && t.Frequency == ReturnFrequency.Monthly);
            repo.Templates.Should().ContainSingle(t => t.ReturnCode == "REPORTS" && t.StructuralCategory == StructuralCategory.MultiRow);
            repo.Templates.Should().ContainSingle(t => t.ReturnCode == "SHEET3" && t.StructuralCategory == StructuralCategory.MultiRow);
            repo.Templates.Should().ContainSingle(t => t.ReturnCode == "FC FHR" &&
                                                      t.CurrentPublishedVersion!.Fields.Any(f => f.IsKeyField && f.FieldName == "indicator_name"));
            repo.Templates.Should().ContainSingle(t => t.ReturnCode == "FC CVR" &&
                                                      t.CurrentPublishedVersion!.Fields.Any(f => f.IsKeyField && f.FieldName == "contravention_type"));
            repo.Templates.Should().ContainSingle(t => t.ReturnCode == "SHEET3" &&
                                                      t.CurrentPublishedVersion!.Fields.Any(f => f.IsKeyField && f.FieldName == "rank_position"));
        }
        finally
        {
            File.Delete(schemaPath);
        }
    }

    private sealed class RecordingTemplateRepository : ITemplateRepository
    {
        public List<ReturnTemplate> Templates { get; } = new();

        public Task<ReturnTemplate?> GetById(int id, CancellationToken ct = default) =>
            Task.FromResult(Templates.FirstOrDefault(t => t.Id == id));

        public Task<ReturnTemplate?> GetByReturnCode(string returnCode, CancellationToken ct = default) =>
            Task.FromResult(Templates.FirstOrDefault(t => t.ReturnCode == returnCode));

        public Task<ReturnTemplate?> GetPublishedByReturnCode(string returnCode, CancellationToken ct = default) =>
            Task.FromResult(Templates.FirstOrDefault(t => t.ReturnCode == returnCode));

        public Task<IReadOnlyList<ReturnTemplate>> GetAll(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReturnTemplate>>(Templates);

        public Task<IReadOnlyList<ReturnTemplate>> GetByFrequency(string frequency, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReturnTemplate>>(Templates.Where(t => t.Frequency.ToString() == frequency).ToList());

        public Task Add(ReturnTemplate template, CancellationToken ct = default)
        {
            template.Id = Templates.Count + 1;
            Templates.Add(template);
            return Task.CompletedTask;
        }

        public Task Update(ReturnTemplate template, CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> ExistsByReturnCode(string returnCode, CancellationToken ct = default) =>
            Task.FromResult(Templates.Any(t => t.ReturnCode == returnCode));

        public Task<IReadOnlyList<ReturnTemplate>> GetAllForTenant(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReturnTemplate>>(Templates.Where(t => t.TenantId == tenantId || t.TenantId == null).ToList());

        public Task<ReturnTemplate?> GetByReturnCodeForTenant(string returnCode, Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(Templates.FirstOrDefault(t => t.ReturnCode == returnCode && (t.TenantId == tenantId || t.TenantId == null)));

        public Task<IReadOnlyList<ReturnTemplate>> GetByModuleIds(IEnumerable<int> moduleIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReturnTemplate>>(Templates.Where(t => t.ModuleId.HasValue && moduleIds.Contains(t.ModuleId.Value)).ToList());

        public Task<TemplateVersion?> GetLatestDraftVersion(string returnCode, CancellationToken ct = default) =>
            Task.FromResult<TemplateVersion?>(null);

        public Task<bool> HasExistingDraft(int templateId, CancellationToken ct = default) =>
            Task.FromResult(Templates.Any(t => t.Id == templateId && t.Versions.Any(v => v.Status is TemplateStatus.Draft or TemplateStatus.Review)));
    }
}
