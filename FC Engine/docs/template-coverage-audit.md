# Template Coverage Audit

Generated from `/Templates` workbook/XSD artifacts vs imported module-definition JSON files.

## Summary

| Module | WB Sheets | JSON Templates | WB Fields | JSON Fields | WB Field Match | XSD Elements | XSD Field Match |
|---|---:|---:|---:|---:|---:|---:|---:|
| BDC_CBN | 7 | 12 | 25 | 175 | 1/25 | 227 | 13/227 |
| MFB_PAR | 9 | 12 | 20 | 159 | 5/20 | 132 | 13/132 |
| NFIU_AML | 10 | 12 | 36 | 89 | 0/36 | 133 | 0/133 |
| DMB_BASEL3 | 13 | 15 | 30 | 678 | 11/30 | 324 | 31/324 |
| NDIC_RETURNS | 9 | 11 | 10 | 295 | 3/10 | 34 | 7/34 |
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

- BDC_Licence_Number
- Reporting_Date
- Transaction_Reference
- Transaction_Type
- Currency_Pair
- Foreign_Currency_Amount
- Local_Currency_Amount
- Exchange_Rate
- CB_Reference_Rate
- Spread_Percentage
- Customer_Type
- Customer_ID_Type
- Customer_ID_Number
- Customer_Name
- Nationality_Residency
- Transaction_Purpose_Code
- Purpose_Description
- Payment_Method
- Source_of_Funds
- Threshold_Flag

Top XSD elements not matched (normalized exact):

- Header
- Transactions
- WeeklyPosition
- FinancialPosition
- SuspiciousReport
- ThresholdReport
- DailySummary
- Signature
- BDCLicenceNo
- BDCName
- ReturnType
- ReportingPeriodStart
- ReportingPeriodEnd
- SubmissionDateTime
- TierCategory
- HeadOfficeLocation
- ContactPerson
- ContactEmail
- ContactPhone
- Jurisdiction

## MFB_PAR

- Workbook: `Templates/MFB_Reporting_Templates.xlsx`
- XSD: `Templates/MFB_Return_Schema_v1.0.xsd`
- Definition: `RegOS™/src/FC.Engine.Migrator/SeedData/ModuleDefinitions/rg08-mfb-par-module-definition.json`
- JSON formulas: 34
- JSON cross-sheet rules: 0
- JSON inter-module flows: 7

Top workbook fields not matched (normalized exact):

- MFB_Licence_Number
- MFB_Name
- Reporting_Date
- Return_Type
- Total_RWA
- Liquidity_Ratio
- PAR_30
- PAR_90
- Gross_Loan_Portfolio
- NPL_Ratio
- Micro_Loan_Pct
- Active_Borrowers
- Female_Borrower_Pct
- OSS
- Collection_Rate

Top XSD elements not matched (normalized exact):

- Header
- CapitalAdequacy
- Liquidity
- PortfolioAtRisk
- CreditRisk
- FinancialStatements
- AMLCompliance
- FinancialInclusion
- Governance
- OperationalRisk
- Signature
- MFBLicenceNo
- MFBName
- ReturnType
- ReportingPeriodStart
- ReportingPeriodEnd
- SubmissionDateTime
- ContactPerson
- ContactEmail
- ContactPhone

## NFIU_AML

- Workbook: `Templates/NFIU_Reporting_Templates.xlsx`
- XSD: `Templates/NFIU_Return_Schema_v1.0.xsd`
- Definition: `RegOS™/src/FC.Engine.Migrator/SeedData/ModuleDefinitions/rg08-nfiu-aml-module-definition.json`
- JSON formulas: 6
- JSON cross-sheet rules: 0
- JSON inter-module flows: 0

Top workbook fields not matched (normalized exact):

- NFIU_Reg_ID
- RE_Name
- RE_Category
- Report_Type
- Submission_Type
- Report_Date
- Transaction_Date
- Amount
- Currency
- Subject_Name
- BVN
- NIN
- Account_No
- PEP_Status
- Risk_Rating
- Suspicion_Indicator
- Narrative
- Filing_Deadline
- Submission Type
- Submission Type

Top XSD elements not matched (normalized exact):

- Header
- STR
- CTR
- FTR
- NIL
- PEPReport
- SAR
- TFS
- ComplianceSummary
- RiskAssessment
- EnforcementResponse
- Signature
- NFIUEntityID
- ReportingEntityName
- ReturnType
- RECategory
- SubmissionType
- RegulatoryAuthority
- ReportingPeriodStart
- ReportingPeriodEnd

## DMB_BASEL3

- Workbook: `Templates/DMB_Reporting_Templates.xlsx`
- XSD: `Templates/DMB_Return_Schema_v1.0.xsd`
- Definition: `RegOS™/docs/module-definitions/rg09/dmb_basel3.json`
- JSON formulas: 218
- JSON cross-sheet rules: 28
- JSON inter-module flows: 10

Top workbook fields not matched (normalized exact):

- Bank_Licence_Number
- Bank_Name
- Reporting_Date
- Licence_Category
- Return_Type
- Consolidated_Solo
- Credit_Risk_RWA
- Market_Risk_RWA
- Op_Risk_RWA
- HQLA_Total
- NPL_Ratio
- Provision_Coverage
- Net_Open_Position
- NOP_Limit_Pct
- Leverage_Ratio
- ECL_Stage1
- ECL_Stage2
- ECL_Stage3
- Gross_Income

Top XSD elements not matched (normalized exact):

- Header
- CapitalAdequacy
- LiquidityCoverage
- StableFunding
- CreditRisk
- MarketRisk
- OperationalRisk
- LargeExposures
- AssetQuality
- FinancialStatements
- AMLCompliance
- FXPosition
- DepositStructure
- StressTesting
- Governance
- Signature
- BankLicenceNo
- BankName
- ReturnType
- LicenceCategory

## NDIC_RETURNS

- Workbook: `Templates/NDIC_Returns_Reporting_Templates.xlsx`
- XSD: `Templates/NDIC_Return_Schema_v1.0.xsd`
- Definition: `RegOS™/docs/module-definitions/rg09/ndic_returns.json`
- JSON formulas: 71
- JSON cross-sheet rules: 18
- JSON inter-module flows: 2

Top workbook fields not matched (normalized exact):

- Institution_Type
- Return_Type
- MDIC_Amount
- DPAS_Bucket
- Premium_Rate
- Insured_Deposits
- CAR

Top XSD elements not matched (normalized exact):

- Header
- DepositLiabilities
- FinancialCondition
- Signature
- InstitutionType
- CBNLicenceNo
- ReturnType
- ReportingDate
- InsuredDeposits
- TotalDepositors
- FullyCoveredDepositors
- CoveragePct
- ApplicableMDIC
- AssessableDeposits
- BasePremiumRate
- QuantitativeAddOn
- QualitativeAddOn
- TotalPremiumRate
- GrossPremium
- NetPremium

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
