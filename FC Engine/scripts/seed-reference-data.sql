-- Seed reference data for FC Engine
-- Run this after migrations have been applied

-- Insert sample institutions
IF NOT EXISTS (SELECT 1 FROM institutions WHERE InstitutionCode = 'FC001')
BEGIN
    INSERT INTO institutions (InstitutionCode, InstitutionName, LicenseType, IsActive, CreatedAt)
    VALUES
        ('FC001', 'Sample Finance Company Ltd', 'Finance Company', 1, GETUTCDATE()),
        ('FC002', 'Example Microfinance Bank', 'Microfinance Bank', 1, GETUTCDATE());
END

-- Insert return periods for 2024-2025
DECLARE @year INT = 2024;
DECLARE @month INT = 1;

WHILE @year <= 2025
BEGIN
    WHILE @month <= 12
    BEGIN
        -- Monthly periods
        IF NOT EXISTS (SELECT 1 FROM return_periods WHERE [Year] = @year AND [Month] = @month AND Frequency = 'Monthly')
        BEGIN
            INSERT INTO return_periods ([Year], [Month], Frequency, ReportingDate, IsOpen, CreatedAt)
            VALUES (@year, @month, 'Monthly', EOMONTH(DATEFROMPARTS(@year, @month, 1)), 1, GETUTCDATE());
        END

        -- Quarterly periods (March, June, September, December)
        IF @month IN (3, 6, 9, 12) AND NOT EXISTS (SELECT 1 FROM return_periods WHERE [Year] = @year AND [Month] = @month AND Frequency = 'Quarterly')
        BEGIN
            INSERT INTO return_periods ([Year], [Month], Frequency, ReportingDate, IsOpen, CreatedAt)
            VALUES (@year, @month, 'Quarterly', EOMONTH(DATEFROMPARTS(@year, @month, 1)), 1, GETUTCDATE());
        END

        -- Semi-Annual periods (June, December)
        IF @month IN (6, 12) AND NOT EXISTS (SELECT 1 FROM return_periods WHERE [Year] = @year AND [Month] = @month AND Frequency = 'SemiAnnual')
        BEGIN
            INSERT INTO return_periods ([Year], [Month], Frequency, ReportingDate, IsOpen, CreatedAt)
            VALUES (@year, @month, 'SemiAnnual', EOMONTH(DATEFROMPARTS(@year, @month, 1)), 1, GETUTCDATE());
        END

        SET @month = @month + 1;
    END
    SET @month = 1;
    SET @year = @year + 1;
END

PRINT 'Reference data seeded successfully.';
