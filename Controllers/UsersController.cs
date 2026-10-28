using BudgetAPI.Authorization;
using BudgetAPI.Models;
using BudgetAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace BudgetAPI.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly IUserService _userService;

        public UsersController(IUserService userService)
        {
            _userService = userService;
        }

        [AllowAnonymous]
        [HttpPost("authenticate")]
        public IActionResult Authenticate(UsersAuthenticateRequest model) =>
            Ok(_userService.Authenticate(model));

        [AllowAnonymous]
        [HttpPost("register")]
        public IActionResult Register(UsersRegisterRequest newUser)
        {
            if (_userService.UserExists(newUser))
                return Conflict(new { message = "Não foi possível concluir o cadastro. Verifique os dados informados." });

            _userService.Register(newUser);
            return Accepted(new { message = "Cadastro criado. Confirme seu e-mail para ativar a conta." });
        }

        [AllowAnonymous]
        [HttpPost("verify-email")]
        public IActionResult VerifyEmail(EmailTokenRequest model)
        {
            _userService.VerifyEmail(model);
            return Ok(new { message = "E-mail confirmado." });
        }

        [AllowAnonymous]
        [HttpPost("resend-verification")]
        public IActionResult ResendVerification(PasswordResetEmailRequest model)
        {
            _userService.ResendEmailVerification(model.Email);
            return Ok(new { message = "Se a conta precisar de confirmação, enviaremos instruções ao endereço informado." });
        }

        [AllowAnonymous]
        [HttpPost("forgot-password")]
        public IActionResult ForgotPassword(PasswordResetEmailRequest model)
        {
            _userService.RequestPasswordReset(model.Email);
            return Ok(new { message = "Se existir uma conta com e-mail confirmado, enviaremos instruções para redefinir a senha." });
        }

        [AllowAnonymous]
        [HttpPost("reset-password")]
        public IActionResult ResetPassword(PasswordResetRequest model)
        {
            _userService.ResetPassword(model);
            return Ok(new { message = "Senha redefinida." });
        }

        [HttpPost("change-password")]
        public IActionResult ChangePassword(PasswordChangeRequest model)
        {
            _userService.ChangePassword(model);
            return NoContent();
        }

        [HttpGet("profile")]
        public IActionResult GetProfile()
        {
            var user = HttpContext.Items["User"] as Users;
            if (user == null)
                return Unauthorized(new { message = "Unauthorized" });

            return Ok(new UserProfileResponse
            {
                Name = user.Name,
                Email = user.Email,
                EmailVerified = user.EmailVerified,
                PendingEmail = user.PendingEmail
            });
        }

        [HttpPut("profile")]
        public IActionResult UpdateProfile(UserProfileUpdateRequest model) =>
            Ok(_userService.UpdateProfile(model));

        [HttpGet]
        public IActionResult GetAll()
        {
            var user = HttpContext.Items["User"] as Users;
            return user == null ? Unauthorized() : Ok(new[] { user });
        }

        [HttpGet("{id}")]
        public IActionResult GetById(int id)
        {
            var currentUser = HttpContext.Items["User"] as Users;
            if (currentUser == null)
                return Unauthorized();
            if (currentUser.Id != id)
                return Forbid();
            return Ok(_userService.GetById(id));
        }

        [HttpPut("{id}")]
        public IActionResult Update(int id, UsersUpdateRequest currentUser)
        {
            var authenticatedUser = HttpContext.Items["User"] as Users;
            if (authenticatedUser == null)
                return Unauthorized();
            if (authenticatedUser.Id != id)
                return Forbid();

            _userService.Update(id, currentUser);
            return Ok(new { message = "Usuário atualizado!" });
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            var authenticatedUser = HttpContext.Items["User"] as Users;
            if (authenticatedUser == null)
                return Unauthorized();
            if (authenticatedUser.Id != id)
                return Forbid();

            _userService.Delete(id);
            return Ok(new { message = "Usuário excluído!" });
        }

        public class FcmTokenDTO
        {
            public string Token { get; set; } = "";
            public string Timezone { get; set; } = "";
        }

        [HttpPost("fcmtoken")]
        public IActionResult UpdateFcmToken([FromBody] FcmTokenDTO dto)
        {
            var user = HttpContext.Items["User"] as Users;
            if (user == null)
                return Unauthorized(new { message = "Usuário não autenticado." });

            _userService.UpdateFcmToken(user.Id, dto.Token, dto.Timezone);
            return Ok(new { message = "Token atualizado com sucesso!" });
        }
    }
}
