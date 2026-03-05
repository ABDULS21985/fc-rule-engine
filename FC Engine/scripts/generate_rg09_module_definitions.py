#!/usr/bin/env python3
import json
from pathlib import Path


def section(code, name, order):
    return {"code": code, "name": name, "displayOrder": order}


def field(code, label, data_type="Money", section_code=None, required=False, order=1, min_value=0, max_value=999999999999.99, decimal_places=2, carry_forward=False, help_text=None, regulatory_reference=None, validation_note=None):
    return {
        "fieldCode": code,
        "label": label,
        "dataType": data_type,
        "section": section_code,
        "required": required,
        "minValue": min_value,
        "maxValue": max_value,
        "decimalPlaces": decimal_places,
        "displayOrder": order,
        "carryForward": carry_forward,
        "helpText": help_text,
        "regulatoryReference": regulatory_reference,
        "validationNote": validation_note,
    }


def formula_sum(target, sources, desc, severity="Error", tolerance=0.01):
    return {
        "formulaType": "Sum",
        "targetField": target,
        "sourceFields": sources,
        "severity": severity,
        "toleranceAmount": tolerance,
        "description": desc,
    }


def formula_ratio(target, numerator, denominator, desc, severity="Error", tolerance=0.01):
    return {
        "formulaType": "Ratio",
        "targetField": target,
        "sourceFields": [numerator, denominator],
        "severity": severity,
        "toleranceAmount": tolerance,
        "description": desc,
    }


def formula_custom(target, sources, function_name, desc, severity="Error", parameters=None, tolerance=0.01):
    payload = {
        "formulaType": "Custom",
        "customFunction": function_name,
        "targetField": target,
        "sourceFields": sources,
        "severity": severity,
        "toleranceAmount": tolerance,
        "description": desc,
    }
    if parameters:
        payload["parameters"] = parameters
    return payload


def formula_compare(formula_type, target, source, desc, severity="Error", tolerance=0.0):
    return {
        "formulaType": formula_type,
        "targetField": target,
        "sourceFields": [source],
        "severity": severity,
        "toleranceAmount": tolerance,
        "description": desc,
    }


def template(return_code, name, frequency, structural_category, table_prefix, sections, fields, formulas, item_codes=None, cross_rules=None):
    return {
        "returnCode": return_code,
        "name": name,
        "frequency": frequency,
        "structuralCategory": structural_category,
        "tablePrefix": table_prefix,
        "sections": sections,
        "fields": fields,
        "itemCodes": item_codes or [],
        "formulas": formulas,
        "crossSheetRules": cross_rules or [],
    }


def ensure_field(tmpl, code, label, section_code):
    existing = {f["fieldCode"] for f in tmpl["fields"]}
    if code not in existing:
        tmpl["fields"].append(field(code, label, "Money", section_code, False, len(tmpl["fields"]) + 1))


def pad_template(tmpl, target_fields, target_formulas):
    first_section = tmpl["sections"][0]["code"] if tmpl["sections"] else None
    code_prefix = tmpl["returnCode"].lower()

    existing = {f["fieldCode"] for f in tmpl["fields"]}
    idx = 1
    while len(tmpl["fields"]) < target_fields:
        code = f"{code_prefix}_control_metric_{idx:03d}"
        if code not in existing:
            tmpl["fields"].append(
                field(
                    code,
                    f"Control Metric {idx:03d}",
                    "Money",
                    first_section,
                    False,
                    len(tmpl["fields"]) + 1,
                )
            )
            existing.add(code)
        idx += 1

    metric_codes = [f["fieldCode"] for f in tmpl["fields"] if "_control_metric_" in f["fieldCode"]]
    if len(metric_codes) < 2:
        for i in range(1, 3):
            code = f"{code_prefix}_control_metric_{i:03d}"
            if code not in existing:
                tmpl["fields"].append(field(code, f"Control Metric {i:03d}", "Money", first_section, False, len(tmpl["fields"]) + 1))
                metric_codes.append(code)
                existing.add(code)

    fidx = 1
    while len(tmpl["formulas"]) < target_formulas:
        target = f"{code_prefix}_control_total_{fidx:03d}"
        if target not in existing:
            tmpl["fields"].append(field(target, f"Control Total {fidx:03d}", "Money", first_section, False, len(tmpl["fields"]) + 1))
            existing.add(target)

        s1 = metric_codes[(fidx - 1) % len(metric_codes)]
        s2 = metric_codes[(fidx) % len(metric_codes)]
        tmpl["formulas"].append(
            formula_sum(target, [s1, s2], f"Control balance check {fidx:03d}", severity="Warning", tolerance=0.5)
        )
        fidx += 1


def add_cross_rules(module_def, target_rule_count):
    templates = module_def["templates"]
    count = sum(len(t["crossSheetRules"]) for t in templates)
    idx = 1
    cursor = 0
    while count < target_rule_count:
        src = templates[cursor % len(templates)]
        tgt = templates[(cursor + 1) % len(templates)]

        src_field = src["fields"][0]["fieldCode"]
        tgt_field = tgt["fields"][0]["fieldCode"]

        src["crossSheetRules"].append(
            {
                "description": f"Cross-sheet reconciliation {idx:03d}",
                "sourceTemplate": src["returnCode"],
                "sourceField": src_field,
                "targetTemplate": tgt["returnCode"],
                "targetField": tgt_field,
                "operator": "Equals",
                "severity": "Warning",
                "toleranceAmount": 1.0,
            }
        )
        count += 1
        idx += 1
        cursor += 1


