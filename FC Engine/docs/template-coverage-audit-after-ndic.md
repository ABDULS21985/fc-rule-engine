# Template Coverage Audit

Generated from `/Templates` workbook/XSD artifacts vs imported module-definition JSON files.

## Summary

| Module | WB Sheets | JSON Templates | WB Fields | JSON Fields | WB Field Match | XSD Elements | XSD Field Match |
|---|---:|---:|---:|---:|---:|---:|---:|
| BDC_CBN | 7 | 12 | 25 | 412 | 25/25 | 227 | 227/227 |
| MFB_PAR | 9 | 12 | 20 | 292 | 20/20 | 132 | 132/132 |
| NFIU_AML | 10 | 12 | 36 | 239 | 36/36 | 133 | 133/133 |
| DMB_BASEL3 | 13 | 15 | 30 | 1110 | 30/30 | 324 | 324/324 |
| NDIC_RETURNS | 9 | 11 | 10 | 406 | 10/10 | 34 | 34/34 |
| PSP_FINTECH | 10 | 14 | 18 | 284 | 2/18 | 105 | 4/105 |
| PMB_CBN | 10 | 12 | 10 | 260 | 0/10 | 57 | 6/57 |

## Details

## BDC_CBN

- Workbook: `Templates/BDC_Reporting_Templates.xlsx`
- XSD: `Templates/BDC_Return_Schema_v1.0.xsd`
- Definition: `RegOS™/src/FC.Engine.Migrator/SeedData/ModuleDefinitions/rg08-bdc-cbn-module-definition.json`
- JSON formulas: 35
- JSON cross-sheet rules: 3
- JSON inter-module flows: 4

Top workbook fields not matched (normalized exact):

- None

Top XSD elements not matched (normalized exact):

- None

## MFB_PAR

- Workbook: `Templates/MFB_Reporting_Templates.xlsx`
- XSD: `Templates/MFB_Return_Schema_v1.0.xsd`
- Definition: `RegOS™/src/FC.Engine.Migrator/SeedData/ModuleDefinitions/rg08-mfb-par-module-definition.json`
- JSON formulas: 34
- JSON cross-sheet rules: 0
- JSON inter-module flows: 7

Top workbook fields not matched (normalized exact):

- None

Top XSD elements not matched (normalized exact):

- None

## NFIU_AML

- Workbook: `Templates/NFIU_Reporting_Templates.xlsx`
- XSD: `Templates/NFIU_Return_Schema_v1.0.xsd`
- Definition: `RegOS™/src/FC.Engine.Migrator/SeedData/ModuleDefinitions/rg08-nfiu-aml-module-definition.json`
- JSON formulas: 6
- JSON cross-sheet rules: 0
- JSON inter-module flows: 0

Top workbook fields not matched (normalized exact):

- None

Top XSD elements not matched (normalized exact):

- None

## DMB_BASEL3

- Workbook: `Templates/DMB_Reporting_Templates.xlsx`
- XSD: `Templates/DMB_Return_Schema_v1.0.xsd`
- Definition: `RegOS™/docs/module-definitions/rg09/dmb_basel3.json`
- JSON formulas: 265
- JSON cross-sheet rules: 28
- JSON inter-module flows: 10

Top workbook fields not matched (normalized exact):

- None

Top XSD elements not matched (normalized exact):

- None

## NDIC_RETURNS

- Workbook: `Templates/NDIC_Returns_Reporting_Templates.xlsx`
- XSD: `Templates/NDIC_Return_Schema_v1.0.xsd`
- Definition: `RegOS™/docs/module-definitions/rg09/ndic_returns.json`
- JSON formulas: 81
- JSON cross-sheet rules: 18
- JSON inter-module flows: 2

Top workbook fields not matched (normalized exact):

- None

Top XSD elements not matched (normalized exact):

- None

## PSP_FINTECH

- Workbook: `Templates/PSP_Fintech_Reporting_Templates.xlsx`
- XSD: `Templates/PSP_Return_Schema_v1.0.xsd`
- Definition: `RegOS™/docs/module-definitions/rg09/psp_fintech.json`
- JSON formulas: 69
- JSON cross-sheet rules: 18
- JSON inter-module flows: 7

Top workbook fields not matched (normalized exact):

- CBN_Licence_No
- PSP_Name
- Reporting_Date
- Return_Type
- Txn_Volume
- Txn_Value
- Success_Rate
- Fraud_Count
- Fraud_Loss
- Total_Wallets
- KYC_Tier
- System_Uptime
- PCI_DSS_Status
- API_Calls
- Rural_Agent_Pct
- Chargeback_Ratio

Top XSD elements not matched (normalized exact):

- Header
- TransactionVolumes
- FraudDisputes
- AgentNetwork
- WalletAccounts
- FinancialStatements
- FinancialInclusion
- ITRiskSecurity
- AMLCompliance
- Governance
- OpenBanking
- Signature
- CBNLicenceNo
- PSPName
- ReturnType
- ReportingPeriodStart
- ReportingPeriodEnd
- SubmissionDateTime
- ContactPerson
- ContactEmail

## PMB_CBN

- Workbook: `Templates/PMB_CBN_FMBN_Reporting_Templates.xlsx`
- XSD: `Templates/PMB_Return_Schema_v1.0.xsd`
- Definition: `RegOS™/docs/module-definitions/rg09/pmb_cbn.json`
- JSON formulas: 60
- JSON cross-sheet rules: 16
- JSON inter-module flows: 6

Top workbook fields not matched (normalized exact):

- PMB_Name
- PMB_Licence
- PMB_Category
- Return_Type
- Mortgage_Product
- Property_Type
- LTV_Ratio
- NHF_Rate
- NPL_Ratio
- CAR

Top XSD elements not matched (normalized exact):

- Header
- MortgagePortfolio
- NHFLending
- AssetQuality
- CapitalAdequacy
- Deposits
- HousingSector
- Signature
- PMBLicenceNo
- PMBName
- PMBCategory
- ReturnType
- ReportFrequency
- ReportingPeriodStart
- ReportingPeriodEnd
- FMBNAccredited
- Product
- ProductType
- LoanCount
- Outstanding
