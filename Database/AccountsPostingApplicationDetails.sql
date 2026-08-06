IF COL_LENGTH(N'dbo.AccountsApplications', N'MaximumAmount') IS NULL
BEGIN ALTER TABLE dbo.AccountsApplications ADD MaximumAmount decimal(18,2) NULL; END;
GO
IF OBJECT_ID(N'dbo.AccountsPostingApplicationDetails', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.AccountsPostingApplicationDetails
 (
  Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AccountsPostingApplicationDetails PRIMARY KEY,
  AccountPostingId int NOT NULL, AccountApplicationId int NOT NULL,
  Amount decimal(18,2) NOT NULL, GrossAmount decimal(18,2) NULL,
  TotalGrossBalance decimal(18,2) NULL, TotalBalance decimal(18,2) NULL, TotalIOF decimal(18,2) NULL, TotalIR decimal(18,2) NULL,
  IOFElapsedDays int NULL, CreatedAt datetime NOT NULL CONSTRAINT DF_AccountsPostingApplicationDetails_CreatedAt DEFAULT GETDATE(),
  CONSTRAINT UQ_AccountsPostingApplicationDetails_Posting_Application UNIQUE(AccountPostingId, AccountApplicationId),
  CONSTRAINT FK_AccountsPostingApplicationDetails_AccountsPostings FOREIGN KEY(AccountPostingId) REFERENCES dbo.AccountsPostings(Id) ON DELETE CASCADE,
  CONSTRAINT FK_AccountsPostingApplicationDetails_AccountsApplications FOREIGN KEY(AccountApplicationId) REFERENCES dbo.AccountsApplications(Id)
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_AccountsPostingApplicationDetails_AccountApplicationId' AND object_id=OBJECT_ID(N'dbo.AccountsPostingApplicationDetails'))
 CREATE INDEX IX_AccountsPostingApplicationDetails_AccountApplicationId ON dbo.AccountsPostingApplicationDetails(AccountApplicationId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_AccountsPostingApplicationDetails_AccountPostingId' AND object_id=OBJECT_ID(N'dbo.AccountsPostingApplicationDetails'))
 CREATE INDEX IX_AccountsPostingApplicationDetails_AccountPostingId ON dbo.AccountsPostingApplicationDetails(AccountPostingId);
GO

IF COL_LENGTH(N'dbo.AccountsPostingApplicationDetails', N'TotalBalance') IS NULL
BEGIN
 ALTER TABLE dbo.AccountsPostingApplicationDetails ADD TotalBalance decimal(18,2) NULL;
END;
GO