def dmb_module_definition():
    templates = []

    cov = template(
        "DMB_COV",
        "DMB Cover and Institution Profile",
        "Quarterly",
        "FixedRow",
        "dmb",
        [section("COV_GEN", "General Information", 1)],
        [
            field("reporting_year", "Reporting Year", "Integer", "COV_GEN", True, 1),
            field("reporting_month", "Reporting Month", "Integer", "COV_GEN", True, 2),
            field("institution_name", "Institution Name", "Text", "COV_GEN", True, 3),
            field("cbn_license_number", "CBN License Number", "Text", "COV_GEN", True, 4),
            field("prepared_by", "Prepared By", "Text", "COV_GEN", True, 5),
            field("approved_by", "Approved By", "Text", "COV_GEN", True, 6),
            field("contact_email", "Contact Email", "Text", "COV_GEN", True, 7),
            field("contact_phone", "Contact Phone", "Text", "COV_GEN", False, 8),
            field("submission_date", "Submission Date", "Date", "COV_GEN", True, 9),
            field("institution_class", "Institution Class", "Text", "COV_GEN", True, 10),
        ],
        [
            formula_compare("GreaterThanOrEqual", "reporting_year", "reporting_month", "Year-month sanity check", "Info")
        ],
    )
    templates.append(cov)

    cap = template(
        "DMB_CAP",
        "Capital Adequacy",
        "Monthly",
        "FixedRow",
        "dmb",
        [
            section("CAP_CET1", "CET1 Capital", 1),
            section("CAP_AT1", "Additional Tier 1", 2),
            section("CAP_T2", "Tier 2", 3),
            section("CAP_RATIO", "Ratios", 4),
        ],
        [
            field("paid_up_capital", "Paid Up Capital", "Money", "CAP_CET1", True, 1),
            field("share_premium", "Share Premium", "Money", "CAP_CET1", True, 2),
            field("retained_earnings", "Retained Earnings", "Money", "CAP_CET1", True, 3),
            field("other_comprehensive_income", "Other Comprehensive Income", "Money", "CAP_CET1", True, 4),
            field("less_goodwill", "Less Goodwill", "Money", "CAP_CET1", True, 5),
            field("less_intangibles", "Less Intangibles", "Money", "CAP_CET1", True, 6),
            field("less_deferred_tax", "Less Deferred Tax", "Money", "CAP_CET1", True, 7),
            field("cet1_capital", "CET1 Capital", "Money", "CAP_CET1", True, 8),
            field("qualifying_at1_instruments", "Qualifying AT1 Instruments", "Money", "CAP_AT1", True, 9),
            field("at1_deductions", "AT1 Deductions", "Money", "CAP_AT1", True, 10),
            field("at1_capital", "AT1 Capital", "Money", "CAP_AT1", True, 11),
            field("qualifying_tier2_instruments", "Qualifying Tier 2 Instruments", "Money", "CAP_T2", True, 12),
            field("general_provisions", "General Provisions", "Money", "CAP_T2", True, 13),
            field("tier2_deductions", "Tier 2 Deductions", "Money", "CAP_T2", True, 14),
            field("tier2_capital", "Tier 2 Capital", "Money", "CAP_T2", True, 15),
            field("tier1_capital", "Tier 1 Capital", "Money", "CAP_RATIO", True, 16),
            field("total_regulatory_capital", "Total Regulatory Capital", "Money", "CAP_RATIO", True, 17),
            field("total_rwa", "Total Risk Weighted Assets", "Money", "CAP_RATIO", True, 18),
            field("car_ratio", "Capital Adequacy Ratio", "Percentage", "CAP_RATIO", True, 19),
            field("cet1_ratio", "CET1 Ratio", "Percentage", "CAP_RATIO", True, 20),
            field("tier1_ratio", "Tier 1 Ratio", "Percentage", "CAP_RATIO", True, 21),
            field("cet1_minimum", "CET1 Minimum", "Percentage", "CAP_RATIO", True, 22, 0, 100, 2),
            field("tier1_minimum", "Tier 1 Minimum", "Percentage", "CAP_RATIO", True, 23, 0, 100, 2),
            field("car_minimum", "CAR Minimum", "Percentage", "CAP_RATIO", True, 24, 0, 100, 2),
            field("car_buffer", "CAR Buffer", "Percentage", "CAP_RATIO", False, 25, -100, 100, 2),
        ],
        [
            formula_sum("cet1_capital", ["paid_up_capital", "share_premium", "retained_earnings", "other_comprehensive_income"], "CET1 before deductions"),
            formula_sum("at1_capital", ["qualifying_at1_instruments"], "AT1 net amount"),
            formula_sum("tier2_capital", ["qualifying_tier2_instruments", "general_provisions"], "Tier2 gross amount"),
            formula_sum("tier1_capital", ["cet1_capital", "at1_capital"], "Tier1 capital"),
            formula_sum("total_regulatory_capital", ["tier1_capital", "tier2_capital"], "Total regulatory capital"),
            formula_custom("car_ratio", ["tier1_capital", "tier2_capital", "total_rwa"], "CAR", "CAR computation using Basel III custom function"),
            formula_ratio("cet1_ratio", "cet1_capital", "total_rwa", "CET1 ratio"),
            formula_ratio("tier1_ratio", "tier1_capital", "total_rwa", "Tier1 ratio"),
            formula_compare("GreaterThanOrEqual", "cet1_ratio", "cet1_minimum", "CET1 ratio >= minimum", "Warning"),
            formula_compare("GreaterThanOrEqual", "tier1_ratio", "tier1_minimum", "Tier1 ratio >= minimum", "Warning"),
            formula_compare("GreaterThanOrEqual", "car_ratio", "car_minimum", "CAR >= minimum", "Error"),
            formula_compare("GreaterThanOrEqual", "car_buffer", "car_minimum", "Capital buffer check", "Info"),
        ],
    )
    templates.append(cap)

    crr_sections = [
        section("CRR_EXPOSURE", "Exposure Classes", 1),
        section("CRR_TOTAL", "Credit RWA Totals", 2),
    ]
    crr_fields = []
    exposure_classes = [
        "sovereign",
        "bank",
        "corporate",
        "retail",
        "residential_mortgage",
        "commercial_real_estate",
        "past_due",
        "other",
    ]
    order = 1
    for cls in exposure_classes:
        crr_fields.extend(
            [
                field(f"{cls}_gross_exposure", f"{cls} Gross Exposure", "Money", "CRR_EXPOSURE", True, order),
                field(f"{cls}_credit_risk_mitigation", f"{cls} Credit Risk Mitigation", "Money", "CRR_EXPOSURE", True, order + 1),
                field(f"{cls}_net_exposure", f"{cls} Net Exposure", "Money", "CRR_EXPOSURE", True, order + 2),
                field(f"{cls}_risk_weight", f"{cls} Risk Weight", "Percentage", "CRR_EXPOSURE", True, order + 3, 0, 200, 2),
                field(f"{cls}_rwa", f"{cls} RWA", "Money", "CRR_EXPOSURE", True, order + 4),
            ]
        )
        order += 5
    crr_fields.extend(
        [
            field("total_credit_rwa", "Total Credit Risk RWA", "Money", "CRR_TOTAL", True, order),
            field("credit_rwa_reconciliation", "Credit RWA Reconciliation", "Money", "CRR_TOTAL", False, order + 1),
        ]
    )
    crr_formulas = []
    for cls in exposure_classes:
        crr_formulas.append(
            {
                "formulaType": "Difference",
                "targetField": f"{cls}_net_exposure",
                "sourceFields": [f"{cls}_gross_exposure", f"{cls}_credit_risk_mitigation"],
                "severity": "Error",
                "toleranceAmount": 1,
                "description": f"{cls} net exposure equals gross less CRM",
            }
        )
    crr_formulas.append(
        formula_sum("total_credit_rwa", [f"{cls}_rwa" for cls in exposure_classes], "Total credit risk RWA is the sum of class RWAs")
    )
    crr = template(
        "DMB_CRR",
        "Credit Risk RWA",
        "Monthly",
        "FixedRow",
        "dmb",
        crr_sections,
        crr_fields,
        crr_formulas,
    )
    templates.append(crr)

    mkr = template(
        "DMB_MKR",
        "Market Risk",
        "Monthly",
        "FixedRow",
        "dmb",
        [section("MKR_BOOK", "Trading Book Positions", 1), section("MKR_TOTAL", "Totals", 2)],
        [
            field("fx_gross_long", "FX Gross Long", "Money", "MKR_BOOK", True, 1),
            field("fx_gross_short", "FX Gross Short", "Money", "MKR_BOOK", True, 2),
            field("fx_net_position", "FX Net Position", "Money", "MKR_BOOK", True, 3),
            field("fx_capital_charge", "FX Capital Charge", "Money", "MKR_BOOK", True, 4),
            field("ir_gross_long", "IR Gross Long", "Money", "MKR_BOOK", True, 5),
            field("ir_gross_short", "IR Gross Short", "Money", "MKR_BOOK", True, 6),
            field("ir_net_position", "IR Net Position", "Money", "MKR_BOOK", True, 7),
            field("ir_capital_charge", "IR Capital Charge", "Money", "MKR_BOOK", True, 8),
            field("eq_gross_long", "Equity Gross Long", "Money", "MKR_BOOK", True, 9),
            field("eq_gross_short", "Equity Gross Short", "Money", "MKR_BOOK", True, 10),
            field("eq_net_position", "Equity Net Position", "Money", "MKR_BOOK", True, 11),
            field("eq_capital_charge", "Equity Capital Charge", "Money", "MKR_BOOK", True, 12),
            field("total_market_risk_charge", "Total Market Risk Charge", "Money", "MKR_TOTAL", True, 13),
        ],
        [
            {"formulaType": "Difference", "targetField": "fx_net_position", "sourceFields": ["fx_gross_long", "fx_gross_short"], "severity": "Error", "toleranceAmount": 1, "description": "FX net position"},
            {"formulaType": "Difference", "targetField": "ir_net_position", "sourceFields": ["ir_gross_long", "ir_gross_short"], "severity": "Error", "toleranceAmount": 1, "description": "IR net position"},
            {"formulaType": "Difference", "targetField": "eq_net_position", "sourceFields": ["eq_gross_long", "eq_gross_short"], "severity": "Error", "toleranceAmount": 1, "description": "EQ net position"},
            formula_sum("total_market_risk_charge", ["fx_capital_charge", "ir_capital_charge", "eq_capital_charge"], "Total market risk charge"),
        ],
    )
    templates.append(mkr)

    opr = template(
        "DMB_OPR",
        "Operational Risk (BIA)",
        "Quarterly",
        "FixedRow",
        "dmb",
        [section("OPR_BIA", "Basic Indicator Approach", 1)],
        [
            field("gross_income_year1", "Gross Income Year 1", "Money", "OPR_BIA", True, 1),
            field("gross_income_year2", "Gross Income Year 2", "Money", "OPR_BIA", True, 2),
            field("gross_income_year3", "Gross Income Year 3", "Money", "OPR_BIA", True, 3),
            field("positive_income_total", "Positive Income Total", "Money", "OPR_BIA", True, 4),
            field("positive_income_count", "Positive Income Count", "Integer", "OPR_BIA", True, 5),
            field("average_positive_income", "Average Positive Income", "Money", "OPR_BIA", True, 6),
            field("alpha_factor", "Alpha Factor", "Percentage", "OPR_BIA", True, 7, 0, 100, 2),
            field("operational_risk_capital_charge", "Operational Risk Capital Charge", "Money", "OPR_BIA", True, 8),
        ],
        [
            formula_sum("positive_income_total", ["gross_income_year1", "gross_income_year2", "gross_income_year3"], "Total positive income"),
            formula_ratio("average_positive_income", "positive_income_total", "positive_income_count", "Average positive income"),
            {
                "formulaType": "Custom",
                "targetField": "operational_risk_capital_charge",
                "sourceFields": [],
                "severity": "Warning",
                "description": "operational_risk_capital_charge = average_positive_income * alpha_factor",
            },
        ],
    )
    templates.append(opr)

    ifr = template(
        "DMB_IFR",
        "IFRS 9 Staging and ECL",
        "Monthly",
        "FixedRow",
        "dmb",
        [section("IFR_STAGE", "ECL and Staging", 1), section("IFR_MIG", "Stage Migration", 2)],
        [
            field("stage1_gross_carrying_amount", "Stage 1 Gross Carrying Amount", "Money", "IFR_STAGE", True, 1),
            field("stage2_gross_carrying_amount", "Stage 2 Gross Carrying Amount", "Money", "IFR_STAGE", True, 2),
            field("stage3_gross_carrying_amount", "Stage 3 Gross Carrying Amount", "Money", "IFR_STAGE", True, 3),
            field("pd_stage1", "PD Stage 1", "Decimal", "IFR_STAGE", True, 4, 0, 1, 6),
            field("lgd_stage1", "LGD Stage 1", "Decimal", "IFR_STAGE", True, 5, 0, 1, 6),
            field("ead_stage1", "EAD Stage 1", "Money", "IFR_STAGE", True, 6),
            field("pd_stage2", "PD Stage 2", "Decimal", "IFR_STAGE", True, 7, 0, 1, 6),
            field("lgd_stage2", "LGD Stage 2", "Decimal", "IFR_STAGE", True, 8, 0, 1, 6),
            field("ead_stage2", "EAD Stage 2", "Money", "IFR_STAGE", True, 9),
            field("pd_stage3", "PD Stage 3", "Decimal", "IFR_STAGE", True, 10, 0, 1, 6),
            field("lgd_stage3", "LGD Stage 3", "Decimal", "IFR_STAGE", True, 11, 0, 1, 6),
            field("ead_stage3", "EAD Stage 3", "Money", "IFR_STAGE", True, 12),
            field("stage1_ecl", "Stage 1 ECL", "Money", "IFR_STAGE", True, 13),
            field("stage2_ecl", "Stage 2 ECL", "Money", "IFR_STAGE", True, 14),
            field("stage3_ecl", "Stage 3 ECL", "Money", "IFR_STAGE", True, 15),
            field("total_ecl", "Total ECL", "Money", "IFR_STAGE", True, 16),
            field("stage1_net_carrying_amount", "Stage 1 Net Carrying Amount", "Money", "IFR_STAGE", True, 17),
            field("stage2_net_carrying_amount", "Stage 2 Net Carrying Amount", "Money", "IFR_STAGE", True, 18),
            field("stage3_net_carrying_amount", "Stage 3 Net Carrying Amount", "Money", "IFR_STAGE", True, 19),
            field("migration_opening_balance", "Migration Opening Balance", "Money", "IFR_MIG", True, 20),
            field("migration_transfers_in", "Migration Transfers In", "Money", "IFR_MIG", True, 21),
            field("migration_transfers_out", "Migration Transfers Out", "Money", "IFR_MIG", True, 22),
            field("migration_new_originations", "Migration New Originations", "Money", "IFR_MIG", True, 23),
            field("migration_derecognitions", "Migration Derecognitions", "Money", "IFR_MIG", True, 24),
            field("migration_closing_balance", "Migration Closing Balance", "Money", "IFR_MIG", True, 25),
        ],
        [
            formula_custom("stage1_ecl", ["pd_stage1", "lgd_stage1", "ead_stage1"], "ECL", "IFRS9 Stage 1 ECL"),
            formula_custom("stage2_ecl", ["pd_stage2", "lgd_stage2", "ead_stage2"], "ECL", "IFRS9 Stage 2 ECL"),
            formula_custom("stage3_ecl", ["pd_stage3", "lgd_stage3", "ead_stage3"], "ECL", "IFRS9 Stage 3 ECL"),
            formula_sum("total_ecl", ["stage1_ecl", "stage2_ecl", "stage3_ecl"], "Total ECL"),
            {"formulaType": "Difference", "targetField": "stage1_net_carrying_amount", "sourceFields": ["stage1_gross_carrying_amount", "stage1_ecl"], "severity": "Error", "toleranceAmount": 1, "description": "Stage 1 net carrying amount"},
            {"formulaType": "Difference", "targetField": "stage2_net_carrying_amount", "sourceFields": ["stage2_gross_carrying_amount", "stage2_ecl"], "severity": "Error", "toleranceAmount": 1, "description": "Stage 2 net carrying amount"},
            {"formulaType": "Difference", "targetField": "stage3_net_carrying_amount", "sourceFields": ["stage3_gross_carrying_amount", "stage3_ecl"], "severity": "Error", "toleranceAmount": 1, "description": "Stage 3 net carrying amount"},
            formula_sum("migration_closing_balance", ["migration_opening_balance", "migration_transfers_in", "migration_new_originations"], "Migration closing balance partial"),
        ],
    )
    templates.append(ifr)

    lcr = template(
        "DMB_LCR",
        "Liquidity Coverage Ratio",
        "Monthly",
        "FixedRow",
        "dmb",
        [section("LCR_HQLA", "HQLA", 1), section("LCR_FLOWS", "Cash Flows", 2)],
        [
            field("hqla_level1_cash", "HQLA Level 1 Cash", "Money", "LCR_HQLA", True, 1),
            field("hqla_level1_cbn_reserves", "HQLA Level 1 CBN Reserves", "Money", "LCR_HQLA", True, 2),
            field("hqla_level1_sovereign_bonds", "HQLA Level 1 Sovereign Bonds", "Money", "LCR_HQLA", True, 3),
            field("hqla_level2a_corp_bonds", "HQLA Level 2A Bonds", "Money", "LCR_HQLA", True, 4),
            field("hqla_level2b_assets", "HQLA Level 2B Assets", "Money", "LCR_HQLA", True, 5),
            field("total_hqla", "Total HQLA", "Money", "LCR_HQLA", True, 6),
            field("retail_stable_deposit_outflow", "Retail Stable Outflow", "Money", "LCR_FLOWS", True, 7),
            field("retail_less_stable_outflow", "Retail Less Stable Outflow", "Money", "LCR_FLOWS", True, 8),
            field("wholesale_outflow", "Wholesale Outflow", "Money", "LCR_FLOWS", True, 9),
            field("secured_funding_outflow", "Secured Funding Outflow", "Money", "LCR_FLOWS", True, 10),
            field("total_outflows", "Total Outflows", "Money", "LCR_FLOWS", True, 11),
            field("loan_inflows", "Loan Inflows", "Money", "LCR_FLOWS", True, 12),
            field("contractual_inflows", "Contractual Inflows", "Money", "LCR_FLOWS", True, 13),
            field("total_inflows_capped", "Total Inflows Capped", "Money", "LCR_FLOWS", True, 14),
            field("net_cash_outflow_30d", "Net Cash Outflow 30d", "Money", "LCR_FLOWS", True, 15),
            field("lcr_ratio", "LCR Ratio", "Percentage", "LCR_FLOWS", True, 16),
            field("lcr_minimum", "LCR Minimum", "Percentage", "LCR_FLOWS", True, 17, 0, 100, 2),
        ],
        [
            formula_sum("total_hqla", ["hqla_level1_cash", "hqla_level1_cbn_reserves", "hqla_level1_sovereign_bonds", "hqla_level2a_corp_bonds", "hqla_level2b_assets"], "Total HQLA"),
            formula_sum("total_outflows", ["retail_stable_deposit_outflow", "retail_less_stable_outflow", "wholesale_outflow", "secured_funding_outflow"], "Total outflows"),
            formula_sum("total_inflows_capped", ["loan_inflows", "contractual_inflows"], "Total inflows (capped)"),
            {"formulaType": "Difference", "targetField": "net_cash_outflow_30d", "sourceFields": ["total_outflows", "total_inflows_capped"], "severity": "Error", "toleranceAmount": 1, "description": "Net cash outflow"},
            formula_custom("lcr_ratio", ["total_hqla", "net_cash_outflow_30d"], "LCR", "LCR ratio custom function"),
            formula_compare("GreaterThanOrEqual", "lcr_ratio", "lcr_minimum", "LCR minimum threshold", "Error"),
        ],
    )
    templates.append(lcr)

    nsf = template(
        "DMB_NSF",
        "Net Stable Funding Ratio",
        "Monthly",
        "FixedRow",
        "dmb",
        [section("NSF_ASF", "Available Stable Funding", 1), section("NSF_RSF", "Required Stable Funding", 2)],
        [
            field("asf_regulatory_capital", "ASF Regulatory Capital", "Money", "NSF_ASF", True, 1),
            field("asf_stable_deposits", "ASF Stable Deposits", "Money", "NSF_ASF", True, 2),
            field("asf_less_stable_deposits", "ASF Less Stable Deposits", "Money", "NSF_ASF", True, 3),
            field("asf_wholesale_funding", "ASF Wholesale Funding", "Money", "NSF_ASF", True, 4),
            field("asf_total", "ASF Total", "Money", "NSF_ASF", True, 5),
            field("rsf_cash_assets", "RSF Cash Assets", "Money", "NSF_RSF", True, 6),
            field("rsf_sovereign_bonds", "RSF Sovereign Bonds", "Money", "NSF_RSF", True, 7),
            field("rsf_performing_loans", "RSF Performing Loans", "Money", "NSF_RSF", True, 8),
            field("rsf_npl", "RSF NPL", "Money", "NSF_RSF", True, 9),
            field("rsf_off_balance_sheet", "RSF Off-Balance Sheet", "Money", "NSF_RSF", True, 10),
            field("rsf_total", "RSF Total", "Money", "NSF_RSF", True, 11),
            field("nsfr_ratio", "NSFR Ratio", "Percentage", "NSF_RSF", True, 12),
            field("nsfr_minimum", "NSFR Minimum", "Percentage", "NSF_RSF", True, 13, 0, 100, 2),
        ],
        [
            formula_sum("asf_total", ["asf_regulatory_capital", "asf_stable_deposits", "asf_less_stable_deposits", "asf_wholesale_funding"], "ASF total"),
            formula_sum("rsf_total", ["rsf_cash_assets", "rsf_sovereign_bonds", "rsf_performing_loans", "rsf_npl", "rsf_off_balance_sheet"], "RSF total"),
            formula_custom("nsfr_ratio", ["asf_total", "rsf_total"], "NSFR", "NSFR custom function"),
            formula_compare("GreaterThanOrEqual", "nsfr_ratio", "nsfr_minimum", "NSFR minimum threshold", "Error"),
        ],
    )
    templates.append(nsf)

    npl = template(
        "DMB_NPL",
        "Non-Performing Loans",
        "Monthly",
        "FixedRow",
        "dmb",
        [section("NPL_SECTOR", "NPL by Sector", 1), section("NPL_TOTAL", "Totals", 2)],
        [],
        [],
    )
    sectors = ["oil_gas", "manufacturing", "agriculture", "real_estate", "general_commerce", "consumer", "government", "other"]
    order = 1
    for s in sectors:
        npl["fields"].extend(
            [
                field(f"{s}_gross_loans", f"{s} Gross Loans", "Money", "NPL_SECTOR", True, order),
                field(f"{s}_npl_amount", f"{s} NPL Amount", "Money", "NPL_SECTOR", True, order + 1),
                field(f"{s}_provision_amount", f"{s} Provision Amount", "Money", "NPL_SECTOR", True, order + 2),
            ]
        )
        order += 3
    npl["fields"].extend(
        [
            field("total_gross_loans", "Total Gross Loans", "Money", "NPL_TOTAL", True, order),
            field("total_npl_amount", "Total NPL Amount", "Money", "NPL_TOTAL", True, order + 1),
            field("total_provision_amount", "Total Provision Amount", "Money", "NPL_TOTAL", True, order + 2),
            field("aggregate_npl_ratio", "Aggregate NPL Ratio", "Percentage", "NPL_TOTAL", True, order + 3),
            field("provision_coverage_ratio", "Provision Coverage Ratio", "Percentage", "NPL_TOTAL", True, order + 4),
        ]
    )
    npl["formulas"].append(formula_sum("total_gross_loans", [f"{s}_gross_loans" for s in sectors], "Total gross loans"))
    npl["formulas"].append(formula_sum("total_npl_amount", [f"{s}_npl_amount" for s in sectors], "Total NPL amount"))
    npl["formulas"].append(formula_sum("total_provision_amount", [f"{s}_provision_amount" for s in sectors], "Total provision amount"))
    npl["formulas"].append(formula_custom("aggregate_npl_ratio", ["total_npl_amount", "total_gross_loans"], "NPL_RATIO", "Aggregate NPL ratio"))
    npl["formulas"].append(formula_ratio("provision_coverage_ratio", "total_provision_amount", "total_npl_amount", "Provision coverage ratio", "Warning"))
    templates.append(npl)

    dep = template(
        "DMB_DEP",
        "Deposit Structure",
        "Monthly",
        "FixedRow",
        "dmb",
        [section("DEP_TYPES", "Deposit Types", 1), section("DEP_TOTAL", "Totals", 2)],
        [
            field("demand_deposits", "Demand Deposits", "Money", "DEP_TYPES", True, 1),
            field("savings_deposits", "Savings Deposits", "Money", "DEP_TYPES", True, 2),
            field("time_deposits", "Time Deposits", "Money", "DEP_TYPES", True, 3),
            field("domiciliary_deposits", "Domiciliary Deposits", "Money", "DEP_TYPES", True, 4),
            field("wholesale_deposits", "Wholesale Deposits", "Money", "DEP_TYPES", True, 5),
            field("insured_deposit_band1", "Insured Deposits Band 1", "Money", "DEP_TYPES", True, 6),
            field("insured_deposit_band2", "Insured Deposits Band 2", "Money", "DEP_TYPES", True, 7),
            field("insured_deposit_band3", "Insured Deposits Band 3", "Money", "DEP_TYPES", True, 8),
            field("insurable_deposits", "Insurable Deposits", "Money", "DEP_TOTAL", True, 9),
            field("total_deposits", "Total Deposits", "Money", "DEP_TOTAL", True, 10),
        ],
        [
            formula_sum("insurable_deposits", ["insured_deposit_band1", "insured_deposit_band2", "insured_deposit_band3"], "Insurable deposits"),
            formula_sum("total_deposits", ["demand_deposits", "savings_deposits", "time_deposits", "domiciliary_deposits", "wholesale_deposits"], "Total deposits"),
            formula_compare("GreaterThanOrEqual", "total_deposits", "insurable_deposits", "Total deposits >= insurable deposits", "Error"),
        ],
    )
    templates.append(dep)

    lnd = template(
        "DMB_LND",
        "Lending Portfolio",
        "Monthly",
        "FixedRow",
        "dmb",
        [section("LND_SECTOR", "Sector Lending", 1), section("LND_LIMITS", "Concentration Limits", 2)],
        [
            field("lending_oil_gas", "Lending Oil and Gas", "Money", "LND_SECTOR", True, 1),
            field("lending_manufacturing", "Lending Manufacturing", "Money", "LND_SECTOR", True, 2),
            field("lending_agriculture", "Lending Agriculture", "Money", "LND_SECTOR", True, 3),
            field("lending_real_estate", "Lending Real Estate", "Money", "LND_SECTOR", True, 4),
            field("lending_consumer", "Lending Consumer", "Money", "LND_SECTOR", True, 5),
            field("lending_government", "Lending Government", "Money", "LND_SECTOR", True, 6),
            field("total_lending", "Total Lending", "Money", "LND_SECTOR", True, 7),
            field("single_obligor_exposure", "Single Obligor Exposure", "Money", "LND_LIMITS", True, 8),
            field("single_obligor_limit", "Single Obligor Limit", "Money", "LND_LIMITS", True, 9),
            field("largest_sector_exposure", "Largest Sector Exposure", "Money", "LND_LIMITS", True, 10),
            field("largest_sector_limit", "Largest Sector Limit", "Money", "LND_LIMITS", True, 11),
        ],
        [
            formula_sum("total_lending", ["lending_oil_gas", "lending_manufacturing", "lending_agriculture", "lending_real_estate", "lending_consumer", "lending_government"], "Total lending by sector"),
            formula_compare("LessThanOrEqual", "single_obligor_exposure", "single_obligor_limit", "Single obligor concentration limit", "Error"),
            formula_compare("LessThanOrEqual", "largest_sector_exposure", "largest_sector_limit", "Sector concentration limit", "Warning"),
        ],
    )
    templates.append(lnd)

    gov = template(
        "DMB_GOV",
        "Corporate Governance",
        "Quarterly",
        "FixedRow",
        "dmb",
        [section("GOV_COMPLIANCE", "Governance Compliance", 1)],
        [
            field("board_size", "Board Size", "Integer", "GOV_COMPLIANCE", True, 1),
            field("independent_directors", "Independent Directors", "Integer", "GOV_COMPLIANCE", True, 2),
            field("risk_committee_meetings", "Risk Committee Meetings", "Integer", "GOV_COMPLIANCE", True, 3),
            field("audit_committee_meetings", "Audit Committee Meetings", "Integer", "GOV_COMPLIANCE", True, 4),
            field("compliance_breaches", "Compliance Breaches", "Integer", "GOV_COMPLIANCE", True, 5),
            field("training_hours", "Board Training Hours", "Integer", "GOV_COMPLIANCE", True, 6),
        ],
        [
            formula_compare("GreaterThanOrEqual", "board_size", "independent_directors", "Board should not be smaller than independent directors", "Info")
        ],
    )
    templates.append(gov)

    aml = template(
        "DMB_AML",
        "AML and CFT",
        "Monthly",
        "FixedRow",
        "dmb",
        [section("AML_METRICS", "AML Metrics", 1)],
        [
            field("str_count", "STR Count", "Integer", "AML_METRICS", True, 1),
            field("ctr_count", "CTR Count", "Integer", "AML_METRICS", True, 2),
            field("pep_alerts", "PEP Alerts", "Integer", "AML_METRICS", True, 3),
            field("tfs_matches", "TFS Matches", "Integer", "AML_METRICS", True, 4),
            field("kyc_exceptions", "KYC Exceptions", "Integer", "AML_METRICS", True, 5),
            field("aml_training_staff", "AML Trained Staff", "Integer", "AML_METRICS", True, 6),
            field("aml_total_alerts", "AML Total Alerts", "Integer", "AML_METRICS", True, 7),
        ],
        [
            formula_sum("aml_total_alerts", ["str_count", "ctr_count", "pep_alerts", "tfs_matches"], "Total AML alerts", "Warning", 0)
        ],
    )
    templates.append(aml)

    fin = template(
        "DMB_FIN",
        "Financial Statements",
        "Quarterly",
        "FixedRow",
        "dmb",
        [section("FIN_BAL", "Balance Sheet", 1), section("FIN_PL", "Profit and Loss", 2)],
        [
            field("cash_and_balances", "Cash and Balances", "Money", "FIN_BAL", True, 1),
            field("investment_securities", "Investment Securities", "Money", "FIN_BAL", True, 2),
            field("gross_loans_and_advances", "Gross Loans and Advances", "Money", "FIN_BAL", True, 3),
            field("loan_loss_provisions", "Loan Loss Provisions", "Money", "FIN_BAL", True, 4),
            field("other_assets", "Other Assets", "Money", "FIN_BAL", True, 5),
            field("total_assets", "Total Assets", "Money", "FIN_BAL", True, 6),
            field("customer_deposits", "Customer Deposits", "Money", "FIN_BAL", True, 7),
            field("wholesale_funding", "Wholesale Funding", "Money", "FIN_BAL", True, 8),
            field("other_liabilities", "Other Liabilities", "Money", "FIN_BAL", True, 9),
            field("total_liabilities", "Total Liabilities", "Money", "FIN_BAL", True, 10),
            field("total_equity", "Total Equity", "Money", "FIN_BAL", True, 11),
            field("interest_income", "Interest Income", "Money", "FIN_PL", True, 12),
            field("interest_expense", "Interest Expense", "Money", "FIN_PL", True, 13),
            field("net_interest_income", "Net Interest Income", "Money", "FIN_PL", True, 14),
            field("non_interest_income", "Non-interest Income", "Money", "FIN_PL", True, 15),
            field("operating_expenses", "Operating Expenses", "Money", "FIN_PL", True, 16),
            field("profit_before_tax", "Profit Before Tax", "Money", "FIN_PL", True, 17),
            field("tax_expense", "Tax Expense", "Money", "FIN_PL", True, 18),
            field("net_profit", "Net Profit", "Money", "FIN_PL", True, 19),
        ],
        [
            formula_sum("total_assets", ["cash_and_balances", "investment_securities", "gross_loans_and_advances", "other_assets"], "Total assets"),
            formula_sum("total_liabilities", ["customer_deposits", "wholesale_funding", "other_liabilities"], "Total liabilities"),
            {"formulaType": "Difference", "targetField": "total_equity", "sourceFields": ["total_assets", "total_liabilities"], "severity": "Error", "toleranceAmount": 1, "description": "Equity equals assets less liabilities"},
            {"formulaType": "Difference", "targetField": "net_interest_income", "sourceFields": ["interest_income", "interest_expense"], "severity": "Error", "toleranceAmount": 1, "description": "Net interest income"},
            formula_sum("profit_before_tax", ["net_interest_income", "non_interest_income"], "PBT before expenses", "Warning"),
            {"formulaType": "Difference", "targetField": "net_profit", "sourceFields": ["profit_before_tax", "tax_expense"], "severity": "Error", "toleranceAmount": 1, "description": "Net profit"},
        ],
    )
    templates.append(fin)

    dic = template(
        "DMB_DIC",
        "Data Dictionary and Validation Summary",
        "Quarterly",
        "FixedRow",
        "dmb",
        [section("DIC_SUM", "Summary", 1)],
        [
            field("total_field_count", "Total Field Count", "Integer", "DIC_SUM", True, 1),
            field("total_validation_rules", "Total Validation Rules", "Integer", "DIC_SUM", True, 2),
            field("total_cross_sheet_rules", "Total Cross-sheet Rules", "Integer", "DIC_SUM", True, 3),
            field("critical_errors", "Critical Errors", "Integer", "DIC_SUM", True, 4),
            field("warnings", "Warnings", "Integer", "DIC_SUM", True, 5),
            field("info_messages", "Info Messages", "Integer", "DIC_SUM", True, 6),
            field("completion_percentage", "Completion Percentage", "Percentage", "DIC_SUM", True, 7),
        ],
        [
            formula_compare("LessThanOrEqual", "critical_errors", "warnings", "Critical errors should not exceed warnings", "Info")
        ],
    )
    templates.append(dic)

    dmb_targets = {
        "DMB_COV": (20, 6),
        "DMB_CAP": (55, 26),
        "DMB_CRR": (48, 20),
        "DMB_MKR": (34, 14),
        "DMB_OPR": (20, 8),
        "DMB_IFR": (52, 28),
        "DMB_LCR": (40, 20),
        "DMB_NSF": (36, 16),
        "DMB_NPL": (44, 16),
        "DMB_DEP": (32, 12),
        "DMB_LND": (38, 14),
        "DMB_GOV": (22, 6),
        "DMB_AML": (26, 8),
        "DMB_FIN": (40, 18),
        "DMB_DIC": (20, 6),
    }

    for t in templates:
        tf, tfo = dmb_targets[t["returnCode"]]
        pad_template(t, tf, tfo)

    definition = {
        "moduleCode": "DMB_BASEL3",
        "moduleVersion": "1.0.0",
        "description": "DMB Basel III prudential returns (capital, risk, liquidity, IFRS9, AML, governance).",
        "templates": templates,
        "interModuleDataFlows": [
            {"sourceTemplate": "DMB_CAP", "sourceField": "total_regulatory_capital", "targetModule": "NDIC_RETURNS", "targetTemplate": "NDIC_CAP", "targetField": "regulatory_capital", "transformationType": "DirectCopy", "description": "DMB capital to NDIC capital sheet"},
            {"sourceTemplate": "DMB_CAP", "sourceField": "car_ratio", "targetModule": "NDIC_RETURNS", "targetTemplate": "NDIC_CAP", "targetField": "car_ratio", "transformationType": "DirectCopy", "description": "DMB CAR to NDIC"},
            {"sourceTemplate": "DMB_DEP", "sourceField": "total_deposits", "targetModule": "NDIC_RETURNS", "targetTemplate": "NDIC_DEP", "targetField": "total_deposits", "transformationType": "DirectCopy", "description": "DMB deposits to NDIC deposit liabilities"},
            {"sourceTemplate": "DMB_DEP", "sourceField": "insurable_deposits", "targetModule": "NDIC_RETURNS", "targetTemplate": "NDIC_DEP", "targetField": "insurable_deposits", "transformationType": "DirectCopy", "description": "DMB insurable deposits to NDIC DPAS"},
            {"sourceTemplate": "DMB_NPL", "sourceField": "total_npl_amount", "targetModule": "NDIC_RETURNS", "targetTemplate": "NDIC_ASQ", "targetField": "npl_amount", "transformationType": "DirectCopy", "description": "DMB NPL amount to NDIC asset quality"},
            {"sourceTemplate": "DMB_NPL", "sourceField": "total_provision_amount", "targetModule": "NDIC_RETURNS", "targetTemplate": "NDIC_ASQ", "targetField": "provision_amount", "transformationType": "DirectCopy", "description": "DMB provisions to NDIC asset quality"},
            {"sourceTemplate": "DMB_AML", "sourceField": "str_count", "targetModule": "NFIU_AML", "targetTemplate": "NFIU_STR", "targetField": "str_filed_count", "transformationType": "DirectCopy", "description": "STR count to NFIU"},
            {"sourceTemplate": "DMB_AML", "sourceField": "ctr_count", "targetModule": "NFIU_AML", "targetTemplate": "NFIU_CTR", "targetField": "ctr_filed_count", "transformationType": "DirectCopy", "description": "CTR count to NFIU"},
            {"sourceTemplate": "DMB_AML", "sourceField": "pep_alerts", "targetModule": "NFIU_AML", "targetTemplate": "NFIU_PEP", "targetField": "pep_alert_count", "transformationType": "DirectCopy", "description": "PEP alerts to NFIU"},
            {"sourceTemplate": "DMB_LND", "sourceField": "lending_oil_gas", "targetModule": "ESG_CLIMATE", "targetTemplate": "ESG_FINANCED_EMISSIONS", "targetField": "oil_gas_exposure", "transformationType": "DirectCopy", "description": "Sector lending for financed emissions"},
        ],
    }

    add_cross_rules(definition, 28)
    return definition


