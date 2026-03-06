using FC.Engine.Domain.ValueObjects;

namespace FC.Engine.Domain.Abstractions;

public interface IReportQueryEngine
{
    Task<ReportQueryResult> Execute(
        ReportDefinition definition,
        Guid tenantId,
        List<string> entitledModuleCodes,
        CancellationToken ct = default);
}
