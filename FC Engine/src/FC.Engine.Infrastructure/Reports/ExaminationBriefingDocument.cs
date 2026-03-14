using System.Globalization;
using FC.Engine.Domain.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FC.Engine.Infrastructure.Reports;

public sealed class ExaminationBriefingDocument : IDocument
{
    private readonly ExaminationBriefing _briefing;
    private readonly string _regulatorName;

    private const string Primary = "#0A2F5C";
    private const string Accent = "#C8A951";

    public ExaminationBriefingDocument(ExaminationBriefing briefing, string regulatorName)
    {
        _briefing = briefing;
        _regulatorName = regulatorName;
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(28);
            page.DefaultTextStyle(x => x.FontSize(10));

            page.Header().Column(column =>
            {
                column.Item().Row(row =>
                {
                    row.RelativeItem().Column(inner =>
                    {
                        inner.Item().Text("CONFIDENTIAL - Examination Briefing").FontSize(18).Bold().FontColor(Primary);
                        inner.Item().Text($"{_briefing.InstitutionName} | {_briefing.LicenceCategory} | {_briefing.RegulatorAgency}").FontSize(10).FontColor(Colors.Grey.Darken1);
                    });
                    row.ConstantItem(160).AlignRight().Column(inner =>
                    {
                        inner.Item().Text(DateTime.UtcNow.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture)).FontSize(8).FontColor(Colors.Grey.Medium);
                        inner.Item().Text(_regulatorName).FontSize(8).FontColor(Colors.Grey.Medium);
                    });
                });
                column.Item().PaddingTop(6).LineHorizontal(1).LineColor(Accent);
            });

            page.Content().PaddingTop(12).Column(column =>
            {
                ComposeCover(column);
                ComposeEntityOverview(column);
                ComposeRiskAssessment(column);
                ComposeDataQuality(column);
                ComposeComplianceHealth(column);
                ComposeFilingHistory(column);
                ComposePeerComparison(column);
                ComposeSanctionsAndAml(column);
                ComposeSupervisoryHistory(column);
                ComposeFocusAreas(column);
            });