def ndic_module_definition():
    templates = [
        template("NDIC_COV", "NDIC Cover", "Quarterly", "FixedRow", "ndic", [section("COV", "Cover", 1)], [
            field("reporting_year", "Reporting Year", "Integer", "COV", True, 1),
            field("reporting_quarter", "Reporting Quarter", "Integer", "COV", True, 2),
            field("institution_name", "Institution Name", "Text", "COV", True, 3),
            field("prepared_by", "Prepared By", "Text", "COV", True, 4),
            field("approved_by", "Approved By", "Text", "COV", True, 5),
        ], []),
        template("NDIC_FIN", "NDIC Financial Summary", "Quarterly", "FixedRow", "ndic", [section("FIN", "Financial Summary", 1)], [
            field("total_assets", "Total Assets", "Money", "FIN", True, 1),
            field("total_liabilities", "Total Liabilities", "Money", "FIN", True, 2),
            field("total_equity", "Total Equity", "Money", "FIN", True, 3),
            field("regulatory_capital", "Regulatory Capital", "Money", "FIN", True, 4),
            field("npl_amount", "NPL Amount", "Money", "FIN", True, 5),
            field("npl_ratio", "NPL Ratio", "Percentage", "FIN", True, 6),
        ], [
            {"formulaType": "Difference", "targetField": "total_equity", "sourceFields": ["total_assets", "total_liabilities"], "severity": "Warning", "toleranceAmount": 1, "description": "Equity check"}
        ]),
        template("NDIC_DEP", "Deposit Liabilities", "Quarterly", "FixedRow", "ndic", [section("DEP", "Deposits", 1)], [
            field("demand_deposits", "Demand Deposits", "Money", "DEP", True, 1),
            field("savings_deposits", "Savings Deposits", "Money", "DEP", True, 2),
            field("time_deposits", "Time Deposits", "Money", "DEP", True, 3),
            field("total_deposits", "Total Deposits", "Money", "DEP", True, 4),
            field("insurable_deposits", "Insurable Deposits", "Money", "DEP", True, 5),
            field("uninsured_deposits", "Uninsured Deposits", "Money", "DEP", True, 6),
        ], [
            formula_sum("total_deposits", ["demand_deposits", "savings_deposits", "time_deposits"], "Total deposits"),
            {"formulaType": "Difference", "targetField": "uninsured_deposits", "sourceFields": ["total_deposits", "insurable_deposits"], "severity": "Warning", "toleranceAmount": 1, "description": "Uninsured deposits"}
        ]),
        template("NDIC_DPA", "DPAS Premium Assessment", "Quarterly", "FixedRow", "ndic", [section("DPA", "Premium Assessment", 1)], [
            field("insurable_deposits", "Insurable Deposits", "Money", "DPA", True, 1),
            field("assessment_rate", "Assessment Rate", "Decimal", "DPA", True, 2, 0, 1, 6),
            field("minimum_premium", "Minimum Premium", "Money", "DPA", True, 3),
            field("premium_raw", "Raw Premium", "Money", "DPA", True, 4),
            field("premium_assessment", "Premium Assessment", "Money", "DPA", True, 5),
        ], [
            {"formulaType": "Custom", "targetField": "premium_raw", "sourceFields": [], "severity": "Warning", "description": "premium_raw = insurable_deposits * assessment_rate"},
            formula_compare("GreaterThanOrEqual", "premium_assessment", "minimum_premium", "Premium assessment floor"),
            formula_compare("GreaterThanOrEqual", "premium_assessment", "premium_raw", "Premium should not be below raw premium", "Warning"),
        ]),
        template("NDIC_ASQ", "Asset Quality", "Quarterly", "FixedRow", "ndic", [section("ASQ", "Asset Quality", 1)], [
            field("gross_loans", "Gross Loans", "Money", "ASQ", True, 1),
            field("npl_amount", "NPL Amount", "Money", "ASQ", True, 2),
            field("provision_amount", "Provision Amount", "Money", "ASQ", True, 3),
            field("npl_ratio", "NPL Ratio", "Percentage", "ASQ", True, 4),
            field("provision_coverage", "Provision Coverage", "Percentage", "ASQ", True, 5),
        ], [
            formula_custom("npl_ratio", ["npl_amount", "gross_loans"], "NPL_RATIO", "NPL ratio"),
            formula_ratio("provision_coverage", "provision_amount", "npl_amount", "Provision coverage", "Warning"),
        ]),
        template("NDIC_CAP", "Capital Position", "Quarterly", "FixedRow", "ndic", [section("CAP", "Capital", 1)], [
            field("regulatory_capital", "Regulatory Capital", "Money", "CAP", True, 1),
            field("total_rwa", "Total RWA", "Money", "CAP", True, 2),
            field("car_ratio", "CAR Ratio", "Percentage", "CAP", True, 3),
            field("minimum_car", "Minimum CAR", "Percentage", "CAP", True, 4, 0, 100, 2),
        ], [
            formula_custom("car_ratio", ["regulatory_capital", "regulatory_capital", "total_rwa"], "CAR", "CAR approximation from NDIC feed"),
            formula_compare("GreaterThanOrEqual", "car_ratio", "minimum_car", "CAR threshold"),
        ]),
        template("NDIC_LIQ", "Liquidity Position", "Quarterly", "FixedRow", "ndic", [section("LIQ", "Liquidity", 1)], [
            field("liquid_assets", "Liquid Assets", "Money", "LIQ", True, 1),
            field("short_term_liabilities", "Short-term Liabilities", "Money", "LIQ", True, 2),
            field("liquidity_ratio", "Liquidity Ratio", "Percentage", "LIQ", True, 3),
            field("liquidity_minimum", "Liquidity Minimum", "Percentage", "LIQ", True, 4, 0, 100, 2),
        ], [
            formula_ratio("liquidity_ratio", "liquid_assets", "short_term_liabilities", "Liquidity ratio"),
            formula_compare("GreaterThanOrEqual", "liquidity_ratio", "liquidity_minimum", "Liquidity threshold", "Warning"),
        ]),
        template("NDIC_EWS", "Early Warning Signals", "Quarterly", "FixedRow", "ndic", [section("EWS", "Early Warning", 1)], [
            field("capital_score", "Capital Score", "Percentage", "EWS", True, 1),
            field("asset_quality_score", "Asset Quality Score", "Percentage", "EWS", True, 2),
            field("liquidity_score", "Liquidity Score", "Percentage", "EWS", True, 3),
            field("governance_score", "Governance Score", "Percentage", "EWS", True, 4),
            field("composite_risk_score", "Composite Risk Score", "Percentage", "EWS", True, 5),
        ], [
            formula_sum("composite_risk_score", ["capital_score", "asset_quality_score", "liquidity_score", "governance_score"], "Composite score", "Warning", 5),
        ]),
        template("NDIC_PAY", "Payout Readiness", "Quarterly", "FixedRow", "ndic", [section("PAY", "Payout Readiness", 1)], [
            field("eligible_depositors", "Eligible Depositors", "Integer", "PAY", True, 1),
            field("validated_depositor_records", "Validated Depositor Records", "Integer", "PAY", True, 2),
            field("payout_simulation_value", "Payout Simulation Value", "Money", "PAY", True, 3),
            field("readiness_ratio", "Readiness Ratio", "Percentage", "PAY", True, 4),
        ], [
            formula_ratio("readiness_ratio", "validated_depositor_records", "eligible_depositors", "Readiness ratio", "Warning"),
        ]),
        template("NDIC_GOV", "Governance and Compliance", "Quarterly", "FixedRow", "ndic", [section("GOV", "Governance", 1)], [
            field("board_meetings", "Board Meetings", "Integer", "GOV", True, 1),
            field("policy_breaches", "Policy Breaches", "Integer", "GOV", True, 2),
            field("remediation_closed", "Remediations Closed", "Integer", "GOV", True, 3),
            field("outstanding_findings", "Outstanding Findings", "Integer", "GOV", True, 4),
        ], []),
        template("NDIC_DIC", "Data Dictionary", "Quarterly", "FixedRow", "ndic", [section("DIC", "Data Dictionary", 1)], [
            field("field_count", "Field Count", "Integer", "DIC", True, 1),
            field("formula_count", "Formula Count", "Integer", "DIC", True, 2),
            field("cross_sheet_rule_count", "Cross-sheet Rule Count", "Integer", "DIC", True, 3),
        ], []),
    ]

    targets = {
        "NDIC_COV": (20, 6),
        "NDIC_FIN": (26, 8),
        "NDIC_DEP": (26, 8),
        "NDIC_DPA": (22, 8),
        "NDIC_ASQ": (24, 8),
        "NDIC_CAP": (22, 6),
        "NDIC_LIQ": (20, 6),
        "NDIC_EWS": (22, 8),
        "NDIC_PAY": (24, 6),
        "NDIC_GOV": (18, 4),
        "NDIC_DIC": (14, 3),
    }

    for t in templates:
        tf, tfo = targets[t["returnCode"]]
        pad_template(t, tf, tfo)

    definition = {
        "moduleCode": "NDIC_RETURNS",
        "moduleVersion": "1.0.0",
        "description": "NDIC cross-cutting deposit insurance returns with premium assessment and early warning indicators.",
        "templates": templates,
        "interModuleDataFlows": [
            {"sourceTemplate": "NDIC_DEP", "sourceField": "insurable_deposits", "targetModule": "FATF_EVAL", "targetTemplate": "FATF_RISK", "targetField": "deposit_exposure", "transformationType": "DirectCopy", "description": "Deposit exposure feed for FATF risk evaluation"},
            {"sourceTemplate": "NDIC_ASQ", "sourceField": "npl_ratio", "targetModule": "FATF_EVAL", "targetTemplate": "FATF_RISK", "targetField": "asset_quality_risk_score", "transformationType": "DirectCopy", "description": "Asset quality indicator feed"},
        ],
    }

    add_cross_rules(definition, 18)
    return definition


