IF COL_LENGTH('dbo.AccountsPostings', 'DiRatePercent') IS NULL
    ALTER TABLE dbo.AccountsPostings ADD DiRatePercent decimal(18,10) NULL;
GO

IF COL_LENGTH('dbo.AccountsPostings', 'DiRateDate') IS NULL
    ALTER TABLE dbo.AccountsPostings ADD DiRateDate date NULL;
GO

IF COL_LENGTH('dbo.AccountsPostings', 'DiRateFallback') IS NULL
    ALTER TABLE dbo.AccountsPostings ADD DiRateFallback bit NULL;
GO

CREATE INDEX IX_AccountsPostings_AccountId_Type_DiRateDate
    ON dbo.AccountsPostings (AccountId, Type, DiRateDate DESC)
    INCLUDE (DiRatePercent, DiRateFallback)
    WHERE DiRatePercent IS NOT NULL AND DiRateDate IS NOT NULL;
GO
