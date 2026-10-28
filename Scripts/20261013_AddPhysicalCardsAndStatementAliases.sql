IF OBJECT_ID(N'dbo.CardsPhysicalCards', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CardsPhysicalCards
    (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CardsPhysicalCards PRIMARY KEY,
        CardId int NOT NULL,
        NameOnCard varchar(100) NOT NULL,
        LastFourDigits char(4) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_CardsPhysicalCards_IsActive DEFAULT (1),
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_CardsPhysicalCards_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_CardsPhysicalCards_Cards FOREIGN KEY (CardId)
            REFERENCES dbo.Cards(Id) ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX UX_CardsPhysicalCards_Card_LastFour_Name
        ON dbo.CardsPhysicalCards (CardId, LastFourDigits, NameOnCard);
END;

IF OBJECT_ID(N'dbo.CardStatementReconciliationMatches', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CardStatementReconciliationMatches
    (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CardStatementReconciliationMatches PRIMARY KEY,
        CardId int NOT NULL,
        Reference varchar(6) NOT NULL,
        TransactionFingerprint char(64) NOT NULL,
        PurchaseDate date NOT NULL,
        PhysicalCardId int NULL,
        LastFourDigits char(4) NOT NULL,
        NormalizedStatementDescription varchar(150) NOT NULL,
        Installment varchar(20) NOT NULL CONSTRAINT DF_CardStatementReconciliationMatches_Installment DEFAULT (''),
        AmountBrl decimal(18,2) NOT NULL,
        Occurrence int NOT NULL CONSTRAINT DF_CardStatementReconciliationMatches_Occurrence DEFAULT (0),
        PostingId int NULL,
        IsResolved bit NOT NULL CONSTRAINT DF_CardStatementReconciliationMatches_IsResolved DEFAULT (0),
        IsIgnored bit NOT NULL CONSTRAINT DF_CardStatementReconciliationMatches_IsIgnored DEFAULT (0),
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_CardStatementReconciliationMatches_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_CardStatementReconciliationMatches_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_CardStatementReconciliationMatches_Cards FOREIGN KEY (CardId)
            REFERENCES dbo.Cards(Id) ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX UX_CardStatementReconciliationMatches_Transaction
        ON dbo.CardStatementReconciliationMatches (CardId, Reference, TransactionFingerprint);

    CREATE INDEX IX_CardStatementReconciliationMatches_PostingId
        ON dbo.CardStatementReconciliationMatches (PostingId);
END;

IF OBJECT_ID(N'dbo.CardStatementReconciliationMatches', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.CardStatementReconciliationMatches', N'IsIgnored') IS NULL
BEGIN
    ALTER TABLE dbo.CardStatementReconciliationMatches
        ADD IsIgnored bit NOT NULL
            CONSTRAINT DF_CardStatementReconciliationMatches_IsIgnored DEFAULT (0);
END;

IF COL_LENGTH(N'dbo.Cards', N'StatementProvider') IS NULL
BEGIN
    ALTER TABLE dbo.Cards ADD StatementProvider varchar(30) NULL;
END;

IF COL_LENGTH(N'dbo.Cards', N'StatementPasswordProtected') IS NULL
BEGIN
    ALTER TABLE dbo.Cards ADD StatementPasswordProtected nvarchar(2048) NULL;
END;

IF COL_LENGTH(N'dbo.CardsPostings', N'PhysicalCardId') IS NULL
BEGIN
    ALTER TABLE dbo.CardsPostings ADD PhysicalCardId int NULL;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_CardsPostings_CardsPhysicalCards'
)
BEGIN
    ALTER TABLE dbo.CardsPostings
        ADD CONSTRAINT FK_CardsPostings_CardsPhysicalCards
        FOREIGN KEY (PhysicalCardId)
        REFERENCES dbo.CardsPhysicalCards(Id)
        ON DELETE SET NULL;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_CardsPostings_PhysicalCardId'
      AND object_id = OBJECT_ID(N'dbo.CardsPostings')
)
BEGIN
    CREATE INDEX IX_CardsPostings_PhysicalCardId
        ON dbo.CardsPostings (PhysicalCardId);
END;

IF OBJECT_ID(N'dbo.CardStatementDescriptionAliases', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CardStatementDescriptionAliases
    (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CardStatementDescriptionAliases PRIMARY KEY,
        CardId int NOT NULL,
        PhysicalCardId int NOT NULL,
        StatementDescription varchar(150) NOT NULL,
        NormalizedStatementDescription varchar(150) NOT NULL,
        BudgetDescription varchar(150) NOT NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_CardStatementDescriptionAliases_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_CardStatementDescriptionAliases_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_CardStatementDescriptionAliases_Cards FOREIGN KEY (CardId)
            REFERENCES dbo.Cards(Id) ON DELETE CASCADE,
        CONSTRAINT FK_CardStatementDescriptionAliases_PhysicalCards FOREIGN KEY (PhysicalCardId)
            REFERENCES dbo.CardsPhysicalCards(Id)
    );

    CREATE UNIQUE INDEX UX_CardStatementDescriptionAliases_Card_Physical_Normalized
        ON dbo.CardStatementDescriptionAliases (CardId, PhysicalCardId, NormalizedStatementDescription);
END;