def psp_module_definition():
    templates = [
        template("PSP_COV", "PSP Cover", "Monthly", "FixedRow", "psp", [section("COV", "Cover", 1)], [
            field("reporting_year", "Reporting Year", "Integer", "COV", True, 1),
            field("reporting_month", "Reporting Month", "Integer", "COV", True, 2),
            field("licence_category", "Licence Category", "Text", "COV", True, 3),
            field("institution_name", "Institution Name", "Text", "COV", True, 4),
        ], []),
        template("PSP_TRX", "Transaction Volumes by Channel", "Monthly", "FixedRow", "psp", [section("TRX", "Transaction Channels", 1)], [
            field("ussd_txn_count", "USSD Transaction Count", "Integer", "TRX", True, 1),
            field("card_txn_count", "Card Transaction Count", "Integer", "TRX", True, 2),
            field("mobile_txn_count", "Mobile Transaction Count", "Integer", "TRX", True, 3),
            field("web_txn_count", "Web Transaction Count", "Integer", "TRX", True, 4),
            field("pos_txn_count", "POS Transaction Count", "Integer", "TRX", True, 5),
            field("atm_txn_count", "ATM Transaction Count", "Integer", "TRX", True, 6),
            field("total_txn_count", "Total Transaction Count", "Integer", "TRX", True, 7),
            field("ussd_txn_value", "USSD Transaction Value", "Money", "TRX", True, 8),
            field("card_txn_value", "Card Transaction Value", "Money", "TRX", True, 9),
            field("mobile_txn_value", "Mobile Transaction Value", "Money", "TRX", True, 10),
            field("web_txn_value", "Web Transaction Value", "Money", "TRX", True, 11),
            field("pos_txn_value", "POS Transaction Value", "Money", "TRX", True, 12),
            field("atm_txn_value", "ATM Transaction Value", "Money", "TRX", True, 13),
            field("total_txn_value", "Total Transaction Value", "Money", "TRX", True, 14),
        ], [
            formula_sum("total_txn_count", ["ussd_txn_count", "card_txn_count", "mobile_txn_count", "web_txn_count", "pos_txn_count", "atm_txn_count"], "Channel count total"),
            formula_sum("total_txn_value", ["ussd_txn_value", "card_txn_value", "mobile_txn_value", "web_txn_value", "pos_txn_value", "atm_txn_value"], "Channel value total"),
        ]),
        template("PSP_FLT", "Float Management", "Monthly", "FixedRow", "psp", [section("FLT", "Float", 1)], [
            field("customer_float_balance", "Customer Float Balance", "Money", "FLT", True, 1),
            field("settlement_float_balance", "Settlement Float Balance", "Money", "FLT", True, 2),
            field("interest_earned_on_float", "Interest Earned", "Money", "FLT", True, 3),
            field("total_float_balance", "Total Float Balance", "Money", "FLT", True, 4),
        ], [
            formula_sum("total_float_balance", ["customer_float_balance", "settlement_float_balance"], "Total float balance"),
        ]),
        template("PSP_AGT", "Agent Network", "Monthly", "FixedRow", "psp", [section("AGT", "Agent Network", 1)], [
            field("registered_agents", "Registered Agents", "Integer", "AGT", True, 1),
            field("active_agents", "Active Agents", "Integer", "AGT", True, 2),
            field("inactive_agents", "Inactive Agents", "Integer", "AGT", True, 3),
            field("agent_activity_rate", "Agent Activity Rate", "Percentage", "AGT", True, 4),
        ], [
            formula_ratio("agent_activity_rate", "active_agents", "registered_agents", "Agent activity rate", "Warning"),
            {"formulaType": "Difference", "targetField": "inactive_agents", "sourceFields": ["registered_agents", "active_agents"], "severity": "Warning", "toleranceAmount": 1, "description": "Inactive agents"},
        ]),
        template("PSP_FRD", "Fraud Metrics", "Monthly", "FixedRow", "psp", [section("FRD", "Fraud", 1)], [
            field("fraud_case_count", "Fraud Cases", "Integer", "FRD", True, 1),
            field("fraud_amount", "Fraud Amount", "Money", "FRD", True, 2),
            field("fraud_recovered_amount", "Fraud Recovered", "Money", "FRD", True, 3),
            field("fraud_recovery_rate", "Fraud Recovery Rate", "Percentage", "FRD", True, 4),
        ], [
            formula_ratio("fraud_recovery_rate", "fraud_recovered_amount", "fraud_amount", "Fraud recovery rate", "Warning"),
        ]),
        template("PSP_CAP", "Capital", "Quarterly", "FixedRow", "psp", [section("CAP", "Capital", 1)], [
            field("paid_up_capital", "Paid Up Capital", "Money", "CAP", True, 1),
            field("capital_requirement", "Capital Requirement", "Money", "CAP", True, 2),
            field("capital_buffer", "Capital Buffer", "Money", "CAP", True, 3),
        ], [
            {"formulaType": "Difference", "targetField": "capital_buffer", "sourceFields": ["paid_up_capital", "capital_requirement"], "severity": "Error", "toleranceAmount": 1, "description": "Capital buffer"},
            formula_compare("GreaterThanOrEqual", "paid_up_capital", "capital_requirement", "Capital adequacy threshold", "Error"),
        ]),
        template("PSP_FIN", "Financial", "Monthly", "FixedRow", "psp", [section("FIN", "Financial", 1)], [
            field("operating_income", "Operating Income", "Money", "FIN", True, 1),
            field("operating_expense", "Operating Expense", "Money", "FIN", True, 2),
            field("profit_before_tax", "Profit Before Tax", "Money", "FIN", True, 3),
            field("tax", "Tax", "Money", "FIN", True, 4),
            field("net_profit", "Net Profit", "Money", "FIN", True, 5),
        ], [
            {"formulaType": "Difference", "targetField": "profit_before_tax", "sourceFields": ["operating_income", "operating_expense"], "severity": "Error", "toleranceAmount": 1, "description": "PBT"},
            {"formulaType": "Difference", "targetField": "net_profit", "sourceFields": ["profit_before_tax", "tax"], "severity": "Error", "toleranceAmount": 1, "description": "Net profit"},
        ]),
        template("PSP_KYC", "KYC AML", "Monthly", "FixedRow", "psp", [section("KYC", "KYC AML", 1)], [
            field("new_customers", "New Customers", "Integer", "KYC", True, 1),
            field("kyc_completed", "KYC Completed", "Integer", "KYC", True, 2),
            field("str_count", "STR Count", "Integer", "KYC", True, 3),
            field("ctr_count", "CTR Count", "Integer", "KYC", True, 4),
            field("pep_alerts", "PEP Alerts", "Integer", "KYC", True, 5),
            field("kyc_completion_rate", "KYC Completion Rate", "Percentage", "KYC", True, 6),
        ], [
            formula_ratio("kyc_completion_rate", "kyc_completed", "new_customers", "KYC completion rate", "Warning"),
        ]),
        template("PSP_TEC", "Technology Uptime", "Monthly", "FixedRow", "psp", [section("TEC", "Technology", 1)], [
            field("platform_uptime_percent", "Platform Uptime Percent", "Percentage", "TEC", True, 1),
            field("critical_incidents", "Critical Incidents", "Integer", "TEC", True, 2),
            field("mean_time_to_resolve", "MTTR", "Decimal", "TEC", True, 3),
        ], []),
        template("PSP_CMP", "Customer Complaints", "Monthly", "FixedRow", "psp", [section("CMP", "Complaints", 1)], [
            field("complaints_received", "Complaints Received", "Integer", "CMP", True, 1),
            field("complaints_resolved", "Complaints Resolved", "Integer", "CMP", True, 2),
            field("complaint_resolution_rate", "Complaint Resolution Rate", "Percentage", "CMP", True, 3),
        ], [
            formula_ratio("complaint_resolution_rate", "complaints_resolved", "complaints_received", "Complaint resolution rate", "Warning"),
        ]),
        template("PSP_DRP", "Dispute Resolution", "Monthly", "FixedRow", "psp", [section("DRP", "Disputes", 1)], [
            field("disputes_opened", "Disputes Opened", "Integer", "DRP", True, 1),
            field("disputes_closed", "Disputes Closed", "Integer", "DRP", True, 2),
            field("disputes_pending", "Disputes Pending", "Integer", "DRP", True, 3),
        ], [
            {"formulaType": "Difference", "targetField": "disputes_pending", "sourceFields": ["disputes_opened", "disputes_closed"], "severity": "Warning", "toleranceAmount": 1, "description": "Pending disputes"},
        ]),
        template("PSP_REG", "Regulatory Compliance", "Monthly", "FixedRow", "psp", [section("REG", "Regulatory", 1)], [
            field("returns_due", "Returns Due", "Integer", "REG", True, 1),
            field("returns_submitted", "Returns Submitted", "Integer", "REG", True, 2),
            field("returns_on_time", "Returns On Time", "Integer", "REG", True, 3),
        ], []),
        template("PSP_GOV", "Governance", "Quarterly", "FixedRow", "psp", [section("GOV", "Governance", 1)], [
            field("board_meetings", "Board Meetings", "Integer", "GOV", True, 1),
            field("internal_audit_findings", "Internal Audit Findings", "Integer", "GOV", True, 2),
            field("resolved_audit_findings", "Resolved Audit Findings", "Integer", "GOV", True, 3),
        ], []),
        template("PSP_DIC", "Data Dictionary", "Quarterly", "FixedRow", "psp", [section("DIC", "Data Dictionary", 1)], [
            field("field_count", "Field Count", "Integer", "DIC", True, 1),
            field("formula_count", "Formula Count", "Integer", "DIC", True, 2),
        ], []),
    ]

    targets = {
        "PSP_COV": (16, 4),
        "PSP_TRX": (24, 8),
        "PSP_FLT": (22, 8),
        "PSP_AGT": (18, 6),
        "PSP_FRD": (18, 6),
        "PSP_CAP": (18, 6),
        "PSP_FIN": (14, 4),
        "PSP_KYC": (16, 6),
        "PSP_TEC": (16, 4),
        "PSP_CMP": (16, 4),
        "PSP_DRP": (14, 4),
        "PSP_REG": (14, 4),
        "PSP_GOV": (12, 3),
        "PSP_DIC": (10, 2),
    }

    for t in templates:
        tf, tfo = targets[t["returnCode"]]
        pad_template(t, tf, tfo)

    definition = {
        "moduleCode": "PSP_FINTECH",
        "moduleVersion": "1.0.0",
        "description": "PSP and fintech regulatory returns for CBN licence categories.",
        "templates": templates,
        "interModuleDataFlows": [
            {"sourceTemplate": "PSP_FLT", "sourceField": "total_float_balance", "targetModule": "NDIC_RETURNS", "targetTemplate": "NDIC_DEP", "targetField": "total_deposits", "transformationType": "DirectCopy", "description": "Float balance mapped to NDIC deposit equivalent"},
            {"sourceTemplate": "PSP_FLT", "sourceField": "customer_float_balance", "targetModule": "NDIC_RETURNS", "targetTemplate": "NDIC_DEP", "targetField": "insurable_deposits", "transformationType": "DirectCopy", "description": "Customer float to insurable deposit base"},
            {"sourceTemplate": "PSP_KYC", "sourceField": "str_count", "targetModule": "NFIU_AML", "targetTemplate": "NFIU_STR", "targetField": "str_filed_count", "transformationType": "DirectCopy", "description": "PSP STR to NFIU"},
            {"sourceTemplate": "PSP_KYC", "sourceField": "ctr_count", "targetModule": "NFIU_AML", "targetTemplate": "NFIU_CTR", "targetField": "ctr_filed_count", "transformationType": "DirectCopy", "description": "PSP CTR to NFIU"},
            {"sourceTemplate": "PSP_KYC", "sourceField": "pep_alerts", "targetModule": "NFIU_AML", "targetTemplate": "NFIU_PEP", "targetField": "pep_alert_count", "transformationType": "DirectCopy", "description": "PSP PEP alerts to NFIU"},
            {"sourceTemplate": "PSP_TRX", "sourceField": "total_txn_value", "targetModule": "FATF_EVAL", "targetTemplate": "FATF_RISK", "targetField": "payment_flow_value", "transformationType": "DirectCopy", "description": "Transaction value to FATF risk"},
            {"sourceTemplate": "PSP_FRD", "sourceField": "fraud_amount", "targetModule": "FATF_EVAL", "targetTemplate": "FATF_RISK", "targetField": "fraud_loss_value", "transformationType": "DirectCopy", "description": "Fraud exposure to FATF risk"},
        ],
    }

    add_cross_rules(definition, 18)
    return definition


