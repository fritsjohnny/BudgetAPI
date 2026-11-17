-- Adds the account-level IOF exemption flag.
-- Existing accounts default to taxable (0), preserving current calculations.
IF COL_LENGTH(N'dbo.Accounts', N'IsIOFExempt') IS NULL
BEGIN
    ALTER TABLE [dbo].[Accounts]
        ADD [IsIOFExempt] bit NOT NULL
            CONSTRAINT [DF_Accounts_IsIOFExempt] DEFAULT (0);
END;
