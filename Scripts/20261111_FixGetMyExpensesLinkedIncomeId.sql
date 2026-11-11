SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER FUNCTION [dbo].[GetMyExpenses]
(
    @Reference VARCHAR(MAX),
    @UserId INT
)
RETURNS TABLE
AS
RETURN
(
    WITH CTE_MyCardsPostings AS
    (
        SELECT
            cp.CardId,
            c.Name,
            SUM(cp.Amount) AS MyTotal
        FROM CardsPostings cp
        JOIN Cards c ON c.Id = cp.CardId
        WHERE cp.Reference = @Reference
          AND c.UserId = @UserId
          AND cp.PeopleId IS NULL
        GROUP BY cp.CardId, c.Name
    )
    SELECT
        e.Id,
        e.UserId,
        e.Reference,
        e.Position,
        e.Description,
        IIF(e.CardId IS NULL, e.ToPay, ISNULL(mcp.MyTotal, 0.00)) AS ToPay,
        IIF(e.CardId IS NULL, e.Paid, 0.00) AS Paid,
        e.Note,
        e.CardId,
        e.AccountId,
        e.DueDate,
        e.ParcelNumber,
        e.Parcels,
        IIF(e.CardId IS NULL, e.TotalToPay, ISNULL(mcp.MyTotal, 0.00)) AS TotalToPay,
        e.CategoryId,
        e.Scheduled,
        e.PeopleId,
        e.Fixed,
        e.RelatedId,
        IIF(e.CardId IS NULL, e.ToPay - ABS(e.Paid), ISNULL(mcp.MyTotal, 0.00)) AS Remaining,
        c.Name AS Category,
        e.DueDay,
        e.ExpectedValue,
        e.LinkedIncomeId
    FROM Expenses e
    LEFT JOIN CTE_MyCardsPostings mcp ON mcp.CardId = e.CardId
    LEFT JOIN Categories c ON c.Id = e.CategoryId
    WHERE e.Reference = @Reference
      AND e.UserId = @UserId
      AND e.PeopleId IS NULL
      AND IIF(e.CardId IS NULL, e.ToPay, ISNULL(mcp.MyTotal, 0.00)) > 0
);
GO