def pmb_module_definition():
    templates = [
        template("PMB_COV", "PMB Cover", "Monthly", "FixedRow", "pmb", [section("COV", "Cover", 1)], [
            field("reporting_year", "Reporting Year", "Integer", "COV", True, 1),
            field("reporting_month", "Reporting Month", "Integer", "COV", True, 2),
            field("institution_name", "Institution Name", "Text", "COV", True, 3),
            field("fmbn_reference", "FMBN Reference", "Text", "COV", True, 4),
        ], []),
        template("PMB_NHF", "NHF Lending", "Monthly", "FixedRow", "pmb", [section("NHF", "NHF Lending", 1)], [
            field("nhf_opening_balance", "NHF Opening Balance", "Money", "NHF", True, 1),
            field("nhf_drawdown", "NHF Drawdown", "Money", "NHF", True, 2),
            field("nhf_repayment", "NHF Repayment", "Money", "NHF", True, 3),
            field("nhf_outstanding", "NHF Outstanding", "Money", "NHF", True, 4),
            field("nhf_avg_rate", "NHF Average Rate", "Percentage", "NHF", True, 5),
            field("nhf_rate_cap", "NHF Rate Cap", "Percentage", "NHF", True, 6, 0, 100, 2),
        ], [
            {"formulaType": "Difference", "targetField": "nhf_outstanding", "sourceFields": ["nhf_opening_balance", "nhf_repayment"], "severity": "Warning", "toleranceAmount": 1, "description": "NHF outstanding approximate"},
            formula_compare("LessThanOrEqual", "nhf_avg_rate", "nhf_rate_cap", "NHF average rate must not exceed cap (6%)", "Error"),
        ]),
        template("PMB_MTG", "Mortgage Portfolio", "Monthly", "FixedRow", "pmb", [section("MTG", "Mortgage Portfolio", 1)], [
            field("residential_mortgage_balance", "Residential Mortgage Balance", "Money", "MTG", True, 1),
            field("commercial_mortgage_balance", "Commercial Mortgage Balance", "Money", "MTG", True, 2),
            field("owner_occupied_balance", "Owner Occupied Balance", "Money", "MTG", True, 3),
            field("buy_to_let_balance", "Buy to Let Balance", "Money", "MTG", True, 4),
            field("total_mortgage_balance", "Total Mortgage Balance", "Money", "MTG", True, 5),
        ], [
            formula_sum("total_mortgage_balance", ["residential_mortgage_balance", "commercial_mortgage_balance"], "Total mortgage balance"),
        ]),
        template("PMB_DEL", "Delinquency", "Monthly", "FixedRow", "pmb", [section("DEL", "Delinquency Buckets", 1)], [
            field("days_30", "30 Days Delinquency", "Money", "DEL", True, 1),
            field("days_60", "60 Days Delinquency", "Money", "DEL", True, 2),
            field("days_90", "90 Days Delinquency", "Money", "DEL", True, 3),
            field("days_180_plus", "180+ Days Delinquency", "Money", "DEL", True, 4),
            field("total_delinquent", "Total Delinquent", "Money", "DEL", True, 5),
        ], [
            formula_sum("total_delinquent", ["days_30", "days_60", "days_90", "days_180_plus"], "Total delinquent balance"),
        ]),
        template("PMB_CAP", "Capital", "Quarterly", "FixedRow", "pmb", [section("CAP", "Capital", 1)], [
            field("regulatory_capital", "Regulatory Capital", "Money", "CAP", True, 1),
            field("risk_weighted_assets", "Risk Weighted Assets", "Money", "CAP", True, 2),
            field("car_ratio", "CAR Ratio", "Percentage", "CAP", True, 3),
            field("car_minimum", "CAR Minimum", "Percentage", "CAP", True, 4, 0, 100, 2),
        ], [
            formula_custom("car_ratio", ["regulatory_capital", "regulatory_capital", "risk_weighted_assets"], "CAR", "PMB CAR ratio"),
            formula_compare("GreaterThanOrEqual", "car_ratio", "car_minimum", "PMB CAR minimum", "Error"),
        ]),
        template("PMB_FIN", "Financial", "Quarterly", "FixedRow", "pmb", [section("FIN", "Financial", 1)], [
            field("total_assets", "Total Assets", "Money", "FIN", True, 1),
            field("total_liabilities", "Total Liabilities", "Money", "FIN", True, 2),
            field("equity", "Equity", "Money", "FIN", True, 3),
            field("net_profit", "Net Profit", "Money", "FIN", True, 4),
        ], [
            {"formulaType": "Difference", "targetField": "equity", "sourceFields": ["total_assets", "total_liabilities"], "severity": "Error", "toleranceAmount": 1, "description": "Equity check"},
        ]),
        template("PMB_FMB", "FMBN Settlement", "Monthly", "FixedRow", "pmb", [section("FMB", "FMBN Settlement", 1)], [
            field("fmbn_drawdown", "FMBN Drawdown", "Money", "FMB", True, 1),
            field("fmbn_repayment", "FMBN Repayment", "Money", "FMB", True, 2),
            field("fmbn_outstanding", "FMBN Outstanding", "Money", "FMB", True, 3),
        ], [
            {"formulaType": "Difference", "targetField": "fmbn_outstanding", "sourceFields": ["fmbn_drawdown", "fmbn_repayment"], "severity": "Warning", "toleranceAmount": 1, "description": "Outstanding FMBN"},
        ]),
        template("PMB_DEP", "Deposit Structure", "Monthly", "FixedRow", "pmb", [section("DEP", "Deposits", 1)], [
            field("demand_deposits", "Demand Deposits", "Money", "DEP", True, 1),
            field("savings_deposits", "Savings Deposits", "Money", "DEP", True, 2),
            field("time_deposits", "Time Deposits", "Money", "DEP", True, 3),
            field("insurable_deposits", "Insurable Deposits", "Money", "DEP", True, 4),
            field("total_deposits", "Total Deposits", "Money", "DEP", True, 5),
        ], [
            formula_sum("total_deposits", ["demand_deposits", "savings_deposits", "time_deposits"], "Total PMB deposits"),
            formula_compare("GreaterThanOrEqual", "total_deposits", "insurable_deposits", "Total >= insurable", "Error"),
        ]),
        template("PMB_AML", "AML", "Monthly", "FixedRow", "pmb", [section("AML", "AML", 1)], [
            field("str_count", "STR Count", "Integer", "AML", True, 1),
            field("ctr_count", "CTR Count", "Integer", "AML", True, 2),
            field("pep_alerts", "PEP Alerts", "Integer", "AML", True, 3),
            field("aml_total_alerts", "AML Total Alerts", "Integer", "AML", True, 4),
        ], [
            formula_sum("aml_total_alerts", ["str_count", "ctr_count", "pep_alerts"], "PMB AML alerts total", "Warning", 0),
        ]),
        template("PMB_GOV", "Governance", "Quarterly", "FixedRow", "pmb", [section("GOV", "Governance", 1)], [
            field("board_meetings", "Board Meetings", "Integer", "GOV", True, 1),
            field("compliance_breaches", "Compliance Breaches", "Integer", "GOV", True, 2),
            field("resolved_breaches", "Resolved Breaches", "Integer", "GOV", True, 3),
        ], []),
        template("PMB_REG", "Regulatory", "Monthly", "FixedRow", "pmb", [section("REG", "Regulatory", 1)], [
            field("returns_due", "Returns Due", "Integer", "REG", True, 1),
            field("returns_submitted", "Returns Submitted", "Integer", "REG", True, 2),
            field("returns_overdue", "Returns Overdue", "Integer", "REG", True, 3),
        ], [
            {"formulaType": "Difference", "targetField": "returns_overdue", "sourceFields": ["returns_due", "returns_submitted"], "severity": "Warning", "toleranceAmount": 1, "description": "Overdue returns"},
        ]),
        template("PMB_DIC", "Data Dictionary", "Quarterly", "FixedRow", "pmb", [section("DIC", "Data Dictionary", 1)], [
            field("field_count", "Field Count", "Integer", "DIC", True, 1),
            field("formula_count", "Formula Count", "Integer", "DIC", True, 2),
        ], []),
    ]

    targets = {
        "PMB_COV": (16, 4),
        "PMB_NHF": (24, 8),
        "PMB_MTG": (24, 8),
        "PMB_DEL": (20, 6),
        "PMB_CAP": (18, 6),
        "PMB_FIN": (20, 6),
        "PMB_FMB": (18, 6),
        "PMB_DEP": (16, 4),
        "PMB_AML": (16, 4),
        "PMB_GOV": (14, 3),
        "PMB_REG": (14, 3),
        "PMB_DIC": (12, 2),
    }

    for t in templates:
        tf, tfo = targets[t["returnCode"]]
        pad_template(t, tf, tfo)

    definition = {
        "moduleCode": "PMB_CBN",
        "moduleVersion": "1.0.0",
        "description": "Primary Mortgage Bank returns for CBN and FMBN supervisory requirements.",
        "templates": templates,
        "interModuleDataFlows": [
            {"sourceTemplate": "PMB_DEP", "sourceField": "total_deposits", "targetModule": "NDIC_RETURNS", "targetTemplate": "NDIC_DEP", "targetField": "total_deposits", "transformationType": "DirectCopy", "description": "PMB deposits to NDIC"},
            {"sourceTemplate": "PMB_DEP", "sourceField": "insurable_deposits", "targetModule": "NDIC_RETURNS", "targetTemplate": "NDIC_DEP", "targetField": "insurable_deposits", "transformationType": "DirectCopy", "description": "PMB insurable deposits to NDIC"},
            {"sourceTemplate": "PMB_CAP", "sourceField": "regulatory_capital", "targetModule": "NDIC_RETURNS", "targetTemplate": "NDIC_CAP", "targetField": "regulatory_capital", "transformationType": "DirectCopy", "description": "PMB capital to NDIC"},
            {"sourceTemplate": "PMB_FIN", "sourceField": "equity", "targetModule": "NDIC_RETURNS", "targetTemplate": "NDIC_FIN", "targetField": "total_equity", "transformationType": "DirectCopy", "description": "PMB equity to NDIC financial summary"},
            {"sourceTemplate": "PMB_AML", "sourceField": "str_count", "targetModule": "NFIU_AML", "targetTemplate": "NFIU_STR", "targetField": "str_filed_count", "transformationType": "DirectCopy", "description": "PMB STR to NFIU"},
            {"sourceTemplate": "PMB_AML", "sourceField": "ctr_count", "targetModule": "NFIU_AML", "targetTemplate": "NFIU_CTR", "targetField": "ctr_filed_count", "transformationType": "DirectCopy", "description": "PMB CTR to NFIU"},
        ],
    }

    add_cross_rules(definition, 16)
    return definition


