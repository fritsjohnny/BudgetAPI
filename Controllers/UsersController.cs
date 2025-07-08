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
        public IActionResult Authenticate(UsersAuthenticateRequest model)
        {
            UsersAuthenticateResponse? response = _userService.Authenticate(model);

            return Ok(response);
        }

        [AllowAnonymous]
        [HttpPost("register")]
        public IActionResult Register(UsersRegisterRequest newUser)
        {
            if (_userService.UserExists(newUser))
            {
                return Problem("Usuário já existe!");
            }

            _userService.Register(newUser);

            return Ok(new { message = "Cadastro concluído!" });
        }

        [HttpGet]
        public IActionResult GetAll()
        {
            IEnumerable<Users>? users = _userService.GetAll();

            return Ok(users);
        }

        [HttpGet("{id}")]
        public IActionResult GetById(int id)
        {
            Users? user = _userService.GetById(id);

            return Ok(user);
        }

        [HttpPut("{id}")]
        public IActionResult Update(int id, UsersUpdateRequest currentUser)
        {
            if (_userService.UserExists(id, currentUser))
            {
                return Problem("Usuário já existe!");
            }

            _userService.Update(id, currentUser);

            return Ok(new { message = "Usuário atualizado!" });
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
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
            {
                return Problem("Usuário não autenticado.");
            }

            _userService.UpdateFcmToken(user.Id, dto.Token, dto.Timezone);

            return Ok(new { message = "Token atualizado com sucesso!" });
        }
    }
}
