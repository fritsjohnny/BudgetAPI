SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID('dbo.TGR_UpdateIncomes', 'TR') IS NULL
BEGIN
    THROW 51000, 'Expected trigger dbo.TGR_UpdateIncomes was not found.', 1;
END;
GO
ALTER TRIGGER dbo.TGR_UpdateIncomes
ON dbo.AccountsPostings
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    /*
        Os contextos são capturados antes da eventual exclusão do CardsReceipts.
        Isso permite recalcular os lados antigo e novo de um UPDATE.
    */
    DECLARE @AffectedContexts TABLE
    (
        AccountId INT NOT NULL,
        CardId INT NULL,
        Reference VARCHAR(6) NOT NULL,
        Type VARCHAR(1) NOT NULL,
        UserId INT NOT NULL
    );

    ;WITH AffectedPostings AS
    (
        SELECT
            AccountId,
            Reference,
            Type,
            CardReceiptId
        FROM inserted

        UNION

        SELECT
            AccountId,
            Reference,
            Type,
            CardReceiptId
        FROM deleted
    )
    INSERT INTO @AffectedContexts
    (
        AccountId,
        CardId,
        Reference,
        Type,
        UserId
    )
    SELECT DISTINCT
        COALESCE(cr.AccountId, ap.AccountId) AS AccountId,
        cr.CardId,
        ap.Reference,
        ap.Type,
        a.UserId
    FROM AffectedPostings ap
    LEFT JOIN dbo.CardsReceipts cr
        ON cr.Id = ap.CardReceiptId
    INNER JOIN dbo.Accounts a
        ON a.Id = COALESCE(cr.AccountId, ap.AccountId)
    WHERE ap.Reference IS NOT NULL
      AND ap.Type IS NOT NULL;

    /*
        Mantém a regra existente:
        ao excluir diretamente um lançamento de conta vinculado,
        o respectivo recebimento de cartão também é excluído.
    */
    IF EXISTS (SELECT 1 FROM deleted)
       AND NOT EXISTS (SELECT 1 FROM inserted)
    BEGIN
        DELETE cr
        FROM dbo.CardsReceipts cr
        INNER JOIN
        (
            SELECT DISTINCT CardReceiptId
            FROM deleted
            WHERE CardReceiptId IS NOT NULL
        ) d
            ON d.CardReceiptId = cr.Id;
    END;


    /*
        Garante uma receita de rendimento por conta e referência mensal.
        Replica o padrão existente: descrição da conta, Type Y, ToReceive e
        Received iguais ao total de rendimentos, TotalToReceive igual a zero.
        Só cria a linha se ainda houver lançamento Y vigente no contexto.
    */
    ;WITH MissingYieldIncomes AS
    (
        SELECT DISTINCT
            context.AccountId,
            context.Reference,
            context.UserId,
            account.Name AS AccountName
        FROM @AffectedContexts AS context
        INNER JOIN dbo.Accounts AS account
            ON account.Id = context.AccountId
           AND account.UserId = context.UserId
        WHERE context.Type = 'Y'
          AND EXISTS
          (
              SELECT 1
              FROM dbo.AccountsPostings AS posting
              WHERE posting.AccountId = context.AccountId
                AND posting.Reference = context.Reference
                AND posting.Type = 'Y'
          )
          AND NOT EXISTS
          (
              SELECT 1
              FROM dbo.Incomes AS existingIncome WITH (UPDLOCK, HOLDLOCK)
              WHERE existingIncome.AccountId = context.AccountId
                AND existingIncome.Reference = context.Reference
                AND existingIncome.Type = 'Y'
                AND existingIncome.UserId = context.UserId
          )
    ),
    PositionedYieldIncomes AS
    (
        SELECT
            missingIncome.AccountId,
            missingIncome.Reference,
            missingIncome.UserId,
            missingIncome.AccountName,
            COALESCE(positionData.MaxPosition, 0)
                + ROW_NUMBER() OVER
                  (
                      PARTITION BY missingIncome.UserId, missingIncome.Reference
                      ORDER BY missingIncome.AccountId
                  ) AS Position,
            COALESCE(
                dbo.GetAccountsPostingsTotalByType
                (
                    missingIncome.AccountId,
                    missingIncome.Reference,
                    'Y',
                    missingIncome.UserId
                ),
                0
            ) AS YieldAmount
        FROM MissingYieldIncomes AS missingIncome
        OUTER APPLY
        (
            SELECT MAX(existingIncome.Position) AS MaxPosition
            FROM dbo.Incomes AS existingIncome WITH (UPDLOCK, HOLDLOCK)
            WHERE existingIncome.UserId = missingIncome.UserId
              AND existingIncome.Reference = missingIncome.Reference
        ) AS positionData
    )
    INSERT INTO dbo.Incomes
    (
        UserId,
        Reference,
        Position,
        Description,
        ToReceive,
        Received,
        CardId,
        AccountId,
        Type,
        PeopleId,
        RelatedId,
        ParcelNumber,
        Parcels,
        TotalToReceive
    )
    SELECT
        UserId,
        Reference,
        Position,
        LEFT(AccountName, 50),
        YieldAmount,
        YieldAmount,
        NULL,
        AccountId,
        'Y',
        NULL,
        NULL,
        NULL,
        NULL,
        0
    FROM PositionedYieldIncomes;
    /*
        Receitas vinculadas por conta/referência/tipo.
        ReceiptDate não participa do UPDATE e permanece preservado.
    */
    UPDATE income
       SET income.ToReceive =
               CASE
                   WHEN context.Type IN ('Y', 'C')
                       THEN totals.TotalAmount
                   ELSE income.ToReceive
               END,
           income.Received = totals.TotalAmount
    FROM dbo.Incomes income
    INNER JOIN
    (
        SELECT DISTINCT
            AccountId,
            Reference,
            Type,
            UserId
        FROM @AffectedContexts
    ) context
        ON context.AccountId = income.AccountId
       AND context.Reference = income.Reference
       AND context.Type = income.Type
       AND context.UserId = income.UserId
    CROSS APPLY
    (
        SELECT COALESCE(
            dbo.GetAccountsPostingsTotalByType
            (
                context.AccountId,
                context.Reference,
                context.Type,
                context.UserId
            ),
            0
        ) AS TotalAmount
    ) totals;

    /*
        Receitas automáticas de cartão.
        A atualização é executada para todos os cartões/referências afetados.
    */
    UPDATE income
       SET income.ToReceive = COALESCE(
               dbo.GetCardTotalAmount2
               (
                   context.CardId,
                   context.Reference,
                   context.UserId
               ),
               0
           ),
           income.Received = COALESCE(
               dbo.GetCardReceiptsTotalAmount
               (
                   context.CardId,
                   context.Reference,
                   context.UserId
               ),
               0
           )
    FROM dbo.Incomes income
    INNER JOIN
    (
        SELECT DISTINCT
            CardId,
            Reference,
            Type,
            UserId
        FROM @AffectedContexts
        WHERE CardId IS NOT NULL
    ) context
        ON context.CardId = income.CardId
       AND context.Reference = income.Reference
       AND context.Type = income.Type
       AND context.UserId = income.UserId;
END;

GO
