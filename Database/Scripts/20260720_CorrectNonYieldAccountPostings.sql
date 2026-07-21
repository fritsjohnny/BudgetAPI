SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @Corrected TABLE
    (
        Id INT NOT NULL,
        AccountId INT NOT NULL,
        Reference VARCHAR(6) NOT NULL,
        Type VARCHAR(10) NULL,
        Description VARCHAR(255) NULL,
        Amount NUMERIC(18, 2) NOT NULL,
        PreviousGrossAmount NUMERIC(18, 2) NULL,
        PreviousTotalGrossBalance NUMERIC(18, 2) NULL,
        PreviousTotalIOF NUMERIC(18, 2) NULL,
        PreviousTotalIR NUMERIC(18, 2) NULL,
        PreviousIOFElapsedDays INT NULL
    );

    UPDATE ap
       SET ap.GrossAmount = NULL,
           ap.TotalGrossBalance = NULL,
           ap.TotalIOF = NULL,
           ap.TotalIR = NULL,
           ap.IOFElapsedDays = NULL
    OUTPUT deleted.Id,
           deleted.AccountId,
           deleted.Reference,
           deleted.Type,
           deleted.Description,
           deleted.Amount,
           deleted.GrossAmount,
           deleted.TotalGrossBalance,
           deleted.TotalIOF,
           deleted.TotalIR,
           deleted.IOFElapsedDays
      INTO @Corrected
    FROM dbo.AccountsPostings ap
    WHERE UPPER(ISNULL(ap.Type, '')) <> 'Y'
      AND (
          ap.GrossAmount IS NOT NULL OR
          ap.TotalGrossBalance IS NOT NULL OR
          ap.TotalIOF IS NOT NULL OR
          ap.TotalIR IS NOT NULL OR
          ap.IOFElapsedDays IS NOT NULL
      );

    SELECT COUNT(*) AS CorrectedRows
    FROM @Corrected;

    SELECT *
    FROM @Corrected
    ORDER BY Reference, AccountId, Id;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.AccountsPostings ap
        WHERE UPPER(ISNULL(ap.Type, '')) <> 'Y'
          AND (
              ap.GrossAmount IS NOT NULL OR
              ap.TotalGrossBalance IS NOT NULL OR
              ap.TotalIOF IS NOT NULL OR
              ap.TotalIR IS NOT NULL OR
              ap.IOFElapsedDays IS NOT NULL
          )
    )
    BEGIN
        THROW 50001, 'Ainda existem lançamentos não rendimento com campos exclusivos de rendimento preenchidos.', 1;
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
    BEGIN
        ROLLBACK TRANSACTION;
    END;

    THROW;
END CATCH;
GO

SELECT ap.Id,
       ap.AccountId,
       ap.Date,
       ap.Reference,
       ap.Description,
       ap.Amount,
       ap.Type,
       ap.GrossAmount,
       ap.TotalGrossBalance,
       ap.TotalIOF,
       ap.TotalIR,
       ap.IOFElapsedDays
FROM dbo.AccountsPostings ap
WHERE UPPER(ISNULL(ap.Type, '')) <> 'Y'
  AND (
      ap.GrossAmount IS NOT NULL OR
      ap.TotalGrossBalance IS NOT NULL OR
      ap.TotalIOF IS NOT NULL OR
      ap.TotalIR IS NOT NULL OR
      ap.IOFElapsedDays IS NOT NULL
  )
ORDER BY ap.Reference, ap.AccountId, ap.Id;
GO
