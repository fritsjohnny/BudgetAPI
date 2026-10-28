IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.Users', N'Email') IS NULL
        ALTER TABLE dbo.Users ADD Email nvarchar(254) NULL;

    IF COL_LENGTH(N'dbo.Users', N'EmailVerified') IS NULL
        ALTER TABLE dbo.Users ADD EmailVerified bit NOT NULL
            CONSTRAINT DF_Users_EmailVerified DEFAULT (0);

    IF COL_LENGTH(N'dbo.Users', N'PendingEmail') IS NULL
        ALTER TABLE dbo.Users ADD PendingEmail nvarchar(254) NULL;

    IF COL_LENGTH(N'dbo.Users', N'PasswordChangedAt') IS NULL
        ALTER TABLE dbo.Users ADD PasswordChangedAt datetime2(7) NULL;
END;
GO

IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.Users', N'Email') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1 FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = N'UX_Users_Email'
   )
BEGIN
    CREATE UNIQUE INDEX UX_Users_Email
        ON dbo.Users(Email)
        WHERE Email IS NOT NULL;
END;
GO

IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.Users', N'PendingEmail') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1 FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = N'UX_Users_PendingEmail'
   )
BEGIN
    CREATE UNIQUE INDEX UX_Users_PendingEmail
        ON dbo.Users(PendingEmail)
        WHERE PendingEmail IS NOT NULL;
END;
GO

IF OBJECT_ID(N'dbo.AccountVerificationCodes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AccountVerificationCodes
    (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AccountVerificationCodes PRIMARY KEY,
        UserId int NOT NULL,
        Purpose nvarchar(32) NOT NULL,
        ValueHash char(64) NOT NULL,
        Email nvarchar(254) NULL,
        CreatedAtUtc datetime2(0) NOT NULL,
        ExpiresAtUtc datetime2(0) NOT NULL,
        CONSTRAINT FK_AccountVerificationCodes_Users
            FOREIGN KEY (UserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE
    );
END;
GO

IF OBJECT_ID(N'dbo.AccountVerificationCodes', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1 FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'dbo.AccountVerificationCodes')
         AND name = N'UX_AccountVerificationCodes_ValueHash'
   )
BEGIN
    CREATE UNIQUE INDEX UX_AccountVerificationCodes_ValueHash
        ON dbo.AccountVerificationCodes(ValueHash);
END;
GO

IF OBJECT_ID(N'dbo.AccountVerificationCodes', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1 FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'dbo.AccountVerificationCodes')
         AND name = N'IX_AccountVerificationCodes_User_Purpose_Created'
   )
BEGIN
    CREATE INDEX IX_AccountVerificationCodes_User_Purpose_Created
        ON dbo.AccountVerificationCodes(UserId, Purpose, CreatedAtUtc);
END;
GO
