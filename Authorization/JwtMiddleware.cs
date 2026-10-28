using BudgetAPI.Models;
using BudgetAPI.Services;

namespace BudgetAPI.Authorization
{
    public class JwtMiddleware
    {
        private readonly RequestDelegate _next;

        public JwtMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context, IUserService userService, IJwtUtils jwtUtils)
        {
            string? token = context.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
            int? userId = jwtUtils.ValidateToken(token, out long tokenPasswordVersion);

            if (userId != null)
            {
                try
                {
                    var user = userService.GetById(userId.Value);
                    long currentPasswordVersion = user.PasswordChangedAt?.Ticks ?? 0;
                    if (currentPasswordVersion == tokenPasswordVersion)
                        context.Items["User"] = user;
                }
                catch (KeyNotFoundException)
                {
                    // A deleted account invalidates all of its outstanding tokens.
                }
            }

            await _next(context);
        }
    }
}
