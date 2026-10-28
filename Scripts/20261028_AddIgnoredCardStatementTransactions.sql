IF OBJECT_ID(N'dbo.CardStatementReconciliationMatches', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.CardStatementReconciliationMatches', N'IsIgnored') IS NULL
BEGIN
    ALTER TABLE dbo.CardStatementReconciliationMatches
        ADD IsIgnored bit NOT NULL
            CONSTRAINT DF_CardStatementReconciliationMatches_IsIgnored DEFAULT (0);
END;

IF OBJECT_ID(N'dbo.CardStatementReconciliationMatches', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.CardStatementReconciliationMatches', N'PostingId') IS NOT NULL
   AND EXISTS
   (
       SELECT 1
       FROM sys.columns
       WHERE object_id = OBJECT_ID(N'dbo.CardStatementReconciliationMatches')
         AND name = N'PostingId'
         AND is_nullable = 0
   )
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.CardStatementReconciliationMatches')
          AND name = N'IX_CardStatementReconciliationMatches_PostingId'
    )
    BEGIN
        DROP INDEX IX_CardStatementReconciliationMatches_PostingId
            ON dbo.CardStatementReconciliationMatches;
    END;

    ALTER TABLE dbo.CardStatementReconciliationMatches
        ALTER COLUMN PostingId int NULL;
END;

IF OBJECT_ID(N'dbo.CardStatementReconciliationMatches', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1
       FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'dbo.CardStatementReconciliationMatches')
         AND name = N'IX_CardStatementReconciliationMatches_PostingId'
   )
BEGIN
    CREATE INDEX IX_CardStatementReconciliationMatches_PostingId
        ON dbo.CardStatementReconciliationMatches (PostingId);
END;
