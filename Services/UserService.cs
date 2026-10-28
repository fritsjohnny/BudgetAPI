using System.Data;
using System.Security.Cryptography;
using System.Text;
using BudgetAPI.Authorization;
using BudgetAPI.Data;
using BudgetAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetAPI.Services
{
    public interface IUserService
    {
        UsersAuthenticateResponse Authenticate(UsersAuthenticateRequest model);
        IEnumerable<Users> GetAll();
        Users GetById(int id);
        void Register(UsersRegisterRequest newUser);
        void Update(int id, UsersUpdateRequest model);
        UserProfileResponse UpdateProfile(UserProfileUpdateRequest model);
        void ChangePassword(PasswordChangeRequest model);
        void RequestPasswordReset(string email);
        void ResetPassword(PasswordResetRequest model);
        void VerifyEmail(EmailTokenRequest model);
        void ResendEmailVerification(string email);
        void Delete(int id);
        bool UserExists(UsersRegisterRequest user);
        bool UserExists(int id, UsersUpdateRequest currentUser);
        void UpdateFcmToken(int userId, string token, string timezone);
    }

    public class UserService : IUserService
    {
        private const string VerifyEmailPurpose = "verify-email";
        private const string ResetPasswordPurpose = "reset-password";
        private static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(24);
        private static readonly TimeSpan ResetLifetime = TimeSpan.FromMinutes(30);
        private readonly BudgetContext _context;
        private readonly IJwtUtils _jwtUtils;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IAccountEmailService _emailService;
        private readonly ILogger<UserService> _logger;

        public UserService(
            BudgetContext context,
            IJwtUtils jwtUtils,
            IHttpContextAccessor httpContextAccessor,
            IAccountEmailService emailService,
            ILogger<UserService> logger)
        {
            _context = context;
            _jwtUtils = jwtUtils;
            _httpContextAccessor = httpContextAccessor;
            _emailService = emailService;
            _logger = logger;
        }

        public UsersAuthenticateResponse Authenticate(UsersAuthenticateRequest model)
        {
            string login = model.Login.Trim();
            Users? user = _context.Users.SingleOrDefault(x => x.Login == login);

            if (user == null || !BCrypt.Net.BCrypt.Verify(model.Password, user.Password))
                throw new UnauthorizedAccessException("Login ou senha está incorreto!");

            if (!string.IsNullOrWhiteSpace(user.Email) && !user.EmailVerified)
                throw new UnauthorizedAccessException("Confirme seu e-mail antes de entrar. Verifique sua caixa de entrada ou solicite um novo link.");

            return new UsersAuthenticateResponse
            {
                Id = user.Id,
                Name = user.Name,
                Login = user.Login,
                Email = user.Email,
                EmailVerified = user.EmailVerified,
                Token = _jwtUtils.GenerateToken(user)
            };
        }

        public void Register(UsersRegisterRequest newUser)
        {
            newUser.Name = newUser.Name.Trim();
            newUser.Login = newUser.Login.Trim();
            newUser.Email = NormalizeEmail(newUser.Email);

            if (_context.Users.Any(x => x.Login == newUser.Login))
                throw new InvalidOperationException("Não foi possível concluir o cadastro. Verifique os dados informados.");

            if (_context.Users.Any(x => x.Email == newUser.Email || x.PendingEmail == newUser.Email))
                throw new InvalidOperationException("Não foi possível concluir o cadastro. Verifique os dados informados.");

            EnsurePasswordLength(newUser.Password);
            var user = new Users
            {
                Name = newUser.Name,
                Login = newUser.Login,
                Email = newUser.Email,
                EmailVerified = false,
                Password = BCrypt.Net.BCrypt.HashPassword(newUser.Password)
            };

            _context.Users.Add(user);
            _context.SaveChanges();
            string token = CreateCode(user, VerifyEmailPurpose, user.Email, VerificationLifetime);
            _context.SaveChanges();

            try
            {
                _emailService.SendVerificationAsync(user.Email!, token).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // Registration remains pending; the generic resend endpoint can deliver another link.
                _logger.LogError(ex, "Could not send registration verification message for user {UserId}", user.Id);
            }
        }

        public void RequestPasswordReset(string email)
        {
            string normalizedEmail = NormalizeEmail(email);
            Users? user = _context.Users.SingleOrDefault(x => x.Email == normalizedEmail && x.EmailVerified);
            if (user == null)
                return;

            DateTime cutoff = DateTime.UtcNow.AddHours(-1);
            int recentRequests = _context.AccountVerificationCodes.Count(x =>
                x.UserId == user.Id && x.Purpose == ResetPasswordPurpose && x.CreatedAtUtc >= cutoff);
            if (recentRequests >= 5)
                return;

            string token = CreateCode(user, ResetPasswordPurpose, normalizedEmail, ResetLifetime);
            _context.SaveChanges();

            try
            {
                _emailService.SendPasswordResetAsync(normalizedEmail, token).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // Keep the public response indistinguishable for known and unknown addresses.
                _logger.LogError(ex, "Could not send password recovery message for user {UserId}", user.Id);
            }
        }

        public void ResetPassword(PasswordResetRequest model)
        {
            string email = NormalizeEmail(model.Email);
            EnsurePasswordLength(model.NewPassword);
            string hash = HashCode(model.Token);

            using var transaction = _context.Database.BeginTransaction(IsolationLevel.Serializable);
            DateTime now = DateTime.UtcNow;
            AccountVerificationCode? code = _context.AccountVerificationCodes
                .SingleOrDefault(x => x.ValueHash == hash && x.Purpose == ResetPasswordPurpose &&
                    x.Email == email && x.ExpiresAtUtc > now);

            if (code == null)
                throw new InvalidOperationException("O link expirou ou já foi utilizado. Solicite uma nova redefinição.");

            Users user = GetUser(code.UserId);
            if (!user.EmailVerified || user.Email != email)
                throw new InvalidOperationException("O link expirou ou já foi utilizado. Solicite uma nova redefinição.");

            user.Password = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
            user.PasswordChangedAt = DateTime.UtcNow;
            _context.AccountVerificationCodes.RemoveRange(_context.AccountVerificationCodes.Where(x =>
                x.UserId == user.Id && x.Purpose == ResetPasswordPurpose));
            _context.SaveChanges();
            transaction.Commit();
        }

        public void ChangePassword(PasswordChangeRequest model)
        {
            Users user = GetCurrentUser();
            EnsurePasswordLength(model.NewPassword);

            if (!BCrypt.Net.BCrypt.Verify(model.CurrentPassword, user.Password))
                throw new UnauthorizedAccessException("A senha atual está incorreta.");

            user.Password = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
            user.PasswordChangedAt = DateTime.UtcNow;
            _context.AccountVerificationCodes.RemoveRange(_context.AccountVerificationCodes.Where(x =>
                x.UserId == user.Id && x.Purpose == ResetPasswordPurpose));
            _context.SaveChanges();
        }

        public UserProfileResponse UpdateProfile(UserProfileUpdateRequest model)
        {
            Users user = GetCurrentUser();
            user.Name = model.Name.Trim();
            string? emailToSend = null;

            if (!string.IsNullOrWhiteSpace(model.Email))
            {
                string email = NormalizeEmail(model.Email);
                if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(user.PendingEmail, email, StringComparison.OrdinalIgnoreCase))
                {
                    bool inUse = _context.Users.Any(x => x.Id != user.Id &&
                        (x.Email == email || x.PendingEmail == email));
                    if (inUse)
                        throw new InvalidOperationException("Este e-mail não pode ser usado.");

                    DateTime cutoff = DateTime.UtcNow.AddHours(-1);
                    int recentVerificationRequests = _context.AccountVerificationCodes.Count(x =>
                        x.UserId == user.Id && x.Purpose == VerifyEmailPurpose && x.CreatedAtUtc >= cutoff);
                    if (recentVerificationRequests >= 5)
                        throw new InvalidOperationException("Muitas solicitações de confirmação. Tente novamente mais tarde.");

                    user.PendingEmail = email;
                    emailToSend = email;
                }
            }

            _context.SaveChanges();
            if (emailToSend != null)
            {
                string token = CreateCode(user, VerifyEmailPurpose, emailToSend, VerificationLifetime);
                _context.SaveChanges();
                try
                {
                    _emailService.SendVerificationAsync(emailToSend, token).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not send profile email verification message for user {UserId}", user.Id);
                    throw new InvalidOperationException("Os dados foram salvos, mas não foi possível enviar o e-mail de confirmação. Tente reenviar a confirmação.");
                }
            }

            return new UserProfileResponse
            {
                Name = user.Name,
                Email = user.Email,
                EmailVerified = user.EmailVerified,
                PendingEmail = user.PendingEmail
            };
        }

        public void VerifyEmail(EmailTokenRequest model)
        {
            string email = NormalizeEmail(model.Email);
            string hash = HashCode(model.Token);
            using var transaction = _context.Database.BeginTransaction(IsolationLevel.Serializable);

            DateTime now = DateTime.UtcNow;
            AccountVerificationCode? code = _context.AccountVerificationCodes.SingleOrDefault(x =>
                x.ValueHash == hash && x.Purpose == VerifyEmailPurpose &&
                x.Email == email && x.ExpiresAtUtc > now);
            if (code == null)
                throw new InvalidOperationException("O link expirou ou já foi utilizado. Solicite uma nova confirmação.");

            Users user = GetUser(code.UserId);
            if (user.PendingEmail == email)
            {
                bool inUse = _context.Users.Any(x => x.Id != user.Id && x.Email == email);
                if (inUse)
                    throw new InvalidOperationException("Este e-mail não pode ser usado.");
                user.Email = email;
                user.PendingEmail = null;
            }
            else if (user.Email != email)
            {
                throw new InvalidOperationException("O link expirou ou já foi utilizado. Solicite uma nova confirmação.");
            }

            user.EmailVerified = true;
            _context.AccountVerificationCodes.RemoveRange(_context.AccountVerificationCodes.Where(x =>
                x.UserId == user.Id && x.Purpose == VerifyEmailPurpose));
            _context.SaveChanges();
            transaction.Commit();
        }

        public void ResendEmailVerification(string email)
        {
            string normalizedEmail = NormalizeEmail(email);
            Users? user = _context.Users.SingleOrDefault(x =>
                (x.Email == normalizedEmail && !x.EmailVerified) || x.PendingEmail == normalizedEmail);
            if (user == null)
                return;

            DateTime cutoff = DateTime.UtcNow.AddHours(-1);
            int recentRequests = _context.AccountVerificationCodes.Count(x =>
                x.UserId == user.Id && x.Purpose == VerifyEmailPurpose && x.CreatedAtUtc >= cutoff);
            if (recentRequests >= 5)
                return;

            string destination = user.PendingEmail ?? user.Email!;
            string token = CreateCode(user, VerifyEmailPurpose, destination, VerificationLifetime);
            _context.SaveChanges();
            try
            {
                _emailService.SendVerificationAsync(destination, token).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not resend email verification message for user {UserId}", user.Id);
            }
        }

        private string CreateCode(Users user, string purpose, string? email, TimeSpan lifetime)
        {
            DateTime now = DateTime.UtcNow;
            _context.AccountVerificationCodes.RemoveRange(_context.AccountVerificationCodes.Where(x =>
                x.ExpiresAtUtc <= now));
            string rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            _context.AccountVerificationCodes.Add(new AccountVerificationCode
            {
                UserId = user.Id,
                Purpose = purpose,
                ValueHash = HashCode(rawToken),
                Email = email,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.Add(lifetime)
            });
            return rawToken;
        }

        private static string HashCode(string token) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

        private static void EnsurePasswordLength(string password)
        {
            if (Encoding.UTF8.GetByteCount(password) > 72)
                throw new InvalidOperationException("A senha pode ter no máximo 72 bytes em UTF-8.");
        }

        private Users GetCurrentUser()
        {
            return _httpContextAccessor.HttpContext?.Items["User"] as Users
                ?? throw new UnauthorizedAccessException("Usuário não autenticado.");
        }



        public void Delete(int id)
        {
            Users? user = GetUser(id);
            _context.Users.Remove(user);
            _context.SaveChanges();
        }

        public IEnumerable<Users> GetAll() => _context.Users;

        public Users GetById(int id) => GetUser(id);

        public void Update(int id, UsersUpdateRequest model)
        {
            Users user = GetUser(id);
            user.Name = model.Name;

            _context.SaveChanges();
        }

        public bool UserExists(UsersRegisterRequest user) =>
            _context.Users.Any(x => x.Login == user.Login || x.Email == user.Email);

        public bool UserExists(int id, UsersUpdateRequest currentUser)
        {
            Users user = GetUser(id);
            return currentUser.Login != user.Login && _context.Users.Any(x => x.Login == currentUser.Login);
        }

        public void UpdateFcmToken(int userId, string token, string timezone)
        {
            Users user = GetUser(userId);
            user.FcmToken = token;
            user.TimezoneId = timezone;
            _context.SaveChanges();
        }

        private Users GetUser(int id)
        {
            return _context.Users.Find(id) ?? throw new KeyNotFoundException("Usuário não encontrado!");
        }
    }
}
