using Dapper;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;
using Microsoft.Extensions.Logging;
using QuestPDF.Elements.Table;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FC.Engine.Infrastructure.Services;

/// <summary>
/// Generates the full FSB-style sector stress test report as a branded PDF (R-11).
/// Sections: Cover → Executive Summary → Scenario → Sector Heatmap →
///           Pre/Post Results → Contagion → NDIC Analysis → Policy Recommendations → Appendix.
/// </summary>
public sealed class StressTestReportGenerator : IStressTestReportGenerator
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<StressTestReportGenerator> _log;

    // ── RegOS brand palette ──────────────────────────────────────────────────
    private static readonly Color PrimaryNavy  = Color.FromHex("#0A1628");
    private static readonly Color AccentGold   = Color.FromHex("#D4A017");
    private static readonly Color TextBody     = Color.FromHex("#1C2B40");
    private static readonly Color SurfaceLight = Color.FromHex("#F5F7FA");
    private static readonly Color BandGreen    = Color.FromHex("#2E7D32");
    private static readonly Color BandAmber    = Color.FromHex("#F57C00");
    private static readonly Color BandRed      = Color.FromHex("#C62828");
    private static readonly Color BandCritical = Color.FromHex("#6A1B9A");
    private static readonly Color TableHeader  = Color.FromHex("#1A3A5C");
    private static readonly Color TableRowAlt  = Color.FromHex("#EEF2F7");

    public StressTestReportGenerator(
        IDbConnectionFactory db,
        ILogger<StressTestReportGenerator> log)
    {
        _db = db; _log = log;
    }

    public async Task<byte[]> GenerateAsync(
        long runId, bool anonymiseEntities, CancellationToken ct = default)
    {
        using var conn = await _db.OpenAsync(ct);

        var run = await conn.QuerySingleAsync<RunDetailRow>(
            """
            SELECT r.Id, r.RunGuid, r.PeriodCode, r.TimeHorizon,
                   r.EntitiesShocked, r.ContagionRounds,
                   r.SystemicResilienceScore, r.ExecutiveSummary,
                   r.StartedAt, r.CompletedAt,
                   s.ScenarioCode, s.ScenarioName, s.Category,
                   s.Severity, s.NarrativeSummary
            FROM   StressTestRuns r
            JOIN   StressScenarios s ON s.Id = r.ScenarioId
            WHERE  r.Id = @Id
            """,
            new { Id = runId });

        var entityResults = (await conn.QueryAsync<EntityResultRow>(
            $"""
            SELECT er.InstitutionId,
                   {(anonymiseEntities
                       ? "CAST('Bank ' + CAST(ROW_NUMBER() OVER (ORDER BY er.PostCAR) AS VARCHAR) AS NVARCHAR(150))"
                       : "i.ShortName")} AS InstitutionName,
                   er.InstitutionType,
                   er.PreCAR, er.PreNPL, er.PreLCR,
                   er.PostCAR, er.PostNPL, er.PostLCR,
                   er.DeltaCAR, er.DeltaNPL, er.DeltaLCR,
                   er.PostCapitalShortfall, er.AdditionalProvisions,
                   er.BreachesCAR, er.BreachesLCR, er.IsInsolvent,
                   er.IsContagionVictim, er.ContagionRound, er.FailureCause,
                   er.InsurableDeposits, er.UninsurableDeposits
            FROM   StressTestEntityResults er
            JOIN   Institutions i ON i.Id = er.InstitutionId
            WHERE  er.RunId = @RunId
            ORDER BY er.PostCAR ASC
            """,
            new { RunId = runId })).ToList();

        var sectorAggregates = (await conn.QueryAsync<SectorAggRow>(
            "SELECT * FROM StressTestSectorAggregates WHERE RunId=@Id ORDER BY InstitutionType",
            new { Id = runId })).ToList();

        var contagionEvents = (await conn.QueryAsync<ContagionEventRow>(
            """
            SELECT ce.ContagionRound, ce.FailingInstitutionId, ce.AffectedInstitutionId,
                   ce.ExposureAmount, ce.TransmissionType,
                   fi.ShortName AS FailingName,
                   ai.ShortName AS AffectedName
            FROM   StressTestContagionEvents ce
            JOIN   Institutions fi ON fi.Id = ce.FailingInstitutionId
            JOIN   Institutions ai ON ai.Id = ce.AffectedInstitutionId
            WHERE  ce.RunId = @RunId
            ORDER BY ce.ContagionRound, ce.ExposureAmount DESC
            """,
            new { RunId = runId })).ToList();

        var ndicCapacity = await conn.ExecuteScalarAsync<decimal?>(
            "SELECT ConfigValue FROM SystemConfiguration WHERE ConfigKey='NDIC_FUND_CAPACITY_NGN_MILLIONS'")
            ?? 1_500_000m;

        var score        = (double)(run.SystemicResilienceScore ?? 50m);
        var rating       = GetRating(score);
        var reportDate   = run.CompletedAt ?? DateTimeOffset.UtcNow;
        var totalShortfall = sectorAggregates.Sum(a => a.TotalCapitalShortfall);
        var totalNdic    = sectorAggregates.Sum(a => a.TotalInsurableDepositsAtRisk);

        _log.LogInformation("Generating stress test PDF: RunId={Id} Entities={E}",
            runId, entityResults.Count);

        QuestPDF.Settings.License = LicenseType.Community;

        var pdfBytes = Document.Create(container =>
        {
            // ── Cover Page ───────────────────────────────────────────────────
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);
                page.DefaultTextStyle(t => t.FontFamily("Arial").FontColor(TextBody));

                page.Content().Column(col =>
                {
                    col.Item().Height(PageSizes.A4.Height).Background(PrimaryNavy)
                        .Padding(60).Column(cover =>
                    {
                        cover.Item().PaddingTop(40).Text("CENTRAL BANK OF NIGERIA")
                            .FontSize(11).FontColor(AccentGold).LetterSpacing(0.15f).Bold();
                        cover.Item().PaddingTop(8).Text("BANKING SUPERVISION DEPARTMENT")
                            .FontSize(9).FontColor(Colors.BlueGrey.Lighten4).LetterSpacing(0.1f);

                        cover.Item().PaddingTop(80)
                            .BorderLeft(4).BorderColor(AccentGold).PaddingLeft(20)
                            .Column(title =>
                        {
                            title.Item().Text("SECTOR-WIDE").FontSize(28).Bold().FontColor(Colors.White);
                            title.Item().Text("STRESS TEST REPORT").FontSize(28).Bold().FontColor(AccentGold);
                            title.Item().PaddingTop(12).Text(run.ScenarioName)
                                .FontSize(16).FontColor(Colors.BlueGrey.Lighten4).Italic();
                        });

                        cover.Item().PaddingTop(60).Column(meta =>
                        {
                            var items = new[]
                            {
                                ("Base Period",     run.PeriodCode),
                                ("Time Horizon",    run.TimeHorizon),
                                ("Entities Shocked",$"{run.EntitiesShocked:N0}"),
                                ("Scenario Category",run.Category),
                                ("Severity",        run.Severity),
                                ("Report Date",     reportDate.ToString("dd MMMM yyyy")),
                                ("Classification",  "SUPERVISORY CONFIDENTIAL")
                            };
                            foreach (var (label, value) in items)
                            {
                                meta.Item().Row(r =>
                                {
                                    r.ConstantItem(160).Text(label + ":").FontSize(9).FontColor(Colors.BlueGrey.Lighten4);
                                    r.RelativeItem().Text(value).FontSize(9).Bold().FontColor(Colors.White);
                                });
                                meta.Item().Height(6);
                            }
                        });

                        cover.Item().PaddingTop(80)
                            .BorderTop(1).BorderColor(AccentGold).PaddingTop(20)
                            .Row(footer =>
                        {
                            footer.RelativeItem()
                                .Text("RegOS™ SupTech Platform · Powered by Digibit Global Solutions")
                                .FontSize(8).FontColor(Colors.BlueGrey.Lighten3);
                            footer.ConstantItem(120).AlignRight()
                                .Text($"Run ID: {run.RunGuid.ToString()[..8].ToUpper()}")
                                .FontSize(8).FontColor(Colors.BlueGrey.Lighten3);
                        });
                    });
                });
            });

            // ── Content pages ────────────────────────────────────────────────
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(20); page.MarginBottom(20); page.MarginHorizontal(40);
                page.DefaultTextStyle(t => t.FontFamily("Arial").FontSize(9).FontColor(TextBody));
                page.Header().Element(BuildPageHeader);
                page.Footer().Element(p => BuildPageFooter(p, run.ScenarioName));

                page.Content().Column(body =>
                {
                    // 1. Executive Summary
                    body.Item().Element(c => SectionTitle(c, "1. Executive Summary"));
                    body.Item().Padding(12).Background(SurfaceLight)
                        .Border(1).BorderColor(Color.FromHex("#D1D9E6")).Padding(16).Column(exec =>
                    {
                        exec.Item().Row(r =>
                        {
                            r.ConstantItem(140).Column(gauge =>
                            {
                                gauge.Item().Text("Systemic Resilience Score")
                                    .FontSize(8).FontColor(Colors.Grey.Medium);
                                gauge.Item().PaddingTop(4)
                                    .Text($"{score:F1}/100").FontSize(32).Bold().FontColor(GetRatingColour(rating));
                                gauge.Item().PaddingTop(2)
                                    .Text(rating).FontSize(11).Bold().FontColor(GetRatingColour(rating));
                            });
                            r.RelativeItem().PaddingLeft(16)
                                .Text(run.ExecutiveSummary ?? "Executive summary not available.")
                                .FontSize(8.5f).LineHeight(1.4f);
                        });
                    });
                    body.Item().Height(12);

                    // 2. Scenario Description
                    body.Item().Element(c => SectionTitle(c, "2. Scenario Description"));
                    body.Item().PaddingBottom(8).Text(run.NarrativeSummary).FontSize(8.5f).LineHeight(1.5f);

                    // 3. Sector Results Table
                    body.Item().Element(c => SectionTitle(c, "3. Sector Results — Pre-Stress vs Post-Stress"));
                    body.Item().Table(t =>
                    {
                        t.ColumnsDefinition(cols =>
                        {
                            cols.ConstantColumn(70); cols.RelativeColumn(); cols.RelativeColumn();
                            cols.RelativeColumn(); cols.RelativeColumn(); cols.RelativeColumn();
                            cols.RelativeColumn(); cols.RelativeColumn(); cols.RelativeColumn();
                            cols.RelativeColumn(); cols.RelativeColumn();
                        });

                        void Th(ITableCellContainer c, string text) =>
                            c.Background(TableHeader).Padding(4)
                             .Text(text).FontSize(7.5f).Bold().FontColor(Colors.White).AlignCenter();

                        t.Header(h =>
                        {
                            Th(h.Cell(), "Sector"); Th(h.Cell(), "N");
                            Th(h.Cell(), "CAR Pre"); Th(h.Cell(), "CAR Post");
                            Th(h.Cell(), "NPL Pre"); Th(h.Cell(), "NPL Post");
                            Th(h.Cell(), "LCR Pre"); Th(h.Cell(), "LCR Post");
                            Th(h.Cell(), "Breach"); Th(h.Cell(), "Insolv.");
                            Th(h.Cell(), "Shortfall₦M");
                        });

                        bool alt = false;
                        foreach (var a in sectorAggregates)
                        {
                            var bg = alt ? TableRowAlt : Colors.White;
                            alt = !alt;
                            void Td(ITableCellContainer c, string text, bool highlight = false, Color? fg = null)
                            {
                                var td = c.Background(bg).Padding(4).Text(text).FontSize(7.5f)
                                          .AlignCenter().FontColor(fg ?? TextBody);
                                if (highlight) td.Bold();
                            }

                            Td(t.Cell(), a.InstitutionType);
                            Td(t.Cell(), a.EntityCount.ToString());
                            Td(t.Cell(), $"{a.PreAvgCAR:F1}%");
                            var carC = a.PostAvgCAR < 10 ? BandRed : a.PostAvgCAR < 15 ? BandAmber : BandGreen;
                            Td(t.Cell(), $"{a.PostAvgCAR:F1}%", true, carC);
                            Td(t.Cell(), $"{a.PreAvgNPL:F1}%");
                            var nplC = a.PostAvgNPL > 20 ? BandRed : a.PostAvgNPL > 10 ? BandAmber : BandGreen;
                            Td(t.Cell(), $"{a.PostAvgNPL:F1}%", true, nplC);
                            Td(t.Cell(), $"{a.PreAvgLCR:F1}%");
                            var lcrC = a.PostAvgLCR < 100 ? BandRed : a.PostAvgLCR < 110 ? BandAmber : BandGreen;
                            Td(t.Cell(), $"{a.PostAvgLCR:F1}%", true, lcrC);
                            var brC = a.EntitiesBreachingCAR > 0 ? BandRed : BandGreen;
                            Td(t.Cell(), $"{a.EntitiesBreachingCAR}", a.EntitiesBreachingCAR > 0, brC);
                            var insC = a.EntitiesInsolvent > 0 ? BandCritical : BandGreen;
                            Td(t.Cell(), $"{a.EntitiesInsolvent}", a.EntitiesInsolvent > 0, insC);
                            Td(t.Cell(), $"₦{a.TotalCapitalShortfall:N0}");
                        }

                        var totBg = Color.FromHex("#E8EDF4");
                        void TotalCell(ITableCellContainer c, string text) =>
                            c.Background(totBg).Padding(4).Text(text).FontSize(7.5f).Bold().AlignCenter();

                        TotalCell(t.Cell(), "TOTAL");
                        TotalCell(t.Cell(), $"{sectorAggregates.Sum(a => a.EntityCount)}");
                        TotalCell(t.Cell(), "—"); TotalCell(t.Cell(), "—");
                        TotalCell(t.Cell(), "—"); TotalCell(t.Cell(), "—");
                        TotalCell(t.Cell(), "—"); TotalCell(t.Cell(), "—");
                        TotalCell(t.Cell(), $"{sectorAggregates.Sum(a => a.EntitiesBreachingCAR)}");
                        TotalCell(t.Cell(), $"{sectorAggregates.Sum(a => a.EntitiesInsolvent)}");
                        TotalCell(t.Cell(), $"₦{totalShortfall:N0}");
                    });
                    body.Item().Height(16);

                    // 4. Contagion Analysis
                    body.Item().Element(c => SectionTitle(c, "4. Contagion Analysis"));
                    if (contagionEvents.Count == 0)
                    {
                        body.Item().Padding(8)
                            .Text("No contagion events were triggered under this scenario.")
                            .FontSize(8.5f).Italic();
                    }
                    else
                    {
                        body.Item().PaddingBottom(8)
                            .Text($"The shock propagated across {run.ContagionRounds} contagion round(s), " +
                                  $"affecting {contagionEvents.Select(e => e.AffectedInstitutionId).Distinct().Count()} " +
                                  "entities via interbank exposure and deposit flight channels.")
                            .FontSize(8.5f).LineHeight(1.4f);

                        body.Item().Table(ct2 =>
                        {
                            ct2.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(30); cols.RelativeColumn(); cols.RelativeColumn();
                                cols.ConstantColumn(80); cols.ConstantColumn(80);
                            });
                            ct2.Header(h =>
                            {
                                foreach (var lbl in new[] { "Round","Failing Inst.","Affected Inst.","Exposure ₦M","Channel" })
                                    h.Cell().Background(TableHeader).Padding(4)
                                        .Text(lbl).FontSize(7f).Bold().FontColor(Colors.White);
                            });
                            bool a2 = false;
                            foreach (var evt in contagionEvents.Take(30))
                            {
                                var bg2 = a2 ? TableRowAlt : Colors.White; a2 = !a2;
                                ct2.Cell().Background(bg2).Padding(3).Text(evt.ContagionRound.ToString()).FontSize(7f).AlignCenter();
                                ct2.Cell().Background(bg2).Padding(3).Text(anonymiseEntities ? $"Bank {evt.FailingInstitutionId}" : evt.FailingName).FontSize(7f);
                                ct2.Cell().Background(bg2).Padding(3).Text(anonymiseEntities ? $"Bank {evt.AffectedInstitutionId}" : evt.AffectedName).FontSize(7f);
                                ct2.Cell().Background(bg2).Padding(3).Text($"₦{evt.ExposureAmount:N0}").FontSize(7f).AlignRight();
                                ct2.Cell().Background(bg2).Padding(3).Text(evt.TransmissionType).FontSize(7f).AlignCenter();
                            }
                        });
                    }
                    body.Item().Height(16);

                    // 5. NDIC Exposure Analysis
                    body.Item().Element(c => SectionTitle(c, "5. NDIC Exposure Analysis"));
                    var ndicCoverage = ndicCapacity > 0 ? (double)(totalNdic / ndicCapacity) * 100 : 0.0;
                    body.Item().Background(ndicCoverage > 50 ? Color.FromHex("#FFF3E0") : SurfaceLight)
                        .Border(1).BorderColor(Color.FromHex("#D1D9E6")).Padding(12).Column(ndic =>
                    {
                        ndic.Item().Row(r =>
                        {
                            r.ConstantItem(200).Column(left =>
                            {
                                left.Item().Text("Insurable Deposits at Risk").FontSize(8).FontColor(Colors.Grey.Medium);
                                left.Item().Text($"₦{totalNdic:N0}M").FontSize(20).Bold()
                                    .FontColor(ndicCoverage > 50 ? BandRed : BandAmber);
                                left.Item().PaddingTop(8).Text("NDIC Fund Capacity").FontSize(8).FontColor(Colors.Grey.Medium);
                                left.Item().Text($"₦{ndicCapacity:N0}M").FontSize(14).Bold();
                                left.Item().PaddingTop(4).Text($"Coverage Ratio: {ndicCoverage:F1}%")
                                    .FontSize(9).Bold().FontColor(ndicCoverage > 50 ? BandRed : BandGreen);
                            });
                            r.RelativeItem().PaddingLeft(16).Column(right =>
                            {
                                right.Item().PaddingBottom(4).Text("Per-Sector NDIC Exposure").FontSize(8).Bold();
                                foreach (var a in sectorAggregates.Where(a => a.TotalInsurableDepositsAtRisk > 0))
                                {
                                    right.Item().Row(row =>
                                    {
                                        row.ConstantItem(50).Text(a.InstitutionType).FontSize(7f);
                                        row.RelativeItem().Text($"₦{a.TotalInsurableDepositsAtRisk:N0}M").FontSize(7f).AlignRight();
                                        row.ConstantItem(70).Text($"(unins: ₦{a.TotalUninsurableDepositsAtRisk:N0}M)")
                                            .FontSize(7f).AlignRight().FontColor(Colors.Grey.Medium);
                                    });
                                }
                            });
                        });
                    });
                    body.Item().Height(16);

                    // 6. Entity-Level Results
                    body.Item().Element(c => SectionTitle(c,
                        anonymiseEntities ? "6. Entity-Level Results (Anonymised)" : "6. Entity-Level Results"));
                    body.Item().Table(et =>
                    {
                        et.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(2); cols.RelativeColumn(); cols.RelativeColumn();
                            cols.RelativeColumn(); cols.RelativeColumn(); cols.RelativeColumn();
                            cols.RelativeColumn(); cols.ConstantColumn(50);
                        });
                        et.Header(h =>
                        {
                            foreach (var lbl in new[] { "Institution","Type","CAR Pre","CAR Post","ΔCAR","NPL Post","LCR Post","Status" })
                                h.Cell().Background(TableHeader).Padding(3)
                                    .Text(lbl).FontSize(7f).Bold().FontColor(Colors.White);
                        });
                        bool a3 = false;
                        foreach (var e in entityResults)
                        {
                            var bg3 = e.IsInsolvent ? Color.FromHex("#FCE4EC")
                                : e.BreachesCAR ? Color.FromHex("#FFF3E0")
                                : a3 ? TableRowAlt : Colors.White;
                            a3 = !a3;
                            var statusText   = e.IsInsolvent ? "INSOLVENT" : e.BreachesCAR ? "BREACH" : e.BreachesLCR ? "LCR LOW" : "PASS";
                            var statusColour = e.IsInsolvent ? BandCritical : e.BreachesCAR ? BandRed : e.BreachesLCR ? BandAmber : BandGreen;

                            void Ec(ITableCellContainer c, string text, Color? fg = null, bool bold = false)
                            {
                                var ec = c.Background(bg3).Padding(3).Text(text).FontSize(7f).FontColor(fg ?? TextBody);
                                if (bold) ec.Bold();
                            }

                            Ec(et.Cell(), e.InstitutionName);
                            Ec(et.Cell(), e.InstitutionType);
                            Ec(et.Cell(), $"{e.PreCAR:F1}%");
                            Ec(et.Cell(), $"{e.PostCAR:F1}%", e.PostCAR < 10 ? BandRed : e.PostCAR < 15 ? BandAmber : null, true);
                            Ec(et.Cell(), $"{e.DeltaCAR:+0.0;-0.0}pp", e.DeltaCAR < -5 ? BandRed : null);
                            Ec(et.Cell(), $"{e.PostNPL:F1}%", e.PostNPL > 20 ? BandRed : e.PostNPL > 10 ? BandAmber : null);
                            Ec(et.Cell(), $"{e.PostLCR:F1}%", e.PostLCR < 100 ? BandRed : e.PostLCR < 110 ? BandAmber : null);
                            Ec(et.Cell(), statusText, statusColour, true);
                        }
                    });
                    body.Item().Height(20);

                    // 7. Policy Recommendations
                    body.Item().Element(c => SectionTitle(c, "7. Policy Recommendations"));
                    var recItems = BuildRecommendations(rating, sectorAggregates, run.ContagionRounds,
                        totalShortfall, totalNdic, ndicCapacity);

                    for (int i = 0; i < recItems.Length; i++)
                    {
                        var (title, detail, priority) = recItems[i];
                        var recBg     = priority == "HIGH" ? Color.FromHex("#FFF8E1") : SurfaceLight;
                        var recBorder = priority == "HIGH" ? AccentGold : Color.FromHex("#D1D9E6");
                        body.Item().PaddingBottom(6).Background(recBg).Border(1).BorderColor(recBorder).Padding(10).Column(rec =>
                        {
                            rec.Item().Row(r =>
                            {
                                r.ConstantItem(20).Text($"{i + 1}.").FontSize(9).Bold().FontColor(PrimaryNavy);
                                r.RelativeItem().Column(inner =>
                                {
                                    inner.Item().Row(rr =>
                                    {
                                        rr.RelativeItem().Text(title).FontSize(9).Bold().FontColor(PrimaryNavy);
                                        rr.ConstantItem(45).Background(priority == "HIGH" ? BandRed : BandAmber)
                                            .Padding(2).AlignCenter().Text(priority).FontSize(7f).Bold().FontColor(Colors.White);
                                    });
                                    inner.Item().PaddingTop(4).Text(detail).FontSize(8f).LineHeight(1.4f);
                                });
                            });
                        });
                    }

                    // 8. Appendix
                    body.Item().Height(20);
                    body.Item().Element(c => SectionTitle(c, "Appendix A: Methodology Notes"));
                    body.Item().Text("""
This stress test was conducted using the RegOS™ SupTech Platform Stress Testing Framework.
Transmission coefficients are calibrated to CBN CAMELS methodology and IMF FSAP guidance (2024).
GDP-to-CAR sensitivity coefficients are derived from panel regression analysis of Nigerian banking sector
data (2010–2024) and cross-referenced against World Bank Financial Sector Assessment Program methodologies.
NGFS scenario parameters align with the Phase 4 NGFS Climate Scenarios (June 2023).
Second-round contagion is modelled via Breadth-First Search propagation over the interbank exposure network,
with a 40% Loss Given Default assumption for interbank placements and a maximum cascade depth of 5 rounds.
NDIC exposure estimates use the N5,000,000 per-depositor insurance cap as mandated by the Nigeria Deposit
Insurance Corporation Act 2023. Results are point-in-time and do not incorporate future policy actions
or management responses that may mitigate identified risks.
""").FontSize(8f).LineHeight(1.5f).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();

        _log.LogInformation("Stress test PDF generated: RunId={Id} Bytes={Bytes:N0}",
            runId, pdfBytes.Length);

        return pdfBytes;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (string Title, string Detail, string Priority)[] BuildRecommendations(
        string rating, List<SectorAggRow> aggregates, int contagionRounds,
        decimal totalShortfall, decimal totalNdic, decimal ndicCapacity)
    {
        var recs = new List<(string, string, string)>();
        var totalBreachCAR = aggregates.Sum(a => a.EntitiesBreachingCAR);
        var totalInsolvent = aggregates.Sum(a => a.EntitiesInsolvent);
        var totalEntities  = aggregates.Sum(a => a.EntityCount);

        if (totalInsolvent > 0)
            recs.Add(("Emergency Capital Intervention",
                $"{totalInsolvent} entities are technically insolvent. " +
                $"Total additional capital required: ₦{totalShortfall:N0}M.",
                "HIGH"));

        if (contagionRounds >= 2)
            recs.Add(("Interbank Exposure Limits Review",
                "Contagion propagated across multiple rounds, indicating high interbank interconnectedness. " +
                "The CBN should review single-obligor limits for interbank placements.",
                "HIGH"));

        if (ndicCapacity > 0 && totalNdic / ndicCapacity > 0.30m)
            recs.Add(("NDIC Contingency Planning",
                $"NDIC insurable deposits at risk represent {totalNdic / ndicCapacity * 100:F1}% " +
                "of the Deposit Insurance Fund. NDIC should activate contingency planning.",
                "HIGH"));

        if (aggregates.Any(a => a.PostAvgLCR < 100))
            recs.Add(("Liquidity Support Facility Activation",
                "Multiple institution types post average LCRs below 100%. " +
                "The CBN should pre-position the Standing Lending Facility.",
                "HIGH"));

        recs.Add(("Countercyclical Capital Buffer Review",
            "Assess whether to maintain or release the countercyclical capital buffer.",
            "MEDIUM"));

        recs.Add(("Enhanced Supervisory Monitoring",
            $"{totalBreachCAR} entities breach minimum CAR. Activate enhanced supervisory monitoring " +
            "requiring monthly prudential reporting.",
            "MEDIUM"));

        if (aggregates.Any(a => a.InstitutionType is "MFB" or "DFI" && a.EntitiesBreachingCAR > 0))
            recs.Add(("Development Finance & Microfinance Sector Support",
                "MFBs and DFIs show elevated stress through agricultural and SME loan channels. " +
                "Consider targeted CBN/NIRSAL credit guarantee expansion.",
                "MEDIUM"));

        return recs.ToArray();
    }

    private static void BuildPageHeader(IContainer c) =>
        c.PaddingBottom(8).Row(r =>
        {
            r.RelativeItem().Text("CENTRAL BANK OF NIGERIA · Banking Supervision Department")
                .FontSize(7.5f).FontColor(Colors.Grey.Medium);
            r.ConstantItem(180).AlignRight()
                .Text("SUPERVISORY CONFIDENTIAL — RegOS™ SupTech Platform")
                .FontSize(7.5f).FontColor(Colors.Grey.Medium);
        });

    private static void BuildPageFooter(IContainer c, string scenarioName) =>
        c.PaddingTop(8).BorderTop(1).BorderColor(Color.FromHex("#D1D9E6")).Row(r =>
        {
            r.RelativeItem().Text($"Sector Stress Test: {scenarioName}").FontSize(7f).FontColor(Colors.Grey.Medium);
            r.ConstantItem(80).AlignRight().Text(x =>
                x.CurrentPageNumber().Style(TextStyle.Default.FontSize(7f).FontColor(Colors.Grey.Medium)));
        });

    private static void SectionTitle(IContainer c, string text) =>
        c.PaddingBottom(6).PaddingTop(4)
         .BorderBottom(2).BorderColor(PrimaryNavy).PaddingBottom(4)
         .Text(text).FontSize(11).Bold().FontColor(PrimaryNavy);

    private static string GetRating(double score) => score switch
    {
        >= 80 => "RESILIENT", >= 60 => "ADEQUATE", >= 40 => "VULNERABLE", _ => "CRITICAL"
    };

    private static Color GetRatingColour(string rating) => rating switch
    {
        "RESILIENT"  => BandGreen,
        "ADEQUATE"   => BandAmber,
        "VULNERABLE" => BandRed,
        _            => BandCritical
    };

    // ── Row types ─────────────────────────────────────────────────────────────
    private sealed record RunDetailRow(
        long Id, Guid RunGuid, string PeriodCode, string TimeHorizon,
        int EntitiesShocked, int ContagionRounds,
        decimal? SystemicResilienceScore, string? ExecutiveSummary,
        DateTimeOffset StartedAt, DateTimeOffset? CompletedAt,
        string ScenarioCode, string ScenarioName, string Category,
        string Severity, string NarrativeSummary);

    private sealed record EntityResultRow(
        int InstitutionId, string InstitutionName, string InstitutionType,
        decimal PreCAR, decimal PreNPL, decimal PreLCR,
        decimal PostCAR, decimal PostNPL, decimal PostLCR,
        decimal DeltaCAR, decimal DeltaNPL, decimal DeltaLCR,
        decimal PostCapitalShortfall, decimal AdditionalProvisions,
        bool BreachesCAR, bool BreachesLCR, bool IsInsolvent,
        bool IsContagionVictim, int? ContagionRound, string? FailureCause,
        decimal InsurableDeposits, decimal UninsurableDeposits);

    private sealed record SectorAggRow(
        string InstitutionType, int EntityCount,
        decimal PreAvgCAR, decimal PreAvgNPL, decimal PreAvgLCR,
        decimal PostAvgCAR, decimal PostAvgNPL, decimal PostAvgLCR,
        int EntitiesBreachingCAR, int EntitiesBreachingLCR,
        int EntitiesInsolvent, int EntitiesContagionVictims,
        decimal TotalCapitalShortfall, decimal TotalAdditionalProvisions,
        decimal TotalInsurableDepositsAtRisk, decimal TotalUninsurableDepositsAtRisk);

    private sealed record ContagionEventRow(
        int ContagionRound, int FailingInstitutionId, int AffectedInstitutionId,
        decimal ExposureAmount, string TransmissionType,
        string FailingName, string AffectedName);
}
