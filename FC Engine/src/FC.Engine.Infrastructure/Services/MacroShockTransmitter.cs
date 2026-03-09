using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;

namespace FC.Engine.Infrastructure.Services;

/// <summary>
/// Applies macro-prudential and NGFS climate shocks to a single entity's
/// pre-stress metrics using documented CBN/IMF transmission coefficients (R-07).
/// All arithmetic is traceable: each delta is labelled and summed explicitly.
/// </summary>
public sealed class MacroShockTransmitter : IMacroShockTransmitter
{
    private const decimal EstimatedInsurableShareOfDeposits = 0.72m;

    public EntityShockResult ApplyShock(
        PrudentialMetricSnapshot preStress,
        ResolvedShockParameters  p)
    {
        // ── Step 1: Compute Δ CAR ────────────────────────────────────────
        decimal deltaCAR = 0m;

        if (p.GDPGrowthShock != 0)
            deltaCAR += p.GDPGrowthShock * p.CARDeltaPerGDPPp;

        if (p.OilPriceShockPct != 0)
            deltaCAR += (p.OilPriceShockPct / 100m)
                        * preStress.OilSectorExposurePct
                        * p.CARDeltaPerOilPct * 100m;

        if (p.FXDepreciationPct != 0)
            deltaCAR += (p.FXDepreciationPct / 100m)
                        * preStress.FXLoansAssetPct
                        * p.CARDeltaPerFXPct * 100m;

        if (p.StrandedAssetsPct > 0)
            deltaCAR -= p.StrandedAssetsPct * 0.60m;

        // ── Step 2: Compute Δ NPL ────────────────────────────────────────
        decimal deltaNPL = 0m;

        if (p.GDPGrowthShock != 0)
            deltaNPL += Math.Abs(p.GDPGrowthShock) * p.NPLDeltaPerGDPPp;

        if (p.OilPriceShockPct != 0)
            deltaNPL += (Math.Abs(p.OilPriceShockPct) / 100m)
                        * preStress.OilSectorExposurePct
                        * p.NPLDeltaPerOilPct * 100m;

        if (p.FXDepreciationPct != 0)
            deltaNPL += (p.FXDepreciationPct / 100m)
                        * preStress.FXLoansAssetPct
                        * p.NPLDeltaPerFXPct * 100m;

        if (p.PhysicalRiskHazardCode is "FLOOD" or "DROUGHT" or "FLOOD_DROUGHT")
        {
            var hazardMultiplier = p.PhysicalRiskHazardCode == "FLOOD_DROUGHT" ? 2.0m : 1.0m;
            deltaNPL += preStress.AgriExposurePct * 0.35m * hazardMultiplier;
        }

        // ── Step 3: Compute Δ LCR ────────────────────────────────────────
        decimal deltaLCR = 0m;

        if (p.InterestRateShockBps != 0)
            deltaLCR += (p.InterestRateShockBps / 100m) * p.LCRDeltaPerRateHike100
                        * preStress.BondPortfolioAssetPct * 10m;

        if (p.LCRDeltaPerCyber != 0)
            deltaLCR += p.LCRDeltaPerCyber;

        if (p.DepositOutflowPctCyber != 0)
            deltaLCR -= p.DepositOutflowPctCyber * 0.80m;

        if (p.GDPGrowthShock <= -5m)
            deltaLCR -= preStress.TopDepositorConcentration * 0.20m;

        // ── Step 4: Compute Δ NSFR ───────────────────────────────────────
        decimal deltaNSFR = 0m;
        if (p.InterestRateShockBps != 0)
            deltaNSFR -= (p.InterestRateShockBps / 100m) * 1.5m;
        if (p.GDPGrowthShock < -3m)
            deltaNSFR -= 5.0m;

        // ── Step 5: Compute Δ ROA ────────────────────────────────────────
        decimal deltaROA = 0m;
        if (p.GDPGrowthShock != 0)
            deltaROA += p.GDPGrowthShock * 0.08m;
        if (p.InterestRateShockBps > 0)
            deltaROA -= (p.InterestRateShockBps / 100m) * 0.10m;

        // ── Step 6: Apply deltas ─────────────────────────────────────────
        var postCAR  = Math.Round(preStress.CAR  + deltaCAR,  4);
        var postNPL  = Math.Max(0, Math.Round(preStress.NPL  + deltaNPL,  4));
        var postLCR  = Math.Max(0, Math.Round(preStress.LCR  + deltaLCR,  4));
        var postNSFR = Math.Max(0, Math.Round(preStress.NSFR + deltaNSFR, 4));
        var postROA  = Math.Round(preStress.ROA  + deltaROA,  4);

        // ── Step 7: Capital shortfall & provisioning ─────────────────────
        var minCAR = (decimal)GetMinCAR(preStress.InstitutionType);
        var estimatedGrossLoans = preStress.TotalAssets * 0.65m;
        var additionalProvisions = Math.Max(0, deltaNPL / 100m * estimatedGrossLoans);
        var estimatedRWA = preStress.TotalAssets * 0.78m;
        var capitalShortfall = postCAR < minCAR
            ? Math.Max(0, (minCAR - postCAR) / 100m * estimatedRWA)
            : 0m;

        postCAR -= additionalProvisions / Math.Max(1, estimatedRWA) * 100m;

        // ── Step 8: Breach flags ─────────────────────────────────────────
        bool breachesCAR  = postCAR  < minCAR;
        bool breachesLCR  = postLCR  < 100.0m;
        bool breachesNSFR = postNSFR < 100.0m;
        bool isInsolvent  = postCAR  < 0m;

        // ── Step 9: NDIC exposure (only for failing entities) ────────────
        var insurableDeposits   = breachesCAR || isInsolvent
            ? preStress.TotalDeposits * EstimatedInsurableShareOfDeposits
            : 0m;
        var uninsurableDeposits = breachesCAR || isInsolvent
            ? preStress.TotalDeposits - insurableDeposits
            : 0m;

        return new EntityShockResult(
            InstitutionId:         preStress.InstitutionId,
            InstitutionType:       preStress.InstitutionType,
            PreCAR:                preStress.CAR,
            PreNPL:                preStress.NPL,
            PreLCR:                preStress.LCR,
            PreNSFR:               preStress.NSFR,
            PreROA:                preStress.ROA,
            PreTotalAssets:        preStress.TotalAssets,
            PreTotalDeposits:      preStress.TotalDeposits,
            PostCAR:               postCAR,
            PostNPL:               postNPL,
            PostLCR:               postLCR,
            PostNSFR:              postNSFR,
            PostROA:               postROA,
            PostCapitalShortfall:  capitalShortfall,
            AdditionalProvisions:  additionalProvisions,
            BreachesCAR:           breachesCAR,
            BreachesLCR:           breachesLCR,
            BreachesNSFR:          breachesNSFR,
            IsInsolvent:           isInsolvent,
            InsurableDeposits:     insurableDeposits,
            UninsurableDeposits:   uninsurableDeposits);
    }

    private static double GetMinCAR(string institutionType) => institutionType switch
    {
        "DMB" => 15.0, "MFB" => 10.0, _ => 10.0
    };
}
