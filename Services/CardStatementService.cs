using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using BudgetAPI.Data;
using BudgetAPI.Models;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace BudgetAPI.Services
{
    public class CardStatementService : ICardStatementService
    {
        private const int MaxArchiveBytes = 10 * 1024 * 1024;
        private const long MaxCsvBytes = 5 * 1024 * 1024;

        private readonly BudgetContext _context;
        private readonly IConfiguration _configuration;
        private readonly IDataProtector _statementPasswordProtector;
        private readonly int _userId;

        public CardStatementService(
            BudgetContext context,
            IHttpContextAccessor httpContextAccessor,
            IConfiguration configuration,
            IDataProtectionProvider dataProtectionProvider)
        {
            _context = context;
            _configuration = configuration;
            _statementPasswordProtector = dataProtectionProvider.CreateProtector("BudgetAPI.Cards.StatementPassword.v1");
            _userId = (httpContextAccessor.HttpContext?.Items["User"] as Users)?.Id ?? 0;
        }

        public async Task<C6StatementPreview> PreviewC6Async(IFormFile file, int cardId)
        {
            if (!await OwnsCardAsync(cardId))
            {
                throw new CardStatementImportException("O cartão selecionado não pertence ao usuário atual.");
            }

            if (file.Length <= 0 || file.Length > MaxArchiveBytes)
            {
                throw new CardStatementImportException("O arquivo ZIP está vazio ou excede o limite de 10 MB.");
            }

            string? protectedPassword = await _context.Cards
                .Where(card => card.Id == cardId && card.UserId == _userId)
                .Select(card => card.StatementPasswordProtected)
                .FirstOrDefaultAsync();
            string password;
            try
            {
                password = !string.IsNullOrEmpty(protectedPassword)
                    ? Encoding.UTF8.GetString(_statementPasswordProtector.Unprotect(Convert.FromBase64String(protectedPassword)))
                    : _configuration["C6Statement:ZipPassword"] ?? string.Empty;
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                throw new CardStatementImportException("A senha salva para este cartão não pode ser lida. Cadastre-a novamente no cartão.");
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                throw new CardStatementImportException("Cadastre a senha da fatura neste cartão antes de importar o arquivo.");
            }

            try
            {
                using Stream archiveStream = file.OpenReadStream();
                using ZipFile archive = new(archiveStream)
                {
                    Password = password
                };

                ZipEntry[] csvEntries = archive.Cast<ZipEntry>()
                    .Where(entry => !entry.IsDirectory && entry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (csvEntries.Length != 1)
                {
                    throw new CardStatementImportException("O ZIP precisa conter exatamente um arquivo CSV.");
                }

                ZipEntry csvEntry = csvEntries[0];
                if (csvEntry.Size <= 0 || csvEntry.Size > MaxCsvBytes)
                {
                    throw new CardStatementImportException("O CSV dentro do ZIP está vazio ou excede o limite de 5 MB.");
                }

                using Stream csvStream = archive.GetInputStream(csvEntry);
                using StreamReader reader = new(csvStream, GetC6Encoding(), true);
                string csv = await reader.ReadToEndAsync();

                return ParseC6Csv(file.FileName, csv);
            }
            catch (CardStatementImportException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new CardStatementImportException("Não foi possível abrir o ZIP. Confira se o arquivo é uma fatura C6 válida e se a senha está correta.");
            }
        }

        public async Task<List<CardsPhysicalCard>> GetPhysicalCardsAsync(int cardId)
        {
            await EnsureOwnsCardAsync(cardId);

            return await _context.CardsPhysicalCards
                .Where(card => card.CardId == cardId)
                .OrderBy(card => card.NameOnCard)
                .ThenBy(card => card.LastFourDigits)
                .ToListAsync();
        }

        public async Task<List<CardsPhysicalCard>> SavePhysicalCardsAsync(
            int cardId,
            IReadOnlyCollection<CardsPhysicalCardInput> cards)
        {
            await EnsureOwnsCardAsync(cardId);

            if (cards.Count > 100)
            {
                throw new CardStatementImportException("O arquivo contém mais de 100 cartões físicos.");
            }

            List<CardsPhysicalCard> existingCards = await _context.CardsPhysicalCards
                .Where(card => card.CardId == cardId)
                .ToListAsync();

            foreach (CardsPhysicalCardInput input in cards)
            {
                string nameOnCard = Regex.Replace((input.NameOnCard ?? string.Empty).Trim(), @"\s+", " ");
                string lastFourDigits = (input.LastFourDigits ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(nameOnCard) || nameOnCard.Length > 100 || !Regex.IsMatch(lastFourDigits, @"^\d{4}$"))
                {
                    throw new CardStatementImportException("Cada cartão precisa ter o nome impresso e exatamente quatro dígitos finais.");
                }

                if (input.Id.HasValue)
                {
                    CardsPhysicalCard? cardToUpdate = existingCards.FirstOrDefault(card => card.Id == input.Id.Value);
                    if (cardToUpdate == null)
                    {
                        throw new CardStatementImportException("O cartão físico selecionado não pertence a este cartão.");
                    }

                    bool duplicateCard = existingCards.Any(card =>
                        card.Id != cardToUpdate.Id &&
                        card.LastFourDigits.Trim() == lastFourDigits &&
                        string.Equals(card.NameOnCard.Trim(), nameOnCard, StringComparison.OrdinalIgnoreCase));

                    if (duplicateCard)
                    {
                        throw new CardStatementImportException("Já existe um cartão físico com esse nome e esses quatro dígitos.");
                    }

                    cardToUpdate.NameOnCard = nameOnCard;
                    cardToUpdate.LastFourDigits = lastFourDigits;
                    continue;
                }

                bool alreadySaved = existingCards.Any(card =>
                    card.LastFourDigits.Trim() == lastFourDigits &&
                    string.Equals(card.NameOnCard.Trim(), nameOnCard, StringComparison.OrdinalIgnoreCase));

                if (!alreadySaved)
                {
                    CardsPhysicalCard physicalCard = new()
                    {
                        CardId = cardId,
                        NameOnCard = nameOnCard,
                        LastFourDigits = lastFourDigits,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.CardsPhysicalCards.Add(physicalCard);
                    existingCards.Add(physicalCard);
                }
            }

            await _context.SaveChangesAsync();

            return existingCards
                .OrderBy(card => card.NameOnCard)
                .ThenBy(card => card.LastFourDigits)
                .ToList();
        }

        public async Task<List<CardStatementDescriptionAlias>> GetAliasesAsync(int cardId, int physicalCardId)
        {
            await EnsureOwnsPhysicalCardAsync(cardId, physicalCardId);

            return await _context.CardStatementDescriptionAliases
                .Where(alias => alias.CardId == cardId && alias.PhysicalCardId == physicalCardId)
                .OrderBy(alias => alias.StatementDescription)
                .ToListAsync();
        }

        public async Task<CardStatementDescriptionAlias> SaveAliasAsync(CardStatementDescriptionAliasInput input)
        {
            await EnsureOwnsPhysicalCardAsync(input.CardId, input.PhysicalCardId);

            string statementDescription = NormalizeDisplayDescription(input.StatementDescription);
            string budgetDescription = NormalizeDisplayDescription(input.BudgetDescription);
            string normalizedStatementDescription = NormalizeMatchDescription(statementDescription);

            if (string.IsNullOrWhiteSpace(normalizedStatementDescription) ||
                string.IsNullOrWhiteSpace(budgetDescription) ||
                statementDescription.Length > 150 ||
                budgetDescription.Length > 150)
            {
                throw new CardStatementImportException("As duas descrições são obrigatórias e devem ter até 150 caracteres.");
            }

            CardStatementDescriptionAlias? alias = await _context.CardStatementDescriptionAliases
                .FirstOrDefaultAsync(item =>
                    item.CardId == input.CardId &&
                    item.PhysicalCardId == input.PhysicalCardId &&
                    item.NormalizedStatementDescription == normalizedStatementDescription);

            if (alias == null)
            {
                alias = new CardStatementDescriptionAlias
                {
                    CardId = input.CardId,
                    PhysicalCardId = input.PhysicalCardId,
                    CreatedAt = DateTime.UtcNow
                };

                _context.CardStatementDescriptionAliases.Add(alias);
            }

            alias.StatementDescription = statementDescription;
            alias.NormalizedStatementDescription = normalizedStatementDescription;
            alias.BudgetDescription = budgetDescription;
            alias.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return alias;
        }

        public async Task<bool> DeleteAliasAsync(int cardId, int aliasId)
        {
            await EnsureOwnsCardAsync(cardId);

            CardStatementDescriptionAlias? alias = await _context.CardStatementDescriptionAliases
                .FirstOrDefaultAsync(item => item.Id == aliasId && item.CardId == cardId);

            if (alias == null)
            {
                return false;
            }

            _context.CardStatementDescriptionAliases.Remove(alias);
            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<List<CardStatementReconciliationMatchDTO>> GetReconciliationMatchesAsync(int cardId, string reference)
        {
            await EnsureOwnsCardAsync(cardId);
            ValidateReference(reference);

            return await _context.CardStatementReconciliationMatches
                .Where(match => match.CardId == cardId && match.Reference == reference &&
                    (!match.PostingId.HasValue || match.IsIgnored ||
                     _context.CardsPostings.Any(posting => posting.Id == match.PostingId &&
                         posting.CardId == cardId && posting.Reference == reference && posting.Card!.UserId == _userId)))
                .OrderBy(match => match.PurchaseDate)
                .ThenBy(match => match.Id)
                .Select(match => new CardStatementReconciliationMatchDTO
                {
                    CardId = match.CardId,
                    Reference = match.Reference,
                    PurchaseDate = match.PurchaseDate,
                    PhysicalCardId = match.PhysicalCardId,
                    LastFourDigits = match.LastFourDigits,
                    NormalizedStatementDescription = match.NormalizedStatementDescription,
                    Installment = match.Installment,
                    AmountBrl = match.AmountBrl,
                    Occurrence = match.Occurrence,
                    PostingId = match.PostingId,
                    IsResolved = match.IsResolved,
                    IsIgnored = match.IsIgnored,
                })
                .ToListAsync();
        }

        public async Task<CardStatementReconciliationMatchDTO> SaveReconciliationMatchAsync(
            CardStatementReconciliationMatchInput input)
        {
            await EnsureOwnsCardAsync(input.CardId);
            ValidateReference(input.Reference);

            string lastFourDigits = (input.LastFourDigits ?? string.Empty).Trim();
            string normalizedDescription = NormalizeMatchDescription(input.StatementDescription);
            string installment = NormalizeInstallment(input.Installment);
            if (!Regex.IsMatch(lastFourDigits, @"^\d{4}$") ||
                string.IsNullOrWhiteSpace(normalizedDescription) ||
                normalizedDescription.Length > 150 ||
                installment.Length > 20 ||
                input.AmountBrl <= 0 ||
                input.Occurrence < 0 || input.Occurrence > 10000 ||
                input.PurchaseDate == default)
            {
                throw new CardStatementImportException("A compra informada para associação não é válida.");
            }

            if (input.PhysicalCardId.HasValue)
            {
                await EnsureOwnsPhysicalCardAsync(input.CardId, input.PhysicalCardId.Value);
            }

            CardsPostings? posting = null;
            if (input.PostingId.HasValue)
            {
                posting = await _context.CardsPostings
                    .FirstOrDefaultAsync(item => item.Id == input.PostingId &&
                        item.CardId == input.CardId && item.Reference == input.Reference &&
                        item.Card!.UserId == _userId);
                if (posting == null)
                {
                    throw new CardStatementImportException("O lançamento associado não pertence a este cartão e referência.");
                }
            }

            if (posting != null && posting.PhysicalCardId.HasValue && input.PhysicalCardId.HasValue &&
                posting.PhysicalCardId.Value != input.PhysicalCardId.Value)
            {
                throw new CardStatementImportException("O lançamento está associado a outro cartão físico.");
            }

            string fingerprint = CreateTransactionFingerprint(
                input.PurchaseDate,
                input.PhysicalCardId,
                lastFourDigits,
                normalizedDescription,
                installment,
                input.AmountBrl,
                input.Occurrence);

            CardStatementReconciliationMatch? match = await _context.CardStatementReconciliationMatches
                .FirstOrDefaultAsync(item => item.CardId == input.CardId &&
                    item.Reference == input.Reference && item.TransactionFingerprint == fingerprint);

            DateTime now = DateTime.UtcNow;
            if (match == null)
            {
                match = new CardStatementReconciliationMatch
                {
                    CardId = input.CardId,
                    Reference = input.Reference,
                    TransactionFingerprint = fingerprint,
                    CreatedAt = now,
                };
                _context.CardStatementReconciliationMatches.Add(match);
            }

            match.PurchaseDate = input.PurchaseDate.Date;
            match.PhysicalCardId = input.PhysicalCardId;
            match.LastFourDigits = lastFourDigits;
            match.NormalizedStatementDescription = normalizedDescription;
            match.Installment = installment;
            match.AmountBrl = decimal.Round(input.AmountBrl, 2, MidpointRounding.AwayFromZero);
            match.Occurrence = input.Occurrence;
            match.PostingId = input.PostingId;
            match.IsResolved = !input.IsIgnored && input.IsResolved && posting != null &&
                Math.Abs(posting.Amount - input.AmountBrl) < 0.005m;
            match.IsIgnored = input.IsIgnored;
            match.UpdatedAt = now;

            await _context.SaveChangesAsync();
            return ToReconciliationMatchDTO(match);
        }

        public async Task<List<CardStatementReconciliationMatchDTO>> SaveReconciliationMatchesAsync(
            List<CardStatementReconciliationMatchInput> inputs)
        {
            if (inputs.Count == 0) return new List<CardStatementReconciliationMatchDTO>();

            int cardId = inputs[0].CardId;
            string reference = inputs[0].Reference;
            await EnsureOwnsCardAsync(cardId);
            ValidateReference(reference);
            if (inputs.Any(input => input.CardId != cardId || input.Reference != reference))
                throw new CardStatementImportException("O lote deve conter apenas uma referência e um cartão.");

            var prepared = inputs.Select(input =>
            {
                string lastFourDigits = (input.LastFourDigits ?? string.Empty).Trim();
                string normalizedDescription = NormalizeMatchDescription(input.StatementDescription);
                string installment = NormalizeInstallment(input.Installment);
                if (!Regex.IsMatch(lastFourDigits, @"^\d{4}$") ||
                    string.IsNullOrWhiteSpace(normalizedDescription) || normalizedDescription.Length > 150 ||
                    installment.Length > 20 || input.AmountBrl <= 0 || input.Occurrence < 0 ||
                    input.Occurrence > 10000 || input.PurchaseDate == default)
                    throw new CardStatementImportException("Uma compra do lote de associações não é válida.");

                string fingerprint = CreateTransactionFingerprint(
                    input.PurchaseDate, input.PhysicalCardId, lastFourDigits, normalizedDescription,
                    installment, input.AmountBrl, input.Occurrence);
                return new { Input = input, LastFourDigits = lastFourDigits, NormalizedDescription = normalizedDescription,
                    Installment = installment, Fingerprint = fingerprint };
            }).ToList();

            if (prepared.Select(item => item.Fingerprint).Distinct(StringComparer.Ordinal).Count() != prepared.Count)
                throw new CardStatementImportException("O lote contém compras repetidas.");

            List<int> physicalCardIds = prepared.Where(item => item.Input.PhysicalCardId.HasValue)
                .Select(item => item.Input.PhysicalCardId!.Value).Distinct().ToList();
            HashSet<int> ownedPhysicalCardIds = physicalCardIds.Count == 0
                ? new HashSet<int>()
                : (await _context.CardsPhysicalCards
                    .Where(card => physicalCardIds.Contains(card.Id) && card.CardId == cardId && card.Card!.UserId == _userId)
                    .Select(card => card.Id).ToListAsync()).ToHashSet();
            if (physicalCardIds.Any(id => !ownedPhysicalCardIds.Contains(id)))
                throw new CardStatementImportException("Um cartão físico do lote não pertence ao cartão informado.");

            List<int> postingIds = prepared.Where(item => item.Input.PostingId.HasValue)
                .Select(item => item.Input.PostingId!.Value).Distinct().ToList();
            Dictionary<int, CardsPostings> postingsById = postingIds.Count == 0
                ? new Dictionary<int, CardsPostings>()
                : await _context.CardsPostings
                    .Where(posting => postingIds.Contains(posting.Id) && posting.CardId == cardId &&
                        posting.Reference == reference && posting.Card!.UserId == _userId)
                    .ToDictionaryAsync(posting => posting.Id);
            if (postingIds.Any(id => !postingsById.ContainsKey(id)))
                throw new CardStatementImportException("Um lançamento do lote não pertence ao cartão e referência informados.");

            List<string> fingerprints = prepared.Select(item => item.Fingerprint).ToList();
            Dictionary<string, CardStatementReconciliationMatch> matchesByFingerprint = await _context.CardStatementReconciliationMatches
                .Where(match => match.CardId == cardId && match.Reference == reference && fingerprints.Contains(match.TransactionFingerprint))
                .ToDictionaryAsync(match => match.TransactionFingerprint);

            DateTime now = DateTime.UtcNow;
            foreach (var item in prepared)
            {
                CardStatementReconciliationMatchInput input = item.Input;
                postingsById.TryGetValue(input.PostingId ?? 0, out CardsPostings? posting);
                if (posting?.PhysicalCardId is int existingPhysicalCardId && input.PhysicalCardId.HasValue &&
                    existingPhysicalCardId != input.PhysicalCardId.Value)
                    throw new CardStatementImportException("Um lançamento está associado a outro cartão físico.");

                if (!matchesByFingerprint.TryGetValue(item.Fingerprint, out CardStatementReconciliationMatch? match))
                {
                    match = new CardStatementReconciliationMatch
                    {
                        CardId = cardId,
                        Reference = reference,
                        TransactionFingerprint = item.Fingerprint,
                        CreatedAt = now,
                    };
                    _context.CardStatementReconciliationMatches.Add(match);
                    matchesByFingerprint[item.Fingerprint] = match;
                }

                match.PurchaseDate = input.PurchaseDate.Date;
                match.PhysicalCardId = input.PhysicalCardId;
                match.LastFourDigits = item.LastFourDigits;
                match.NormalizedStatementDescription = item.NormalizedDescription;
                match.Installment = item.Installment;
                match.AmountBrl = decimal.Round(input.AmountBrl, 2, MidpointRounding.AwayFromZero);
                match.Occurrence = input.Occurrence;
                match.PostingId = input.PostingId;
                match.IsResolved = !input.IsIgnored && input.IsResolved && posting != null &&
                    Math.Abs(posting.Amount - input.AmountBrl) < 0.005m;
                match.IsIgnored = input.IsIgnored;
                match.UpdatedAt = now;
            }

            await _context.SaveChangesAsync();
            return prepared.Select(item => ToReconciliationMatchDTO(matchesByFingerprint[item.Fingerprint])).ToList();
        }

        private static CardStatementReconciliationMatchDTO ToReconciliationMatchDTO(CardStatementReconciliationMatch match) => new()
        {
            CardId = match.CardId,
            Reference = match.Reference,
            PurchaseDate = match.PurchaseDate,
            PhysicalCardId = match.PhysicalCardId,
            LastFourDigits = match.LastFourDigits.Trim(),
            NormalizedStatementDescription = match.NormalizedStatementDescription,
            Installment = match.Installment,
            AmountBrl = match.AmountBrl,
            Occurrence = match.Occurrence,
            PostingId = match.PostingId,
            IsResolved = match.IsResolved,
            IsIgnored = match.IsIgnored,
        };

        private static void ValidateReference(string reference)
        {
            if (!Regex.IsMatch(reference ?? string.Empty, @"^\d{6}$"))
            {
                throw new CardStatementImportException("A referência da fatura é inválida.");
            }
        }

        private static string NormalizeInstallment(string? installment)
        {
            string value = Regex.Replace((installment ?? string.Empty).Trim(), @"\s+", string.Empty);
            Match match = Regex.Match(value, @"^(\d+)/(\d+)$");
            if (match.Success &&
                int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) &&
                int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int total))
            {
                return $"{number}/{total}";
            }

            return value.ToUpperInvariant();
        }

        private static string CreateTransactionFingerprint(
            DateTime purchaseDate,
            int? physicalCardId,
            string lastFourDigits,
            string normalizedDescription,
            string installment,
            decimal amountBrl,
            int occurrence)
        {
            string canonical = string.Join("|",
                purchaseDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                physicalCardId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                lastFourDigits,
                normalizedDescription,
                installment,
                decimal.Round(amountBrl, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture),
                occurrence.ToString(CultureInfo.InvariantCulture));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        }

        private async Task<bool> OwnsCardAsync(int cardId)
        {
            return _userId > 0 && await _context.Cards.AnyAsync(card => card.Id == cardId && card.UserId == _userId);
        }

        private async Task EnsureOwnsCardAsync(int cardId)
        {
            if (!await OwnsCardAsync(cardId))
            {
                throw new CardStatementImportException("O cartão selecionado não pertence ao usuário atual.");
            }
        }

        private async Task EnsureOwnsPhysicalCardAsync(int cardId, int physicalCardId)
        {
            await EnsureOwnsCardAsync(cardId);

            bool ownsPhysicalCard = await _context.CardsPhysicalCards.AnyAsync(card =>
                card.Id == physicalCardId &&
                card.CardId == cardId &&
                card.Card!.UserId == _userId);

            if (!ownsPhysicalCard)
            {
                throw new CardStatementImportException("O cartão físico selecionado não pertence à fatura informada.");
            }
        }

        private static C6StatementPreview ParseC6Csv(string fileName, string csv)
        {
            List<List<string>> records = ParseCsvRecords(csv);
            if (records.Count < 2)
            {
                throw new CardStatementImportException("O CSV do C6 não contém lançamentos.");
            }

            Dictionary<string, int> columnIndexes = records[0]
                .Select((name, index) => new { Key = NormalizeHeader(name), Index = index })
                .ToDictionary(column => column.Key, column => column.Index);

            string[] expectedColumns =
            {
                "DATACOMPRA",
                "NOMENOCARTAO",
                "FINALDOCARTAO",
                "CATEGORIA",
                "DESCRICAO",
                "PARCELA",
                "VALOREMUS$",
                "COTACAOEMR$",
                "VALOREMR$"
            };

            if (expectedColumns.Any(column => !columnIndexes.ContainsKey(column)))
            {
                throw new CardStatementImportException("As colunas deste CSV não correspondem ao formato de fatura C6 esperado.");
            }

            List<C6StatementTransaction> transactions = new();
            foreach (List<string> record in records.Skip(1))
            {
                if (record.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                if (record.Count < records[0].Count)
                {
                    throw new CardStatementImportException("O CSV contém uma linha incompleta. Confira o arquivo exportado pelo C6.");
                }

                transactions.Add(new C6StatementTransaction
                {
                    PurchaseDate = ParseDate(GetValue(record, columnIndexes, "DATACOMPRA")),
                    CardholderName = GetValue(record, columnIndexes, "NOMENOCARTAO").Trim(),
                    LastFourDigits = GetValue(record, columnIndexes, "FINALDOCARTAO").Trim(),
                    Category = GetValue(record, columnIndexes, "CATEGORIA").Trim(),
                    Description = GetValue(record, columnIndexes, "DESCRICAO").Trim(),
                    Installment = GetValue(record, columnIndexes, "PARCELA").Trim(),
                    AmountUsd = ParseAmount(GetValue(record, columnIndexes, "VALOREMUS$")),
                    ExchangeRate = ParseAmount(GetValue(record, columnIndexes, "COTACAOEMR$")),
                    AmountBrl = ParseAmount(GetValue(record, columnIndexes, "VALOREMR$"))
                });
            }

            if (transactions.Count == 0)
            {
                throw new CardStatementImportException("O CSV do C6 não contém lançamentos.");
            }

            List<C6PhysicalCardMarker> physicalCards = transactions
                .GroupBy(transaction => new
                {
                    Name = transaction.CardholderName.Trim().ToUpperInvariant(),
                    Suffix = transaction.LastFourDigits
                })
                .Select(group => new C6PhysicalCardMarker
                {
                    CardholderName = group.First().CardholderName,
                    LastFourDigits = group.Key.Suffix,
                    TransactionCount = group.Count()
                })
                .OrderBy(card => card.CardholderName)
                .ThenBy(card => card.LastFourDigits)
                .ToList();

            List<C6StatementTransaction> purchases = transactions
                .Where(transaction => transaction.AmountBrl > 0 && !IsPaymentDescription(transaction.Description))
                .ToList();

            return new C6StatementPreview
            {
                FileName = Path.GetFileName(fileName),
                TransactionCount = transactions.Count,
                PurchaseCount = purchases.Count,
                PaymentOrCreditCount = transactions.Count - purchases.Count,
                PurchasesTotal = purchases.Sum(transaction => transaction.AmountBrl),
                PhysicalCards = physicalCards,
                Transactions = transactions
            };
        }

        private static Encoding GetC6Encoding()
        {
            // A exportação do C6 inspecionada está em UTF-8. O StreamReader
            // ainda detecta BOM se outra versão do arquivo o incluir.
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        }

        private static List<List<string>> ParseCsvRecords(string text)
        {
            List<List<string>> records = new();
            List<string> record = new();
            StringBuilder field = new();
            bool insideQuotes = false;

            for (int index = 0; index < text.Length; index++)
            {
                char current = text[index];

                if (insideQuotes && current == '"' && index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                    continue;
                }

                if (current == '"')
                {
                    insideQuotes = !insideQuotes;
                    continue;
                }

                if (!insideQuotes && current == ';')
                {
                    record.Add(field.ToString());
                    field.Clear();
                    continue;
                }

                if (!insideQuotes && (current == '\r' || current == '\n'))
                {
                    if (current == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                    {
                        index++;
                    }

                    record.Add(field.ToString());
                    field.Clear();
                    if (record.Any(value => !string.IsNullOrWhiteSpace(value)))
                    {
                        records.Add(record);
                    }

                    record = new List<string>();
                    continue;
                }

                field.Append(current);
            }

            if (insideQuotes)
            {
                throw new CardStatementImportException("O CSV contém um campo entre aspas que não foi fechado.");
            }

            record.Add(field.ToString());
            if (record.Any(value => !string.IsNullOrWhiteSpace(value)))
            {
                records.Add(record);
            }

            return records;
        }

        private static string NormalizeHeader(string header)
        {
            string decomposed = header.Normalize(NormalizationForm.FormD);
            StringBuilder normalized = new();

            foreach (char character in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (char.IsLetterOrDigit(character) || character == '$')
                {
                    normalized.Append(char.ToUpperInvariant(character));
                }
            }

            // O C6 inclui "(em ...)" nos cabeçalhos atuais de valores; versões
            // anteriores podem omitir essa expressão.
            return normalized.ToString() switch
            {
                "DATADECOMPRA" => "DATACOMPRA",
                "VALOREMEMUS$" => "VALOREMUS$",
                "VALOREMEMR$" => "VALOREMR$",
                _ => normalized.ToString()
            };
        }

        private static string GetValue(IReadOnlyList<string> record, IReadOnlyDictionary<string, int> columns, string column)
        {
            return record[columns[column]];
        }

        private static DateTime ParseDate(string value)
        {
            if (!DateTime.TryParseExact(value.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
            {
                throw new CardStatementImportException("O CSV contém uma data que não corresponde ao formato do C6.");
            }

            return date.Date;
        }

        private static decimal ParseAmount(string value)
        {
            if (!decimal.TryParse(value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount))
            {
                throw new CardStatementImportException("O CSV contém um valor numérico que não corresponde ao formato do C6.");
            }

            return amount;
        }

        private static bool IsPaymentDescription(string description)
        {
            return NormalizeMatchDescription(description).StartsWith("PAGAMENTOFATURA", StringComparison.Ordinal);
        }

        private static string NormalizeDisplayDescription(string? description)
        {
            return Regex.Replace((description ?? string.Empty).Trim(), @"\s+", " ");
        }

        private static string NormalizeMatchDescription(string? description)
        {
            string decomposed = (description ?? string.Empty).Normalize(NormalizationForm.FormD);
            StringBuilder normalized = new();

            foreach (char character in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (char.IsLetterOrDigit(character))
                {
                    normalized.Append(char.ToUpperInvariant(character));
                }
            }

            return normalized.ToString();
        }
    }
}
