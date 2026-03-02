-- ============================================================================
-- DFIS Finance Companies (FC) Statutory Returns Database Schema
-- Generated from: DFIS - FC Return Templates 0917 - Unmerge (002).xlsb
-- Total Tables: 103 (1:1 mapping with every sheet)
-- ============================================================================


-- ============================================================================
-- TABLE 1: Sheet2 - Return Code Reference List
-- ============================================================================
CREATE TABLE sheet2_return_codes (
    id SERIAL PRIMARY KEY,
    return_code VARCHAR(20) NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- ============================================================================
-- TABLE 2: CleanedSummary - Cleaned List of FC Returns Templates
-- ============================================================================
CREATE TABLE cleaned_summary (
    id SERIAL PRIMARY KEY,
    serial_no INT,
    return_code VARCHAR(20),
    frequency VARCHAR(20),           -- MONTHLY, QUARTERLY, SEMI-ANNUAL
    description TEXT,
    institution_type VARCHAR(10),    -- FC
    owner VARCHAR(20),               -- DFIS
    other_stakeholders TEXT,
    harmonised_template_desc TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- ============================================================================
-- TABLE 3: SUMMARY - Full List of FC Returns Templates
-- ============================================================================
CREATE TABLE summary (
    id SERIAL PRIMARY KEY,
    serial_no INT,
    return_code VARCHAR(20),
    frequency VARCHAR(20),
    description TEXT,
    institution_type VARCHAR(10),
    owner VARCHAR(20),
    other_stakeholders TEXT,
    harmonised_template_desc TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- ============================================================================
-- REFERENCE TABLES (shared across returns)
-- ============================================================================

CREATE TABLE institutions (
    id SERIAL PRIMARY KEY,
    institution_code VARCHAR(20) NOT NULL UNIQUE,
    institution_name VARCHAR(255) NOT NULL,
    institution_type VARCHAR(10) DEFAULT 'FC',
    state_name VARCHAR(100),
    state_code VARCHAR(10),
    local_government_name VARCHAR(100),
    address TEXT,
    city VARCHAR(100),
    email VARCHAR(255),
    phone_1 VARCHAR(50),
    phone_2 VARCHAR(50),
    fax VARCHAR(50),
    licence_no VARCHAR(50),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE return_periods (
    id SERIAL PRIMARY KEY,
    reporting_date DATE NOT NULL,
    period_type VARCHAR(20) NOT NULL,
    year INT NOT NULL,
    month INT,
    quarter INT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE return_submissions (
    id SERIAL PRIMARY KEY,
    institution_id INT NOT NULL REFERENCES institutions(id),
    return_period_id INT NOT NULL REFERENCES return_periods(id),
    return_code VARCHAR(20) NOT NULL,
    submission_date TIMESTAMP,
    status VARCHAR(20) DEFAULT 'DRAFT',
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(institution_id, return_period_id, return_code)
);

CREATE TABLE bank_codes (
    id SERIAL PRIMARY KEY,
    bank_code VARCHAR(20) NOT NULL UNIQUE,
    bank_name VARCHAR(255) NOT NULL,
    institution_type VARCHAR(50)
);

CREATE TABLE sectors (
    id SERIAL PRIMARY KEY,
    sector_code VARCHAR(20) NOT NULL UNIQUE,
    sector_name VARCHAR(255) NOT NULL
);

CREATE TABLE sub_sectors (
    id SERIAL PRIMARY KEY,
    sector_id INT NOT NULL REFERENCES sectors(id),
    sub_sector_code VARCHAR(20) NOT NULL UNIQUE,
    sub_sector_name VARCHAR(255) NOT NULL
);

CREATE TABLE states (
    id SERIAL PRIMARY KEY,
    state_code VARCHAR(10) NOT NULL UNIQUE,
    state_name VARCHAR(100) NOT NULL
);

CREATE TABLE local_governments (
    id SERIAL PRIMARY KEY,
    state_id INT NOT NULL REFERENCES states(id),
    lg_code VARCHAR(20) NOT NULL UNIQUE,
    lg_name VARCHAR(255) NOT NULL
);


-- ============================================================================
-- TABLE 4: MFCR 300 - Monthly Statement of Financial Position
-- ============================================================================
CREATE TABLE mfcr_300 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    -- FINANCIAL ASSETS: Cash
    cash_notes NUMERIC(20,2),                           -- 10110
    cash_coins NUMERIC(20,2),                           -- 10120
    total_cash NUMERIC(20,2),                           -- 10140
    -- Due From
    due_from_banks_nigeria NUMERIC(20,2),               -- 10170
    uncleared_effects NUMERIC(20,2),                    -- 10180
    due_from_other_fi NUMERIC(20,2),                    -- 10185
    total_due_from_banks_nigeria NUMERIC(20,2),         -- 10190
    due_from_banks_oecd NUMERIC(20,2),                  -- 10210
    due_from_banks_non_oecd NUMERIC(20,2),              -- 10220
    total_due_from_banks_outside NUMERIC(20,2),         -- 10230
    -- Money at Call
    money_at_call_secured NUMERIC(20,2),                -- 10250
    money_at_call_unsecured NUMERIC(20,2),              -- 10260
    total_money_at_call NUMERIC(20,2),                  -- 10240
    -- Bank Placements
    placements_secured_banks NUMERIC(20,2),             -- 10280
    placements_unsecured_banks NUMERIC(20,2),           -- 10290
    placements_discount_houses NUMERIC(20,2),           -- 10295
    total_bank_placements NUMERIC(20,2),                -- 10270
    -- Derivative Financial Assets
    derivative_financial_assets NUMERIC(20,2),          -- 10370
    -- Securities
    treasury_bills NUMERIC(20,2),                       -- 10380
    fgn_bonds NUMERIC(20,2),                            -- 10390
    state_govt_bonds NUMERIC(20,2),                     -- 10400
    local_govt_bonds NUMERIC(20,2),                     -- 10410
    corporate_bonds NUMERIC(20,2),                      -- 10420
    other_bonds NUMERIC(20,2),                          -- 10430
    treasury_certificates NUMERIC(20,2),                -- 10440
    cbn_registered_certificates NUMERIC(20,2),          -- 10450
    certificates_of_deposit NUMERIC(20,2),              -- 10460
    commercial_papers NUMERIC(20,2),                    -- 10470
    total_securities NUMERIC(20,2),                     -- 10480
    -- Loans and Receivables
    loans_to_fi_nigeria NUMERIC(20,2),                  -- 10490
    loans_to_subsidiary_nigeria NUMERIC(20,2),          -- 10500
    loans_to_subsidiary_outside NUMERIC(20,2),          -- 10510
    loans_to_associate_nigeria NUMERIC(20,2),           -- 10520
    loans_to_associate_outside NUMERIC(20,2),           -- 10530
    loans_to_other_entities_outside NUMERIC(20,2),      -- 10540
    loans_to_government NUMERIC(20,2),                  -- 10545
    loans_to_other_customers NUMERIC(20,2),             -- 10550
    total_gross_loans NUMERIC(20,2),                    -- 10560
    impairment_on_loans NUMERIC(20,2),                  -- 10570
    total_net_loans NUMERIC(20,2),                      -- 10580
    -- Other Investments
    other_investments_quoted NUMERIC(20,2),              -- 10590
    other_investments_unquoted NUMERIC(20,2),            -- 10600
    investments_in_subsidiaries NUMERIC(20,2),           -- 10610
    investments_in_associates NUMERIC(20,2),             -- 10620
    -- Other Assets
    other_assets NUMERIC(20,2),                         -- 10630
    intangible_assets NUMERIC(20,2),                    -- 10640
    non_current_assets_held_for_sale NUMERIC(20,2),     -- 10650
    property_plant_equipment NUMERIC(20,2),             -- 10660
    total_assets NUMERIC(20,2),                         -- 10670
    -- LIABILITIES: Borrowings
    borrowings_from_banks NUMERIC(20,2),                -- 10680
    borrowings_from_other_fc NUMERIC(20,2),             -- 10690
    borrowings_from_other_fi NUMERIC(20,2),             -- 10700
    borrowings_from_individuals NUMERIC(20,2),          -- 10710
    total_borrowings NUMERIC(20,2),                     -- 10720
    -- Other Liabilities
    derivative_financial_liabilities NUMERIC(20,2),     -- 10730
    other_liabilities NUMERIC(20,2),                    -- 10740
    total_liabilities NUMERIC(20,2),                    -- 10750
    -- EQUITY
    paid_up_capital NUMERIC(20,2),                      -- 10760
    share_premium NUMERIC(20,2),                        -- 10770
    retained_earnings NUMERIC(20,2),                    -- 10780
    statutory_reserve NUMERIC(20,2),                    -- 10790
    other_reserves NUMERIC(20,2),                       -- 10800
    revaluation_reserve NUMERIC(20,2),                  -- 10810
    minority_interest NUMERIC(20,2),                    -- 10820
    total_equity NUMERIC(20,2),                         -- 10830
    total_liabilities_and_equity NUMERIC(20,2),         -- 10840
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 5: MFCR 1000 - Statement of Comprehensive Income
-- ============================================================================
CREATE TABLE mfcr_1000 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    -- Interest Income
    interest_income_loans NUMERIC(20,2),                    -- 30120
    interest_income_leases NUMERIC(20,2),                   -- 30130
    interest_income_govt_securities NUMERIC(20,2),          -- 30140
    interest_income_bank_placements NUMERIC(20,2),          -- 30150
    discount_income NUMERIC(20,2),                          -- 30160
    interest_income_others NUMERIC(20,2),                   -- 30170
    total_interest_income NUMERIC(20,2),                    -- 30180
    interest_income_loans_ytd NUMERIC(20,2),
    interest_income_leases_ytd NUMERIC(20,2),
    interest_income_govt_securities_ytd NUMERIC(20,2),
    interest_income_bank_placements_ytd NUMERIC(20,2),
    discount_income_ytd NUMERIC(20,2),
    interest_income_others_ytd NUMERIC(20,2),
    total_interest_income_ytd NUMERIC(20,2),
    -- Interest Expense
    interest_on_borrowings NUMERIC(20,2),                   -- 30200
    interest_expense_others NUMERIC(20,2),                  -- 30220
    total_interest_expense NUMERIC(20,2),                   -- 30230
    interest_on_borrowings_ytd NUMERIC(20,2),
    interest_expense_others_ytd NUMERIC(20,2),
    total_interest_expense_ytd NUMERIC(20,2),
    net_interest_income NUMERIC(20,2),                      -- 30240
    net_interest_income_ytd NUMERIC(20,2),
    -- Fees & Commission
    commissions NUMERIC(20,2),                              -- 30260
    credit_related_fee_income NUMERIC(20,2),                -- 30270
    other_fees NUMERIC(20,2),                               -- 30280
    total_fees_commission_income NUMERIC(20,2),             -- 30290
    fees_commission_expenses NUMERIC(20,2),                 -- 30300
    net_fees_commission_income NUMERIC(20,2),               -- 30310
    commissions_ytd NUMERIC(20,2),
    credit_related_fee_income_ytd NUMERIC(20,2),
    other_fees_ytd NUMERIC(20,2),
    total_fees_commission_income_ytd NUMERIC(20,2),
    fees_commission_expenses_ytd NUMERIC(20,2),
    net_fees_commission_income_ytd NUMERIC(20,2),
    -- Other Operating Income
    other_operating_income NUMERIC(20,2),                   -- 30320
    other_operating_income_ytd NUMERIC(20,2),
    total_operating_income NUMERIC(20,2),                   -- 30340
    total_operating_income_ytd NUMERIC(20,2),
    -- Impairment Charges
    impairment_charge_loans NUMERIC(20,2),                  -- 30360
    impairment_charge_others NUMERIC(20,2),                 -- 30380
    total_impairment_charge NUMERIC(20,2),                  -- 30400
    impairment_charge_loans_ytd NUMERIC(20,2),
    impairment_charge_others_ytd NUMERIC(20,2),
    total_impairment_charge_ytd NUMERIC(20,2),
    -- Operating Expenses
    staff_costs NUMERIC(20,2),                              -- 30420
    depreciation_amortization NUMERIC(20,2),                -- 30440
    other_operating_expenses NUMERIC(20,2),                 -- 30460
    total_operating_expenses NUMERIC(20,2),                 -- 30480
    staff_costs_ytd NUMERIC(20,2),
    depreciation_amortization_ytd NUMERIC(20,2),
    other_operating_expenses_ytd NUMERIC(20,2),
    total_operating_expenses_ytd NUMERIC(20,2),
    -- Profit
    profit_before_tax NUMERIC(20,2),                        -- 30500
    tax_expense NUMERIC(20,2),                              -- 30540
    profit_after_tax NUMERIC(20,2),                         -- 30600
    profit_before_tax_ytd NUMERIC(20,2),
    tax_expense_ytd NUMERIC(20,2),
    profit_after_tax_ytd NUMERIC(20,2),
    -- OCI
    other_comprehensive_income NUMERIC(20,2),               -- 30790
    total_comprehensive_income NUMERIC(20,2),               -- 30800
    other_comprehensive_income_ytd NUMERIC(20,2),
    total_comprehensive_income_ytd NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 6: MFCR 100 - Memorandum Items
-- ============================================================================
CREATE TABLE mfcr_100 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    -- New Loans Disbursed - Current Month
    credit_female_number INT,                       -- 21115
    credit_female_value NUMERIC(20,2),
    credit_male_number INT,                         -- 21120
    credit_male_value NUMERIC(20,2),
    credit_company_number INT,                      -- 21125
    credit_company_value NUMERIC(20,2),
    total_credit_number INT,                        -- 21110
    total_credit_value NUMERIC(20,2),
    borrowings_female_number INT,                   -- 21180
    borrowings_female_value NUMERIC(20,2),
    borrowings_male_number INT,                     -- 21185
    borrowings_male_value NUMERIC(20,2),
    borrowings_company_number INT,                  -- 21190
    borrowings_company_value NUMERIC(20,2),
    total_borrowings_number INT,                    -- 21130
    total_borrowings_value NUMERIC(20,2),
    -- Cumulative to Date
    credit_female_number_ytd INT,
    credit_female_value_ytd NUMERIC(20,2),
    credit_male_number_ytd INT,
    credit_male_value_ytd NUMERIC(20,2),
    credit_company_number_ytd INT,
    credit_company_value_ytd NUMERIC(20,2),
    borrowings_female_number_ytd INT,
    borrowings_female_value_ytd NUMERIC(20,2),
    borrowings_male_number_ytd INT,
    borrowings_male_value_ytd NUMERIC(20,2),
    borrowings_company_number_ytd INT,
    borrowings_company_value_ytd NUMERIC(20,2),
    -- Staff
    senior_staff_male INT,                          -- 21220
    senior_staff_female INT,
    junior_staff_male INT,                          -- 21225
    junior_staff_female INT,
    total_staff_male INT,                           -- 21230
    total_staff_female INT,
    number_of_loan_officers INT,                    -- 21235
    staff_resigned_terminated INT,                  -- 21240
    new_recruitments INT,                           -- 21245
    -- Examination
    date_last_cbn_ndic_examination DATE,            -- 21250
    cbn_recommended_provision NUMERIC(20,2),        -- 21255
    cbn_provision_loans_receivables NUMERIC(20,2),  -- 21260
    cbn_provision_other_assets NUMERIC(20,2),       -- 21265
    cbn_provision_off_balance NUMERIC(20,2),        -- 21270
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 7: MFCR 302 - Schedule of Balances Held with Banks
-- ============================================================================
CREATE TABLE mfcr_302 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    bank_code VARCHAR(20),
    institution_name VARCHAR(255),
    institution_type VARCHAR(50),
    account_number VARCHAR(50),
    amount_ngn NUMERIC(20,2),
    currency_type VARCHAR(10),          -- NGN, USD
    cleared_uncleared VARCHAR(20),      -- Cleared / Uncleared
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 8: MFCR 304 - Schedule of Secured Money at Call with Banks
-- ============================================================================
CREATE TABLE mfcr_304 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    bank_code VARCHAR(20),
    bank_name VARCHAR(255),
    type_of_call_money VARCHAR(20),     -- Secured / Unsecured
    effective_date DATE,
    reference VARCHAR(100),
    collateral_type VARCHAR(50),        -- CASH, TBILLS, BONDS, GUARANTEES, OTHERS
    collateral_value NUMERIC(20,2),
    tenor_days INT,
    amount_ngn NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 9: MFCR 306 - Schedule of Unsecured Money at Call with Banks
-- ============================================================================
CREATE TABLE mfcr_306 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    bank_code VARCHAR(20),
    bank_name VARCHAR(255),
    amount_ngn NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 10: MFCR 306-1 - Schedule of Money at Call (Combined)
-- ============================================================================
CREATE TABLE mfcr_306_1 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    bank_code VARCHAR(20),
    bank_name VARCHAR(255),
    type_of_call_money VARCHAR(20),     -- Secured / Unsecured
    amount_ngn NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 11: MFCR 308 - Schedule of Secured Placements with Banks
-- ============================================================================
CREATE TABLE mfcr_308 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    institution_code VARCHAR(20),
    institution_type VARCHAR(50),       -- Bank
    institution_name VARCHAR(255),
    placement_type VARCHAR(20),         -- Secured
    transaction_reference VARCHAR(100),
    collateral_type VARCHAR(50),        -- CASH, TBILLS, BONDS, GUARANTEES, OTHERS
    effective_date DATE,
    tenor_days INT,
    currency_type VARCHAR(10),          -- NGN, USD
    amount_ngn NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 12: MFCR 310 - Schedule of Unsecured Placements with Banks
-- ============================================================================
CREATE TABLE mfcr_310 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    bank_code VARCHAR(20),
    bank_name VARCHAR(255),
    amount_ngn NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 13: MFCR 312 - Schedule of Secured Placements with Discount Houses
-- ============================================================================
CREATE TABLE mfcr_312 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    dh_code VARCHAR(20),
    discount_house_name VARCHAR(255),
    amount_ngn NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 14: MFCR 314 - Schedule of Unsecured Placements with Discount Houses
-- ============================================================================
CREATE TABLE mfcr_314 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    dh_code VARCHAR(20),
    institution_type VARCHAR(50),
    institution_name VARCHAR(255),
    placement_type VARCHAR(50),         -- Unsecured / Secured
    amount_ngn NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 15: MFCR 314-1 - Schedule of Placements (Combined)
-- ============================================================================
CREATE TABLE mfcr_314_1 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    dh_code VARCHAR(20),
    institution_name VARCHAR(255),
    placement_type VARCHAR(20),         -- Secured / Unsecured
    institution_type VARCHAR(50),       -- Banks / DHs
    amount_ngn NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 16: MFCR 316 - Schedule of Derivative Financial Assets
-- ============================================================================
CREATE TABLE mfcr_316 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    measurement_classification VARCHAR(50),  -- For Hedging / Not for Hedging
    derivative_type VARCHAR(100),            -- FX Forwards, FX Futures, FX Swaps, Forward Contracts, PUT Options, OTHERS
    counterparty VARCHAR(255),
    notional_amount NUMERIC(20,2),
    currency_type VARCHAR(10),               -- NGN / USD
    carrying_value NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 17: MFCR 318 - Treasury Bills
-- ============================================================================
CREATE TABLE mfcr_318 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    classification VARCHAR(50),                 -- FVTPL / Available for Sale
    pledge_type VARCHAR(20),                    -- PLEDGED / UNPLEDGED
    measurement_type VARCHAR(30),               -- FVTPL, FVOCI, AMORTISED COST
    description TEXT,
    date_of_purchase DATE,
    tenor_days INT,
    coupon_rate NUMERIC(10,4),
    face_value NUMERIC(20,2),
    purchase_price NUMERIC(20,2),
    book_value NUMERIC(20,2),
    par_value_units INT,
    maturity_date DATE,
    market_price NUMERIC(20,2),
    market_value NUMERIC(20,2),
    gain_loss_on_mtm NUMERIC(20,2),
    impairment NUMERIC(20,2),
    net_carrying_value NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 18: MFCR 320 - Federal Government Bonds
-- ============================================================================
CREATE TABLE mfcr_320 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    classification VARCHAR(50),                 -- FVTPL / Available for Sale
    counterparty VARCHAR(255),
    pledge_type VARCHAR(20),
    measurement_type VARCHAR(30),
    description TEXT,
    date_of_purchase DATE,
    maturity VARCHAR(50),
    tenor_days INT,
    face_value NUMERIC(20,2),
    purchase_price NUMERIC(20,2),
    book_value NUMERIC(20,2),
    par_value_units INT,
    maturity_date DATE,
    market_price NUMERIC(20,2),
    market_value NUMERIC(20,2),
    gain_loss_on_mtm NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 19: MFCR 322 - State Government Bonds
-- ============================================================================
CREATE TABLE mfcr_322 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    classification VARCHAR(50),
    bond_type VARCHAR(50),
    issuer VARCHAR(255),
    series_tranche VARCHAR(100),
    date_of_purchase DATE,
    coupon_rate NUMERIC(10,4),
    face_value NUMERIC(20,2),
    purchase_price NUMERIC(20,2),
    book_value NUMERIC(20,2),
    par_value_units INT,
    maturity_date DATE,
    market_price NUMERIC(20,2),
    market_value NUMERIC(20,2),
    gain_loss_on_mtm NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 20: MFCR 324 - Local Government Bonds
-- ============================================================================
CREATE TABLE mfcr_324 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    classification VARCHAR(50),
    bond_type VARCHAR(50),
    description TEXT,
    date_of_purchase DATE,
    tenor_days INT,
    coupon_rate NUMERIC(10,4),
    face_value NUMERIC(20,2),
    purchase_price NUMERIC(20,2),
    book_value NUMERIC(20,2),
    par_value_units INT,
    maturity_date DATE,
    market_price NUMERIC(20,2),
    market_value NUMERIC(20,2),
    gain_loss_on_mtm NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 21: MFCR 326 - Corporate Bonds
-- ============================================================================
CREATE TABLE mfcr_326 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    classification VARCHAR(50),
    bond_type VARCHAR(50),
    description TEXT,
    date_of_purchase DATE,
    tenor_days INT,
    coupon_rate NUMERIC(10,4),
    face_value NUMERIC(20,2),
    purchase_price NUMERIC(20,2),
    book_value NUMERIC(20,2),
    par_value_units INT,
    maturity_date DATE,
    market_price NUMERIC(20,2),
    market_value NUMERIC(20,2),
    gain_loss_on_mtm NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 22: MFCR 328 - Other Bonds
-- ============================================================================
CREATE TABLE mfcr_328 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    classification VARCHAR(50),
    bond_type VARCHAR(50),
    description TEXT,
    date_of_purchase DATE,
    tenor_days INT,
    coupon_rate NUMERIC(10,4),
    face_value NUMERIC(20,2),
    purchase_price NUMERIC(20,2),
    book_value NUMERIC(20,2),
    par_value_units INT,
    maturity_date DATE,
    market_price NUMERIC(20,2),
    market_value NUMERIC(20,2),
    gain_loss_on_mtm NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 23: MFCR 330 - Treasury Certificates & OMO Bills
-- ============================================================================
CREATE TABLE mfcr_330 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    classification VARCHAR(50),
    investment_type VARCHAR(50),             -- Treasury Certificates / OMO Bills
    pledge_type VARCHAR(20),
    measurement_type VARCHAR(30),
    description TEXT,
    date_of_purchase DATE,
    tenor_days INT,
    coupon_rate NUMERIC(10,4),
    face_value NUMERIC(20,2),
    purchase_price NUMERIC(20,2),
    book_value NUMERIC(20,2),
    par_value_units INT,
    maturity_date DATE,
    market_price NUMERIC(20,2),
    market_value NUMERIC(20,2),
    gain_loss_on_mtm NUMERIC(20,2),
    impairment NUMERIC(20,2),
    net_carrying_value NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 24: MFCR 332 - CBN Pledged OMO Bills
-- ============================================================================
CREATE TABLE mfcr_332 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    classification VARCHAR(50),
    bond_type VARCHAR(50),
    description TEXT,
    date_of_purchase DATE,
    tenor_days INT,
    coupon_rate NUMERIC(10,4),
    face_value NUMERIC(20,2),
    purchase_price NUMERIC(20,2),
    book_value NUMERIC(20,2),
    par_value_units INT,
    maturity_date DATE,
    market_price NUMERIC(20,2),
    market_value NUMERIC(20,2),
    gain_loss_on_mtm NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 25: MFCR 336 - Commercial Papers
-- ============================================================================
CREATE TABLE mfcr_336 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    classification VARCHAR(50),
    paper_type VARCHAR(50),
    description TEXT,
    date_of_purchase DATE,
    tenor_days INT,
    coupon_rate NUMERIC(10,4),
    face_value NUMERIC(20,2),
    purchase_price NUMERIC(20,2),
    book_value NUMERIC(20,2),
    par_value_units INT,
    maturity_date DATE,
    market_price NUMERIC(20,2),
    market_value NUMERIC(20,2),
    gain_loss_on_mtm NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 26: MFCR 336-1 - Schedule of Securities (Combined)
-- ============================================================================
CREATE TABLE mfcr_336_1 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    classification VARCHAR(50),
    security_type VARCHAR(50),              -- Commercial Paper, FGN Bonds, etc.
    description TEXT,
    date_of_purchase DATE,
    tenor_days INT,
    coupon_rate NUMERIC(10,4),
    face_value NUMERIC(20,2),
    purchase_price NUMERIC(20,2),
    book_value NUMERIC(20,2),
    par_value_units INT,
    maturity_date DATE,
    market_price NUMERIC(20,2),
    market_value NUMERIC(20,2),
    gain_loss_on_mtm NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 27: MFCR 338 - Loans & Receivables - Other Financial Institutions
-- ============================================================================
CREATE TABLE mfcr_338 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    fi_name VARCHAR(255),
    in_out_nigeria VARCHAR(20),
    purpose TEXT,
    facility_type VARCHAR(100),
    country VARCHAR(100),
    relationship VARCHAR(100),
    tenor VARCHAR(50),
    date_granted DATE,
    approved_limit NUMERIC(20,2),
    carrying_amount NUMERIC(20,2),
    status_performing NUMERIC(20,2),
    status_watchlist NUMERIC(20,2),
    status_substandard NUMERIC(20,2),
    status_doubtful NUMERIC(20,2),
    status_very_doubtful NUMERIC(20,2),
    status_lost NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 28: MFCR 340 - Loans to Subsidiary Companies in Nigeria
-- ============================================================================
CREATE TABLE mfcr_340 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    affiliate_name VARCHAR(255),
    affiliate_type VARCHAR(100),
    term_loan NUMERIC(20,2),
    overdraft NUMERIC(20,2),
    other_loans NUMERIC(20,2),
    advances_under_lease NUMERIC(20,2),
    bankers_acceptances NUMERIC(20,2),
    commercial_papers NUMERIC(20,2),
    bills_discounted NUMERIC(20,2),
    others NUMERIC(20,2),
    total NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 29: MFCR 342 - Loans to Subsidiary Companies Outside Nigeria
-- ============================================================================
CREATE TABLE mfcr_342 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    affiliate_name VARCHAR(255),
    term_loan NUMERIC(20,2),
    overdraft NUMERIC(20,2),
    other_loans NUMERIC(20,2),
    advances_under_lease NUMERIC(20,2),
    bankers_acceptances NUMERIC(20,2),
    commercial_papers NUMERIC(20,2),
    bills_discounted NUMERIC(20,2),
    others NUMERIC(20,2),
    total NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 30: MFCR 350 - Impairments on Loans/Receivables & Leases
-- ============================================================================
CREATE TABLE mfcr_350 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    obligor_name VARCHAR(255),
    account_number VARCHAR(50),
    opening_carrying_amount NUMERIC(20,2),
    impairment_balance_opening NUMERIC(20,2),
    impairment_charge_month NUMERIC(20,2),
    recoveries_month NUMERIC(20,2),
    impairment_for_period NUMERIC(20,2),
    net_carrying_amount NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 31: MFCR 344 - Credits to Associate/Affiliate Companies in Nigeria
-- ============================================================================
CREATE TABLE mfcr_344 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    affiliate_name VARCHAR(255),
    term_loan NUMERIC(20,2),
    overdraft NUMERIC(20,2),
    other_loans NUMERIC(20,2),
    advances_under_lease NUMERIC(20,2),
    bankers_acceptances NUMERIC(20,2),
    commercial_papers NUMERIC(20,2),
    bills_discounted NUMERIC(20,2),
    others NUMERIC(20,2),
    total NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 32: MFCR 346 - Loans to Associate/Affiliate Companies Outside Nigeria
-- ============================================================================
CREATE TABLE mfcr_346 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    affiliate_name VARCHAR(255),
    term_loan NUMERIC(20,2),
    overdraft NUMERIC(20,2),
    other_loans NUMERIC(20,2),
    advances_under_lease NUMERIC(20,2),
    bankers_acceptances NUMERIC(20,2),
    commercial_papers NUMERIC(20,2),
    bills_discounted NUMERIC(20,2),
    others NUMERIC(20,2),
    total NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 33: MFCR 346-1 - Loans to Subsidiaries/Associates (Combined)
-- ============================================================================
CREATE TABLE mfcr_346_1 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    affiliate_name VARCHAR(255),
    entity_relationship VARCHAR(50),    -- Associate / Subsidiary / etc.
    term_loan NUMERIC(20,2),
    overdraft NUMERIC(20,2),
    other_loans NUMERIC(20,2),
    advances_under_lease NUMERIC(20,2),
    bankers_acceptances NUMERIC(20,2),
    commercial_papers NUMERIC(20,2),
    bills_discounted NUMERIC(20,2),
    others NUMERIC(20,2),
    total NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 34: MFCR 348 - Loans & Receivables - Other Entities Outside Nigeria
-- ============================================================================
CREATE TABLE mfcr_348 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    obligor VARCHAR(255),
    in_out_nigeria VARCHAR(20),
    purpose TEXT,
    sector VARCHAR(100),
    sub_sector VARCHAR(100),
    tenor VARCHAR(50),
    date_granted DATE,
    maturity_date DATE,
    approved_limit NUMERIC(20,2),
    approved_amount NUMERIC(20,2),
    carrying_amount NUMERIC(20,2),
    term_loan NUMERIC(20,2),
    overdraft NUMERIC(20,2),
    advances_under_lease NUMERIC(20,2),
    bankers_acceptances NUMERIC(20,2),
    commercial_papers NUMERIC(20,2),
    bills_discounted NUMERIC(20,2),
    others NUMERIC(20,2),
    status_performing NUMERIC(20,2),
    status_watchlist NUMERIC(20,2),
    status_substandard NUMERIC(20,2),
    status_doubtful NUMERIC(20,2),
    status_very_doubtful NUMERIC(20,2),
    status_lost NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 35: MFCR 351(2) - Cheques Purchased, Factored Debts, Advances Under Leases
-- ============================================================================
CREATE TABLE mfcr_351_2 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    obligor VARCHAR(255),
    purpose TEXT,
    facility_category VARCHAR(100),     -- CHEQUES PURCHASED, FACTORED DEBTS, ADVANCES UNDER LEASES
    sub_sector VARCHAR(100),
    tenor VARCHAR(50),
    date_granted DATE,
    maturity_date DATE,
    approved_limit NUMERIC(20,2),
    approved_amount NUMERIC(20,2),
    carrying_amount NUMERIC(20,2),
    term_loan NUMERIC(20,2),
    overdraft NUMERIC(20,2),
    advances_under_lease NUMERIC(20,2),
    bankers_acceptances NUMERIC(20,2),
    commercial_papers NUMERIC(20,2),
    bills_discounted NUMERIC(20,2),
    others NUMERIC(20,2),
    status_performing NUMERIC(20,2),
    status_watchlist NUMERIC(20,2),
    status_substandard NUMERIC(20,2),
    status_doubtful NUMERIC(20,2),
    status_very_doubtful NUMERIC(20,2),
    status_lost NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 36: MFCR 351 - Loans & Receivables - Government
-- ============================================================================
CREATE TABLE mfcr_351 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    obligor VARCHAR(255),
    purpose TEXT,
    tier_of_government VARCHAR(50),     -- FEDERAL, STATE, LOCAL, PARASTATALS
    sub_sector VARCHAR(100),
    tenor VARCHAR(50),
    date_granted DATE,
    maturity_date DATE,
    approved_limit NUMERIC(20,2),
    approved_amount NUMERIC(20,2),
    carrying_amount NUMERIC(20,2),
    term_loan NUMERIC(20,2),
    overdraft NUMERIC(20,2),
    advances_under_lease NUMERIC(20,2),
    bankers_acceptances NUMERIC(20,2),
    commercial_papers NUMERIC(20,2),
    bills_discounted NUMERIC(20,2),
    others NUMERIC(20,2),
    status_performing NUMERIC(20,2),
    status_watchlist NUMERIC(20,2),
    status_substandard NUMERIC(20,2),
    status_doubtful NUMERIC(20,2),
    status_very_doubtful NUMERIC(20,2),
    status_lost NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 37: MFCR 349 - Loans & Receivables - Other Customers
-- ============================================================================
CREATE TABLE mfcr_349 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    obligor VARCHAR(255),
    in_out_nigeria VARCHAR(20),
    purpose TEXT,
    sector VARCHAR(100),
    sub_sector VARCHAR(100),
    tenor VARCHAR(50),
    date_granted DATE,
    maturity_date DATE,
    approved_limit NUMERIC(20,2),
    approved_amount NUMERIC(20,2),
    carrying_amount NUMERIC(20,2),
    term_loan NUMERIC(20,2),
    overdraft NUMERIC(20,2),
    advances_under_lease NUMERIC(20,2),
    bankers_acceptances NUMERIC(20,2),
    commercial_papers NUMERIC(20,2),
    bills_discounted NUMERIC(20,2),
    others NUMERIC(20,2),
    status_performing NUMERIC(20,2),
    status_watchlist NUMERIC(20,2),
    status_substandard NUMERIC(20,2),
    status_doubtful NUMERIC(20,2),
    status_very_doubtful NUMERIC(20,2),
    status_lost NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 38: MFCR 352 - Other Investments Quoted/Unquoted
-- ============================================================================
CREATE TABLE mfcr_352 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    investment_type VARCHAR(50),             -- Equity-quoted, Equity-unquoted
    measurement_type VARCHAR(30),            -- FVTPL, FVOCI, Amortised Cost
    description TEXT,
    date_of_purchase DATE,
    carrying_value_beginning NUMERIC(20,2),
    impairment NUMERIC(20,2),
    carrying_value_end NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 39: MFCR 354 - Investments in Subsidiaries/Associates
-- ============================================================================
CREATE TABLE mfcr_354 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    company_name VARCHAR(255),
    investment_type VARCHAR(50),         -- Quoted / Unquoted
    entity_type VARCHAR(50),             -- FI / Others
    sector VARCHAR(100),
    sub_sector VARCHAR(100),
    date_of_purchase DATE,
    purchase_value NUMERIC(20,2),
    carrying_value_beginning NUMERIC(20,2),
    market_value_end NUMERIC(20,2),
    impairment NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 40: MFCR 356 - Other Assets
-- ============================================================================
CREATE TABLE mfcr_356 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    item_code INT NOT NULL,                 -- 33002, 33008, 33014, 33020, 33026, 33032, 33038
    item_description VARCHAR(255),
    carrying_begin_naira NUMERIC(20,2),
    carrying_begin_foreign NUMERIC(20,2),
    impairment_naira NUMERIC(20,2),
    impairment_foreign NUMERIC(20,2),
    net_amount_naira NUMERIC(20,2),
    net_amount_foreign NUMERIC(20,2),
    total NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 41: MFCR 357 - Breakdown of Other Assets
-- ============================================================================
CREATE TABLE mfcr_357 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    item_code VARCHAR(20),
    item_description TEXT,
    amount_naira NUMERIC(20,2),
    amount_foreign NUMERIC(20,2),
    impairment_naira NUMERIC(20,2),
    impairment_foreign NUMERIC(20,2),
    net_amount_naira NUMERIC(20,2),
    net_amount_foreign NUMERIC(20,2),
    total NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 42: MFCR 358 - Intangible Assets
-- ============================================================================
CREATE TABLE mfcr_358 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    item_code INT NOT NULL,                 -- 33102-33120
    item_description VARCHAR(255),
    carrying_begin_naira NUMERIC(20,2),
    carrying_begin_foreign NUMERIC(20,2),
    impairment_naira NUMERIC(20,2),
    impairment_foreign NUMERIC(20,2),
    carrying_end_naira NUMERIC(20,2),
    carrying_end_foreign NUMERIC(20,2),
    total NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 43: MFCR 360 - Non-Current Assets Held for Sale
-- ============================================================================
CREATE TABLE mfcr_360 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    asset_type VARCHAR(100),
    description TEXT,
    location VARCHAR(255),
    date_of_purchase DATE,
    date_transferred DATE,
    purchase_cost_naira NUMERIC(20,2),
    purchase_cost_foreign NUMERIC(20,2),
    book_value_naira NUMERIC(20,2),
    book_value_foreign NUMERIC(20,2),
    impairment_naira NUMERIC(20,2),
    impairment_foreign NUMERIC(20,2),
    carrying_amount_naira NUMERIC(20,2),
    carrying_amount_foreign NUMERIC(20,2),
    total NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 44: MFCR 362 - Property, Plant and Equipment
-- ============================================================================
CREATE TABLE mfcr_362 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    item_code INT NOT NULL,                 -- 33302-33320
    item_description VARCHAR(255),
    gross_amount NUMERIC(20,2),
    additions_disposals NUMERIC(20,2),
    transferred_to_held_for_sale NUMERIC(20,2),
    accumulated_depreciation NUMERIC(20,2),
    accumulated_impairment NUMERIC(20,2),
    revaluation_gain_loss NUMERIC(20,2),
    net_carrying_amount NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 45: QFCR 364 - Direct Credit Substitutes
-- ============================================================================
CREATE TABLE qfcr_364 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    customer_name VARCHAR(255),
    customer_address TEXT,
    customer_id VARCHAR(50),
    transaction_code VARCHAR(50),
    transaction_type TEXT,
    amount NUMERIC(20,2),
    currency_type VARCHAR(10),
    date_booked DATE,
    maturity_date DATE,
    beneficiary VARCHAR(255),
    collateral_type VARCHAR(100),
    collateral_value NUMERIC(20,2),
    date_of_last_renewal DATE,
    status_classification VARCHAR(50),
    expected_loss NUMERIC(20,2),
    sector VARCHAR(100),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 46: QFCR 366 - Transaction-Related Contingent Items
-- ============================================================================
CREATE TABLE qfcr_366 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    customer_name VARCHAR(255),
    customer_address TEXT,
    customer_id VARCHAR(50),
    transaction_code VARCHAR(50),
    transaction_type TEXT,
    amount NUMERIC(20,2),
    currency_type VARCHAR(10),
    date_booked DATE,
    maturity_date DATE,
    beneficiary VARCHAR(255),
    collateral_type VARCHAR(100),
    collateral_value NUMERIC(20,2),
    date_of_last_renewal DATE,
    status_classification VARCHAR(50),
    expected_loss NUMERIC(20,2),
    sector VARCHAR(100),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 47: QFCR 368 - Short-term Self-Liquidating Trade-Related Contingencies
-- ============================================================================
CREATE TABLE qfcr_368 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    customer_name VARCHAR(255),
    customer_address TEXT,
    customer_id VARCHAR(50),
    transaction_code VARCHAR(50),
    transaction_type TEXT,
    amount NUMERIC(20,2),
    currency_type VARCHAR(10),
    date_booked DATE,
    maturity_date DATE,
    beneficiary VARCHAR(255),
    collateral_type VARCHAR(100),
    collateral_value NUMERIC(20,2),
    date_of_last_renewal DATE,
    status_classification VARCHAR(50),
    expected_loss NUMERIC(20,2),
    sector VARCHAR(100),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 48: QFCR 370 - Forward Asset Purchase / Deposits / Partly Paid Shares
-- ============================================================================
CREATE TABLE qfcr_370 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    customer_name VARCHAR(255),
    customer_address TEXT,
    customer_id VARCHAR(50),
    transaction_code VARCHAR(50),
    transaction_type TEXT,
    amount NUMERIC(20,2),
    currency_type VARCHAR(10),
    date_booked DATE,
    maturity_date DATE,
    beneficiary VARCHAR(255),
    collateral_type VARCHAR(100),
    collateral_value NUMERIC(20,2),
    date_of_last_renewal DATE,
    status_classification VARCHAR(50),
    expected_loss NUMERIC(20,2),
    sector VARCHAR(100),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 49: QFCR 372 - Note Issuance Facilities / Revolving Underwriting
-- ============================================================================
CREATE TABLE qfcr_372 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    customer_name VARCHAR(255),
    customer_address TEXT,
    customer_id VARCHAR(50),
    transaction_code VARCHAR(50),
    transaction_type TEXT,
    amount NUMERIC(20,2),
    currency_type VARCHAR(10),
    date_booked DATE,
    maturity_date DATE,
    beneficiary VARCHAR(255),
    collateral_type VARCHAR(100),
    collateral_value NUMERIC(20,2),
    date_of_last_renewal DATE,
    status_classification VARCHAR(50),
    expected_loss NUMERIC(20,2),
    sector VARCHAR(100),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 50: QFCR 374 - Other Commitments (Maturity Over 1 Year)
-- ============================================================================
CREATE TABLE qfcr_374 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    customer_name VARCHAR(255),
    customer_address TEXT,
    customer_id VARCHAR(50),
    transaction_code VARCHAR(50),
    transaction_type TEXT,
    amount NUMERIC(20,2),
    currency_type VARCHAR(10),
    date_booked DATE,
    maturity_date DATE,
    beneficiary VARCHAR(255),
    collateral_type VARCHAR(100),
    collateral_value NUMERIC(20,2),
    date_of_last_renewal DATE,
    status_classification VARCHAR(50),
    expected_loss NUMERIC(20,2),
    sector VARCHAR(100),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 51: MFCR 376 - Schedule of Contingent Liabilities
-- ============================================================================
CREATE TABLE mfcr_376 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    customer_name VARCHAR(255),
    customer_address TEXT,
    customer_id VARCHAR(50),
    transaction_code VARCHAR(50),
    transaction_type TEXT,
    amount NUMERIC(20,2),
    currency_type VARCHAR(10),
    date_booked DATE,
    maturity_date DATE,
    beneficiary VARCHAR(255),
    collateral_type VARCHAR(100),
    collateral_value NUMERIC(20,2),
    date_of_last_renewal DATE,
    status_classification VARCHAR(50),  -- PERFORMING / NON-PERFORMING
    expected_loss NUMERIC(20,2),
    sector VARCHAR(100),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 52: QFCR 376-1 - Contingent Liabilities (Maturity Up to 1 Year)
-- ============================================================================
CREATE TABLE qfcr_376_1 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    customer_name VARCHAR(255),
    customer_address TEXT,
    customer_id VARCHAR(50),
    transaction_code VARCHAR(50),
    transaction_type TEXT,
    amount NUMERIC(20,2),
    currency_type VARCHAR(10),
    date_booked DATE,
    maturity_date DATE,
    beneficiary VARCHAR(255),
    collateral_type VARCHAR(100),
    collateral_value NUMERIC(20,2),
    date_of_last_renewal DATE,
    status_classification VARCHAR(50),
    expected_loss NUMERIC(20,2),
    sector VARCHAR(100),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 53: QFCR 377 - Borrowings from Banks
-- ============================================================================
CREATE TABLE qfcr_377 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    bank_code VARCHAR(20),
    bank_name VARCHAR(255),
    amount_ngn NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 54: QFCR 379 - Borrowings from Other Finance Companies
-- ============================================================================
CREATE TABLE qfcr_379 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    institution_code VARCHAR(20),
    institution_name VARCHAR(255),
    amount_ngn NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 55: QFCR 381 - Borrowings from Other Financial Institutions
-- ============================================================================
CREATE TABLE qfcr_381 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    institution_code VARCHAR(20),
    institution_name VARCHAR(255),
    amount_ngn NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 56: MFCR 334-1 - Schedule of Certificates of Deposit Issued
-- ============================================================================
CREATE TABLE mfcr_334_1 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    bank_code VARCHAR(20),
    bank_name VARCHAR(255),
    effective_date DATE,
    currency_type VARCHAR(10),          -- NGN / USD
    amount_ngn NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 57: MFCR 334 - Schedule of Certificates of Deposit Held
-- ============================================================================
CREATE TABLE mfcr_334 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    bank_code VARCHAR(20),
    bank_name VARCHAR(255),
    effective_date DATE,
    currency_type VARCHAR(10),
    amount_ngn NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 58: QFCR 381-1 - Schedule of Borrowings (Combined)
-- ============================================================================
CREATE TABLE qfcr_381_1 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    fi_code VARCHAR(20),
    institution_name VARCHAR(255),
    institution_type VARCHAR(100),      -- Banks in Nigeria, Banks outside Nigeria, Finance Companies, OFI, Corporate Bodies, Individuals
    sector VARCHAR(100),
    amount_ngn NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 59: MFCR 385 - Derivative Financial Liabilities
-- ============================================================================
CREATE TABLE mfcr_385 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    derivative_type VARCHAR(100),       -- For Hedging / Non-Hedging
    notional_amount NUMERIC(20,2),
    carrying_value NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 60: MFCR 387 - Other Liabilities
-- ============================================================================
CREATE TABLE mfcr_387 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    item_code INT NOT NULL,             -- 34702-34740
    item_description VARCHAR(255),
    amount_naira NUMERIC(20,2),
    amount_foreign NUMERIC(20,2),
    total NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 61: MFCR 388 - Breakdown of Other Liabilities
-- ============================================================================
CREATE TABLE mfcr_388 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    branch_name VARCHAR(255),
    creditor_name VARCHAR(255),
    creditor_address TEXT,
    account_number VARCHAR(50),
    description TEXT,
    amount_naira NUMERIC(20,2),
    amount_foreign NUMERIC(20,2),
    total NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 62: MFCR 395 - Recoveries from Classified Accounts
-- ============================================================================
CREATE TABLE mfcr_395 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    customer_name VARCHAR(255),
    account_number VARCHAR(50),
    borrower_code VARCHAR(50),
    loan_amount NUMERIC(20,2),
    insider_related BOOLEAN,
    impairment NUMERIC(20,2),
    loan_type VARCHAR(100),
    recovered_amount NUMERIC(20,2),
    amount_outstanding NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 63: MFCR 397 - Recoveries from Fully Impaired Accounts
-- ============================================================================
CREATE TABLE mfcr_397 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    customer_name VARCHAR(255),
    account_number VARCHAR(50),
    loan_type VARCHAR(100),
    insider_related BOOLEAN,
    amount_written_off NUMERIC(20,2),
    opening_recovery_amount NUMERIC(20,2),
    amount_recovered_period NUMERIC(20,2),
    recovered_amount_total NUMERIC(20,2),
    amount_outstanding NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 64: MFCR 397-1 - Quarterly Recoveries (Combined Impaired/Classified)
-- ============================================================================
CREATE TABLE mfcr_397_1 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    customer_name VARCHAR(255),
    account_number VARCHAR(50),
    borrower_code VARCHAR(50),
    loan_amount NUMERIC(20,2),
    insider_related BOOLEAN,
    impairment NUMERIC(20,2),
    loan_type VARCHAR(100),
    account_type VARCHAR(20),           -- Impaired / Classified
    recovered_amount NUMERIC(20,2),
    amount_outstanding NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 65: MFCR 1002 - Income from Government Securities
-- ============================================================================
CREATE TABLE mfcr_1002 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    item_code INT NOT NULL,             -- 10201-10209
    item_description VARCHAR(255),
    book_value NUMERIC(20,2),
    coupon_rate NUMERIC(10,4),
    interest_income NUMERIC(20,2),
    fair_value_gain_loss NUMERIC(20,2),
    gain_loss_on_disposal NUMERIC(20,2),
    total_amount NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 66: MFCR 1004 - Other Interest Income
-- ============================================================================
CREATE TABLE mfcr_1004 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    item_description TEXT,
    amount_ngn NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 67: MFCR 1006 - Breakdown of Interest Income
-- ============================================================================
CREATE TABLE mfcr_1006 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    item_code INT,                      -- 10101-10110+
    item_description VARCHAR(255),
    latest_month NUMERIC(20,2),
    year_to_date NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 68: MFCR 1008 - Other Interest Expense
-- ============================================================================
CREATE TABLE mfcr_1008 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    item_description TEXT,
    amount_ngn NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 69: MFCR 1010 - Breakdown of Total Interest Expense
-- ============================================================================
CREATE TABLE mfcr_1010 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    item_code INT,                      -- 10301-10307
    item_description VARCHAR(255),
    latest_month NUMERIC(20,2),
    year_to_date NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 70: MFCR 1012 - Other Fees
-- ============================================================================
CREATE TABLE mfcr_1012 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    item_description TEXT,
    amount_ngn NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 71: MFCR 1014 - Equity Investment Income
-- ============================================================================
CREATE TABLE mfcr_1014 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    company_name VARCHAR(255),
    type_of_business VARCHAR(100),
    amount_invested NUMERIC(20,2),
    percent_holding NUMERIC(10,4),
    carrying_value NUMERIC(20,2),
    disposal_value NUMERIC(20,2),
    dividend_income NUMERIC(20,2),
    profit_loss_for_period NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 72: MFCR 1016 - Other Trading Income
-- ============================================================================
CREATE TABLE mfcr_1016 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    item_description TEXT,
    type_of_income VARCHAR(100),
    amount_ngn NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 73: MFCR 1018 - Other Income
-- ============================================================================
CREATE TABLE mfcr_1018 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    item_description TEXT,
    amount_ngn NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 74: MFCR 1018-1 - Breakdown of Income (Combined)
-- ============================================================================
CREATE TABLE mfcr_1018_1 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    item_description TEXT,
    type_of_income VARCHAR(100),        -- Other Fees, Other Income, Trading Investment Income
    amount_ngn NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 75: MFCR 1020 - Other Operating Expenses
-- ============================================================================
CREATE TABLE mfcr_1020 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    item_code INT,                      -- 10401-10412
    item_description VARCHAR(255),
    amount_ngn NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 76: MFCR 1510 - Credit to Directors/Employees/Shareholders
-- ============================================================================
CREATE TABLE mfcr_1510 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    account_number VARCHAR(50),
    customer_name VARCHAR(255),
    amount_granted NUMERIC(20,2),
    relationship VARCHAR(100),
    date_granted DATE,
    expiry_date DATE,
    principal_payable NUMERIC(20,2),
    accrued_interest NUMERIC(20,2),
    total_principal_interest NUMERIC(20,2),
    interest_rate NUMERIC(10,4),
    security_nature VARCHAR(255),
    security_value NUMERIC(20,2),
    security_valuation_date DATE,
    security_amount NUMERIC(20,2),
    remarks TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 77: MFCR 1520 - Lending Above Statutory Limit
-- ============================================================================
CREATE TABLE mfcr_1520 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    shareholders_fund NUMERIC(20,2),
    reserves NUMERIC(20,2),
    total_capital_reserves NUMERIC(20,2),
    lending_limit NUMERIC(20,2),
    serial_no INT,
    customer_name VARCHAR(255),
    facility_type VARCHAR(100),
    amount_authorised NUMERIC(20,2),
    date_authorised DATE,
    expiry_date DATE,
    unutilised_credit NUMERIC(20,2),
    outstanding_credit NUMERIC(20,2),
    total_credit NUMERIC(20,2),
    credit_performance_status VARCHAR(50),
    date_utilisation_exceeded_limit DATE,
    cbn_approval_no VARCHAR(50),
    remarks TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 78: MFCR 1530 - Borrowings from Individuals & Non-Financial Companies
-- ============================================================================
CREATE TABLE mfcr_1530 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    customer VARCHAR(255),
    amount_borrowed NUMERIC(20,2),
    certificate_no VARCHAR(50),
    tenor VARCHAR(50),
    effective_date DATE,
    maturity_date DATE,
    interest_rate NUMERIC(10,4),
    upfront_interest_paid NUMERIC(20,2),
    accrued_interest_payable NUMERIC(20,2),
    times_rolled_over INT,
    remark TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 79: MFCR 1540 - Maturity Profile of Financial Assets & Liabilities
-- ============================================================================
CREATE TABLE mfcr_1540 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    item_code INT NOT NULL,
    item_description VARCHAR(255),
    asset_or_liability VARCHAR(20),         -- ASSET / LIABILITY
    tenor_0_30_days NUMERIC(20,2),
    tenor_31_90_days NUMERIC(20,2),
    tenor_91_180_days NUMERIC(20,2),
    tenor_181_365_days NUMERIC(20,2),
    tenor_above_1yr_below_3yr NUMERIC(20,2),
    tenor_3yr_and_above NUMERIC(20,2),
    total NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 80: MFCR 1550 - Credit by Sector & Loan Type
-- ============================================================================
CREATE TABLE mfcr_1550 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    item_code INT NOT NULL,
    sector_name VARCHAR(255),
    loan_counterparty VARCHAR(100),
    currency_type VARCHAR(10),
    term_loan NUMERIC(20,2),
    overdraft NUMERIC(20,2),
    other_loans NUMERIC(20,2),
    advances_under_lease NUMERIC(20,2),
    bankers_acceptances NUMERIC(20,2),
    commercial_papers NUMERIC(20,2),
    bills_discounted NUMERIC(20,2),
    others NUMERIC(20,2),
    performing NUMERIC(20,2),
    watchlist NUMERIC(20,2),
    substandard NUMERIC(20,2),
    doubtful NUMERIC(20,2),
    very_doubtful NUMERIC(20,2),
    lost NUMERIC(20,2),
    total NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 81: MFCR 1570 - Frauds & Forgeries
-- ============================================================================
CREATE TABLE mfcr_1570 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    surname VARCHAR(100),
    first_name VARCHAR(100),
    middle_name VARCHAR(100),
    designation VARCHAR(100),
    sex VARCHAR(10),
    date_of_birth DATE,
    nationality VARCHAR(50),
    state_of_origin VARCHAR(50),
    passport_number VARCHAR(50),
    national_id_number VARCHAR(50),
    branch_name VARCHAR(255),
    branch_code VARCHAR(20),
    state_code VARCHAR(10),
    department VARCHAR(100),
    date_of_fraud DATE,
    type_of_fraud VARCHAR(100),
    date_discovered DATE,
    amount_involved NUMERIC(20,2),
    fraud_status VARCHAR(100),
    amount_recovered NUMERIC(20,2),
    actual_loss NUMERIC(20,2),
    action_type VARCHAR(100),
    action_date DATE,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 82: MFCR 1590 - Dismissed/Terminated Staff
-- ============================================================================
CREATE TABLE mfcr_1590 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    surname VARCHAR(100),
    first_name VARCHAR(100),
    middle_name VARCHAR(100),
    staff_code VARCHAR(50),
    designation VARCHAR(100),
    permanent_home_address TEXT,
    date_of_birth DATE,
    staff_name VARCHAR(255),
    branch_name VARCHAR(255),
    department VARCHAR(100),
    prev_org_name VARCHAR(255),
    prev_org_address TEXT,
    prev_period_from DATE,
    prev_period_to DATE,
    date_terminated DATE,
    reason_for_termination TEXT,
    fraud_type VARCHAR(100),
    nok_surname VARCHAR(100),
    nok_first_name VARCHAR(100),
    nok_middle_name VARCHAR(100),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 83: MFCR 1600 - Consumer Complaints
-- ============================================================================
CREATE TABLE mfcr_1600 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    complaint_ref_no VARCHAR(50),
    petitioner_name VARCHAR(255),
    address_contact TEXT,
    email VARCHAR(255),
    subject TEXT,
    category VARCHAR(100),
    date_received DATE,
    date_resolved DATE,
    resolution_efforts TEXT,
    major_disagreement_areas TEXT,
    currency_type VARCHAR(10),
    amount_claimed NUMERIC(20,2),
    amount_refunded NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 84: MFCR 1610 - Loans to State Governments and FCT
-- ============================================================================
CREATE TABLE mfcr_1610 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    state_name VARCHAR(100),
    state_code VARCHAR(10),
    entity_type VARCHAR(50),                -- STATE PARASTATAL / STATE GOVERNMENT
    term_loan_up_to_2yr NUMERIC(20,2),
    term_loan_2_5yr NUMERIC(20,2),
    term_loan_5_10yr NUMERIC(20,2),
    term_loan_over_10yr NUMERIC(20,2),
    term_loan_subtotal NUMERIC(20,2),
    overdraft NUMERIC(20,2),
    others NUMERIC(20,2),
    total_loans NUMERIC(20,2),
    bonds_up_to_2yr NUMERIC(20,2),
    bonds_2_5yr NUMERIC(20,2),
    bonds_5_10yr NUMERIC(20,2),
    bonds_over_10yr NUMERIC(20,2),
    total_bonds NUMERIC(20,2),
    grand_total NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 85: MFCR 1620 - Loans to Local Governments
-- ============================================================================
CREATE TABLE mfcr_1620 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    state_name VARCHAR(100),
    state_code VARCHAR(10),
    lg_name VARCHAR(255),
    lg_code VARCHAR(20),
    term_loan_up_to_2yr NUMERIC(20,2),
    term_loan_2_5yr NUMERIC(20,2),
    term_loan_5_10yr NUMERIC(20,2),
    term_loan_over_10yr NUMERIC(20,2),
    overdraft NUMERIC(20,2),
    others NUMERIC(20,2),
    total_loans NUMERIC(20,2),
    bonds_up_to_2yr NUMERIC(20,2),
    bonds_2_5yr NUMERIC(20,2),
    bonds_5_10yr NUMERIC(20,2),
    bonds_over_10yr NUMERIC(20,2),
    total_bonds NUMERIC(20,2),
    grand_total NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 86: MFCR 1630 - Quarterly Statistical Returns
-- ============================================================================
CREATE TABLE mfcr_1630 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    item_description VARCHAR(255),
    cbn_naira NUMERIC(20,2),
    cbn_foreign NUMERIC(20,2),
    other_depository_corp_naira NUMERIC(20,2),
    other_depository_corp_foreign NUMERIC(20,2),
    other_financial_corp_naira NUMERIC(20,2),
    other_financial_corp_foreign NUMERIC(20,2),
    public_nfc_naira NUMERIC(20,2),
    public_nfc_foreign NUMERIC(20,2),
    other_domestic_naira NUMERIC(20,2),
    other_domestic_foreign NUMERIC(20,2),
    central_govt_naira NUMERIC(20,2),
    central_govt_foreign NUMERIC(20,2),
    state_govt_naira NUMERIC(20,2),
    state_govt_foreign NUMERIC(20,2),
    local_govt_naira NUMERIC(20,2),
    local_govt_foreign NUMERIC(20,2),
    other_residents_naira NUMERIC(20,2),
    other_residents_foreign NUMERIC(20,2),
    non_residents_naira NUMERIC(20,2),
    non_residents_foreign NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 87: SFCR 1900 - Semi-Annual Return on Investment in Shares
-- ============================================================================
CREATE TABLE sfcr_1900 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    company_name VARCHAR(255),
    investment_type VARCHAR(100),
    cbn_approval_date DATE,
    place_of_incorporation VARCHAR(255),
    business_description TEXT,
    percentage_ownership NUMERIC(10,4),
    acquisition_date DATE,
    is_quoted BOOLEAN,
    nominal_value_per_share NUMERIC(20,2),
    cost_of_purchase NUMERIC(20,2),
    book_value NUMERIC(20,2),
    fair_value NUMERIC(20,2),
    date_sold DATE,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 88: SFCR 1910 - Corporate Profile
-- ============================================================================
CREATE TABLE sfcr_1910 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    institution_code VARCHAR(20),
    institution_name VARCHAR(255),
    address TEXT,
    city VARCHAR(100),
    state VARCHAR(100),
    licence_no VARCHAR(50),
    phone_1 VARCHAR(50),
    phone_2 VARCHAR(50),
    fax VARCHAR(50),
    number_of_branches INT,
    last_cbn_exam_date DATE,
    senior_staff_count INT,
    junior_staff_count INT,
    audit_firm VARCHAR(255),
    audit_firm_address TEXT,
    institution_email VARCHAR(255),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 89: SFCR 1920 - Branch Network
-- ============================================================================
CREATE TABLE sfcr_1920 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    branch_code VARCHAR(20),
    branch_name VARCHAR(255),
    branch_address TEXT,
    city VARCHAR(100),
    state VARCHAR(100),
    date_opened DATE,
    total_staff INT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 90: SFCR 1930 - Directors
-- ============================================================================
CREATE TABLE sfcr_1930 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    last_name VARCHAR(100),
    middle_name VARCHAR(100),
    first_name VARCHAR(100),
    office_address TEXT,
    office_phone VARCHAR(50),
    home_address TEXT,
    home_phone VARCHAR(50),
    title VARCHAR(50),
    birth_date DATE,
    profession_occupation VARCHAR(100),
    academic_qualification VARCHAR(255),
    date_appointed DATE,
    mode_of_appointment VARCHAR(50),
    exec_non_exec VARCHAR(20),
    date_resigned DATE,
    related_companies TEXT,
    address TEXT,
    rc_br_sr_no VARCHAR(50),
    crm_no VARCHAR(50),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 91: SFCR 1940 - Shareholders
-- ============================================================================
CREATE TABLE sfcr_1940 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    shareholder_name VARCHAR(255),
    shareholder_address TEXT,
    state_of_origin VARCHAR(100),
    date_of_birth DATE,
    rc_br_sr_no VARCHAR(50),
    equity_interest_pct NUMERIC(10,4),
    number_of_shares_held BIGINT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 92: SFCR 1950 - Management and Top Officers
-- ============================================================================
CREATE TABLE sfcr_1950 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    full_names VARCHAR(255),
    home_address TEXT,
    phone_no VARCHAR(50),
    title VARCHAR(100),
    birth_date DATE,
    academic_qualification VARCHAR(255),
    position VARCHAR(100),
    date_appointed DATE,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 93: SFCR 1960 - Branch Closures
-- ============================================================================
CREATE TABLE sfcr_1960 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    branch_code VARCHAR(20),
    branch_name VARCHAR(255),
    date_opened DATE,
    date_closed DATE,
    cbn_approval_date DATE,
    remarks TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 94: FC CAR 1 - Capital Adequacy Requirement (Risk Weighted Assets)
-- ============================================================================
CREATE TABLE fc_car_1 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    item_code VARCHAR(20),
    item_description VARCHAR(255),
    asset_value NUMERIC(20,2),
    risk_weight NUMERIC(10,4),
    risk_weighted_asset NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 95: FC CAR 2 - Capital Adequacy (Qualifying Capital Computation)
-- ============================================================================
CREATE TABLE fc_car_2 (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    item_code VARCHAR(20),
    item_description VARCHAR(255),
    -- Tier 1 Capital
    tier1_amount NUMERIC(20,2),
    -- Tier 2 Capital
    tier2_amount NUMERIC(20,2),
    total_qualifying_capital NUMERIC(20,2),
    total_risk_weighted_assets NUMERIC(20,2),
    capital_adequacy_ratio NUMERIC(10,4),
    minimum_ratio NUMERIC(10,4) DEFAULT 0.125,  -- 12.5%
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 96: FC ACR - Adjusted Capital Ratio
-- ============================================================================
CREATE TABLE fc_acr (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    capital_funds NUMERIC(20,2),
    net_credit NUMERIC(20,2),
    adjusted_capital_ratio NUMERIC(10,4),
    minimum_ratio NUMERIC(10,4) DEFAULT 0.10,   -- 1:10
    is_compliant BOOLEAN,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 97: FC FHR - Financial Health/Indicators Report
-- ============================================================================
CREATE TABLE fc_fhr (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    indicator_name VARCHAR(255),
    indicator_value NUMERIC(20,4),
    indicator_unit VARCHAR(20),         -- %, Ratio, Amount
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 98: FC CVR - Contraventions/Penalties Report
-- ============================================================================
CREATE TABLE fc_cvr (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    contravention_type VARCHAR(255),
    contravention_details TEXT,
    penalty_amount NUMERIC(20,2),
    penalty_rate NUMERIC(20,2),
    days_late INT,
    is_waived BOOLEAN DEFAULT FALSE,
    waiver_date DATE,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 99: FC RATING - CAMEL Rating
-- ============================================================================
CREATE TABLE fc_rating (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    -- CAMEL Components
    capital_adequacy_score NUMERIC(10,4),
    asset_quality_score NUMERIC(10,4),
    management_score NUMERIC(10,4),
    earnings_score NUMERIC(10,4),
    liquidity_score NUMERIC(10,4),
    composite_rating NUMERIC(10,4),
    rating_grade VARCHAR(10),           -- 1-5 or A-E
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 100: CONSOL - Consolidated Aggregate Statements
-- ============================================================================
CREATE TABLE consol (
    id SERIAL PRIMARY KEY,
    reporting_date DATE NOT NULL,
    return_code VARCHAR(20),            -- MFCR300, MFCR1000
    item_code VARCHAR(20),
    item_description VARCHAR(255),
    consolidated_amount NUMERIC(20,2),
    mom_change NUMERIC(20,2),           -- Month-on-Month change
    yoy_change NUMERIC(20,2),           -- Year-on-Year change
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 101: NPL - Non-Performing Loans Schedule
-- ============================================================================
CREATE TABLE npl (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES return_submissions(id),
    serial_no INT,
    customer_code VARCHAR(50),
    customer_name VARCHAR(255),
    total_credit NUMERIC(20,2),
    principal_due_unpaid NUMERIC(20,2),
    accrued_interest_unpaid NUMERIC(20,2),
    total_outstanding NUMERIC(20,2),
    watchlist NUMERIC(20,2),
    substandard NUMERIC(20,2),
    doubtful NUMERIC(20,2),
    very_doubtful NUMERIC(20,2),
    lost NUMERIC(20,2),
    fc_provision NUMERIC(20,2),
    remarks TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 102: REPORTS - Key Risk Indicators
-- ============================================================================
CREATE TABLE reports_kri (
    id SERIAL PRIMARY KEY,
    reporting_date DATE,
    serial_no INT,
    indicator_name VARCHAR(255),
    threshold NUMERIC(10,4),
    computed_value NUMERIC(20,4),
    comment TEXT,
    trend VARCHAR(50),                  -- MoM, YoY
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- TABLE 103: Sheet3 - Top 10 FCs Rankings
-- ============================================================================
CREATE TABLE sheet3_top10_rankings (
    id SERIAL PRIMARY KEY,
    reporting_date DATE,
    ranking_category VARCHAR(100),      -- TOTAL ASSETS, TOTAL BORROWINGS, etc.
    rank_position INT,
    institution_code VARCHAR(20),
    institution_name VARCHAR(255),
    amount NUMERIC(20,2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- INDEXES
-- ============================================================================

CREATE INDEX idx_submissions_institution ON return_submissions(institution_id);
CREATE INDEX idx_submissions_period ON return_submissions(return_period_id);
CREATE INDEX idx_submissions_code ON return_submissions(return_code);

CREATE INDEX idx_mfcr300_sub ON mfcr_300(submission_id);
CREATE INDEX idx_mfcr1000_sub ON mfcr_1000(submission_id);
CREATE INDEX idx_mfcr100_sub ON mfcr_100(submission_id);
CREATE INDEX idx_mfcr302_sub ON mfcr_302(submission_id);
CREATE INDEX idx_mfcr304_sub ON mfcr_304(submission_id);
CREATE INDEX idx_mfcr306_sub ON mfcr_306(submission_id);
CREATE INDEX idx_mfcr306_1_sub ON mfcr_306_1(submission_id);
CREATE INDEX idx_mfcr308_sub ON mfcr_308(submission_id);
CREATE INDEX idx_mfcr310_sub ON mfcr_310(submission_id);
CREATE INDEX idx_mfcr312_sub ON mfcr_312(submission_id);
CREATE INDEX idx_mfcr314_sub ON mfcr_314(submission_id);
CREATE INDEX idx_mfcr314_1_sub ON mfcr_314_1(submission_id);
CREATE INDEX idx_mfcr316_sub ON mfcr_316(submission_id);
CREATE INDEX idx_mfcr318_sub ON mfcr_318(submission_id);
CREATE INDEX idx_mfcr320_sub ON mfcr_320(submission_id);
CREATE INDEX idx_mfcr322_sub ON mfcr_322(submission_id);
CREATE INDEX idx_mfcr324_sub ON mfcr_324(submission_id);
CREATE INDEX idx_mfcr326_sub ON mfcr_326(submission_id);
CREATE INDEX idx_mfcr328_sub ON mfcr_328(submission_id);
CREATE INDEX idx_mfcr330_sub ON mfcr_330(submission_id);
CREATE INDEX idx_mfcr332_sub ON mfcr_332(submission_id);
CREATE INDEX idx_mfcr334_sub ON mfcr_334(submission_id);
CREATE INDEX idx_mfcr334_1_sub ON mfcr_334_1(submission_id);
CREATE INDEX idx_mfcr336_sub ON mfcr_336(submission_id);
CREATE INDEX idx_mfcr336_1_sub ON mfcr_336_1(submission_id);
CREATE INDEX idx_mfcr338_sub ON mfcr_338(submission_id);
CREATE INDEX idx_mfcr340_sub ON mfcr_340(submission_id);
CREATE INDEX idx_mfcr342_sub ON mfcr_342(submission_id);
CREATE INDEX idx_mfcr344_sub ON mfcr_344(submission_id);
CREATE INDEX idx_mfcr346_sub ON mfcr_346(submission_id);
CREATE INDEX idx_mfcr346_1_sub ON mfcr_346_1(submission_id);
CREATE INDEX idx_mfcr348_sub ON mfcr_348(submission_id);
CREATE INDEX idx_mfcr349_sub ON mfcr_349(submission_id);
CREATE INDEX idx_mfcr350_sub ON mfcr_350(submission_id);
CREATE INDEX idx_mfcr351_sub ON mfcr_351(submission_id);
CREATE INDEX idx_mfcr351_2_sub ON mfcr_351_2(submission_id);
CREATE INDEX idx_mfcr352_sub ON mfcr_352(submission_id);
CREATE INDEX idx_mfcr354_sub ON mfcr_354(submission_id);
CREATE INDEX idx_mfcr356_sub ON mfcr_356(submission_id);
CREATE INDEX idx_mfcr357_sub ON mfcr_357(submission_id);
CREATE INDEX idx_mfcr358_sub ON mfcr_358(submission_id);
CREATE INDEX idx_mfcr360_sub ON mfcr_360(submission_id);
CREATE INDEX idx_mfcr362_sub ON mfcr_362(submission_id);
CREATE INDEX idx_qfcr364_sub ON qfcr_364(submission_id);
CREATE INDEX idx_qfcr366_sub ON qfcr_366(submission_id);
CREATE INDEX idx_qfcr368_sub ON qfcr_368(submission_id);
CREATE INDEX idx_qfcr370_sub ON qfcr_370(submission_id);
CREATE INDEX idx_qfcr372_sub ON qfcr_372(submission_id);
CREATE INDEX idx_qfcr374_sub ON qfcr_374(submission_id);
CREATE INDEX idx_mfcr376_sub ON mfcr_376(submission_id);
CREATE INDEX idx_qfcr376_1_sub ON qfcr_376_1(submission_id);
CREATE INDEX idx_qfcr377_sub ON qfcr_377(submission_id);
CREATE INDEX idx_qfcr379_sub ON qfcr_379(submission_id);
CREATE INDEX idx_qfcr381_sub ON qfcr_381(submission_id);
CREATE INDEX idx_qfcr381_1_sub ON qfcr_381_1(submission_id);
CREATE INDEX idx_mfcr385_sub ON mfcr_385(submission_id);
CREATE INDEX idx_mfcr387_sub ON mfcr_387(submission_id);
CREATE INDEX idx_mfcr388_sub ON mfcr_388(submission_id);
CREATE INDEX idx_mfcr395_sub ON mfcr_395(submission_id);
CREATE INDEX idx_mfcr397_sub ON mfcr_397(submission_id);
CREATE INDEX idx_mfcr397_1_sub ON mfcr_397_1(submission_id);
CREATE INDEX idx_mfcr1002_sub ON mfcr_1002(submission_id);
CREATE INDEX idx_mfcr1004_sub ON mfcr_1004(submission_id);
CREATE INDEX idx_mfcr1006_sub ON mfcr_1006(submission_id);
CREATE INDEX idx_mfcr1008_sub ON mfcr_1008(submission_id);
CREATE INDEX idx_mfcr1010_sub ON mfcr_1010(submission_id);
CREATE INDEX idx_mfcr1012_sub ON mfcr_1012(submission_id);
CREATE INDEX idx_mfcr1014_sub ON mfcr_1014(submission_id);
CREATE INDEX idx_mfcr1016_sub ON mfcr_1016(submission_id);
CREATE INDEX idx_mfcr1018_sub ON mfcr_1018(submission_id);
CREATE INDEX idx_mfcr1018_1_sub ON mfcr_1018_1(submission_id);
CREATE INDEX idx_mfcr1020_sub ON mfcr_1020(submission_id);
CREATE INDEX idx_mfcr1510_sub ON mfcr_1510(submission_id);
CREATE INDEX idx_mfcr1520_sub ON mfcr_1520(submission_id);
CREATE INDEX idx_mfcr1530_sub ON mfcr_1530(submission_id);
CREATE INDEX idx_mfcr1540_sub ON mfcr_1540(submission_id);
CREATE INDEX idx_mfcr1550_sub ON mfcr_1550(submission_id);
CREATE INDEX idx_mfcr1570_sub ON mfcr_1570(submission_id);
CREATE INDEX idx_mfcr1590_sub ON mfcr_1590(submission_id);
CREATE INDEX idx_mfcr1600_sub ON mfcr_1600(submission_id);
CREATE INDEX idx_mfcr1610_sub ON mfcr_1610(submission_id);
CREATE INDEX idx_mfcr1620_sub ON mfcr_1620(submission_id);
CREATE INDEX idx_mfcr1630_sub ON mfcr_1630(submission_id);
CREATE INDEX idx_sfcr1900_sub ON sfcr_1900(submission_id);
CREATE INDEX idx_sfcr1910_sub ON sfcr_1910(submission_id);
CREATE INDEX idx_sfcr1920_sub ON sfcr_1920(submission_id);
CREATE INDEX idx_sfcr1930_sub ON sfcr_1930(submission_id);
CREATE INDEX idx_sfcr1940_sub ON sfcr_1940(submission_id);
CREATE INDEX idx_sfcr1950_sub ON sfcr_1950(submission_id);
CREATE INDEX idx_sfcr1960_sub ON sfcr_1960(submission_id);
CREATE INDEX idx_fc_car1_sub ON fc_car_1(submission_id);
CREATE INDEX idx_fc_car2_sub ON fc_car_2(submission_id);
CREATE INDEX idx_fc_acr_sub ON fc_acr(submission_id);
CREATE INDEX idx_fc_fhr_sub ON fc_fhr(submission_id);
CREATE INDEX idx_fc_cvr_sub ON fc_cvr(submission_id);
CREATE INDEX idx_fc_rating_sub ON fc_rating(submission_id);
CREATE INDEX idx_npl_sub ON npl(submission_id);