            page.Footer().AlignCenter().Text(text =>
            {
                text.Span("CONFIDENTIAL - RegulatorIQ Intelligence Product - Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                text.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                text.Span(" of ").FontSize(8).FontColor(Colors.Grey.Medium);
                text.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
            });
        });
    }

    private void ComposeCover(ColumnDescriptor column)
    {
        column.Item().PaddingBottom(12).Background(Colors.Grey.Lighten4).Padding(12).Column(inner =>
        {
            inner.Item().Text("1. Cover").FontSize(13).Bold().FontColor(Primary);
            inner.Item().PaddingTop(4).Text($"Institution: {_briefing.InstitutionName}");
            inner.Item().Text($"Licence Category: {_briefing.LicenceCategory}");
            inner.Item().Text($"Regulator Agency: {_briefing.RegulatorAgency}");
            inner.Item().Text($"Generated: {_briefing.GeneratedAt:dd MMM yyyy HH:mm} UTC");
            inner.Item().Text($"Data Sources: {string.Join(", ", _briefing.DataSourcesUsed.Distinct(StringComparer.OrdinalIgnoreCase))}");
        });
    }

    private void ComposeEntityOverview(ColumnDescriptor column)
    {
        var profile = _briefing.Profile;

        ComposeSection(column, "2. Entity Overview", body =>
        {
            body.Item().Text($"Institution: {profile.InstitutionName}");
            body.Item().Text($"Latest Reporting Period: {profile.LatestPeriodCode ?? "N/A"}");
            body.Item().Text($"Latest Submission: {FormatDate(profile.LatestSubmissionAt)}");

            body.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(1);
                    columns.RelativeColumn(1);
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("Metric");
                    header.Cell().Element(HeaderCell).Text("Value");
                    header.Cell().Element(HeaderCell).Text("Source");
                });

                foreach (var metric in profile.KeyMetrics.Take(8))
                {
                    table.Cell().Element(BodyCell).Text(metric.MetricLabel);
                    table.Cell().Element(BodyCell).Text(FormatMetric(metric.MetricCode, metric.Value));
                    table.Cell().Element(BodyCell).Text(metric.ModuleCode ?? "RG-07");
                }
            });
        });
    }

    private void ComposeRiskAssessment(ColumnDescriptor column)
    {
        var profile = _briefing.Profile;

        ComposeSection(column, "3. Risk Assessment", body =>
        {
            body.Item().Text($"Filing Risk: {FormatRisk(profile.FilingRisk?.RiskBand, profile.FilingRisk?.PredictedValue)}");
            body.Item().Text($"Capital Forecast: {FormatRisk(profile.CapitalForecast?.RiskBand, profile.CapitalForecast?.PredictedValue)}");
            body.Item().Text($"CAMELS Composite: {(profile.CamelsScore?.Composite.ToString("N1", CultureInfo.InvariantCulture) ?? "N/A")}");
            body.Item().Text($"Early Warning Flags: {profile.EarlyWarningFlags.Count}");

            if (profile.EarlyWarningFlags.Count > 0)
            {
                body.Item().PaddingTop(4).Column(flags =>
                {
                    foreach (var flag in profile.EarlyWarningFlags.Take(6))
                    {
                        flags.Item().Text($"- {flag.FlagCode}: {flag.Message}");
                    }
                });
            }
        });
    }

    private void ComposeDataQuality(ColumnDescriptor column)
    {
        ComposeSection(column, "4. Data Quality", body =>
        {
            var anomaly = _briefing.Profile.Anomaly;
            body.Item().Text($"Quality Score: {(anomaly?.QualityScore?.ToString("N1", CultureInfo.InvariantCulture) ?? "N/A")}");
            body.Item().Text($"Traffic Light: {anomaly?.TrafficLight ?? "N/A"}");
            body.Item().Text($"Findings: {anomaly?.TotalFindings ?? 0} (alerts: {anomaly?.AlertCount ?? 0}, warnings: {anomaly?.WarningCount ?? 0})");
            body.Item().PaddingTop(4).Text(anomaly?.NarrativeSummary ?? "No anomaly narrative is available.");
        });
    }

    private void ComposeComplianceHealth(ColumnDescriptor column)
    {
        ComposeSection(column, "5. Compliance Health", body =>
        {
            var chs = _briefing.Profile.ComplianceHealth;
            body.Item().Text($"Overall CHS Score: {(chs?.OverallScore.ToString("N1", CultureInfo.InvariantCulture) ?? "N/A")}");
            body.Item().Text($"Rating: {(chs is null ? "N/A" : chs.Rating.ToString())}");
            body.Item().Text($"Filing Timeliness Pillar: {(chs?.FilingTimeliness.ToString("N1", CultureInfo.InvariantCulture) ?? "N/A")}");
            body.Item().Text($"Data Quality Pillar: {(chs?.DataQuality.ToString("N1", CultureInfo.InvariantCulture) ?? "N/A")}");
            body.Item().Text($"Regulatory Capital Pillar: {(chs?.RegulatoryCapital.ToString("N1", CultureInfo.InvariantCulture) ?? "N/A")}");
            body.Item().Text($"Audit Governance Pillar: {(chs?.AuditGovernance.ToString("N1", CultureInfo.InvariantCulture) ?? "N/A")}");
        });
    }

    private void ComposeFilingHistory(ColumnDescriptor column)
    {
        ComposeSection(column, "6. Filing History", body =>
        {
            var filing = _briefing.Profile.FilingTimeliness;
            body.Item().Text($"Total Filings: {filing?.TotalFilings ?? 0}");
            body.Item().Text($"On-Time Filings: {filing?.OnTimeFilings ?? 0}");
            body.Item().Text($"Late Filings: {filing?.LateFilings ?? 0}");
            body.Item().Text($"Overdue Filings: {filing?.OverdueFilings ?? 0}");
            body.Item().Text($"Latest Deadline: {FormatDate(filing?.LatestDeadline)}");
            body.Item().Text($"Latest Submission: {FormatDate(filing?.LatestSubmittedAt)}");
        });
    }

    private void ComposePeerComparison(ColumnDescriptor column)
    {
        ComposeSection(column, "7. Peer Comparison", body =>
        {
            var peer = _briefing.PeerContext;
            body.Item().Text($"Peer Entity Count: {peer.EntityCount}");
            body.Item().Text($"Average CAR: {FormatPercent(peer.AverageCarRatio)}");
            body.Item().Text($"Average NPL: {FormatPercent(peer.AverageNplRatio)}");
            body.Item().Text($"Average Liquidity: {FormatPercent(peer.AverageLiquidityRatio)}");
            body.Item().Text($"Average CHS: {(peer.AverageComplianceHealthScore?.ToString("N1", CultureInfo.InvariantCulture) ?? "N/A")}");
        });
    }

    private void ComposeSanctionsAndAml(ColumnDescriptor column)
    {
        ComposeSection(column, "8. Sanctions and AML", body =>
        {
            var sanctions = _briefing.Profile.SanctionsExposure;
            body.Item().Text($"Sanctions Matches: {sanctions?.MatchCount ?? 0}");
            body.Item().Text($"Highest Match Score: {(sanctions?.HighestMatchScore?.ToString("N1", CultureInfo.InvariantCulture) ?? "N/A")}");
            body.Item().Text($"Highest Risk Level: {sanctions?.HighestRiskLevel ?? "N/A"}");
            body.Item().Text($"Latest Matched Name: {sanctions?.LatestMatchedName ?? "N/A"}");

            var str = _briefing.Profile.StrAdequacy;
            body.Item().PaddingTop(4).Text($"STR Filing Count: {str?.StrFilingCount ?? 0}");
            body.Item().Text($"Peer Average STR Count: {(str?.PeerAverageStrCount?.ToString("N1", CultureInfo.InvariantCulture) ?? "N/A")}");
            body.Item().Text($"Structuring Alerts: {str?.StructuringAlertCount ?? 0}");
        });
    }

    private void ComposeSupervisoryHistory(ColumnDescriptor column)
    {
        ComposeSection(column, "9. Supervisory History", body =>
        {
            if (_briefing.OpenInvestigations.Count == 0)
            {
                body.Item().Text("No open investigation or supervisory-history records were supplied to this briefing.");
                return;
            }

            foreach (var item in _briefing.OpenInvestigations.Take(8))
            {
                body.Item().Text($"- {item.Reference} | {item.Status} | {item.Summary}");
            }
        });
    }

    private void ComposeFocusAreas(ColumnDescriptor column)
    {
        ComposeSection(column, "10. Suggested Focus Areas", body =>
        {
            if (_briefing.FocusAreas.Count == 0)
            {
                body.Item().Text("No additional focus areas were generated.");
                return;
            }

            foreach (var focusArea in _briefing.FocusAreas.Take(10))
            {
                body.Item().PaddingBottom(4).Column(inner =>
                {
                    inner.Item().Text($"{focusArea.Area} ({focusArea.Priority})").Bold();
                    inner.Item().Text(focusArea.Reason);
                });
            }
        });
    }

    private static void ComposeSection(ColumnDescriptor column, string title, Action<ColumnDescriptor> composeBody)
    {
        column.Item().PaddingBottom(12).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(inner =>
        {
            inner.Item().Text(title).FontSize(12).Bold().FontColor(Primary);
            composeBody(inner);
        });
    }

    private static IContainer HeaderCell(IContainer container) =>
        container.Background(Primary).Padding(6).DefaultTextStyle(x => x.FontColor(Colors.White).SemiBold());

    private static IContainer BodyCell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).PaddingHorizontal(6);

    private static string FormatDate(DateTime? value) =>
        value.HasValue ? value.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) : "N/A";

    private static string FormatPercent(decimal? value) =>
        value.HasValue ? $"{value.Value:N1}%" : "N/A";

    private static string FormatMetric(string metricCode, decimal? value)
    {
        if (!value.HasValue)
        {
            return "N/A";
        }

        return metricCode switch
        {
            "carratio" or "nplratio" or "liquidityratio" or "loandepositratio" or "roa" => $"{value.Value:N1}%",
            "totalassets" => $"NGN {value.Value:N0}",
            _ => value.Value.ToString("N2", CultureInfo.InvariantCulture)
        };
    }

    private static string FormatRisk(string? riskBand, decimal? value)
    {
        if (string.IsNullOrWhiteSpace(riskBand) && !value.HasValue)
        {
            return "N/A";
        }

        if (!value.HasValue)
        {
            return riskBand ?? "N/A";
        }

        return $"{riskBand ?? "N/A"} ({value.Value:N2})";
    }
}
