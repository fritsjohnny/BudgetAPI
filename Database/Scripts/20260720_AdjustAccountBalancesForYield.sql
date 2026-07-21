SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER FUNCTION dbo.GetRealBalance
(
    @AccountId INT,
    @UserId INT
)
RETURNS NUMERIC(18, 2)
AS
BEGIN
    DECLARE @Result NUMERIC(18, 2);

    SELECT @Result = SUM(ap.Amount)
    FROM dbo.AccountsPostings ap
    INNER JOIN dbo.Accounts a ON a.Id = ap.AccountId
    WHERE a.UserId = @UserId
      AND (@AccountId = 0 OR ap.AccountId = @AccountId);

    RETURN ISNULL(@Result, 0);
END;
GO

CREATE OR ALTER FUNCTION dbo.GetRealGrossBalance
(
    @AccountId INT,
    @UserId INT
)
RETURNS NUMERIC(18, 2)
AS
BEGIN
    DECLARE @Result NUMERIC(18, 2);

    SELECT @Result = SUM(
        CASE
            WHEN ISNULL(a.TotalBalanceGross, 0) = 0
                THEN dbo.GetRealBalance(a.Id, @UserId)
            ELSE a.TotalBalanceGross
        END
    )
    FROM dbo.Accounts a
    WHERE a.UserId = @UserId
      AND (@AccountId = 0 OR a.Id = @AccountId);

    RETURN ISNULL(@Result, 0);
END;
GO

CREATE OR ALTER FUNCTION dbo.GetTotalGrossBalance
(
    @AccountId INT,
    @Reference VARCHAR(6),
    @UserId INT
)
RETURNS NUMERIC(18, 2)
AS
BEGIN
    DECLARE @CurrentGross NUMERIC(18, 2);
    DECLARE @FutureMovements NUMERIC(18, 2);

    SET @CurrentGross = dbo.GetRealGrossBalance(@AccountId, @UserId);

    SELECT @FutureMovements = SUM(COALESCE(ap.GrossAmount, ap.Amount, 0))
    FROM dbo.AccountsPostings ap
    INNER JOIN dbo.Accounts a ON a.Id = ap.AccountId
    WHERE a.UserId = @UserId
      AND (@AccountId = 0 OR ap.AccountId = @AccountId)
      AND ap.Reference > @Reference;

    RETURN ISNULL(@CurrentGross, 0) - ISNULL(@FutureMovements, 0);
END;
GO

CREATE OR ALTER FUNCTION dbo.GetAccountTotals
(
    @AccountId INT,
    @Reference VARCHAR(6),
    @UserId INT
)
RETURNS TABLE
AS
RETURN
(
    SELECT a.Id,
           a.UserId,
           a.Name,
           a.Color,
           a.Background,
           a.CalcInGeneral,
           a.Disabled,
           a.Position,
           a.AppPackageName,
           ISNULL(dbo.GetTotalBalance(0, @Reference, @UserId), 0) AS GrandTotalBalance,
           ISNULL(dbo.GetTotalBalance(@AccountId, @Reference, @UserId), 0) AS TotalBalance,
           ISNULL(dbo.GetRealBalance(@AccountId, @UserId), 0) AS CurrentBalance,
           ISNULL(dbo.GetRealGrossBalance(@AccountId, @UserId), 0) AS CurrentGrossBalance,
           ISNULL(dbo.GetPreviousBalance(@AccountId, @Reference, @UserId), 0) AS PreviousBalance,
           ISNULL(dbo.GetTotalYields(@AccountId, @Reference, @UserId), 0) AS TotalYields,
           ISNULL(dbo.GetTotalYields(0, @Reference, @UserId), 0) AS GrandTotalYields,
           a.YieldPercent,
           a.YieldIndex,
           a.IrPercent,
           a.IsTaxExempt,
           ISNULL(dbo.GetTotalGrossBalance(@AccountId, @Reference, @UserId), 0) AS TotalBalanceGross
    FROM dbo.Accounts a
    WHERE a.Id = @AccountId
      AND a.UserId = @UserId
      AND a.CalcInGeneral = 1
);
GO