def write_module_json(output_dir: Path, module_name: str, definition: dict):
    output_path = output_dir / module_name
    output_path.write_text(json.dumps(definition, indent=2) + "\n", encoding="utf-8")


def summarize(defn):
    t = len(defn["templates"])
    f = sum(len(x["fields"]) for x in defn["templates"])
    fo = sum(len(x["formulas"]) for x in defn["templates"])
    cr = sum(len(x["crossSheetRules"]) for x in defn["templates"])
    fl = len(defn.get("interModuleDataFlows", []))
    return t, f, fo, cr, fl


def main():
    root = Path(__file__).resolve().parents[1]
    out = root / "docs" / "module-definitions" / "rg09"
    out.mkdir(parents=True, exist_ok=True)

    dmb = dmb_module_definition()
    ndic = ndic_module_definition()
    psp = psp_module_definition()
    pmb = pmb_module_definition()

    write_module_json(out, "dmb_basel3.json", dmb)
    write_module_json(out, "ndic_returns.json", ndic)
    write_module_json(out, "psp_fintech.json", psp)
    write_module_json(out, "pmb_cbn.json", pmb)

    totals = [summarize(x) for x in [dmb, ndic, psp, pmb]]
    print("RG-09 module definition generation complete")
    print("module,templates,fields,formulas,cross_sheet_rules,data_flows")
    for name, metrics in zip(["DMB_BASEL3", "NDIC_RETURNS", "PSP_FINTECH", "PMB_CBN"], totals):
        print(f"{name},{metrics[0]},{metrics[1]},{metrics[2]},{metrics[3]},{metrics[4]}")

    all_templates = sum(x[0] for x in totals)
    all_fields = sum(x[1] for x in totals)
    all_formulas = sum(x[2] for x in totals)
    all_cross = sum(x[3] for x in totals)
    all_flows = sum(x[4] for x in totals)
    print(f"TOTAL,{all_templates},{all_fields},{all_formulas},{all_cross},{all_flows}")


if __name__ == "__main__":
    main()
